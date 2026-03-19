using System.Collections.Generic;

namespace PcStatsMonitor.Models
{
    public class WeatherCondition
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public static List<WeatherCondition> GetAllConditions()
        {
            return new List<WeatherCondition>
            {
                new() { Id = 800, Name = "Clear", Description = "Clear sky" },
                new() { Id = 801, Name = "Mostly Clear", Description = "Few clouds: 11-25%" },
                new() { Id = 802, Name = "Partly Cloudy", Description = "Scattered clouds: 25-50%" },
                new() { Id = 803, Name = "Broken Clouds", Description = "Broken clouds: 51-84%" },
                new() { Id = 804, Name = "Overcast", Description = "Overcast clouds: 85-100%" },
                
                // Thunderstorms
                new() { Id = 211, Name = "Thunderstorm", Description = "Standard thunderstorm" },
                new() { Id = 202, Name = "Heavy Thunderstorm", Description = "Thunderstorm with heavy rain" },
                
                // Drizzle
                new() { Id = 300, Name = "Light Drizzle", Description = "Light intensity drizzle" },
                
                // Rain
                new() { Id = 500, Name = "Light Rain", Description = "Light rain" },
                new() { Id = 502, Name = "Heavy Rain", Description = "Heavy intensity rain" },
                new() { Id = 511, Name = "Freezing Rain", Description = "Freezing rain" },
                
                // Snow
                new() { Id = 600, Name = "Light Snow", Description = "Light snow" },
                new() { Id = 602, Name = "Heavy Snow", Description = "Heavy snow" },
                new() { Id = 611, Name = "Sleet", Description = "Sleeting rain" },
                
                // Atmosphere
                new() { Id = 701, Name = "Mist", Description = "Misty atmosphere" },
                new() { Id = 721, Name = "Haze", Description = "Hazy conditions" },
                new() { Id = 741, Name = "Fog", Description = "Foggy conditions" },
                new() { Id = 771, Name = "Squall", Description = "Sudden strong winds" },
                new() { Id = 781, Name = "Tornado", Description = "Tornado conditions" }
            };
        }
    }
}
