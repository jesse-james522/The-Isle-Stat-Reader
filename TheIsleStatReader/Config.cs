using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TheIsleStatReader
{
    internal static class Config
    {
        // ------------------------------------------------------------------
        // User-configurable settings (persisted to settings.json)
        // ------------------------------------------------------------------
        public static string PakDirectory { get; set; } = "";
        public static string AesKey { get; set; } = "";
        public static string MappingsPath { get; set; } = "";

        private static string SettingsFile =>
            Path.Combine(AppContext.BaseDirectory, "settings.json");

        /// <summary>
        /// Loads persisted settings from disk. Silently tolerates a missing or
        /// malformed file — fields are simply left at defaults.
        /// </summary>
        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                var json = File.ReadAllText(SettingsFile);
                var data = JsonSerializer.Deserialize<SettingsData>(json);
                if (data == null) return;
                PakDirectory = data.PakDirectory ?? "";
                AesKey = data.AesKey ?? "";
                MappingsPath = data.MappingsPath ?? "";
            }
            catch
            {
                // Ignore — keep defaults.
            }
        }

        /// <summary>
        /// Writes current settings to disk next to the executable.
        /// </summary>
        public static void Save()
        {
            try
            {
                var data = new SettingsData
                {
                    PakDirectory = PakDirectory,
                    AesKey = AesKey,
                    MappingsPath = MappingsPath
                };
                var json = JsonSerializer.Serialize(data,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch
            {
                // Ignore write failures (read-only folder, etc.)
            }
        }

        /// <summary>
        /// True if the minimum required settings (pak directory + AES key) are
        /// present and the pak directory exists on disk.
        /// </summary>
        public static bool IsValid() =>
            !string.IsNullOrWhiteSpace(PakDirectory) &&
            Directory.Exists(PakDirectory) &&
            !string.IsNullOrWhiteSpace(AesKey);

        private sealed class SettingsData
        {
            public string? PakDirectory { get; set; }
            public string? AesKey { get; set; }
            public string? MappingsPath { get; set; }
        }

        // ------------------------------------------------------------------
        // Fixed values (not user-configurable)
        // ------------------------------------------------------------------

        /// <summary>
        /// Dinosaurs that use a 2/3 thirst correction factor for hunger/thirst calculations.
        /// </summary>
        public static readonly HashSet<string> AquaticDinos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Beipiaosaurus",
            "Deinosuchus"
        };

        /// <summary>
        /// Speed curves: multiply values by this factor to convert from UU/s to km/h.
        /// </summary>
        public const double SpeedConversionFactor = 0.036;

        /// <summary>
        /// Elder growth threshold (75% of growth).
        /// </summary>
        public const double ElderThreshold = 0.75;

        /// <summary>
        /// Subadult growth threshold (50% of growth).
        /// </summary>
        public const double SubadultThreshold = 0.50;

        /// <summary>
        /// Juvenile growth threshold (25% of growth).
        /// </summary>
        public const double JuvenileThreshold = 0.25;

        /// <summary>
        /// Number of interpolated points per curve segment.
        /// </summary>
        public const int PointsPerSegment = 25;
    }
}
