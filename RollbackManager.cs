using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SamFirm
{
    public class RollbackManager
    {
        private readonly string backupDirectory;
        private readonly List<BackupEntry> backupEntries;
        
        public RollbackManager()
        {
            backupDirectory = Path.Combine(Path.GetTempPath(), "SamFirm_Backups");
            backupEntries = new List<BackupEntry>();
            
            // Ensure backup directory exists
            Directory.CreateDirectory(backupDirectory);
        }
        
        public async Task<string> CreateBackupAsync(ParsedFirmware firmware, string description = "")
        {
            Logger.WriteLog("Creating firmware backup for rollback protection...", false);
            
            var backupId = Guid.NewGuid().ToString("N")[..8];
            var backupPath = Path.Combine(backupDirectory, $"backup_{backupId}");
            Directory.CreateDirectory(backupPath);
            
            var backupEntry = new BackupEntry
            {
                BackupId = backupId,
                BackupPath = backupPath,
                CreatedAt = DateTime.Now,
                Description = description,
                OriginalFirmwarePath = firmware.FilePath,
                PartitionBackups = new List<PartitionBackup>()
            };
            
            try
            {
                // Create metadata file
                await CreateBackupMetadataAsync(backupEntry, firmware);
                
                // Backup each partition
                foreach (var partition in firmware.Partitions)
                {
                    await BackupPartitionAsync(partition, backupEntry);
                }
                
                // Create backup manifest
                await CreateBackupManifestAsync(backupEntry);
                
                backupEntries.Add(backupEntry);
                
                Logger.WriteLog($"Backup created successfully: {backupId}", false);
                Logger.WriteLog($"Backup location: {backupPath}", false);
                
                return backupId;
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Backup creation failed: {ex.Message}", false);
                
                // Cleanup failed backup
                if (Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, true);
                }
                
                throw;
            }
        }
        
        public async Task<bool> RestoreFromBackupAsync(string backupId, string outputPath)
        {
            Logger.WriteLog($"Restoring firmware from backup: {backupId}", false);
            
            var backupEntry = backupEntries.Find(b => b.BackupId == backupId);
            if (backupEntry == null)
            {
                // Try to load backup from disk
                backupEntry = await LoadBackupEntryAsync(backupId);
                if (backupEntry == null)
                {
                    Logger.WriteLog($"Backup {backupId} not found", false);
                    return false;
                }
            }
            
            try
            {
                // Validate backup integrity
                if (!await ValidateBackupIntegrityAsync(backupEntry))
                {
                    Logger.WriteLog("Backup integrity validation failed", false);
                    return false;
                }
                
                // Restore firmware
                var restoredFirmware = await RestoreFirmwareFromBackupAsync(backupEntry);
                
                // Create TAR archive from restored firmware
                var tarHandler = new TarArchiveHandler();
                await tarHandler.CreateTarArchiveAsync(restoredFirmware, outputPath);
                
                Logger.WriteLog($"Firmware restored successfully to: {outputPath}", false);
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Restore failed: {ex.Message}", false);
                return false;
            }
        }
        
        public async Task<List<BackupInfo>> ListBackupsAsync()
        {
            var backupInfos = new List<BackupInfo>();
            
            // Load backups from disk
            if (Directory.Exists(backupDirectory))
            {
                var backupDirs = Directory.GetDirectories(backupDirectory, "backup_*");
                foreach (var backupDir in backupDirs)
                {
                    var backupId = Path.GetFileName(backupDir).Replace("backup_", "");
                    var backupEntry = await LoadBackupEntryAsync(backupId);
                    
                    if (backupEntry != null)
                    {
                        backupInfos.Add(new BackupInfo
                        {
                            BackupId = backupEntry.BackupId,
                            Description = backupEntry.Description,
                            CreatedAt = backupEntry.CreatedAt,
                            OriginalFirmwarePath = backupEntry.OriginalFirmwarePath,
                            BackupSize = await GetBackupSizeAsync(backupEntry.BackupPath),
                            PartitionCount = backupEntry.PartitionBackups.Count
                        });
                    }
                }
            }
            
            return backupInfos;
        }
        
        public async Task<bool> DeleteBackupAsync(string backupId)
        {
            Logger.WriteLog($"Deleting backup: {backupId}", false);
            
            try
            {
                var backupPath = Path.Combine(backupDirectory, $"backup_{backupId}");
                if (Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, true);
                    
                    // Remove from memory list
                    backupEntries.RemoveAll(b => b.BackupId == backupId);
                    
                    Logger.WriteLog($"Backup {backupId} deleted successfully", false);
                    return true;
                }
                else
                {
                    Logger.WriteLog($"Backup {backupId} not found", false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Failed to delete backup {backupId}: {ex.Message}", false);
                return false;
            }
        }
        
        public async Task<bool> ValidateBackupAsync(string backupId)
        {
            var backupEntry = await LoadBackupEntryAsync(backupId);
            if (backupEntry == null)
            {
                return false;
            }
            
            return await ValidateBackupIntegrityAsync(backupEntry);
        }
        
        public async Task CleanupOldBackupsAsync(int maxBackups = 5, int maxAgeDays = 30)
        {
            Logger.WriteLog("Cleaning up old backups...", false);
            
            var backups = await ListBackupsAsync();
            var cutoffDate = DateTime.Now.AddDays(-maxAgeDays);
            
            // Sort by creation date (newest first)
            backups.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            
            int deletedCount = 0;
            
            for (int i = 0; i < backups.Count; i++)
            {
                var backup = backups[i];
                
                // Delete if too old or exceeds max count
                if (backup.CreatedAt < cutoffDate || i >= maxBackups)
                {
                    if (await DeleteBackupAsync(backup.BackupId))
                    {
                        deletedCount++;
                    }
                }
            }
            
            Logger.WriteLog($"Cleaned up {deletedCount} old backups", false);
        }
        
        private async Task CreateBackupMetadataAsync(BackupEntry backupEntry, ParsedFirmware firmware)
        {
            var metadata = new BackupMetadata
            {
                BackupId = backupEntry.BackupId,
                CreatedAt = backupEntry.CreatedAt,
                Description = backupEntry.Description,
                OriginalFirmwarePath = firmware.FilePath,
                OriginalFileName = firmware.FileName,
                PartitionCount = firmware.Partitions.Count,
                TotalSize = firmware.Partitions.Sum(p => p.Size),
                SamFirmVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            };
            
            var metadataPath = Path.Combine(backupEntry.BackupPath, "metadata.json");
            var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataPath, json);
        }
        
        private async Task BackupPartitionAsync(FirmwarePartition partition, BackupEntry backupEntry)
        {
            var partitionBackupPath = Path.Combine(backupEntry.BackupPath, $"{partition.Name}.backup");
            
            // Write partition data
            await File.WriteAllBytesAsync(partitionBackupPath, partition.Data);
            
            // Calculate checksum
            var checksum = CalculateChecksum(partition.Data);
            
            var partitionBackup = new PartitionBackup
            {
                PartitionName = partition.Name,
                PartitionType = partition.Type,
                BackupPath = partitionBackupPath,
                OriginalSize = partition.Size,
                Checksum = checksum
            };
            
            backupEntry.PartitionBackups.Add(partitionBackup);
            
            Logger.WriteLog($"Backed up partition: {partition.Name} ({partition.Size} bytes)", false);
        }
        
        private async Task CreateBackupManifestAsync(BackupEntry backupEntry)
        {
            var manifestPath = Path.Combine(backupEntry.BackupPath, "manifest.json");
            var json = System.Text.Json.JsonSerializer.Serialize(backupEntry, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, json);
        }
        
        private async Task<BackupEntry> LoadBackupEntryAsync(string backupId)
        {
            var backupPath = Path.Combine(backupDirectory, $"backup_{backupId}");
            var manifestPath = Path.Combine(backupPath, "manifest.json");
            
            if (!File.Exists(manifestPath))
            {
                return null;
            }
            
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath);
                return System.Text.Json.JsonSerializer.Deserialize<BackupEntry>(json);
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Failed to load backup manifest: {ex.Message}", false);
                return null;
            }
        }
        
        private async Task<bool> ValidateBackupIntegrityAsync(BackupEntry backupEntry)
        {
            Logger.WriteLog($"Validating backup integrity: {backupEntry.BackupId}", false);
            
            // Check if backup directory exists
            if (!Directory.Exists(backupEntry.BackupPath))
            {
                Logger.WriteLog("Backup directory not found", false);
                return false;
            }
            
            // Validate each partition backup
            foreach (var partitionBackup in backupEntry.PartitionBackups)
            {
                if (!File.Exists(partitionBackup.BackupPath))
                {
                    Logger.WriteLog($"Partition backup file not found: {partitionBackup.PartitionName}", false);
                    return false;
                }
                
                // Validate file size
                var fileInfo = new FileInfo(partitionBackup.BackupPath);
                if (fileInfo.Length != partitionBackup.OriginalSize)
                {
                    Logger.WriteLog($"Partition backup size mismatch: {partitionBackup.PartitionName}", false);
                    return false;
                }
                
                // Validate checksum
                var data = await File.ReadAllBytesAsync(partitionBackup.BackupPath);
                var checksum = CalculateChecksum(data);
                if (checksum != partitionBackup.Checksum)
                {
                    Logger.WriteLog($"Partition backup checksum mismatch: {partitionBackup.PartitionName}", false);
                    return false;
                }
            }
            
            Logger.WriteLog("Backup integrity validation passed", false);
            return true;
        }
        
        private async Task<ParsedFirmware> RestoreFirmwareFromBackupAsync(BackupEntry backupEntry)
        {
            var firmware = new ParsedFirmware
            {
                FilePath = backupEntry.OriginalFirmwarePath,
                FileName = Path.GetFileName(backupEntry.OriginalFirmwarePath),
                Partitions = new List<FirmwarePartition>()
            };
            
            // Restore each partition
            foreach (var partitionBackup in backupEntry.PartitionBackups)
            {
                var data = await File.ReadAllBytesAsync(partitionBackup.BackupPath);
                
                var partition = new FirmwarePartition
                {
                    Name = partitionBackup.PartitionName,
                    Type = partitionBackup.PartitionType,
                    Data = data,
                    Size = data.Length
                };
                
                firmware.Partitions.Add(partition);
                
                Logger.WriteLog($"Restored partition: {partition.Name} ({partition.Size} bytes)", false);
            }
            
            return firmware;
        }
        
        private async Task<long> GetBackupSizeAsync(string backupPath)
        {
            long totalSize = 0;
            
            if (Directory.Exists(backupPath))
            {
                var files = Directory.GetFiles(backupPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    totalSize += fileInfo.Length;
                }
            }
            
            return totalSize;
        }
        
        private string CalculateChecksum(byte[] data)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha256.ComputeHash(data);
                return Convert.ToBase64String(hash);
            }
        }
    }
    
    public class BackupEntry
    {
        public string BackupId { get; set; }
        public string BackupPath { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Description { get; set; }
        public string OriginalFirmwarePath { get; set; }
        public List<PartitionBackup> PartitionBackups { get; set; } = new List<PartitionBackup>();
    }
    
    public class PartitionBackup
    {
        public string PartitionName { get; set; }
        public PartitionType PartitionType { get; set; }
        public string BackupPath { get; set; }
        public long OriginalSize { get; set; }
        public string Checksum { get; set; }
    }
    
    public class BackupMetadata
    {
        public string BackupId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Description { get; set; }
        public string OriginalFirmwarePath { get; set; }
        public string OriginalFileName { get; set; }
        public int PartitionCount { get; set; }
        public long TotalSize { get; set; }
        public string SamFirmVersion { get; set; }
    }
    
    public class BackupInfo
    {
        public string BackupId { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public string OriginalFirmwarePath { get; set; }
        public long BackupSize { get; set; }
        public int PartitionCount { get; set; }
    }
}

