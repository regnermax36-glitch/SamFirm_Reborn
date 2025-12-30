using System;
using System.IO;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.GZip;

namespace SamFirm
{
    public class TarArchiveHandler
    {
        public async Task<ParsedFirmware> ExtractTarArchiveAsync(string tarPath)
        {
            Logger.WriteLog($"Extracting TAR archive: {Path.GetFileName(tarPath)}", false);
            
            var firmware = new ParsedFirmware
            {
                FilePath = tarPath,
                FileName = Path.GetFileName(tarPath)
            };
            
            using (var fileStream = File.OpenRead(tarPath))
            using (var tarStream = new TarInputStream(fileStream))
            {
                TarEntry entry;
                while ((entry = tarStream.GetNextEntry()) != null)
                {
                    if (entry.IsDirectory) continue;
                    
                    var partition = new FirmwarePartition
                    {
                        Name = entry.Name,
                        Size = entry.Size,
                        Offset = tarStream.Position
                    };
                    
                    // Extract partition data
                    var buffer = new byte[entry.Size];
                    await tarStream.ReadAsync(buffer, 0, (int)entry.Size);
                    partition.Data = buffer;
                    
                    // Determine partition type
                    partition.Type = DeterminePartitionType(entry.Name, buffer);
                    
                    firmware.Partitions.Add(partition);
                    Logger.WriteLog($"Extracted partition: {partition.Name} ({partition.Type}) - {partition.Size} bytes", false);
                }
            }
            
            Logger.WriteLog($"TAR extraction completed. Found {firmware.Partitions.Count} partitions", false);
            return firmware;
        }
        
        public async Task CreateTarArchiveAsync(ParsedFirmware firmware, string outputPath)
        {
            Logger.WriteLog($"Creating TAR archive: {Path.GetFileName(outputPath)}", false);
            
            using (var fileStream = File.Create(outputPath))
            using (var tarStream = new TarOutputStream(fileStream))
            {
                foreach (var partition in firmware.Partitions)
                {
                    // Create TAR entry
                    var entry = TarEntry.CreateTarEntry(partition.Name);
                    entry.Size = partition.Data.Length;
                    entry.ModTime = DateTime.Now;
                    
                    // Write entry header
                    tarStream.PutNextEntry(entry);
                    
                    // Write partition data
                    await tarStream.WriteAsync(partition.Data, 0, partition.Data.Length);
                    
                    // Close entry
                    tarStream.CloseEntry();
                    
                    Logger.WriteLog($"Added partition to TAR: {partition.Name} - {partition.Size} bytes", false);
                }
            }
            
            Logger.WriteLog("TAR archive creation completed", false);
        }
        
        public TarOutputStream CreateTarWriter(Stream outputStream)
        {
            return new TarOutputStream(outputStream);
        }
        
        public void WritePartitionToTar(object tarWriter, FirmwarePartition partition)
        {
            if (tarWriter is TarOutputStream tarStream)
            {
                try
                {
                    // Create TAR entry
                    var entry = TarEntry.CreateTarEntry(partition.Name);
                    entry.Size = partition.Data.Length;
                    entry.ModTime = DateTime.Now;
                    
                    // Write entry header
                    tarStream.PutNextEntry(entry);
                    
                    // Write partition data
                    tarStream.Write(partition.Data, 0, partition.Data.Length);
                    
                    // Close entry
                    tarStream.CloseEntry();
                    
                    Logger.WriteLog($"Written partition to TAR stream: {partition.Name}", false);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"Error writing partition to TAR: {ex.Message}", false);
                }
            }
        }
        
        public async Task<bool> ValidateTarArchiveAsync(string tarPath)
        {
            try
            {
                Logger.WriteLog($"Validating TAR archive: {Path.GetFileName(tarPath)}", false);
                
                using (var fileStream = File.OpenRead(tarPath))
                using (var tarStream = new TarInputStream(fileStream))
                {
                    TarEntry entry;
                    int entryCount = 0;
                    
                    while ((entry = tarStream.GetNextEntry()) != null)
                    {
                        if (!entry.IsDirectory)
                        {
                            entryCount++;
                            
                            // Validate entry size
                            if (entry.Size < 0)
                            {
                                Logger.WriteLog($"Invalid entry size for {entry.Name}", false);
                                return false;
                            }
                            
                            // Try to read entry data
                            var buffer = new byte[Math.Min(1024, entry.Size)];
                            var bytesRead = await tarStream.ReadAsync(buffer, 0, buffer.Length);
                            
                            if (bytesRead == 0 && entry.Size > 0)
                            {
                                Logger.WriteLog($"Could not read data for {entry.Name}", false);
                                return false;
                            }
                        }
                    }
                    
                    if (entryCount == 0)
                    {
                        Logger.WriteLog("TAR archive is empty", false);
                        return false;
                    }
                    
                    Logger.WriteLog($"TAR archive validation passed. Found {entryCount} entries", false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"TAR validation error: {ex.Message}", false);
                return false;
            }
        }
        
        public async Task<long> GetTarArchiveSizeAsync(string tarPath)
        {
            try
            {
                var fileInfo = new FileInfo(tarPath);
                return fileInfo.Length;
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Error getting TAR size: {ex.Message}", false);
                return 0;
            }
        }
        
        public async Task<int> GetTarEntryCountAsync(string tarPath)
        {
            try
            {
                int entryCount = 0;
                
                using (var fileStream = File.OpenRead(tarPath))
                using (var tarStream = new TarInputStream(fileStream))
                {
                    TarEntry entry;
                    while ((entry = tarStream.GetNextEntry()) != null)
                    {
                        if (!entry.IsDirectory)
                        {
                            entryCount++;
                        }
                    }
                }
                
                return entryCount;
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Error counting TAR entries: {ex.Message}", false);
                return 0;
            }
        }
        
        public async Task ExtractSpecificPartitionAsync(string tarPath, string partitionName, string outputPath)
        {
            Logger.WriteLog($"Extracting partition {partitionName} from TAR archive", false);
            
            using (var fileStream = File.OpenRead(tarPath))
            using (var tarStream = new TarInputStream(fileStream))
            {
                TarEntry entry;
                while ((entry = tarStream.GetNextEntry()) != null)
                {
                    if (entry.IsDirectory) continue;
                    
                    if (entry.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Found the partition, extract it
                        using (var outputStream = File.Create(outputPath))
                        {
                            var buffer = new byte[32 * 1024]; // 32KB buffer
                            int bytesRead;
                            
                            while ((bytesRead = await tarStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await outputStream.WriteAsync(buffer, 0, bytesRead);
                            }
                        }
                        
                        Logger.WriteLog($"Partition {partitionName} extracted to {outputPath}", false);
                        return;
                    }
                }
                
                Logger.WriteLog($"Partition {partitionName} not found in TAR archive", false);
            }
        }
        
        public async Task<string[]> ListTarContentsAsync(string tarPath)
        {
            var contents = new System.Collections.Generic.List<string>();
            
            try
            {
                using (var fileStream = File.OpenRead(tarPath))
                using (var tarStream = new TarInputStream(fileStream))
                {
                    TarEntry entry;
                    while ((entry = tarStream.GetNextEntry()) != null)
                    {
                        if (!entry.IsDirectory)
                        {
                            contents.Add($"{entry.Name} ({entry.Size} bytes)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Error listing TAR contents: {ex.Message}", false);
            }
            
            return contents.ToArray();
        }
        
        public async Task<bool> AddPartitionToTarAsync(string tarPath, FirmwarePartition partition)
        {
            try
            {
                // Create a temporary file for the new TAR
                var tempTarPath = tarPath + ".tmp";
                
                using (var outputStream = File.Create(tempTarPath))
                using (var tarOutputStream = new TarOutputStream(outputStream))
                {
                    // Copy existing entries
                    using (var inputStream = File.OpenRead(tarPath))
                    using (var tarInputStream = new TarInputStream(inputStream))
                    {
                        TarEntry entry;
                        while ((entry = tarInputStream.GetNextEntry()) != null)
                        {
                            if (!entry.IsDirectory)
                            {
                                // Copy existing entry
                                tarOutputStream.PutNextEntry(entry);
                                
                                var buffer = new byte[32 * 1024];
                                int bytesRead;
                                while ((bytesRead = await tarInputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await tarOutputStream.WriteAsync(buffer, 0, bytesRead);
                                }
                                
                                tarOutputStream.CloseEntry();
                            }
                        }
                    }
                    
                    // Add new partition
                    var newEntry = TarEntry.CreateTarEntry(partition.Name);
                    newEntry.Size = partition.Data.Length;
                    newEntry.ModTime = DateTime.Now;
                    
                    tarOutputStream.PutNextEntry(newEntry);
                    await tarOutputStream.WriteAsync(partition.Data, 0, partition.Data.Length);
                    tarOutputStream.CloseEntry();
                }
                
                // Replace original file with updated one
                File.Delete(tarPath);
                File.Move(tempTarPath, tarPath);
                
                Logger.WriteLog($"Added partition {partition.Name} to TAR archive", false);
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Error adding partition to TAR: {ex.Message}", false);
                return false;
            }
        }
        
        public async Task<bool> RemovePartitionFromTarAsync(string tarPath, string partitionName)
        {
            try
            {
                // Create a temporary file for the new TAR
                var tempTarPath = tarPath + ".tmp";
                bool partitionFound = false;
                
                using (var outputStream = File.Create(tempTarPath))
                using (var tarOutputStream = new TarOutputStream(outputStream))
                {
                    using (var inputStream = File.OpenRead(tarPath))
                    using (var tarInputStream = new TarInputStream(inputStream))
                    {
                        TarEntry entry;
                        while ((entry = tarInputStream.GetNextEntry()) != null)
                        {
                            if (!entry.IsDirectory)
                            {
                                // Skip the partition to be removed
                                if (entry.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase))
                                {
                                    partitionFound = true;
                                    continue;
                                }
                                
                                // Copy other entries
                                tarOutputStream.PutNextEntry(entry);
                                
                                var buffer = new byte[32 * 1024];
                                int bytesRead;
                                while ((bytesRead = await tarInputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await tarOutputStream.WriteAsync(buffer, 0, bytesRead);
                                }
                                
                                tarOutputStream.CloseEntry();
                            }
                        }
                    }
                }
                
                if (partitionFound)
                {
                    // Replace original file with updated one
                    File.Delete(tarPath);
                    File.Move(tempTarPath, tarPath);
                    
                    Logger.WriteLog($"Removed partition {partitionName} from TAR archive", false);
                    return true;
                }
                else
                {
                    // Clean up temp file
                    File.Delete(tempTarPath);
                    Logger.WriteLog($"Partition {partitionName} not found in TAR archive", false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"Error removing partition from TAR: {ex.Message}", false);
                return false;
            }
        }
        
        private PartitionType DeterminePartitionType(string fileName, byte[] data)
        {
            var name = fileName.ToLowerInvariant();
            
            if (name.Contains("boot") || name.Contains("recovery"))
                return PartitionType.Boot;
            else if (name.Contains("system") || name.Contains("super"))
                return PartitionType.System;
            else if (name.Contains("vendor"))
                return PartitionType.Vendor;
            else if (name.Contains("userdata") || name.Contains("data"))
                return PartitionType.UserData;
            else if (name.Contains("modem") || name.Contains("radio"))
                return PartitionType.Modem;
            else if (name.Contains("bootloader") || name.Contains("sbl"))
                return PartitionType.Bootloader;
            else if (name.Contains("kernel"))
                return PartitionType.Kernel;
            else if (name.Contains("dtb") || name.Contains("dtbo"))
                return PartitionType.DeviceTree;
            else if (name.Contains("vbmeta"))
                return PartitionType.VBMeta;
            else
                return PartitionType.Unknown;
        }
    }
}

