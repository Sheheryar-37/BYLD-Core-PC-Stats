using System.Collections.Generic;

namespace PcStatsMonitor.Models
{
    public class WeatherCondition
    {
        public int Id { get; set; }
        public int IconNumber { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ResourceKey { get; set; }

        public static List<WeatherCondition> GetAllConditions()
        {
            var list = new List<WeatherCondition>();
            
            // Adding all 60 as defined in WeatherIconsColored.xaml
            list.Add(new() { Id = 804, IconNumber = 1, Name = "Overcast", ResourceKey = "Cloud_Pure", Description = "Cloudy skies" });
            list.Add(new() { Id = 803, IconNumber = 2, Name = "Broken Clouds", ResourceKey = "Cloud_Double", Description = "Dense clouds" });
            list.Add(new() { Id = 800, IconNumber = 3, Name = "Clear Day", ResourceKey = "Sun_Pure", Description = "Bright sun" });
            list.Add(new() { Id = 800, IconNumber = 4, Name = "Low Sun", ResourceKey = "Sun_Low", Description = "Sunset/Sunrise" });
            list.Add(new() { Id = 800, IconNumber = 5, Name = "Clear Night", ResourceKey = "Moon_Pure", Description = "Clear moon" });
            list.Add(new() { Id = 801, IconNumber = 6, Name = "Partly Cloudy Night", ResourceKey = "Moon_Cloud", Description = "Moon and clouds" });
            list.Add(new() { Id = 801, IconNumber = 7, Name = "Partly Cloudy Day", ResourceKey = "Sun_Cloud", Description = "Sun and clouds" });
            list.Add(new() { Id = 600, IconNumber = 8, Name = "Snowflake", ResourceKey = "Snowflake_Large", Description = "Large snowflake" });
            list.Add(new() { Id = 803, IconNumber = 9, Name = "Cloud Ensemble", ResourceKey = "Cloud_Overlay_L", Description = "Overlapping clouds" });
            list.Add(new() { Id = 804, IconNumber = 10, Name = "Triple Cloud", ResourceKey = "Cloud_Triple", Description = "Heavy overcast" });
            
            list.Add(new() { Id = 500, IconNumber = 11, Name = "Light Rain", ResourceKey = "Cloud_Rain_Drop", Description = "Single raindrops" });
            list.Add(new() { Id = 502, IconNumber = 12, Name = "Heavy Rain", ResourceKey = "Cloud_Rain_Heavy", Description = "Multiple raindrops" });
            list.Add(new() { Id = 521, IconNumber = 13, Name = "Double Cloud Rain", ResourceKey = "DoubleCloud_Rain_Drop", Description = "Rain from dense clouds" });
            list.Add(new() { Id = 503, IconNumber = 14, Name = "Extreme Rain", ResourceKey = "TripleCloud_Rain_Drop", Description = "Heavy system rain" });
            list.Add(new() { Id = 500, IconNumber = 15, Name = "Sun Rain", ResourceKey = "Sun_Cloud_Rain_Drop", Description = "Sun and raindrops" });
            list.Add(new() { Id = 500, IconNumber = 16, Name = "Sun Rain Slashes", ResourceKey = "Sun_Cloud_Rain_Slashes", Description = "Sun and light rain" });
            list.Add(new() { Id = 501, IconNumber = 17, Name = "Sun Mod Rain", ResourceKey = "Sun_Cloud_Rain_Mod", Description = "Sun and moderate rain" });
            list.Add(new() { Id = 300, IconNumber = 18, Name = "Sun Drizzle", ResourceKey = "Sun_Cloud_Drizzle", Description = "Sun and light drizzle" });
            list.Add(new() { Id = 500, IconNumber = 19, Name = "Sun Showers", ResourceKey = "Sun_Cloud_Rain_4", Description = "Scattered sun showers" });
            list.Add(new() { Id = 502, IconNumber = 20, Name = "Double Cloud Heavy Rain", ResourceKey = "DoubleCloud_Rain_Heavy", Description = "Dense rain system" });

            list.Add(new() { Id = 500, IconNumber = 21, Name = "Moon Rain", ResourceKey = "Moon_Cloud_Rain_Drop", Description = "Night rain" });
            list.Add(new() { Id = 501, IconNumber = 22, Name = "Moon Mod Rain", ResourceKey = "Moon_Cloud_Rain_Mod", Description = "Night moderate rain" });
            list.Add(new() { Id = 300, IconNumber = 23, Name = "Moon Drizzle", ResourceKey = "Moon_Cloud_Drizzle", Description = "Night drizzle" });
            list.Add(new() { Id = 500, IconNumber = 24, Name = "Moon Showers", ResourceKey = "Moon_Cloud_Rain_4", Description = "Night scattered showers" });
            list.Add(new() { Id = 502, IconNumber = 25, Name = "Moon Heavy Rain", ResourceKey = "Moon_DoubleCloud_Rain_Heavy", Description = "Night heavy rain" });
            list.Add(new() { Id = 500, IconNumber = 26, Name = "Cloud Rain Slashes", ResourceKey = "DoubleCloud_Rain_Slashes", Description = "Light rain system" });
            list.Add(new() { Id = 501, IconNumber = 27, Name = "Cloud Mod Rain", ResourceKey = "DoubleCloud_Rain_Mod", Description = "Moderate rain system" });
            list.Add(new() { Id = 500, IconNumber = 28, Name = "Drizzle Drop", ResourceKey = "DoubleCloud_Rain_1", Description = "Single drizzle drop" });
            list.Add(new() { Id = 500, IconNumber = 29, Name = "Scattered Drops", ResourceKey = "DoubleCloud_Rain_4", Description = "Light scattered rain" });
            list.Add(new() { Id = 502, IconNumber = 30, Name = "System Heavy Rain", ResourceKey = "DoubleCloud_Rain_Heavy_Alt", Description = "Major rain system" });

            list.Add(new() { Id = 211, IconNumber = 31, Name = "Thunderstorm", ResourceKey = "Cloud_Lightning", Description = "Standard lightning" });
            list.Add(new() { Id = 202, IconNumber = 32, Name = "Double Thunder", ResourceKey = "DoubleCloud_Lightning", Description = "Dense thunder clouds" });
            list.Add(new() { Id = 202, IconNumber = 33, Name = "Triple Thunder", ResourceKey = "TripleCloud_Lightning", Description = "Extreme thunder system" });
            list.Add(new() { Id = 211, IconNumber = 34, Name = "Moon Thunder", ResourceKey = "Moon_DoubleCloud_Lightning", Description = "Night lightning" });
            list.Add(new() { Id = 211, IconNumber = 35, Name = "System Lightning", ResourceKey = "System_Lightning", Description = "Broad lightning system" });
            list.Add(new() { Id = 211, IconNumber = 36, Name = "Sun Lightning", ResourceKey = "Sun_Cloud_Lightning", Description = "Daytime lightning" });
            list.Add(new() { Id = 211, IconNumber = 37, Name = "Sun Double Thunder", ResourceKey = "Sun_DoubleCloud_Lightning", Description = "Strong daytime thunder" });
            list.Add(new() { Id = 211, IconNumber = 38, Name = "Contrast Lightning", ResourceKey = "HighContrast_Lightning", Description = "Vivid lighting strikes" });
            list.Add(new() { Id = 211, IconNumber = 39, Name = "Ensemble Lightning", ResourceKey = "SunEnsemble_Lightning", Description = "Complex storm system" });
            list.Add(new() { Id = 611, IconNumber = 40, Name = "Sleet", ResourceKey = "Sleet_Pure", Description = "Rain and snow mix" });

            list.Add(new() { Id = 611, IconNumber = 41, Name = "Sun Sleet", ResourceKey = "Sun_Cloud_Sleet", Description = "Daytime sleet" });
            list.Add(new() { Id = 611, IconNumber = 42, Name = "Moon Sleet", ResourceKey = "Moon_Cloud_Sleet", Description = "Nighttime sleet" });
            list.Add(new() { Id = 600, IconNumber = 43, Name = "Moon Snow", ResourceKey = "Moon_Cloud_Snow", Description = "Night light snow" });
            list.Add(new() { Id = 602, IconNumber = 44, Name = "Moon Heavy Snow", ResourceKey = "Moon_Double_Snow", Description = "Night heavy snow" });
            list.Add(new() { Id = 600, IconNumber = 45, Name = "Sun Snow", ResourceKey = "Sun_Cloud_Snow", Description = "Daytime light snow" });
            list.Add(new() { Id = 602, IconNumber = 46, Name = "Sun Heavy Snow", ResourceKey = "Sun_Double_Snow", Description = "Daytime heavy snow" });
            list.Add(new() { Id = 600, IconNumber = 47, Name = "Snow Ensemble", ResourceKey = "Cloud_Snow_Ensemble", Description = "Widespread light snow" });
            list.Add(new() { Id = 602, IconNumber = 48, Name = "Extreme Snow", ResourceKey = "MultiCloud_Snow", Description = "Heavy snow system" });
            list.Add(new() { Id = 602, IconNumber = 49, Name = "Contrast Snow", ResourceKey = "HighContrast_Snow", Description = "Vivid snow icons" });
            list.Add(new() { Id = 602, IconNumber = 50, Name = "Cloud Snow", ResourceKey = "Cloud_Snow", Description = "Standard snowy sky" });

            list.Add(new() { Id = 771, IconNumber = 51, Name = "Pure Wind", ResourceKey = "Wind_Pure", Description = "Strong breeze" });
            list.Add(new() { Id = 771, IconNumber = 52, Name = "Cloud Wind", ResourceKey = "Cloud_Wind", Description = "Windy clouds" });
            list.Add(new() { Id = 771, IconNumber = 53, Name = "Double Cloud Wind", ResourceKey = "DoubleCloud_Wind", Description = "Strong gusty system" });
            list.Add(new() { Id = 771, IconNumber = 54, Name = "Moon Wind", ResourceKey = "Moon_Cloud_Wind", Description = "Night breeze" });
            list.Add(new() { Id = 771, IconNumber = 55, Name = "Sun Wind", ResourceKey = "Sun_Cloud_Wind", Description = "Daytime breeze" });
            list.Add(new() { Id = 771, IconNumber = 56, Name = "Ensemble Wind", ResourceKey = "Ensemble_Wind", Description = "Broad wind flow" });
            list.Add(new() { Id = 771, IconNumber = 57, Name = "Wind System", ResourceKey = "Ensemble_Wind_Sys", Description = "Major wind front" });
            list.Add(new() { Id = 500, IconNumber = 58, Name = "Sun Storm", ResourceKey = "Sun_Storm", Description = "Sun, rain and wind" });
            list.Add(new() { Id = 500, IconNumber = 59, Name = "Moon Storm", ResourceKey = "Moon_Storm", Description = "Moon, rain and wind" });
            list.Add(new() { Id = 202, IconNumber = 60, Name = "Extreme System", ResourceKey = "System_Extreme", Description = "Thunder, rain and wind" });

            return list;
        }
    }
}
