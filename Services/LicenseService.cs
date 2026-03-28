using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;
using PcStatsMonitor.Controls;

namespace PcStatsMonitor.Services
{
    public enum LicenseType { Lifetime, Limited }

    public class LicenseService
    {
        private const string LicenseFile = "license.key";
        private const string SecretSalt = "BYLD_CORE_PRO_STATS_2026"; // Change for production
        private readonly ILogger<LicenseService>? _logger;

        public LicenseService(ILogger<LicenseService>? logger = null)
        {
            _logger = logger;
        }

        public bool CheckLicense(out string errorMessage)
        {
            errorMessage = "";
            
            if (!File.Exists(LicenseFile))
            {
                errorMessage = "License key not found. Please contact support to obtain a valid license.";
                return false;
            }

            try
            {
                string key = File.ReadAllText(LicenseFile).Trim();
                return ValidateKey(key, out errorMessage);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading license file.");
                errorMessage = "Failed to read license file.";
                return false;
            }
        }

        public bool ValidateKey(string key, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                // Key format: Base64(MachineId|Type|Expiry|Signature)
                byte[] data = Convert.FromBase64String(key);
                string decoded = Encoding.UTF8.GetString(data);
                string[] parts = decoded.Split('|');

                if (parts.Length != 4)
                {
                    errorMessage = "Invalid license key format.";
                    return false;
                }

                string machineId = parts[0];
                string typeStr = parts[1];
                string expiryStr = parts[2];
                string signature = parts[3];

                // 1. Verify Signature
                string payload = $"{machineId}|{typeStr}|{expiryStr}";
                string expectedSignature = GenerateSignature(payload);

                if (signature != expectedSignature)
                {
                    errorMessage = "License key verification failed (Invalid Signature).";
                    return false;
                }

                // 2. Verify Machine ID
                string currentMachineId = GetMachineId();
                if (machineId != "ANY" && machineId != currentMachineId)
                {
                    errorMessage = "This license key is not registered for this computer.";
                    return false;
                }

                // 3. Verify Expiry
                if (typeStr == "T") // Time Limited
                {
                    if (DateTime.TryParseExact(expiryStr, "ddMMyyyy", null, System.Globalization.DateTimeStyles.None, out DateTime expiryDate))
                    {
                        if (DateTime.Now > expiryDate)
                        {
                            errorMessage = $"License expired on {expiryDate:dd MMM yyyy}. Please renew your subscription.";
                            return false;
                        }
                    }
                    else
                    {
                        errorMessage = "Invalid expiry date in license key.";
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                errorMessage = "Corrupt or invalid license key.";
                return false;
            }
        }

        public string GetMachineId()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return obj["SerialNumber"]?.ToString()?.Trim() ?? "UNKNOWN";
                }
            }
            catch { }
            return "UNKNOWN";
        }

        public string GenerateSignature(string payload)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload + SecretSalt));
            return Convert.ToBase64String(hash).Substring(0, 16); // 16 chars is enough for this
        }

        public string CreateKey(string machineId, LicenseType type, string expiryDate = "0")
        {
            string typeStr = type == LicenseType.Lifetime ? "L" : "T";
            string payload = $"{machineId}|{typeStr}|{expiryDate}";
            string signature = GenerateSignature(payload);
            string fullPayload = $"{payload}|{signature}";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(fullPayload));
        }
    }
}
