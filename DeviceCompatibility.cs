using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SamFirm
{
    public class DeviceCompatibility
    {
        private readonly Dictionary<string, DeviceInfo> deviceDatabase;
        
        public DeviceCompatibility()
        {
            deviceDatabase = InitializeDeviceDatabase();
        }
        
        private Dictionary<string, DeviceInfo> InitializeDeviceDatabase()
        {
            return new Dictionary<string, DeviceInfo>
            {
                ["SM-S731B"] = new DeviceInfo
                {
                    Model = "SM-S731B",
                    Codename = "r8s",
                    SoC = "Exynos 2200",
                    Architecture = "arm64",
                    BootloaderVersion = "S731BXXU1AVL4",
                    PartitionLayout = "GPT",
                    SecurityLevel = 3,
                    KnoxVersion = "3.8",
                    AndroidVersion = "12",
                    OneUIVersion = "4.1",
                    CompatibleModels = new List<string> { "SM-F731B", "SM-S731U", "SM-S731N" }
                },
                ["SM-F731B"] = new DeviceInfo
                {
                    Model = "SM-F731B",
                    Codename = "q2q",
                    SoC = "Snapdragon 8 Gen 1",
                    Architecture = "arm64",
                    BootloaderVersion = "F731BXXU1AVL4",
                    PartitionLayout = "GPT",
                    SecurityLevel = 3,
                    KnoxVersion = "3.8",
                    AndroidVersion = "12",
                    OneUIVersion = "4.1",
                    CompatibleModels = new List<string> { "SM-S731B", "SM-F731U", "SM-F731N" }
                }
            };
        }
        
        public async Task<bool> ValidateCompatibilityAsync(string sourceModel, string targetModel)
        {
            Logger.WriteLog($"Validating compatibility between {sourceModel} and {targetModel}...", false);
            
            if (!deviceDatabase.ContainsKey(sourceModel) || !deviceDatabase.ContainsKey(targetModel))
            {
                Logger.WriteLog("Device model not found in compatibility database", false);
                return false;
            }
            
            var sourceDevice = deviceDatabase[sourceModel];
            var targetDevice = deviceDatabase[targetModel];
            
            // Check architecture compatibility
            if (sourceDevice.Architecture != targetDevice.Architecture)
            {
                Logger.WriteLog("Architecture mismatch - porting not supported", false);
                return false;
            }
            
            // Check if models are in compatibility list
            if (!sourceDevice.CompatibleModels.Contains(targetModel))
            {
                Logger.WriteLog("Models are not in compatibility matrix", false);
                return false;
            }
            
            // Check security level compatibility
            if (Math.Abs(sourceDevice.SecurityLevel - targetDevice.SecurityLevel) > 1)
            {
                Logger.WriteLog("Security level difference too high for safe porting", false);
                return false;
            }
            
            // Check Android version compatibility
            if (sourceDevice.AndroidVersion != targetDevice.AndroidVersion)
            {
                Logger.WriteLog("Warning: Android version mismatch detected", false);
            }
            
            Logger.WriteLog("Compatibility validation passed", false);
            return true;
        }
        
        public DeviceInfo GetDeviceInfo(string model)
        {
            return deviceDatabase.ContainsKey(model) ? deviceDatabase[model] : null;
        }
        
        public List<string> GetCompatibleModels(string model)
        {
            var device = GetDeviceInfo(model);
            return device?.CompatibleModels ?? new List<string>();
        }
    }
    
    public class DeviceInfo
    {
        public string Model { get; set; }
        public string Codename { get; set; }
        public string SoC { get; set; }
        public string Architecture { get; set; }
        public string BootloaderVersion { get; set; }
        public string PartitionLayout { get; set; }
        public int SecurityLevel { get; set; }
        public string KnoxVersion { get; set; }
        public string AndroidVersion { get; set; }
        public string OneUIVersion { get; set; }
        public List<string> CompatibleModels { get; set; } = new List<string>();
    }
}

