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
        private const string LicenseFileName = "license.key";
        private const string PublicKeyXml = "<RSAKeyValue><Modulus>z3kSxekPCZRQE83ZGsRY5ozEqmaiHaqZslppeBuw+f2Tj+x4sjgVQRp1iQCFME4k074FD0eOAgjcka2eUCvjYs0lmF41RyHlbqhQB0aJQ1NpC0v3+stqphSF00l5edy34t9EnMLHzrF1QoyFYCI76wtJR7yoG1V+rPWPnjFFqOhaQZwjKfPm3/7sv1NwGq37n3xP0CLzxcyqJwnOriekH31k8cfNywGqa4cX4i1aQ5W95bIVTM+AmqtNf7QdLeCfIQKr0MZOhI3Y+7u+lC5JZo6ds1cESlOrjgCUW0W7Eg/BZyHQ4tL8kScmJ92dpmgUPdlAPuFwJgFlvet7+L68mQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        /// <summary>
        /// Durable, absolute path to the license file. Stored under ProgramData so it
        /// survives app reinstalls, upgrades and folder cleanups — the previous relative
        /// "license.key" resolved to the install folder, which is wiped on uninstall, so a
        /// valid licence was lost on every reinstall. A license.key left next to the exe by
        /// an older build is read once and migrated forward.
        /// </summary>
        public static string LicenseFilePath { get; } = ResolveLicensePath();

        private static string ResolveLicensePath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BYLD Core");
            string durable = Path.Combine(dir, LicenseFileName);
            if (File.Exists(durable)) return durable;

            string legacy = Path.Combine(AppContext.BaseDirectory, LicenseFileName);
            if (File.Exists(legacy) && TryMigrate(legacy, durable, dir)) return durable;
            return File.Exists(legacy) ? legacy : durable;
        }

        private static bool TryMigrate(string legacy, string durable, string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                File.Copy(legacy, durable, overwrite: false);
                return true;
            }
            catch
            {
                return false; // ProgramData not writable — keep using the legacy path
            }
        }

        /// <summary>Persists a raw license key string to the durable license path.</summary>
        public static void SaveLicenseKey(string rawKey)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LicenseFilePath)!);
            File.WriteAllText(LicenseFilePath, rawKey);
        }

        public bool CheckLicense(out string errorMessage)
        {
            errorMessage = "";

            if (!File.Exists(LicenseFilePath))
            {
                errorMessage = "License key not found. Please register your software through Settings.";
                return false;
            }

            try
            {
                string key = File.ReadAllText(LicenseFilePath).Trim();
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
