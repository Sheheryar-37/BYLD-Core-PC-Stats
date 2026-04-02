using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace PcStatsMonitor.Services
{
    public enum LicenseType { Lifetime, Limited }

    public class LicenseService
    {
        private const string LicenseFile = "license.key";
        private const string PublicKeyXml = "<RSAKeyValue><Modulus>z3kSxekPCZRQE83ZGsRY5ozEqmaiHaqZslppeBuw+f2Tj+x4sjgVQRp1iQCFME4k074FD0eOAgjcka2eUCvjYs0lmF41RyHlbqhQB0aJQ1NpC0v3+stqphSF00l5edy34t9EnMLHzrF1QoyFYCI76wtJR7yoG1V+rPWPnjFFqOhaQZwjKfPm3/7sv1NwGq37n3xP0CLzxcyqJwnOriekH31k8cfNywGqa4cX4i1aQ5W95bIVTM+AmqtNf7QdLeCfIQKr0MZOhI3Y+7u+lC5JZo6ds1cESlOrjgCUW0W7Eg/BZyHQ4tL8kScmJ92dpmgUPdlAPuFwJgFlvet7+L68mQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public bool CheckLicense(out string errorMessage)
        {
            errorMessage = "";
            
            if (!File.Exists(LicenseFile))
            {
                errorMessage = "License key not found. Please register your software through Settings.";
                return false;
            }

            try
            {
                string key = File.ReadAllText(LicenseFile).Trim();
                return ValidateKey(key, out errorMessage);
            }
            catch (Exception)
            {
                errorMessage = "Failed to read license file.";
                return false;
            }
        }

        public bool ValidateKey(string key, out string errorMessage)
        {
            errorMessage = "";
            if (string.IsNullOrWhiteSpace(key))
            {
                errorMessage = "The provided license key string is empty.";
                return false;
            }

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
                string signatureBase64 = parts[3];

                // 1. Verify RSA Signature using Hardcoded Public Key
                string payload = $"{machineId}|{typeStr}|{expiryStr}";
                byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
                byte[] signatureBytes = Convert.FromBase64String(signatureBase64);

                using var rsa = new RSACryptoServiceProvider(2048);
                rsa.FromXmlString(PublicKeyXml);

                bool isSignatureValid = rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                if (!isSignatureValid)
                {
                    errorMessage = "License key verification failed (Cryptographic Signature is Invalid).";
                    return false;
                }

                // 2. Verify Machine ID
                string currentMachineId = GetMachineId();
                if (!string.Equals(machineId, "ANY", StringComparison.OrdinalIgnoreCase) && machineId != currentMachineId)
                {
                    errorMessage = "This license key is not registered for this computer's motherboard.";
                    return false;
                }

                // 3. Verify Expiry
                if (typeStr == "T") // Time Limited
                {
                    if (DateTime.TryParseExact(expiryStr, "ddMMyyyy", null, System.Globalization.DateTimeStyles.None, out DateTime expiryDate))
                    {
                        if (DateTime.Now > expiryDate)
                        {
                            errorMessage = $"License expired on {expiryDate:dd MMM yyyy}.";
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
                errorMessage = "Corrupt or fundamentally invalid license key.";
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
    }
}
