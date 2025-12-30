using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SamFirm
{
    public class PartitionAnalyzer
    {
        public async Task<PartitionCompatibilityMap> AnalyzeCompatibilityAsync(ParsedFirmware sourceFirmware, ParsedFirmware baseFirmware)
        {
            Logger.WriteLog("Analyzing partition compatibility...", false);
            
            var compatibilityMap = new PartitionCompatibilityMap();
            
            // Analyze each source partition against base partitions
            foreach (var sourcePartition in sourceFirmware.Partitions)
            {
                var compatibility = await AnalyzePartitionCompatibilityAsync(sourcePartition, baseFirmware.Partitions);
                compatibilityMap.PartitionCompatibilities.Add(sourcePartition.Name, compatibility);
            }
            
            // Generate compatibility report
            await GenerateCompatibilityReportAsync(compatibilityMap);
            
            Logger.WriteLog("Partition compatibility analysis completed", false);
            return compatibilityMap;
        }
        
        private async Task<PartitionCompatibility> AnalyzePartitionCompatibilityAsync(FirmwarePartition sourcePartition, List<FirmwarePartition> basePartitions)
        {
            var compatibility = new PartitionCompatibility
            {
                SourcePartition = sourcePartition.Name,
                CompatibilityLevel = CompatibilityLevel.Unknown
            };
            
            // Find matching partition by type
            var matchingPartitions = basePartitions.Where(p => p.Type == sourcePartition.Type).ToList();
            
            if (matchingPartitions.Count == 0)
            {
                compatibility.CompatibilityLevel = CompatibilityLevel.Incompatible;
                compatibility.Issues.Add("No matching partition type found in base firmware");
                return compatibility;
            }
            
            // Analyze compatibility with each matching partition
            var bestMatch = await FindBestMatchingPartitionAsync(sourcePartition, matchingPartitions);
            compatibility.TargetPartition = bestMatch.Name;
            
            // Perform detailed compatibility analysis
            await AnalyzeDetailedCompatibilityAsync(sourcePartition, bestMatch, compatibility);
            
            return compatibility;
        }
        
        private async Task<FirmwarePartition> FindBestMatchingPartitionAsync(FirmwarePartition sourcePartition, List<FirmwarePartition> candidates)
        {
            FirmwarePartition bestMatch = candidates.First();
            int bestScore = 0;
            
            foreach (var candidate in candidates)
            {
                int score = await CalculateCompatibilityScoreAsync(sourcePartition, candidate);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = candidate;
                }
            }
            
            return bestMatch;
        }
        
        private async Task<int> CalculateCompatibilityScoreAsync(FirmwarePartition source, FirmwarePartition target)
        {
            int score = 0;
            
            // Same partition type
            if (source.Type == target.Type)
                score += 100;
            
            // Similar size (within 20%)
            var sizeDifference = Math.Abs(source.Size - target.Size) / (double)Math.Max(source.Size, target.Size);
            if (sizeDifference < 0.2)
                score += 50;
            else if (sizeDifference < 0.5)
                score += 25;
            
            // Name similarity
            if (source.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase))
                score += 75;
            else if (source.Name.Contains(target.Name, StringComparison.OrdinalIgnoreCase) ||
                     target.Name.Contains(source.Name, StringComparison.OrdinalIgnoreCase))
                score += 25;
            
            // Content analysis for specific partition types
            switch (source.Type)
            {
                case PartitionType.Boot:
                    score += await AnalyzeBootPartitionCompatibilityAsync(source, target);
                    break;
                case PartitionType.System:
                    score += await AnalyzeSystemPartitionCompatibilityAsync(source, target);
                    break;
                case PartitionType.Kernel:
                    score += await AnalyzeKernelPartitionCompatibilityAsync(source, target);
                    break;
            }
            
            return score;
        }
        
        private async Task AnalyzeDetailedCompatibilityAsync(FirmwarePartition source, FirmwarePartition target, PartitionCompatibility compatibility)
        {
            // Size compatibility
            var sizeDifference = Math.Abs(source.Size - target.Size) / (double)Math.Max(source.Size, target.Size);
            if (sizeDifference > 0.5)
            {
                compatibility.Issues.Add($"Significant size difference: {sizeDifference:P1}");
                compatibility.CompatibilityLevel = CompatibilityLevel.LowCompatibility;
            }
            
            // Content compatibility based on partition type
            switch (source.Type)
            {
                case PartitionType.Boot:
                    await AnalyzeBootPartitionDetailedAsync(source, target, compatibility);
                    break;
                case PartitionType.System:
                    await AnalyzeSystemPartitionDetailedAsync(source, target, compatibility);
                    break;
                case PartitionType.Kernel:
                    await AnalyzeKernelPartitionDetailedAsync(source, target, compatibility);
                    break;
                case PartitionType.DeviceTree:
                    await AnalyzeDeviceTreePartitionDetailedAsync(source, target, compatibility);
                    break;
                case PartitionType.Vendor:
                    await AnalyzeVendorPartitionDetailedAsync(source, target, compatibility);
                    break;
                default:
                    compatibility.CompatibilityLevel = CompatibilityLevel.MediumCompatibility;
                    break;
            }
            
            // Set final compatibility level if not already set
            if (compatibility.CompatibilityLevel == CompatibilityLevel.Unknown)
            {
                if (compatibility.Issues.Count == 0)
                    compatibility.CompatibilityLevel = CompatibilityLevel.HighCompatibility;
                else if (compatibility.Issues.Count <= 2)
                    compatibility.CompatibilityLevel = CompatibilityLevel.MediumCompatibility;
                else
                    compatibility.CompatibilityLevel = CompatibilityLevel.LowCompatibility;
            }
        }
        
        private async Task<int> AnalyzeBootPartitionCompatibilityAsync(FirmwarePartition source, FirmwarePartition target)
        {
            int score = 0;
            
            // Check for Android boot image magic
            if (HasAndroidBootMagic(source.Data) && HasAndroidBootMagic(target.Data))
                score += 50;
            
            // Check for similar bootloader versions
            var sourceBootloaderInfo = ExtractBootloaderInfo(source.Data);
            var targetBootloaderInfo = ExtractBootloaderInfo(target.Data);
            
            if (sourceBootloaderInfo != null && targetBootloaderInfo != null)
            {
                if (sourceBootloaderInfo.Version == targetBootloaderInfo.Version)
                    score += 30;
                else if (IsBootloaderVersionCompatible(sourceBootloaderInfo.Version, targetBootloaderInfo.Version))
                    score += 15;
            }
            
            return score;
        }
        
        private async Task<int> AnalyzeSystemPartitionCompatibilityAsync(FirmwarePartition source, FirmwarePartition target)
        {
            int score = 0;
            
            // Check for Android system structure
            if (HasAndroidSystemStructure(source.Data) && HasAndroidSystemStructure(target.Data))
                score += 40;
            
            // Check for similar Android versions
            var sourceAndroidVersion = ExtractAndroidVersion(source.Data);
            var targetAndroidVersion = ExtractAndroidVersion(target.Data);
            
            if (sourceAndroidVersion == targetAndroidVersion)
                score += 30;
            else if (Math.Abs(sourceAndroidVersion - targetAndroidVersion) <= 1)
                score += 15;
            
            return score;
        }
        
        private async Task<int> AnalyzeKernelPartitionCompatibilityAsync(FirmwarePartition source, FirmwarePartition target)
        {
            int score = 0;
            
            // Check for kernel magic
            if (HasKernelMagic(source.Data) && HasKernelMagic(target.Data))
                score += 40;
            
            // Check for similar kernel versions
            var sourceKernelVersion = ExtractKernelVersion(source.Data);
            var targetKernelVersion = ExtractKernelVersion(target.Data);
            
            if (sourceKernelVersion == targetKernelVersion)
                score += 30;
            else if (IsKernelVersionCompatible(sourceKernelVersion, targetKernelVersion))
                score += 15;
            
            return score;
        }
        
        private async Task AnalyzeBootPartitionDetailedAsync(FirmwarePartition source, FirmwarePartition target, PartitionCompatibility compatibility)
        {
            // Check Android boot image compatibility
            if (!HasAndroidBootMagic(source.Data) || !HasAndroidBootMagic(target.Data))
            {
                compatibility.Issues.Add("Missing Android boot image magic");
                compatibility.CompatibilityLevel = CompatibilityLevel.LowCompatibility;
                return;
            }
            
            // Check bootloader compatibility
            var sourceBootloaderInfo = ExtractBootloaderInfo(source.Data);
            var targetBootloaderInfo = ExtractBootloaderInfo(target.Data);
            
            if (sourceBootloaderInfo != null && targetBootloaderInfo != null)
            {
                if (!IsBootloaderVersionCompatible(sourceBootloaderInfo.Version, targetBootloaderInfo.Version))
                {
                    compatibility.Issues.Add($"Bootloader version mismatch: {sourceBootloaderInfo.Version} vs {targetBootloaderInfo.Version}");
                }
                
                if (sourceBootloaderInfo.HasSecureBoot != targetBootloaderInfo.HasSecureBoot)
                {
                    compatibility.Issues.Add("Secure boot configuration mismatch");
                }
            }
            
            compatibility.CompatibilityLevel = compatibility.Issues.Count == 0 ? 
                CompatibilityLevel.HighCompatibility : CompatibilityLevel.MediumCompatibility;
        }
        
        private async Task AnalyzeSystemPartitionDetailedAsync(FirmwarePartition source, FirmwarePartition target, PartitionCompatibility compatibility)
        {
            // Check Android system structure
            if (!HasAndroidSystemStructure(source.Data) || !HasAndroidSystemStructure(target.Data))
            {
                compatibility.Issues.Add("Missing Android system structure");
                compatibility.CompatibilityLevel = CompatibilityLevel.LowCompatibility;
                return;
            }
            
            // Check Android version compatibility
            var sourceAndroidVersion = ExtractAndroidVersion(source.Data);
            var targetAndroidVersion = ExtractAndroidVersion(target.Data);
            
            if (Math.Abs(sourceAndroidVersion - targetAndroidVersion) > 1)
            {
                compatibility.Issues.Add($"Android version mismatch: {sourceAndroidVersion} vs {targetAndroidVersion}");
            }
            
            // Check system partition size
            if (source.Size > target.Size * 1.2)
            {
                compatibility.Issues.Add("Source system partition significantly larger than target");
            }
            
            compatibility.CompatibilityLevel = compatibility.Issues.Count == 0 ? 
                CompatibilityLevel.HighCompatibility : CompatibilityLevel.MediumCompatibility;
        }
        
        private async Task AnalyzeKernelPartitionDetailedAsync(FirmwarePartition source, FirmwarePartition target, PartitionCompatibility compatibility)
        {
            // Check kernel magic
            if (!HasKernelMagic(source.Data) || !HasKernelMagic(target.Data))
            {
                compatibility.Issues.Add("Missing kernel magic bytes");
                compatibility.CompatibilityLevel = CompatibilityLevel.LowCompatibility;
                return;
            }
            
            // Check kernel version compatibility
            var sourceKernelVersion = ExtractKernelVersion(source.Data);
            var targetKernelVersion = ExtractKernelVersion(target.Data);
            
            if (!IsKernelVersionCompatible(sourceKernelVersion, targetKernelVersion))
            {
                compatibility.Issues.Add($"Kernel version incompatibility: {sourceKernelVersion} vs {targetKernelVersion}");
            }
            
            compatibility.CompatibilityLevel = compatibility.Issues.Count == 0 ? 
                CompatibilityLevel.HighCompatibility : CompatibilityLevel.MediumCompatibility;
        }
        
        private async Task AnalyzeDeviceTreePartitionDetailedAsync(FirmwarePartition source, FirmwarePartition target, PartitionCompatibility compatibility)
        {
            // Check device tree magic
            if (!HasDeviceTreeMagic(source.Data) || !HasDeviceTreeMagic(target.Data))
            {
                compatibility.Issues.Add("Missing device tree magic bytes");
                compatibility.CompatibilityLevel = CompatibilityLevel.LowCompatibility;
                return;
            }
            
            // Device tree requires careful analysis for hardware compatibility
            compatibility.Issues.Add("Device tree requires manual review for hardware compatibility");
            compatibility.CompatibilityLevel = CompatibilityLevel.MediumCompatibility;
        }
        
        private async Task AnalyzeVendorPartitionDetailedAsync(FirmwarePartition source, FirmwarePartition target, PartitionCompatibility compatibility)
        {
            // Vendor partition analysis
            if (source.Size > target.Size * 1.5)
            {
                compatibility.Issues.Add("Source vendor partition significantly larger than target");
            }
            
            // Vendor partitions often contain device-specific drivers
            compatibility.Issues.Add("Vendor partition may contain device-specific drivers requiring review");
            compatibility.CompatibilityLevel = CompatibilityLevel.MediumCompatibility;
        }
        
        private async Task GenerateCompatibilityReportAsync(PartitionCompatibilityMap compatibilityMap)
        {
            Logger.WriteLog("=== Partition Compatibility Report ===", false);
            
            foreach (var kvp in compatibilityMap.PartitionCompatibilities)
            {
                var compatibility = kvp.Value;
                Logger.WriteLog($"Partition: {compatibility.SourcePartition}", false);
                Logger.WriteLog($"  Target: {compatibility.TargetPartition}", false);
                Logger.WriteLog($"  Compatibility: {compatibility.CompatibilityLevel}", false);
                
                if (compatibility.Issues.Count > 0)
                {
                    Logger.WriteLog("  Issues:", false);
                    foreach (var issue in compatibility.Issues)
                    {
                        Logger.WriteLog($"    - {issue}", false);
                    }
                }
                Logger.WriteLog("", false);
            }
        }
        
        // Helper methods
        private bool HasAndroidBootMagic(byte[] data)
        {
            var magic = System.Text.Encoding.ASCII.GetBytes("ANDROID!");
            return data.Take(8).SequenceEqual(magic);
        }
        
        private bool HasKernelMagic(byte[] data)
        {
            var gzipMagic = new byte[] { 0x1F, 0x8B, 0x08 };
            return data.Take(3).SequenceEqual(gzipMagic) || 
                   data.Contains(System.Text.Encoding.ASCII.GetBytes("Linux version"));
        }
        
        private bool HasDeviceTreeMagic(byte[] data)
        {
            var dtMagic = new byte[] { 0xD0, 0x0D, 0xFE, 0xED };
            return data.Take(4).SequenceEqual(dtMagic);
        }
        
        private bool HasAndroidSystemStructure(byte[] data)
        {
            var indicators = new[] { "system/bin/", "system/lib/", "system/framework/" };
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 10240)).ToArray());
            return indicators.Any(indicator => dataStr.Contains(indicator));
        }
        
        private BootloaderInfo ExtractBootloaderInfo(byte[] data)
        {
            // Extract bootloader information from partition data
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 4096)).ToArray());
            
            var info = new BootloaderInfo();
            
            // Extract version using regex
            var versionMatch = System.Text.RegularExpressions.Regex.Match(dataStr, @"[A-Z]\d{3}[A-Z]{4}\d{1}[A-Z]{3}\d{1}");
            if (versionMatch.Success)
            {
                info.Version = versionMatch.Value;
            }
            
            // Check for security features
            info.HasSecureBoot = data.Contains(System.Text.Encoding.ASCII.GetBytes("SECURE_BOOT"));
            info.HasKnox = data.Contains(System.Text.Encoding.ASCII.GetBytes("KNOX"));
            
            return info;
        }
        
        private int ExtractAndroidVersion(byte[] data)
        {
            // Extract Android version from system partition
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 10240)).ToArray());
            
            // Look for Android version patterns
            var versionMatch = System.Text.RegularExpressions.Regex.Match(dataStr, @"Android (\d+)");
            if (versionMatch.Success && int.TryParse(versionMatch.Groups[1].Value, out int version))
            {
                return version;
            }
            
            return 12; // Default to Android 12
        }
        
        private string ExtractKernelVersion(byte[] data)
        {
            // Extract kernel version from kernel partition
            var dataStr = System.Text.Encoding.ASCII.GetString(data.Take(Math.Min(data.Length, 4096)).ToArray());
            
            var versionMatch = System.Text.RegularExpressions.Regex.Match(dataStr, @"Linux version (\S+)");
            if (versionMatch.Success)
            {
                return versionMatch.Groups[1].Value;
            }
            
            return "Unknown";
        }
        
        private bool IsBootloaderVersionCompatible(string version1, string version2)
        {
            // Simple bootloader version compatibility check
            return !string.IsNullOrEmpty(version1) && !string.IsNullOrEmpty(version2) &&
                   version1.Substring(0, Math.Min(4, version1.Length)) == version2.Substring(0, Math.Min(4, version2.Length));
        }
        
        private bool IsKernelVersionCompatible(string version1, string version2)
        {
            // Simple kernel version compatibility check
            if (string.IsNullOrEmpty(version1) || string.IsNullOrEmpty(version2))
                return false;
            
            var parts1 = version1.Split('.');
            var parts2 = version2.Split('.');
            
            if (parts1.Length >= 2 && parts2.Length >= 2)
            {
                return parts1[0] == parts2[0] && parts1[1] == parts2[1];
            }
            
            return false;
        }
    }
    
    public class PartitionCompatibilityMap
    {
        public Dictionary<string, PartitionCompatibility> PartitionCompatibilities { get; set; } = new Dictionary<string, PartitionCompatibility>();
    }
    
    public class PartitionCompatibility
    {
        public string SourcePartition { get; set; }
        public string TargetPartition { get; set; }
        public CompatibilityLevel CompatibilityLevel { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
    }
    
    public enum CompatibilityLevel
    {
        Unknown,
        Incompatible,
        LowCompatibility,
        MediumCompatibility,
        HighCompatibility
    }
}

