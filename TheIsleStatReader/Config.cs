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

        /// <summary>
        /// Per-curve channel-swap overrides. Key = "{DinoName}|{CurveSuffix}",
        /// value = true means the auto-detected prime/frail assignment is flipped.
        /// Persisted alongside the other settings.
        /// </summary>
        public static Dictionary<string, bool> ChannelSwaps { get; private set; } =
            new(StringComparer.OrdinalIgnoreCase);

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
                PakDirectory  = data.PakDirectory ?? "";
                AesKey        = data.AesKey ?? "";
                MappingsPath  = data.MappingsPath ?? "";
                ChannelSwaps  = data.ChannelSwaps != null
                    ? new Dictionary<string, bool>(data.ChannelSwaps, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
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
                    AesKey       = AesKey,
                    MappingsPath = MappingsPath,
                    ChannelSwaps = ChannelSwaps.Count > 0 ? new Dictionary<string,bool>(ChannelSwaps) : null
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
            public Dictionary<string, bool>? ChannelSwaps { get; set; }
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
        /// Per-species diet-slot speed bonuses (km/h) indexed by slot count 0–3.
        /// Values here are hard-coded from observed in-game data and may need updating
        /// if the game changes.
        /// </summary>
        public static readonly Dictionary<string, double[]> DietSlotSpeedBuffs =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // Gallimimus: +2.9 km/h per filled slot (0 slots = base 46.8 km/h)
            ["Gallimimus"] = new double[] { 46.8, 49.7, 52.6, 55.4 }
        };

        /// <summary>
        /// The Unreal Engine version used for pak loading.
        /// Shown in the UI; affects EGame enum passed to the provider.
        /// </summary>
        public static string UEVersion { get; set; } = "5.6";

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

        /// <summary>
        /// Relative epsilon used when comparing two curve channels to decide
        /// whether they are effectively identical above the elder threshold.
        /// 0.01 = 1% tolerance.
        /// </summary>
        public const double SameCurveRelEpsilon = 0.01;
    }
}
