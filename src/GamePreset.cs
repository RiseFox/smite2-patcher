using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Smite2Patcher
{
    internal enum GpuTier
    {
        Base = 0,
        Mid = 1,
        High = 2,
    }

    /// <summary>
    /// In-game graphics for one PC. Values follow docs/smite-settings-map.md:
    /// quality 0 Low / 1 Medium / 2 High; DLSS 1 Quality, 2 Balanced, 3 Performance;
    /// ReflexMode 2 is the option after On; MaxFPS 6 is 240.
    /// </summary>
    internal sealed class GamePreset
    {
        public static readonly string[] QualityKeys =
        {
            "ShadowQuality", "EffectsQuality", "TextureQuality", "ShadingQuality", "ViewDistanceQuality",
            "FoliageQuality", "ReflectionQuality", "AntiAliasingQuality", "PostProcessQuality",
        };

        // Keys the game drops from the save when they hold these values.
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { "ShadowQuality", "2" }, { "TextureQuality", "2" }, { "ViewDistanceQuality", "2" }, { "ReflexMode", "1" },
        };

        public GpuTier Tier;
        public bool Laptop;
        public int VramGb;
        public readonly Dictionary<string, int> Quality = new Dictionary<string, int>();
        public int DlssMode;
        public bool FrameGeneration;

        public const int AntiAliasingTaa = 2;
        public const int ReflexBoost = 2;
        public const int MaxFps240 = 6;

        public static GamePreset Build(SystemSnapshot pc, bool frameGeneration)
        {
            var preset = new GamePreset
            {
                Laptop = IsLaptop(pc.Gpu.Name),
                VramGb = ReadVramGb(pc.Gpu.Name),
                FrameGeneration = frameGeneration,
            };
            preset.Tier = TierOf(pc.Gpu.Name);

            int shadow, effects, texture, shading, view, foliage, reflection, antiAliasing;
            switch (preset.Tier)
            {
                case GpuTier.High:
                    shadow = 2; effects = 1; texture = 2; shading = 2; view = 2; foliage = 1; reflection = 1; antiAliasing = 2;
                    break;
                case GpuTier.Mid:
                    shadow = 1; effects = 1; texture = 2; shading = 1; view = 2; foliage = 0; reflection = 0; antiAliasing = 2;
                    break;
                default:
                    shadow = 0; effects = 0; texture = preset.VramGb >= 10 ? 2 : 1; shading = 0; view = 1; foliage = 0; reflection = 0; antiAliasing = 1;
                    break;
            }
            preset.Quality["ShadowQuality"] = shadow;
            preset.Quality["EffectsQuality"] = effects;
            preset.Quality["TextureQuality"] = texture;
            preset.Quality["ShadingQuality"] = shading;
            preset.Quality["ViewDistanceQuality"] = view;
            preset.Quality["FoliageQuality"] = foliage;
            preset.Quality["ReflectionQuality"] = reflection;
            preset.Quality["AntiAliasingQuality"] = antiAliasing;
            // The game keeps post-processing at Medium or above whatever is saved.
            preset.Quality["PostProcessQuality"] = 0;

            // Below the screen's own resolution: 1080p → 720p, 1440p → 960p (835p on base cards), 4K → 1080p.
            preset.DlssMode = pc.Height >= 2160 ? 3 : pc.Height >= 1440 && preset.Tier == GpuTier.Base ? 2 : 1;
            return preset;
        }

        /// <summary>Value per save key; null means "remove", as the game does for defaults.</summary>
        public Dictionary<string, string?> SaveValues()
        {
            var values = new Dictionary<string, string?>();
            foreach (var pair in Quality)
            {
                values[pair.Key] = pair.Value.ToString();
            }
            values["AntiAliasingMethod"] = AntiAliasingTaa.ToString();
            values["DLSS"] = DlssMode.ToString();
            values["ReflexMode"] = ReflexBoost.ToString();
            values["MaxFPS"] = MaxFps240.ToString();
            values["DLSSFrameGen"] = FrameGeneration ? "true" : null;
            foreach (var key in values.Keys.ToList())
            {
                if (values[key] != null && Defaults.TryGetValue(key, out string? fallback) && values[key] == fallback)
                {
                    values[key] = null;
                }
            }
            return values;
        }

        public Dictionary<string, int> IniValues() => Quality.ToDictionary(pair => "sg." + pair.Key, pair => pair.Value);

        public bool Matches(SettingsSave save) => SaveValues().All(pair => save.Get(pair.Key) == pair.Value);

        public string TierText => Tier == GpuTier.High ? Lang.T("високий", "high") : Tier == GpuTier.Mid ? Lang.T("середній", "medium") : Lang.T("легкий", "light");

        public string TierWords => Tier == GpuTier.High ? Lang.T("високі", "high") : Tier == GpuTier.Mid ? Lang.T("середні", "medium") : Lang.T("легкі", "light");

        public string DlssText => DlssMode == 3 ? "Performance" : DlssMode == 2 ? "Balanced" : "Quality";

        public static string LevelText(int level) => level >= 2 ? "High" : level == 1 ? "Medium" : "Low";

        /// <summary>
        /// Desktop tiers: base 2060/3050/3060/4060/5050; mid 2070-2080 Ti, 3060 Ti-3070 Ti,
        /// 4060 Ti/4070, 5060/5060 Ti; high 3080+, 4070 SUPER/Ti+, 5070+. Laptops drop one tier.
        /// </summary>
        public static GpuTier TierOf(string name)
        {
            // "RTX 4070 Ti SUPER": generation 4, level 7, then suffixes.
            var match = Regex.Match(name, @"RTX\s*(\d)0(\d)0(.*)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return GpuTier.Base;
            }
            var generation = int.Parse(match.Groups[1].Value);
            var level = int.Parse(match.Groups[2].Value);
            var rest = match.Groups[3].Value;
            var ti = Regex.IsMatch(rest, @"\bTi\b", RegexOptions.IgnoreCase);
            var super = rest.IndexOf("SUPER", StringComparison.OrdinalIgnoreCase) >= 0;

            GpuTier tier;
            if (level <= 5)
            {
                tier = GpuTier.Base;
            }
            else if (level == 6)
            {
                tier = ti || generation >= 5 ? GpuTier.Mid : GpuTier.Base;
            }
            else if (level == 7)
            {
                tier = generation >= 5 || (generation == 4 && (ti || super)) ? GpuTier.High : GpuTier.Mid;
            }
            else
            {
                tier = generation == 2 ? GpuTier.Mid : GpuTier.High;
            }

            if (IsLaptop(name) && tier > GpuTier.Base)
            {
                tier--;
            }
            return tier;
        }

        public static bool IsLaptop(string name) =>
            name.IndexOf("Laptop", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Max-Q", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Mobile", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Video memory from the display driver's registry entry, 0 when unknown.</summary>
        public static int ReadVramGb(string gpuName)
        {
            const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            try
            {
                using (var adapters = Registry.LocalMachine.OpenSubKey(ClassKey))
                {
                    if (adapters == null)
                    {
                        return 0;
                    }
                    foreach (var child in adapters.GetSubKeyNames())
                    {
                        using (var adapter = adapters.OpenSubKey(child))
                        {
                            if (!string.Equals(adapter?.GetValue("DriverDesc") as string, gpuName, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            var size = adapter?.GetValue("HardwareInformation.qwMemorySize");
                            var bytes = size is long wide ? wide : size is int narrow ? (uint)narrow : 0L;
                            return (int)Math.Round(bytes / (1024.0 * 1024 * 1024));
                        }
                    }
                }
            }
            catch (Exception error) when (error is System.Security.SecurityException || error is UnauthorizedAccessException)
            {
            }
            return 0;
        }
    }
}
