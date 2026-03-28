using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PcStatsMonitor.Tools
{
    class LicenseGenerator
    {
        private const string SecretSalt = "BYLD_CORE_PRO_STATS_2026"; // Must match LicenseService.cs

        static void Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("    PC-Stats-Monitor LICENSE GENERATOR  ");
            Console.WriteLine("========================================");
            Console.WriteLine();

            Console.Write("Enter Target Machine ID (Type 'ANY' for universal license): ");
            string machineId = Console.ReadLine()?.Trim() ?? "ANY";

            Console.WriteLine();
            Console.WriteLine("Choose License Type:");
            Console.WriteLine("1. Lifetime (L)");
            Console.WriteLine("2. Time Limited (T)");
            Console.Write("Selection [1/2]: ");
            string choice = Console.ReadLine();

            string typeStr = (choice == "2") ? "T" : "L";
            string expiryDate = "0";

            if (typeStr == "T")
            {
                Console.Write("Enter Expiry Date (Format: ddMMyyyy, e.g., 29032026): ");
                string inputDate = Console.ReadLine()?.Trim() ?? "";
                if (inputDate.Length != 8 || !long.TryParse(inputDate, out _))
                {
                    Console.WriteLine("Invalid date format. Aborting.");
                    return;
                }
                expiryDate = inputDate;
            }

            // Generate Key
            string payload = $"{machineId}|{typeStr}|{expiryDate}";
            string signature = GenerateSignature(payload);
            string fullPayload = $"{payload}|{signature}";
            string finalKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(fullPayload));

            Console.WriteLine();
            Console.WriteLine("----------------------------------------");
            Console.WriteLine("GENERATED LICENSE KEY:");
            Console.WriteLine("----------------------------------------");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(finalKey);
            Console.ResetColor();
            Console.WriteLine("----------------------------------------");
            Console.WriteLine();

            string outPath = "license.key";
            File.WriteAllText(outPath, finalKey);
            Console.WriteLine($"Key also saved to: {Path.GetFullPath(outPath)}");
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static string GenerateSignature(string payload)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload + SecretSalt));
            return Convert.ToBase64String(hash).Substring(0, 16);
        }
    }
}
