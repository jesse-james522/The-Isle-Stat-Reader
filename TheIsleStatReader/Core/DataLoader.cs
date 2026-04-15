using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.Engine.Curves;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;

namespace TheIsleStatReader.Core
{
    /// <summary>
    /// Singleton data loader that wraps CUE4Parse DefaultFileProvider.
    /// Call InitializeAsync() once on startup, then use the query methods freely.
    /// </summary>
    internal sealed class DataLoader
    {
        // ------------------------------------------------------------------
        // Singleton
        // ------------------------------------------------------------------
        private static readonly Lazy<DataLoader> _instance =
            new Lazy<DataLoader>(() => new DataLoader());

        public static DataLoader Instance => _instance.Value;

        private DataLoader() { }

        // ------------------------------------------------------------------
        // Internal state
        // ------------------------------------------------------------------
        private DefaultFileProvider? _provider;
        private bool _initialized;

        // Cache for balance attributes to avoid re-loading on repeated requests
        private readonly Dictionary<string, Dictionary<string, double>> _balanceCache =
            new(StringComparer.OrdinalIgnoreCase);

        // Built once after provider initialisation, then reused until Reset().
        // Anchored on DT_{Name}BalanceAttributes so we never enumerate ATT_ files
        // for things that aren't real dinosaurs.
        private Dictionary<string, DinoEntry>? _dinoIndex;
        private Dictionary<string, string>? _assetPathByFileName;

        /// <summary>
        /// Per-dinosaur asset lookup built once during initialisation.
        /// <see cref="BalanceAttributesPath"/> and <see cref="AttackPowerPath"/> are
        /// stored without the <c>.uasset</c> extension (CUE4Parse convention).
        /// </summary>
        private sealed record DinoEntry(
            string BalanceAttributesPath,
            List<(string FileName, string AssetPath)> AttFiles,
            string? AttackPowerPath);

        /// <summary>
        /// Set by <see cref="InitializeAsync"/> if the mappings file was supplied
        /// but failed to load (e.g. unsupported usmap version). Null on success.
        /// </summary>
        public string? MappingsWarning { get; private set; }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// True once <see cref="InitializeAsync"/> has completed successfully.
        /// </summary>
        public bool IsInitialized => _initialized && _provider != null;

        /// <summary>
        /// Clears provider + caches so <see cref="InitializeAsync"/> can be called
        /// again (e.g. after the user changes settings).
        /// </summary>
        public void Reset()
        {
            _provider = null;
            _initialized = false;
            _balanceCache.Clear();
            _balanceLoadErrors.Clear();
            _dinoIndex = null;
            _assetPathByFileName = null;
            MappingsWarning = null;
        }

        /// <summary>
        /// Initialises the CUE4Parse file provider. Must be called once before any
        /// other method. Safe to await on a background thread.
        /// </summary>
        /// <param name="progress">Optional progress callback: receives 0–100 integers.</param>
        public Task InitializeAsync(IProgress<(int Percent, string Message)>? progress = null)
        {
            return Task.Run(async () =>
            {
                progress?.Report((3, "Preparing compression helpers…"));
                await EnsureCompressionHelpersAsync(progress).ConfigureAwait(false);

                progress?.Report((10, "Creating file provider…"));

                var provider = new DefaultFileProvider(
                    Config.PakDirectory,
                    SearchOption.TopDirectoryOnly,
                    new VersionContainer(EGame.GAME_UE5_6),
                    StringComparer.OrdinalIgnoreCase);

                progress?.Report((15, "Initialising pak index…"));
                provider.Initialize();

                progress?.Report((35, "Submitting AES key…"));
                provider.SubmitKey(new FGuid(), new FAesKey(Config.AesKey));

                progress?.Report((55, "Loading mappings…"));
                if (!string.IsNullOrWhiteSpace(Config.MappingsPath) && File.Exists(Config.MappingsPath))
                {
                    try
                    {
                        provider.MappingsContainer = new FileUsmapTypeMappingsProvider(Config.MappingsPath);
                    }
                    catch (Exception ex)
                    {
                        // Tolerate unsupported usmap versions — curves for native
                        // types (UCurveLinearColor) still work without mappings.
                        MappingsWarning =
                            $"Mappings file could not be loaded ({ex.Message}). " +
                            "Curves will still plot, but balance attributes may be unavailable.";
                        progress?.Report((60, "Mappings skipped — " + ex.Message));
                    }
                }

                // Note: we deliberately do NOT call provider.LoadLocalization().
                // It constructs a CultureInfo which can throw in globalization-
                // invariant self-contained builds, and we don't read any
                // localized strings — only curves and data tables.

                _provider = provider;

                progress?.Report((85, "Indexing dinosaur files…"));
                BuildIndex();

                progress?.Report((95, "Finalising…"));
                _initialized = true;

                progress?.Report((100, "Ready"));
            });
        }

        // ------------------------------------------------------------------
        // Compression helpers (Oodle / Zlib-ng)
        // ------------------------------------------------------------------

        /// <summary>
        /// UE 5.6 IO Store containers (.ucas) are Oodle-compressed, and some
        /// assets use Zlib-ng. CUE4Parse ships helpers that will download the
        /// native DLLs from GitHub on first run and cache them next to the exe.
        /// Both helpers short-circuit if already initialised.
        /// </summary>
        private static async Task EnsureCompressionHelpersAsync(
            IProgress<(int Percent, string Message)>? progress)
        {
            var baseDir = AppContext.BaseDirectory;

            // --- Oodle ---
            if (OodleHelper.Instance is null)
            {
                var oodlePath = Path.Combine(baseDir, OodleHelper.OodleFileName);
                if (!File.Exists(oodlePath))
                    progress?.Report((4, "Downloading Oodle DLL (one-time)…"));
                try
                {
                    await OodleHelper.InitializeAsync(oodlePath).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Non-fatal: the pak files that don't use Oodle will still work.
                    // The real error shows up later as "Oodle decompression failed: not initialized".
                    System.Diagnostics.Debug.WriteLine($"Oodle init failed: {ex.Message}");
                }
            }

            // --- Zlib-ng ---
            if (ZlibHelper.Instance is null)
            {
                var zlibPath = Path.Combine(baseDir, ZlibHelper.DllName);
                if (!File.Exists(zlibPath))
                    progress?.Report((6, "Downloading Zlib-ng DLL (one-time)…"));
                try
                {
                    await ZlibHelper.InitializeAsync(zlibPath).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Zlib init failed: {ex.Message}");
                }
            }
        }

        // ------------------------------------------------------------------
        // Index building
        // ------------------------------------------------------------------

        /// <summary>
        /// Walks the provider's file table once and builds the per-dinosaur lookup.
        /// Anchored on <c>DT_{Name}BalanceAttributes.uasset</c> — only dinos with a
        /// balance table become entries, so stray ATT_ files for things that aren't
        /// real dinosaurs are skipped.
        /// </summary>
        private void BuildIndex()
        {
            var balanceRegex = new Regex(
                @"^DT_([A-Za-z0-9]+)BalanceAttributes\.uasset$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var attRegex = new Regex(
                @"^ATT_([A-Za-z0-9]+)_(.+)\.uasset$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var balancePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var attByDino = new Dictionary<string, List<(string FileName, string AssetPath)>>(
                StringComparer.OrdinalIgnoreCase);

            // Single pass over every file in the VFS.  Prefix filter before regex
            // keeps this cheap even on the 78k-file provider.
            foreach (var key in _provider!.Files.Keys)
            {
                var fn = Path.GetFileName(key);
                if (fn.Length == 0) continue;

                if (fn.StartsWith("DT_", StringComparison.OrdinalIgnoreCase))
                {
                    var m = balanceRegex.Match(fn);
                    if (m.Success)
                        balancePaths[m.Groups[1].Value] = StripUassetExtension(key);
                }
                else if (fn.StartsWith("ATT_", StringComparison.OrdinalIgnoreCase))
                {
                    var m = attRegex.Match(fn);
                    if (!m.Success) continue;
                    var dinoName = m.Groups[1].Value;
                    if (!attByDino.TryGetValue(dinoName, out var list))
                        attByDino[dinoName] = list = new List<(string, string)>();
                    list.Add((fn, StripUassetExtension(key)));
                }
            }

            // Assemble final index — only dinos that actually have a balance table
            var index = new Dictionary<string, DinoEntry>(StringComparer.OrdinalIgnoreCase);
            var flatPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, balancePath) in balancePaths)
            {
                if (!attByDino.TryGetValue(name, out var atts))
                    atts = new List<(string, string)>();

                atts.Sort((a, b) =>
                    StringComparer.OrdinalIgnoreCase.Compare(a.FileName, b.FileName));

                string? attackPowerPath = null;
                string apName = $"ATT_{name}_AttackPower.uasset";
                foreach (var (fn, path) in atts)
                {
                    if (fn.Equals(apName, StringComparison.OrdinalIgnoreCase))
                    {
                        attackPowerPath = path;
                        break;
                    }
                }

                index[name] = new DinoEntry(balancePath, atts, attackPowerPath);

                // Flat filename → asset path map powers FindAssetPath().
                flatPaths[$"DT_{name}BalanceAttributes.uasset"] = balancePath;
                foreach (var (fn, path) in atts)
                    flatPaths[fn] = path;
            }

            _dinoIndex = index;
            _assetPathByFileName = flatPaths;
        }

        private static string StripUassetExtension(string key) =>
            key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                ? key[..^".uasset".Length]
                : key;

        // ------------------------------------------------------------------
        // Dinosaur / file enumeration
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns a sorted list of dinosaur names that have both a
        /// <c>DT_{Name}BalanceAttributes</c> table and (usually) ATT_ curves.
        /// Pulled from the pre-built index — no file scanning per call.
        /// </summary>
        public List<string> GetDinosaurs()
        {
            EnsureInitialized();

            var list = new List<string>(_dinoIndex!.Keys);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>
        /// Returns display names for all plottable assets for a given dinosaur.
        /// Includes real ATT_ files and virtual attack combinations.
        /// Format: "filename.uasset" for real files, "Virtual: {Name} {X} Attack" for virtual.
        /// </summary>
        public List<string> GetPlottableFiles(string dinoName)
        {
            EnsureInitialized();

            var result = new List<string>();

            if (_dinoIndex!.TryGetValue(dinoName, out var entry))
            {
                foreach (var (fileName, _) in entry.AttFiles)
                    result.Add(fileName);
            }

            // Virtual attack curves (requires parsing the balance DT, which is cached)
            result.AddRange(GetAttackVirtualFileNames(dinoName));

            return result;
        }

        // ------------------------------------------------------------------
        // Curve loading
        // ------------------------------------------------------------------

        /// <summary>
        /// Loads a UCurveLinearColor asset and returns sampled curve data plus
        /// the appropriate y-axis label. Virtual attack names ("Virtual: ...") are
        /// handled separately via GetAttackVirtualCurves().
        /// </summary>
        /// <param name="assetPath">Full virtual path without extension, e.g.
        ///     <c>TheIsle/Content/Blueprints/Dinos/Deinosuchus/ATT_Deinosuchus_Speed</c></param>
        /// <param name="fileName">Display filename, used to detect unit keywords.</param>
        public (List<(double[] Times, double[] Values)> Curves, string YLabel)
            GetCurveData(string assetPath, string fileName)
        {
            EnsureInitialized();

            (double conversionFactor, string yLabel) = GetConversionInfo(fileName);

            UObject? obj;
            try
            {
                obj = _provider!.SafeLoadPackageObject(assetPath);
            }
            catch
            {
                return (new List<(double[], double[])>(), yLabel);
            }
            if (obj is not UCurveLinearColor curveAsset)
                return (new List<(double[], double[])>(), yLabel);

            // FloatCurves is a C# field on UCurveLinearColor, populated during
            // Deserialize() — 4 FRichCurve channels (R/G/B/A in material terms,
            // but repurposed by TheIsle as senior/elder/unused/unused).
            var curves = CurveProcessor.ProcessDualCurves(curveAsset.FloatCurves, conversionFactor);
            return (curves, yLabel);
        }

        /// <summary>
        /// Finds the virtual path for a given filename (e.g. "ATT_Rex_Speed.uasset").
        /// Backed by the indexed filename → asset-path map, so this is O(1).
        /// Returns null if not found.
        /// </summary>
        public string? FindAssetPath(string fileName)
        {
            EnsureInitialized();
            return _assetPathByFileName!.TryGetValue(fileName, out var path) ? path : null;
        }

        // ------------------------------------------------------------------
        // Balance Attributes
        // ------------------------------------------------------------------

        /// <summary>
        /// Per-dino capture of the last load error, for surfacing via diagnostics.
        /// </summary>
        private readonly Dictionary<string, string> _balanceLoadErrors =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Loads all rows from the <c>DT_{Name}BalanceAttributes</c> DataTable.
        /// Returns a dictionary mapping row name → float value.
        /// Falls back to scanning package exports in case the generic cast fails.
        /// </summary>
        public Dictionary<string, double> GetBalanceAttributes(string dinoName)
        {
            EnsureInitialized();

            if (_balanceCache.TryGetValue(dinoName, out var cached))
                return cached;

            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            if (!_dinoIndex!.TryGetValue(dinoName, out var entry))
            {
                _balanceCache[dinoName] = result;
                return result;
            }

            UDataTable? dt = null;
            try
            {
                // LoadPackage lets us iterate exports and pick whichever one is a
                // UDataTable (handles subclasses like UCompositeDataTable, and
                // surfaces deserialization errors instead of silently returning null).
                var package = _provider!.LoadPackage(entry.BalanceAttributesPath);
                foreach (var exp in package.GetExports())
                {
                    if (exp is UDataTable found)
                    {
                        dt = found;
                        break;
                    }
                }

                if (dt == null)
                    _balanceLoadErrors[dinoName] = "Package loaded but contained no UDataTable export.";
            }
            catch (Exception ex)
            {
                _balanceLoadErrors[dinoName] =
                    $"{ex.GetType().Name}: {ex.Message}" +
                    (ex.InnerException != null ? $" / inner {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "");
            }

            if (dt?.RowMap != null)
            {
                foreach (KeyValuePair<FName, FStructFallback> kvp in dt.RowMap)
                {
                    result[kvp.Key.Text] = ExtractBalanceRowValue(kvp.Value);
                }
            }

            _balanceCache[dinoName] = result;
            return result;
        }

        /// <summary>
        /// Returns the last error captured while loading the balance table for a
        /// given dino, or <c>null</c> if the load succeeded or was never attempted.
        /// </summary>
        public string? GetBalanceLoadError(string dinoName) =>
            _balanceLoadErrors.TryGetValue(dinoName, out var msg) ? msg : null;

        /// <summary>
        /// Balance-table rows historically used one of several field names for the
        /// actual value. Mirrors the Python fallback chain:
        /// <c>AttributePercentageValues</c> → <c>AttributePercentageValue</c> → <c>Value</c>.
        /// First name that is present in the row's property list wins, even if zero.
        /// </summary>
        private static double ExtractBalanceRowValue(FStructFallback row)
        {
            string[] candidates =
            {
                "AttributePercentageValues",
                "AttributePercentageValue",
                "Value",
            };

            foreach (var name in candidates)
            {
                // Only return this slot if the property actually exists on the row —
                // otherwise GetOrDefault<float>() silently returns 0 and we can't tell
                // a real zero from a missing field.
                foreach (var prop in row.Properties)
                {
                    if (prop.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return row.GetOrDefault<float>(name);
                }
            }
            return 0.0;
        }

        /// <summary>
        /// Returns calculated survival statistics derived from balance attributes.
        /// </summary>
        public Dictionary<string, double> GetCalculatedStats(string dinoName)
        {
            var attrs = GetBalanceAttributes(dinoName);
            double thirstCorrection = Config.AquaticDinos.Contains(dinoName) ? 2.0 / 3.0 : 1.0;

            var stats = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            if (attrs.TryGetValue("Hunger.Decay", out double hungerDecay) && hungerDecay != 0)
                stats["Time to Starve (min)"] = Math.Round((100.0 / Math.Abs(hungerDecay) * thirstCorrection) / 60);

            if (attrs.TryGetValue("Thirst.Decay", out double thirstDecay) && thirstDecay != 0)
                stats["Time to Dehydrate (min)"] = Math.Round((100.0 / Math.Abs(thirstDecay) * thirstCorrection) / 60);

            if (attrs.TryGetValue("Oxygen.Decay", out double oxygenDecay) && oxygenDecay != 0)
                stats["Time Underwater (sec)"] = Math.Round(100.0 / Math.Abs(oxygenDecay));

            if (attrs.TryGetValue("Stamina.Spending.Sprinting", out double staminaDecay) && staminaDecay != 0)
                stats["Sprint Duration (sec)"] = Math.Round(100.0 / Math.Abs(staminaDecay), 0);

            return stats;
        }

        // ------------------------------------------------------------------
        // Virtual attack curves
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns display names for virtual attack curves for a dinosaur.
        /// Format: "Virtual: {Name} {X} Attack"
        /// </summary>
        public List<string> GetAttackVirtualFileNames(string dinoName)
        {
            EnsureInitialized();

            var result = new List<string>();
            if (!_dinoIndex!.TryGetValue(dinoName, out var entry) || entry.AttackPowerPath == null)
                return result;

            var attrs = GetBalanceAttributes(dinoName);
            foreach (var (rowName, value) in attrs)
            {
                if (!rowName.StartsWith("Damage.", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (value == 0.0)
                    continue;

                // Extract X from "Damage.X"
                string x = rowName.Substring("Damage.".Length);
                result.Add($"Virtual: {dinoName} {x} Attack");
            }
            return result;
        }

        /// <summary>
        /// Returns the processed curve data for virtual attack combinations.
        /// Key = virtual display name, Value = list of (times, values) curve pairs.
        /// </summary>
        public Dictionary<string, List<(double[] Times, double[] Values)>>
            GetAttackVirtualCurves(string dinoName)
        {
            EnsureInitialized();

            var result = new Dictionary<string, List<(double[], double[])>>(
                StringComparer.OrdinalIgnoreCase);

            if (!_dinoIndex!.TryGetValue(dinoName, out var entry) || entry.AttackPowerPath == null)
                return result;

            var attrs = GetBalanceAttributes(dinoName);

            // Load base AttackPower curves (no unit conversion for attack)
            var (baseCurves, _) = GetCurveData(entry.AttackPowerPath, "ATT_AttackPower.uasset");
            if (baseCurves.Count == 0)
                return result;

            foreach (var (rowName, damageValue) in attrs)
            {
                if (!rowName.StartsWith("Damage.", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (damageValue == 0.0)
                    continue;

                string x = rowName.Substring("Damage.".Length);
                string virtualName = $"Virtual: {dinoName} {x} Attack";

                var scaledCurves = new List<(double[], double[])>();
                foreach (var (times, values) in baseCurves)
                {
                    var (st, sv) = CurveProcessor.ScaleCurve(times, values, damageValue);
                    scaledCurves.Add((st, sv));
                }

                result[virtualName] = scaledCurves;
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Diagnostics
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns a human-readable dump of the provider's current state:
        /// mounted/unloaded VFS, AES key counts, top file extensions, and
        /// samples of files matching the patterns we care about.
        /// </summary>
        public string GetDiagnostics()
        {
            if (_provider == null)
                return "Provider not initialised.";

            var sb = new StringBuilder();
            sb.AppendLine("=== CUE4Parse Provider Diagnostics ===");
            sb.AppendLine();
            sb.AppendLine($"Pak directory: {Config.PakDirectory}");
            sb.AppendLine($"Mappings loaded: {(_provider.MappingsContainer != null ? "yes" : "no")}");
            if (MappingsWarning != null)
                sb.AppendLine($"Mappings warning: {MappingsWarning}");
            sb.AppendLine($"Oodle initialised: {(OodleHelper.Instance != null ? "yes" : "no")}");
            sb.AppendLine($"Zlib-ng initialised: {(ZlibHelper.Instance != null ? "yes" : "no")}");
            sb.AppendLine();

            TryAppend(sb, "Mounted VFS", () => _provider.MountedVfs.Count.ToString());
            TryAppend(sb, "Unloaded VFS", () => _provider.UnloadedVfs.Count.ToString());
            TryAppend(sb, "Required AES keys", () => _provider.RequiredKeys.Count.ToString());
            TryAppend(sb, "Submitted AES keys", () => _provider.Keys.Count.ToString());
            TryAppend(sb, "Indexed dinosaurs", () => (_dinoIndex?.Count ?? 0).ToString());
            sb.AppendLine();

            // List mounted containers
            try
            {
                sb.AppendLine("Mounted containers:");
                foreach (var vfs in _provider.MountedVfs)
                    sb.AppendLine($"  {vfs.Name}  ({vfs.FileCount} files)");
                sb.AppendLine();
            }
            catch (Exception ex) { sb.AppendLine($"  (error listing: {ex.Message})"); }

            // List unloaded (need a key) containers
            try
            {
                if (_provider.UnloadedVfs.Count > 0)
                {
                    sb.AppendLine("Unloaded containers (missing key?):");
                    foreach (var vfs in _provider.UnloadedVfs)
                        sb.AppendLine($"  {vfs.Name}");
                    sb.AppendLine();
                }
            }
            catch { }

            var files = _provider.Files;
            sb.AppendLine($"Total files in virtual FS: {files.Count}");
            sb.AppendLine();

            // Top extensions
            try
            {
                var extCounts = files.Keys
                    .Select(k => Path.GetExtension(k).ToLowerInvariant())
                    .GroupBy(e => e)
                    .Select(g => new { Ext = g.Key, Count = g.Count() })
                    .OrderByDescending(t => t.Count)
                    .Take(15)
                    .ToList();
                sb.AppendLine("Top file extensions:");
                foreach (var t in extCounts)
                    sb.AppendLine($"  {(string.IsNullOrEmpty(t.Ext) ? "(none)" : t.Ext)}: {t.Count}");
                sb.AppendLine();
            }
            catch (Exception ex) { sb.AppendLine($"(extension count failed: {ex.Message})"); }

            // BalanceAttributes matches
            var balanceHits = files.Keys
                .Where(k => k.Contains("BalanceAttributes", StringComparison.OrdinalIgnoreCase))
                .ToList();
            sb.AppendLine($"Files containing 'BalanceAttributes': {balanceHits.Count}");
            foreach (var f in balanceHits.Take(40))
                sb.AppendLine($"  {f}");
            if (balanceHits.Count > 40)
                sb.AppendLine($"  … {balanceHits.Count - 40} more");
            sb.AppendLine();

            // ATT_ matches
            var attHits = files.Keys
                .Where(k => Path.GetFileName(k).StartsWith("ATT_", StringComparison.OrdinalIgnoreCase))
                .ToList();
            sb.AppendLine($"Files with filename starting 'ATT_': {attHits.Count}");
            foreach (var f in attHits.Take(20))
                sb.AppendLine($"  {f}");
            if (attHits.Count > 20)
                sb.AppendLine($"  … {attHits.Count - 20} more");
            sb.AppendLine();

            // DT_ matches (regardless of "BalanceAttributes")
            var dtHits = files.Keys
                .Where(k => Path.GetFileName(k).StartsWith("DT_", StringComparison.OrdinalIgnoreCase))
                .ToList();
            sb.AppendLine($"Files with filename starting 'DT_': {dtHits.Count}");
            foreach (var f in dtHits.Take(20))
                sb.AppendLine($"  {f}");
            if (dtHits.Count > 20)
                sb.AppendLine($"  … {dtHits.Count - 20} more");
            sb.AppendLine();

            // Probe one balance table — load via LoadPackage so we can see the
            // real exception (SafeLoadPackageObject<T> swallows errors).
            if (_dinoIndex is { Count: > 0 })
            {
                var sampleName = _dinoIndex.Keys.First();
                var sampleEntry = _dinoIndex[sampleName];
                sb.AppendLine($"Balance DT sample: {sampleName}");
                sb.AppendLine($"  path: {sampleEntry.BalanceAttributesPath}");
                try
                {
                    var package = _provider.LoadPackage(sampleEntry.BalanceAttributesPath);
                    var exports = package.GetExports().ToList();
                    sb.AppendLine($"  exports ({exports.Count}):");
                    foreach (var exp in exports)
                        sb.AppendLine($"    {exp.Name}  type={exp.GetType().Name}  class={exp.Class?.Name ?? "(null)"}");

                    var dt = exports.OfType<UDataTable>().FirstOrDefault();
                    if (dt == null)
                    {
                        sb.AppendLine("  (no UDataTable export found)");
                    }
                    else
                    {
                        sb.AppendLine($"  row struct: {dt.RowStructName ?? "(null)"}");
                        sb.AppendLine($"  rows: {dt.RowMap?.Count ?? 0}");
                        if (dt.RowMap != null)
                        {
                            int dumped = 0;
                            foreach (var kvp in dt.RowMap)
                            {
                                if (dumped >= 4) break;
                                sb.AppendLine($"  row '{kvp.Key.Text}' properties ({kvp.Value.Properties.Count}):");
                                foreach (var prop in kvp.Value.Properties)
                                    sb.AppendLine($"    {prop.Name.Text} ({prop.PropertyType.Text}) = {prop.Tag?.GenericValue}");
                                dumped++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  LOAD FAILED: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                        sb.AppendLine($"    inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    sb.AppendLine($"    stack (top):");
                    var stack = (ex.StackTrace ?? "").Split('\n').Take(6);
                    foreach (var line in stack)
                        sb.AppendLine($"      {line.TrimEnd()}");
                }
                sb.AppendLine();
            }

            // First 30 files overall, to give a baseline sense of layout
            sb.AppendLine("First 30 file keys:");
            foreach (var k in files.Keys.Take(30))
                sb.AppendLine($"  {k}");

            return sb.ToString();
        }

        private static void TryAppend(StringBuilder sb, string label, Func<string> getter)
        {
            try { sb.AppendLine($"{label}: {getter()}"); }
            catch (Exception ex) { sb.AppendLine($"{label}: (error: {ex.Message})"); }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void EnsureInitialized()
        {
            if (!_initialized || _provider == null)
                throw new InvalidOperationException(
                    "DataLoader is not initialized. Call InitializeAsync() first.");
        }

        private static (double ConversionFactor, string YLabel) GetConversionInfo(string fileName)
        {
            if (fileName.Contains("Speed", StringComparison.OrdinalIgnoreCase))
                return (Config.SpeedConversionFactor, "Value (km/h)");

            if (fileName.Contains("Weight", StringComparison.OrdinalIgnoreCase))
                return (1.0, "Value (kg)");

            return (1.0, "Value");
        }
    }
}
