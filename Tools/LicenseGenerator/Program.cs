using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PcStatsMonitor.Tools
{
    class LicenseGenerator
    {
        private const string SecretSalt = "BYLD_CORE_PRO_STATS_2026"; // Must match LicenseService.cs

        private static int _sessionCount = 0;

        static void Main(string[] args)
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("========================================");
                Console.WriteLine("    PC-Stats-Monitor LICENSE GENERATOR  ");
                Console.WriteLine("========================================");
                Console.WriteLine($" Session Count: {_sessionCount} Keys Generated");
                Console.WriteLine("========================================");
                Console.WriteLine();

                Console.Write("Enter Target Machine ID (Type 'ANY' for universal license): ");
                string machineIdInput = Console.ReadLine()?.Trim() ?? "ANY";

                Console.WriteLine();
                Console.WriteLine("Choose License Type:");
                Console.WriteLine(" [1] Lifetime (L)");
                Console.WriteLine(" [2] Time Limited (T)");
                Console.Write("Selection: ");
                string choice = Console.ReadLine()?.Trim() ?? "1";

                string typeStr = (choice == "2") ? "T" : "L";
                string expiryDate = "0";

                if (typeStr == "T")
                {
                    while (true)
                    {
                        Console.Write("Enter EXPIRY DATE (Format: ddMMyyyy, e.g., 29032026): ");
                        string inputDate = Console.ReadLine()?.Trim() ?? "";
                        if (inputDate.Length == 8 && long.TryParse(inputDate, out _))
                        {
                            expiryDate = inputDate;
                            break;
                        }
                        Console.WriteLine(" >> ERROR: Date must be exactly 8 digits (ddMMyyyy). Try again.");
                    }
                }

                // Generate Key
                string payload = $"{machineIdInput}|{typeStr}|{expiryDate}";
                string signature = GenerateSignature(payload);
                string fullPayload = $"{payload}|{signature}";
                string finalKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(fullPayload));
                _sessionCount++;

                Console.WriteLine();
                Console.WriteLine("----------------------------------------");
                Console.WriteLine("        GENERATION SUMMARY              ");
                Console.WriteLine("----------------------------------------");
                Console.WriteLine($" SCOPE:   {(machineIdInput == "ANY" ? "Universal (Any PC)" : $"Machine Locked ({machineIdInput})")}");
                Console.WriteLine($" TYPE:    {(typeStr == "L" ? "LIFETIME (Permanent)" : $"LIMITED (Expires {expiryDate})")}");
                Console.WriteLine($" SESS:    Key #{_sessionCount} generated successfully!");
                Console.WriteLine("----------------------------------------");
                Console.WriteLine("        FINAL LICENSE KEY               ");
                Console.WriteLine("----------------------------------------");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(finalKey);
                Console.ResetColor();
                Console.WriteLine("----------------------------------------");
                Console.WriteLine();

                string outPath = "license.key";
                File.WriteAllText(outPath, finalKey);
                Console.WriteLine($">> Copy the key above or use the saved file: {outPath}");
                Console.WriteLine();
                
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("Generate another key? (y/n): ");
                string reload = Console.ReadLine()?.ToLower() ?? "n";
                Console.ResetColor();
                if (reload != "y") break;
            }
            
            Console.WriteLine("Exiting...");
        }

        private static string GenerateSignature(string payload)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload + SecretSalt));
            return Convert.ToBase64String(hash).Substring(0, 16);
        }
    }
}
