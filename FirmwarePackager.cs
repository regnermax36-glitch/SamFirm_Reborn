using System;
using System.IO;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace SamFirm
{
    public class FirmwarePackager
    {
        public async Task<string> PackageFirmwareAsync(ParsedFirmware firmware, string outputPath)
        {
            Logger.WriteLog("Packaging ported firmware...", false);
            
            try
            {
                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                // Create TAR archive
                var tarHandler = new TarArchiveHandler();
                await tarHandler.CreateTarArchiveAsync(firmware, outputPath);
                
                // Generate MD5 checksum
                var md5Path = outputPath + ".md5";
                await GenerateMD5ChecksumAsync(outputPath, md5Path);
                
                // Create package info file
                var infoPath = Path.ChangeExtension(outputPath, ".info");
                await CreatePackageInfoAsync(firmware, infoPath);
                
                // Validate packaged firmware
                if (await ValidatePackagedFirmwareAsync(outputPath))
                {
                    Logger.WriteLog($"Firmware packaged successfully: {outputPath}", false);
                    Logger.WriteLog($"MD5 checksum: {md5Path}", false);
                    Logger.WriteLog($"Package info: {infoPath}", false);
                    
                    return outputPath;
                }
                else
                {
                    throw new Exception("Packaged firmware validation failed");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Firmware packaging failed: {ex.Message}", false);
                throw;
            }
        }
        
        public async Task<string> PackageFirmwareWithCompressionAsync(ParsedFirmware firmware, string outputPath, CompressionType compressionType = CompressionType.None)
        {
            Logger.WriteLog($"Packaging firmware with {compressionType} compression...", false);
            
            string tempTarPath = outputPath + ".tmp";
            
            try
            {
                // First create uncompressed TAR
                await PackageFirmwareAsync(firmware, tempTarPath);
                
                // Apply compression if requested
                if (compressionType != CompressionType.None)
                {
                    await CompressFirmwareAsync(tempTarPath, outputPath, compressionType);
                    
                    // Clean up temporary file
                    if (File.Exists(tempTarPath))
                    {
                        File.Delete(tempTarPath);
                    }
                    
                    // Generate MD5 for compressed file
                    var md5Path = outputPath + ".md5";
                    await GenerateMD5ChecksumAsync(outputPath, md5Path);
                }
                else
                {
                    // Move temp file to final location
                    File.Move(tempTarPath, outputPath);
                }
                
                Logger.WriteLog($"Compressed firmware packaged successfully: {outputPath}", false);
                return outputPath;
            }
            catch (Exception ex)
            {
                // Clean up temporary file
                if (File.Exists(tempTarPath))
                {
                    File.Delete(tempTarPath);
                }
                
                Logger.WriteLog($"Compressed firmware packaging failed: {ex.Message}", false);
                throw;
            }
        }
        
        public async Task<PackageInfo> GetPackageInfoAsync(string packagePath)
        {
            var infoPath = Path.ChangeExtension(packagePath, ".info");
            
            if (File.Exists(infoPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(infoPath);
                    return System.Text.Json.JsonSerializer.Deserialize<PackageInfo>(json);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"Failed to read package info: {ex.Message}", false);
                }
            }
            
            // Generate package info from file
            return await GeneratePackageInfoFromFileAsync(packagePath);
        }
        
        public async Task<bool> ValidatePackageIntegrityAsync(string packagePath)
        {
            Logger.WriteLog($"Validating package integrity: {Path.GetFileName(packagePath)}", false);
            
            try
            {
                // Check if package file exists
                if (!File.Exists(packagePath))
                {
                    Logger.WriteLog("Package file not found", false);
                    return false;
                }
                
                // Validate MD5 checksum if available
                var md5Path = packagePath + ".md5";
                if (File.Exists(md5Path))
                {
                    var expectedMD5 = await File.ReadAllTextAsync(md5Path);
                    var actualMD5 = await CalculateMD5Async(packagePath);
                    
                    if (!expectedMD5.Trim().Equals(actualMD5, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.WriteLog("MD5 checksum mismatch", false);
                        return false;
                    }
                }
                
                // Validate TAR structure
                var tarHandler = new TarArchiveHandler();
                if (!await tarHandler.ValidateTarArchiveAsync(packagePath))
                {
                    Logger.WriteLog("TAR archive validation failed", false);
                    return false;
                }
                
                Logger.WriteLog("Package integrity validation passed", false);
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Package validation error: {ex.Message}", false);
                return false;
            }
        }
        
        public async Task<string> CreateFlashablePackageAsync(ParsedFirmware firmware, string outputPath, FlashablePackageOptions options = null)
        {
            Logger.WriteLog("Creating flashable package...", false);
            
            options = options ?? new FlashablePackageOptions();
            
            try
            {
                // Create base package
                var packagePath = await PackageFirmwareAsync(firmware, outputPath);
                
                // Add flashing scripts if requested
                if (options.IncludeFlashingScripts)
                {
                    await AddFlashingScriptsAsync(packagePath, options);
                }
                
                // Add installation instructions
                if (options.IncludeInstructions)
                {
                    await AddInstallationInstructionsAsync(packagePath, options);
                }
                
                // Create flash-all script
                if (options.CreateFlashAllScript)
                {
                    await CreateFlashAllScriptAsync(packagePath, firmware, options);
                }
                
                Logger.WriteLog($"Flashable package created: {packagePath}", false);
                return packagePath;
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Flashable package creation failed: {ex.Message}", false);
                throw;
            }
        }
        
        private async Task GenerateMD5ChecksumAsync(string filePath, string md5Path)
        {
            Logger.WriteLog("Generating MD5 checksum...", false);
            
            var md5 = await CalculateMD5Async(filePath);
            var fileName = Path.GetFileName(filePath);
            var md5Content = $"{md5}  {fileName}";
            
            await File.WriteAllTextAsync(md5Path, md5Content);
            
            Logger.WriteLog($"MD5: {md5}", false);
        }
        
        private async Task<string> CalculateMD5Async(string filePath)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = await Task.Run(() => md5.ComputeHash(stream));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
        
        private async Task CreatePackageInfoAsync(ParsedFirmware firmware, string infoPath)
        {
            var packageInfo = new PackageInfo
            {
                PackageVersion = "1.0",
                CreatedAt = DateTime.Now,
                OriginalFirmware = firmware.FileName,
                SourceModel = "SM-S731B",
                TargetModel = "SM-F731B",
                PartitionCount = firmware.Partitions.Count,
                TotalSize = firmware.Partitions.Sum(p => p.Size),
                SamFirmVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                PortingMethod = "Ultra-Deep Porting",
                Partitions = firmware.Partitions.Select(p => new PartitionInfo
                {
                    Name = p.Name,
                    Type = p.Type.ToString(),
                    Size = p.Size
                }).ToList()
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(packageInfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(infoPath, json);
        }
        
        private async Task<PackageInfo> GeneratePackageInfoFromFileAsync(string packagePath)
        {
            var fileInfo = new FileInfo(packagePath);
            var tarHandler = new TarArchiveHandler();
            
            return new PackageInfo
            {
                PackageVersion = "Unknown",
                CreatedAt = fileInfo.CreationTime,
                OriginalFirmware = Path.GetFileName(packagePath),
                TargetModel = "SM-F731B",
                TotalSize = fileInfo.Length,
                PartitionCount = await tarHandler.GetTarEntryCountAsync(packagePath)
            };
        }
        
        private async Task<bool> ValidatePackagedFirmwareAsync(string packagePath)
        {
            try
            {
                // Basic file validation
                var fileInfo = new FileInfo(packagePath);
                if (fileInfo.Length < 1024 * 1024) // Minimum 1MB
                {
                    Logger.WriteLog("Package file too small", false);
                    return false;
                }
                
                // TAR validation
                var tarHandler = new TarArchiveHandler();
                return await tarHandler.ValidateTarArchiveAsync(packagePath);
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Package validation error: {ex.Message}", false);
                return false;
            }
        }
        
        private async Task CompressFirmwareAsync(string inputPath, string outputPath, CompressionType compressionType)
        {
            Logger.WriteLog($"Compressing firmware with {compressionType}...", false);
            
            switch (compressionType)
            {
                case CompressionType.GZip:
                    await CompressWithGZipAsync(inputPath, outputPath);
                    break;
                case CompressionType.LZ4:
                    await CompressWithLZ4Async(inputPath, outputPath);
                    break;
                default:
                    throw new NotSupportedException($"Compression type {compressionType} not supported");
            }
        }
        
        private async Task CompressWithGZipAsync(string inputPath, string outputPath)
        {
            using (var inputStream = File.OpenRead(inputPath))
            using (var outputStream = File.Create(outputPath))
            using (var gzipStream = new System.IO.Compression.GZipStream(outputStream, System.IO.Compression.CompressionMode.Compress))
            {
                await inputStream.CopyToAsync(gzipStream);
            }
        }
        
        private async Task CompressWithLZ4Async(string inputPath, string outputPath)
        {
            // LZ4 compression would require additional library
            // For now, fall back to GZip
            await CompressWithGZipAsync(inputPath, outputPath);
        }
        
        private async Task AddFlashingScriptsAsync(string packagePath, FlashablePackageOptions options)
        {
            var packageDir = Path.GetDirectoryName(packagePath);
            
            // Create flash script for Windows
            var flashBatPath = Path.Combine(packageDir, "flash.bat");
            var flashBatContent = GenerateWindowsFlashScript(Path.GetFileName(packagePath), options);
            await File.WriteAllTextAsync(flashBatPath, flashBatContent);
            
            // Create flash script for Linux/Mac
            var flashShPath = Path.Combine(packageDir, "flash.sh");
            var flashShContent = GenerateUnixFlashScript(Path.GetFileName(packagePath), options);
            await File.WriteAllTextAsync(flashShPath, flashShContent);
            
            Logger.WriteLog("Flashing scripts added", false);
        }
        
        private async Task AddInstallationInstructionsAsync(string packagePath, FlashablePackageOptions options)
        {
            var packageDir = Path.GetDirectoryName(packagePath);
            var instructionsPath = Path.Combine(packageDir, "INSTALLATION_INSTRUCTIONS.txt");
            
            var instructions = GenerateInstallationInstructions(options);
            await File.WriteAllTextAsync(instructionsPath, instructions);
            
            Logger.WriteLog("Installation instructions added", false);
        }
        
        private async Task CreateFlashAllScriptAsync(string packagePath, ParsedFirmware firmware, FlashablePackageOptions options)
        {
            var packageDir = Path.GetDirectoryName(packagePath);
            var flashAllPath = Path.Combine(packageDir, "flash-all.bat");
            
            var script = GenerateFlashAllScript(firmware, options);
            await File.WriteAllTextAsync(flashAllPath, script);
            
            Logger.WriteLog("Flash-all script created", false);
        }
        
        private string GenerateWindowsFlashScript(string packageFileName, FlashablePackageOptions options)
        {
            return $@"@echo off
echo Samsung Firmware Flash Script
echo Package: {packageFileName}
echo Target: SM-F731B
echo.
echo WARNING: This will flash firmware to your device!
echo Make sure your device is in download mode and connected via USB.
echo.
pause
echo.
echo Flashing firmware...
heimdall flash --BOOT boot.img --SYSTEM system.img --RECOVERY recovery.img --USERDATA userdata.img
if %errorlevel% neq 0 (
    echo Flash failed! Check your device connection and try again.
    pause
    exit /b 1
)
echo.
echo Flash completed successfully!
echo Your device should reboot automatically.
pause
";
        }
        
        private string GenerateUnixFlashScript(string packageFileName, FlashablePackageOptions options)
        {
            return $@"#!/bin/bash
echo ""Samsung Firmware Flash Script""
echo ""Package: {packageFileName}""
echo ""Target: SM-F731B""
echo """"
echo ""WARNING: This will flash firmware to your device!""
echo ""Make sure your device is in download mode and connected via USB.""
echo """"
read -p ""Press Enter to continue or Ctrl+C to cancel...""
echo """"
echo ""Flashing firmware...""
heimdall flash --BOOT boot.img --SYSTEM system.img --RECOVERY recovery.img --USERDATA userdata.img
if [ $? -ne 0 ]; then
    echo ""Flash failed! Check your device connection and try again.""
    exit 1
fi
echo """"
echo ""Flash completed successfully!""
echo ""Your device should reboot automatically.""
";
        }
        
        private string GenerateInstallationInstructions(FlashablePackageOptions options)
        {
            return @"SAMSUNG FIRMWARE INSTALLATION INSTRUCTIONS
==========================================

IMPORTANT WARNINGS:
- This firmware has been ported from SM-S731B to SM-F731B
- Flashing firmware carries risk of device damage (""bricking"")
- Ensure your device is SM-F731B before proceeding
- This will void your warranty
- Back up your current firmware before flashing

PREREQUISITES:
1. Samsung USB drivers installed
2. Heimdall or Odin flashing tool
3. Device in Download Mode (Power + Volume Down + USB connected)
4. Fully charged battery (>50%)

INSTALLATION STEPS:
1. Extract all files from the firmware package
2. Put your SM-F731B device into Download Mode:
   - Power off the device
   - Hold Volume Down + Power buttons
   - Connect USB cable when prompted
   - Press Volume Up to confirm Download Mode
3. Open Heimdall or Odin
4. Load the firmware files:
   - BOOT: boot.img
   - SYSTEM: system.img  
   - RECOVERY: recovery.img
   - USERDATA: userdata.img (optional)
5. Click Start/Flash
6. Wait for completion (do not disconnect!)
7. Device will reboot automatically

TROUBLESHOOTING:
- If flash fails, try different USB port/cable
- Ensure device drivers are properly installed
- Try Odin if Heimdall fails (or vice versa)
- If device won't boot, try flashing again
- For hard brick, seek professional repair

SUPPORT:
This is experimental ported firmware. Use at your own risk.
No warranty or support is provided.

Created by SamFirm Reborn Ultra-Deep Porting System
";
        }
        
        private string GenerateFlashAllScript(ParsedFirmware firmware, FlashablePackageOptions options)
        {
            var script = @"@echo off
echo Samsung SM-F731B Firmware Flash Script
echo ========================================
echo.
echo This script will flash the complete firmware package.
echo Make sure your device is in Download Mode!
echo.
pause
echo.
";
            
            foreach (var partition in firmware.Partitions)
            {
                var partitionName = partition.Type.ToString().ToUpper();
                script += $"echo Flashing {partitionName}...\n";
                script += $"heimdall flash --{partitionName} {partition.Name}\n";
                script += "if %errorlevel% neq 0 goto :error\n\n";
            }
            
            script += @"
echo.
echo All partitions flashed successfully!
echo Device should reboot automatically.
echo.
pause
exit /b 0

:error
echo.
echo ERROR: Flash failed!
echo Check your device connection and try again.
echo.
pause
exit /b 1
";
            
            return script;
        }
    }
    
    public enum CompressionType
    {
        None,
        GZip,
        LZ4
    }
    
    public class FlashablePackageOptions
    {
        public bool IncludeFlashingScripts { get; set; } = true;
        public bool IncludeInstructions { get; set; } = true;
        public bool CreateFlashAllScript { get; set; } = true;
        public string FlashingTool { get; set; } = "Heimdall";
        public bool IncludeUserData { get; set; } = false;
    }
    
    public class PackageInfo
    {
        public string PackageVersion { get; set; }
        public DateTime CreatedAt { get; set; }
        public string OriginalFirmware { get; set; }
        public string SourceModel { get; set; }
        public string TargetModel { get; set; }
        public int PartitionCount { get; set; }
        public long TotalSize { get; set; }
        public string SamFirmVersion { get; set; }
        public string PortingMethod { get; set; }
        public List<PartitionInfo> Partitions { get; set; } = new List<PartitionInfo>();
    }
    
    public class PartitionInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public long Size { get; set; }
    }
}

