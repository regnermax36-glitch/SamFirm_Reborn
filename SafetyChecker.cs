using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SamFirm
{
    public class SafetyChecker
    {
        public async Task<bool> PerformSafetyChecksAsync(ParsedFirmware firmware)
        {
            Logger.WriteLog("Performing comprehensive safety checks...", false);
            
            var safetyReport = new SafetyReport();
            
            // Critical safety checks
            await CheckBootloaderIntegrityAsync(firmware, safetyReport);
            await CheckKernelIntegrityAsync(firmware, safetyReport);
            await CheckSecurityFeaturesAsync(firmware, safetyReport);
            await CheckPartitionIntegrityAsync(firmware, safetyReport);
            await CheckHardwareCompatibilityAsync(firmware, safetyReport);
            await CheckAntiRollbackProtectionAsync(firmware, safetyReport);
            await CheckCriticalPartitionsAsync(firmware, safetyReport);
            
            // Generate safety report
            await GenerateSafetyReportAsync(safetyReport);
            
            // Determine overall safety
            bool isSafe = safetyReport.CriticalIssues.Count == 0;
            
            if (!isSafe)
            {
                Logger.WriteLog("CRITICAL SAFETY ISSUES DETECTED - FIRMWARE NOT SAFE TO FLASH", false);
                foreach (var issue in safetyReport.CriticalIssues)
                {
                    Logger.WriteLog($"CRITICAL: {issue}", false);
                }
            }
            else if (safetyReport.Warnings.Count > 0)
            {
                Logger.WriteLog("Safety checks passed with warnings:", false);
                foreach (var warning in safetyReport.Warnings)
                {
                    Logger.WriteLog($"WARNING: {warning}", false);
                }
            }
            else
            {
                Logger.WriteLog("All safety checks passed - firmware appears safe to flash", false);
            }
            
            return isSafe;
        }
        
        private async Task CheckBootloaderIntegrityAsync(ParsedFirmware firmware, SafetyReport report)
        {
            Logger.WriteLog("Checking bootloader integrity...", false);
            
            var bootPartition = firmware.Partitions.FirstOrDefault(p => p.Type == PartitionType.Boot || p.Type == PartitionType.Bootloader);
            if (bootPartition == null)
            {
                report.CriticalIssues.Add("No bootloader partition found - device may not boot");
                return;
            }
            
            // Check for bootloader magic bytes
            if (!HasBootloaderMagic(bootPartition.Data))
            {
                report.CriticalIssues.Add("Bootloader magic bytes missing - corrupted bootloader");
                return;
            }
            
            // Check bootloader size
            if (bootPartition.Size < 1024 * 100) // Minimum 100KB
            {
                report.CriticalIssues.Add("Bootloader partition too small - likely corrupted");
                return;
            }
            
            // Check for known dangerous bootloader modifications
            if (HasDangerousBootloaderModifications(bootPartition.Data))
            {
                report.CriticalIssues.Add("Dangerous bootloader modifications detected");
                return;
            }
            
            // Check bootloader version compatibility
            var bootloaderInfo = ExtractBootloaderInfo(bootPartition.Data);
            if (bootloaderInfo != null)
            {
                if (!IsBootloaderVersionSafe(bootloaderInfo.Version))
                {
                    report.Warnings.Add($"Bootloader version {bootloaderInfo.Version} may have compatibility issues");
                }
                
                if (!bootloaderInfo.HasSecureBoot)
                {
                    report.Warnings.Add("Secure boot is disabled - reduced security");
                }
            }
            
            Logger.WriteLog("Bootloader integrity check completed", false);
        }
        
        private async Task CheckKernelIntegrityAsync(ParsedFirmware firmware, SafetyReport report)
        {
            Logger.WriteLog("Checking kernel integrity...", false);
            
            var kernelPartition = firmware.Partitions.FirstOrDefault(p => p.Type == PartitionType.Kernel);
            var bootPartition = firmware.Partitions.FirstOrDefault(p => p.Type == PartitionType.Boot);
            
            bool hasKernel = false;
            byte[] kernelData = null;
            
            if (kernelPartition != null)
            {
                hasKernel = true;
                kernelData = kernelPartition.Data;
            }
            else if (bootPartition != null && HasKernelInBoot(bootPartition.Data))
            {
                hasKernel = true;
                kernelData = bootPartition.Data;
            }
            
            if (!hasKernel)
            {
                report.CriticalIssues.Add("No kernel found - device will not boot");
                return;
            }
            
            // Check kernel magic bytes
            if (!HasKernelMagic(kernelData))
            {
                report.CriticalIssues.Add("Kernel magic bytes missing - corrupted kernel");
                return;
            }
            
            // Check for dangerous kernel modifications
            if (HasDangerousKernelModifications(kernelData))
            {
                report.CriticalIssues.Add("Dangerous kernel modifications detected");
                return;
            }
            
            // Check kernel version
            var kernelVersion = ExtractKernelVersion(kernelData);
            if (!IsKernelVersionSafe(kernelVersion))
            {
                report.Warnings.Add($"Kernel version {kernelVersion} may have security vulnerabilities");
            }
            
            Logger.WriteLog("Kernel integrity check completed", false);
        }
        
        private async Task CheckSecurityFeaturesAsync(ParsedFirmware firmware, SafetyReport report)
        {
            Logger.WriteLog("Checking security features...", false);
            
            // Check for VBMeta partition (Android Verified Boot)
            var vbmetaPartition = firmware.Partitions.FirstOrDefault(p => p.Type == PartitionType.VBMeta);
            if (vbmetaPartition != null)
            {
                if (!IsVBMetaValid(vbmetaPartition.Data))
                {
                    report.CriticalIssues.Add("Invalid VBMeta partition - verified boot will fail");
                }
            }
            else
            {
                report.Warnings.Add("No VBMeta partition found - verified boot may not work");
            }
            
            // Check for Knox security
            var bootPartition = firmware.Partitions.FirstOrDefault(p => p.Type == PartitionType.Boot);
            if (bootPartition != null)
            {
                var bootloaderInfo = ExtractBootloaderInfo(bootPartition.Data);
                if (bootloaderInfo?.HasKnox == true)
                {
                    if (HasKnoxViolations(firmware))
                    {
                        report.CriticalIssues.Add("Knox security violations detected - may trigger Knox warranty void");
                    }
                }
            }
            
            // Check for security patches
            if (!HasLatestSecurityPatches(firmware))
            {
                report.Warnings.Add("Firmware may not have latest security patches");
            }
            
            Logger.WriteLog("Security features check completed", false);
        }
        
        private async Task CheckPartitionIntegrityAsync(ParsedFirmware firmware, SafetyReport report)
        {
            Logger.WriteLog("Checking partition integrity...", false);
            
            // Check for required partitions
            var requiredPartitions = new[] { PartitionType.Boot, PartitionType.System };
            foreach (var requiredType in requiredPartitions)
            {
                if (!firmware.Partitions.Any(p => p.Type == requiredType))
                {
                    report.CriticalIssues.Add($"Missing required partition: {requiredType}");
                }
            }
            
            // Check partition sizes
            foreach (var partition in firmware.Partitions)
            {
                if (partition.Data == null || partition.Data.Length == 0)
                {
                    report.CriticalIssues.Add($"Partition {partition.Name} has no data");
                    continue;
                }
                
                if (partition.Size != partition.Data.Length)
                {
                    report.Warnings.Add($"Partition {partition.Name} size mismatch");
                }
                
                // Check for partition corruption
                if (IsPartitionCorrupted(partition))
                {
                    report.CriticalIssues.Add($"Partition {partition.Name} appears corrupted");
                }
            }
            
            Logger.WriteLog("Partition integrity check completed", false);
        }
        
        private async Task CheckHardwareCompatibilityAsync(ParsedFirmware firmware, SafetyReport report)
        {
            Logger.WriteLog("Checking hardware compatibility...", false);
            
            // Check device tree compatibility
            var dtPartition = firmware.Partitions.FirstOrDefault(p => p.Type == PartitionType.DeviceTree);
            if (dtPartition != null)
            {
                var dtInfo = ExtractDeviceTreeInfo(dtPartition.Data);
                if (dtInfo != null)
                {
                    if (!IsDeviceTreeCompatibleWithSMF731B(dtInfo))
                    {
                        report.CriticalIssues.Add("Device tree not compatible with SM-F731B hardware");
                    }
                }
            }
            
            // Check for hardware-specific drivers
            var vendorPartition = firmware.Partitions.FirstOrDefault(p => p.Type == PartitionType.Vendor);
            if (vendorPartition != null)
            {
                if (HasIncompatibleDrivers(vendorPartition.Data))
                {
                    report.CriticalIssues.Add("Incompatible hardware drivers detected");
                }
            }
            
            Logger.WriteLog("Hardware compatibility check completed", false);
        }
        
        private async Task CheckAntiRollbackProtectionAsync(ParsedFirmware firmware, SafetyReport report)
        {
            Logger.WriteLog("Checking anti-rollback protection...", false);
            
            // Check bootloader version for rollback protection
            var bootPartition = firmware.Partitions.FirstOrDefault(p => p.Type == PartitionType.Boot);
            if (bootPartition != null)
            {
                var bootloaderInfo = ExtractBootloaderInfo(bootPartition.Data);
                if (bootloaderInfo != null)
                {
                    if (IsBootloaderVersionTooOld(bootloaderInfo.Version))
                    {
                        report.CriticalIssues.Add("Bootloader version too old - may trigger anti-rollback protection");
                    }
                }
            }
            
            // Check system version
            var systemPartition = firmware.Partitions.FirstOrDefault(p => p.Type == PartitionType.System);
            if (systemPartition != null)
            {
                var androidVersion = ExtractAndroidVersion(systemPartition.Data);
                if (IsAndroidVersionTooOld(androidVersion))
                {
                    report.Warnings.Add($"Android version {androidVersion} may be too old for this device");
                }
            }
            
            Logger.WriteLog("Anti-rollback protection check completed", false);
        }
        
        private async Task CheckCriticalPartitionsAsync(ParsedFirmware firmware, SafetyReport report)
        {
            Logger.WriteLog("Checking critical partitions...", false);
            
            // Check for partitions that could brick the device if corrupted
            var criticalPartitionTypes = new[] 
            { 
                PartitionType.Boot, 
                PartitionType.Bootloader, 
                PartitionType.System 
            };
            
            foreach (var criticalType in criticalPartitionTypes)
            {
                var partition = firmware.Partitions.FirstOrDefault(p => p.Type == criticalType);
                if (partition != null)
                {
                    if (HasCriticalPartitionIssues(partition))
                    {
                        report.CriticalIssues.Add($"Critical issues detected in {criticalType} partition");
                    }
                }
            }
            
            Logger.WriteLog("Critical partitions check completed", false);
        }
        
        private async Task GenerateSafetyReportAsync(SafetyReport report)
        {
            Logger.WriteLog("=== FIRMWARE SAFETY REPORT ===", false);
            Logger.WriteLog($"Critical Issues: {report.CriticalIssues.Count}", false);
            Logger.WriteLog($"Warnings: {report.Warnings.Count}", false);
            Logger.WriteLog($"Overall Safety: {(report.CriticalIssues.Count == 0 ? "SAFE" : "UNSAFE")}", false);
            Logger.WriteLog("", false);
            
            if (report.CriticalIssues.Count > 0)
            {
                Logger.WriteLog("CRITICAL ISSUES (MUST BE RESOLVED):", false);
                foreach (var issue in report.CriticalIssues)
                {
                    Logger.WriteLog($"  ❌ {issue}", false);
                }
                Logger.WriteLog("", false);
            }
            
            if (report.Warnings.Count > 0)
            {
                Logger.WriteLog("WARNINGS (REVIEW RECOMMENDED):", false);
                foreach (var warning in report.Warnings)
                {
                    Logger.WriteLog($"  ⚠️ {warning}", false);
                }
                Logger.WriteLog("", false);
            }
        }
        
        // Helper methods for safety checks
        private bool HasBootloaderMagic(byte[] data)
        {
            var magicBytes = new byte[][] {
                System.Text.Encoding.ASCII.GetBytes("ANDROID!"),
                System.Text.Encoding.ASCII.GetBytes("BOOTLDR!"),
                new byte[] { 0x00, 0x00, 0xA0, 0xE1 }
            };
            
            return magicBytes.Any(magic => data.Take(Math.Min(1024, data.Length)).ToArray().Contains(magic));
        }
        
        private bool HasKernelMagic(byte[] data)
        {
            var kernelMagic = new byte[] { 0x1F, 0x8B, 0x08 };
            return data.Take(3).SequenceEqual(kernelMagic) || 
                   data.Contains(System.Text.Encoding.ASCII.GetBytes("Linux version"));
        }
        
        private bool HasKernelInBoot(byte[] data)
        {
            return data.Contains(System.Text.Encoding.ASCII.GetBytes("Linux version"));
        }
        
        private bool HasDangerousBootloaderModifications(byte[] data)
        {
            var dangerousPatterns = new[]
            {
                "UNLOCK",
                "TAMPERED",
                "MODIFIED",
                "CUSTOM"
            };
            
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 4096)).ToArray());
            return dangerousPatterns.Any(pattern => dataStr.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }
        
        private bool HasDangerousKernelModifications(byte[] data)
        {
            var dangerousPatterns = new[]
            {
                "su",
                "root",
                "magisk",
                "xposed"
            };
            
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 4096)).ToArray());
            return dangerousPatterns.Any(pattern => dataStr.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }
        
        private bool IsBootloaderVersionSafe(string version)
        {
            // Check if bootloader version is known to be safe
            return !string.IsNullOrEmpty(version) && version.Length >= 10;
        }
        
        private bool IsKernelVersionSafe(string version)
        {
            // Check if kernel version is known to be safe
            return !string.IsNullOrEmpty(version) && !version.Contains("debug");
        }
        
        private bool IsVBMetaValid(byte[] data)
        {
            var vbmetaMagic = System.Text.Encoding.ASCII.GetBytes("AVB0");
            return data.Take(4).SequenceEqual(vbmetaMagic);
        }
        
        private bool HasKnoxViolations(ParsedFirmware firmware)
        {
            // Check for Knox violations
            foreach (var partition in firmware.Partitions)
            {
                var dataStr = System.Text.Encoding.ASCII.GetString(partition.Data.Take(Math.Min(partition.Data.Length, 1024)).ToArray());
                if (dataStr.Contains("KNOX_VOID", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        
        private bool HasLatestSecurityPatches(ParsedFirmware firmware)
        {
            // Simple check for security patch level
            var systemPartition = firmware.Partitions.FirstOrDefault(p => p.Type == PartitionType.System);
            if (systemPartition != null)
            {
                var dataStr = System.Text.Encoding.ASCII.GetString(systemPartition.Data.Take(Math.Min(systemPartition.Data.Length, 10240)).ToArray());
                var currentYear = DateTime.Now.Year;
                return dataStr.Contains(currentYear.ToString());
            }
            return false;
        }
        
        private bool IsPartitionCorrupted(FirmwarePartition partition)
        {
            // Basic corruption checks
            if (partition.Data.All(b => b == 0x00) || partition.Data.All(b => b == 0xFF))
            {
                return true; // All zeros or all ones indicates corruption
            }
            
            // Check for partition-specific corruption patterns
            switch (partition.Type)
            {
                case PartitionType.Boot:
                    return !HasBootloaderMagic(partition.Data);
                case PartitionType.System:
                    return !HasAndroidSystemStructure(partition.Data);
                case PartitionType.Kernel:
                    return !HasKernelMagic(partition.Data);
                default:
                    return false;
            }
        }
        
        private bool HasAndroidSystemStructure(byte[] data)
        {
            var indicators = new[] { "system/bin/", "system/lib/", "system/framework/" };
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 10240)).ToArray());
            return indicators.Any(indicator => dataStr.Contains(indicator));
        }
        
        private DeviceTreeInfo ExtractDeviceTreeInfo(byte[] data)
        {
            // Extract device tree information
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 4096)).ToArray());
            
            var info = new DeviceTreeInfo();
            var modelMatch = System.Text.RegularExpressions.Regex.Match(dataStr, @"model\s*=\s*""([^""]+)""");
            if (modelMatch.Success)
            {
                info.Model = modelMatch.Groups[1].Value;
            }
            
            return info;
        }
        
        private bool IsDeviceTreeCompatibleWithSMF731B(DeviceTreeInfo dtInfo)
        {
            // Check if device tree is compatible with SM-F731B
            return dtInfo.Model?.Contains("F731B", StringComparison.OrdinalIgnoreCase) == true ||
                   dtInfo.Model?.Contains("q2q", StringComparison.OrdinalIgnoreCase) == true;
        }
        
        private bool HasIncompatibleDrivers(byte[] data)
        {
            // Check for drivers that are incompatible with SM-F731B
            var incompatibleDrivers = new[]
            {
                "exynos",
                "mali",
                "s5e"
            };
            
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 10240)).ToArray());
            return incompatibleDrivers.Any(driver => dataStr.Contains(driver, StringComparison.OrdinalIgnoreCase));
        }
        
        private bool IsBootloaderVersionTooOld(string version)
        {
            // Check if bootloader version is too old for anti-rollback protection
            return string.IsNullOrEmpty(version) || version.Length < 10;
        }
        
        private int ExtractAndroidVersion(byte[] data)
        {
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 10240)).ToArray());
            var versionMatch = System.Text.RegularExpressions.Regex.Match(dataStr, @"Android (\d+)");
            if (versionMatch.Success && int.TryParse(versionMatch.Groups[1].Value, out int version))
            {
                return version;
            }
            return 12; // Default
        }
        
        private bool IsAndroidVersionTooOld(int version)
        {
            // Check if Android version is too old
            return version < 11; // Android 11 minimum
        }
        
        private string ExtractKernelVersion(byte[] data)
        {
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 4096)).ToArray());
            var versionMatch = System.Text.RegularExpressions.Regex.Match(dataStr, @"Linux version (\S+)");
            return versionMatch.Success ? versionMatch.Groups[1].Value : "Unknown";
        }
        
        private BootloaderInfo ExtractBootloaderInfo(byte[] data)
        {
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 4096)).ToArray());
            
            var info = new BootloaderInfo();
            var versionMatch = System.Text.RegularExpressions.Regex.Match(dataStr, @"[A-Z]\d{3}[A-Z]{4}\d{1}[A-Z]{3}\d{1}");
            if (versionMatch.Success)
            {
                info.Version = versionMatch.Value;
            }
            
            info.HasSecureBoot = data.Contains(System.Text.Encoding.ASCII.GetBytes("SECURE_BOOT"));
            info.HasKnox = data.Contains(System.Text.Encoding.ASCII.GetBytes("KNOX"));
            
            return info;
        }
        
        private bool HasCriticalPartitionIssues(FirmwarePartition partition)
        {
            // Check for issues that could brick the device
            return IsPartitionCorrupted(partition) || 
                   partition.Data.Length < 1024 || // Too small
                   partition.Data.All(b => b == 0x00); // All zeros
        }
    }
    
    public class SafetyReport
    {
        public List<string> CriticalIssues { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}

