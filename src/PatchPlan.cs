using System;
using System.Collections.Generic;

namespace Smite2Patcher
{
    internal sealed class DriverChange
    {
        public DriverChange(uint id, uint value)
        {
            Id = id;
            Value = value;
        }

        public uint Id { get; }
        public uint Value { get; }
    }

    /// <summary>What the patcher will change on this particular PC.</summary>
    internal sealed class PatchPlan
    {
        public readonly List<DriverChange> Driver = new List<DriverChange>();

        /// <summary>Driver settings the player picked themselves; the patcher leaves them alone.</summary>
        public readonly List<uint> Kept = new List<uint>();

        public readonly List<string> Summary = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> InGame = new List<string>();

        public bool Supported;
        public string? BlockReason;
        public bool SteamFlag;
        public bool FrameGeneration;
        public int FrameCap;
        public GamePreset? Game;

        // With FG and the cap, real frames = refresh × 0.97 / 2. SMITE on RTX 40/50
        // already renders ~110-160 FPS, so below 240 Hz-class screens FG would cut
        // real frames (120 Hz → 58, 165 Hz → 80) and add latency instead of smoothness.
        public const int MinRefreshForFrameGeneration = 200;

        /// <summary>Plain-language lines about this PC, in the order a player cares about.</summary>
        private static void DescribeForPlayer(PatchPlan plan, SystemSnapshot pc, bool fgCapable, bool overridesAllowed)
        {
            var card = pc.Gpu.Name.Replace("NVIDIA GeForce ", "").Replace(" Laptop GPU", Lang.T(" (ноутбук)", " (laptop)"));
            var game = plan.Game;
            if (game != null)
            {
                var shadows = GamePreset.LevelText(game.Quality["ShadowQuality"]);
                var effects = GamePreset.LevelText(game.Quality["EffectsQuality"]);
                var textures = GamePreset.LevelText(game.Quality["TextureQuality"]);
                plan.Summary.Add(Lang.T(
                    "Графіка гри під твою " + card + ": " + game.TierWords + " налаштування — тіні " + shadows +
                    ", ефекти " + effects + ", текстури " + textures + ", DLSS " + game.DlssText + ".",
                    "Game graphics for your " + card + ": " + game.TierWords + " settings — shadows " + shadows +
                    ", effects " + effects + ", textures " + textures + ", DLSS " + game.DlssText + "."));
            }
            if (overridesAllowed && pc.Gpu.IsRtx)
            {
                plan.Summary.Add(plan.Kept.Contains(Nv.SrPreset)
                    ? Lang.T("DLSS-пресет " + Nv.PresetName(pc.DriverPreset) + ", уже вибраний на цьому ПК, лишається як є.",
                             "DLSS preset " + Nv.PresetName(pc.DriverPreset) + " already chosen on this PC stays as it is.")
                    : Lang.T("Новіша модель DLSS від NVIDIA — чіткіша картинка в русі.", "Newer NVIDIA DLSS model — a sharper image in motion."));
            }
            if (plan.FrameGeneration)
            {
                plan.Summary.Add(Lang.T(
                    "Генерація кадрів увімкнена: на моніторі " + pc.RefreshHz + " Гц до " + plan.FrameCap + " кадрів на екрані, рівно і без розривів.",
                    "Frame generation on: up to " + plan.FrameCap + " frames on your " + pc.RefreshHz + " Hz screen, smooth and tear-free."));
            }
            else if (fgCapable)
            {
                plan.Summary.Add(Lang.T(
                    "Генерація кадрів лишається вимкненою: на моніторі " + pc.RefreshHz + " Гц вона тільки додала б затримку.",
                    "Frame generation stays off: on a " + pc.RefreshHz + " Hz screen it would only add latency."));
            }
            if (game != null)
            {
                plan.Summary.Add(Lang.T("NVIDIA Reflex на максимум — менша затримка.", "NVIDIA Reflex at maximum — lower latency."));
            }
            if (plan.SteamFlag)
            {
                plan.Summary.Add(Lang.T("Вимкнути зайву технологію Intel (XeFG), через яку DLSS може не працювати, а процесор — грітися.", "Turn off an extra Intel technology (XeFG) that can stop DLSS from working and heats up the CPU."));
            }
        }

        public static PatchPlan Build(SystemSnapshot pc)
        {
            var plan = new PatchPlan();
            if (!pc.Gpu.IsNvidia)
            {
                plan.BlockReason = Lang.T("Потрібна відеокарта NVIDIA. Іншу відеокарту патчер не налаштовує.", "An NVIDIA graphics card is required. The patcher does not set up other cards.");
                return plan;
            }
            if (pc.DriverError != null || pc.Gpu.DriverVersion == 0)
            {
                plan.BlockReason = Lang.T("Не вдалося прочитати драйвер NVIDIA. Встанови або онови його через NVIDIA App і запусти патчер ще раз.", "Could not read the NVIDIA driver. Install or update it in the NVIDIA App and run the patcher again.");
                return plan;
            }

            plan.Supported = true;
            var overridesAllowed = pc.Gpu.DriverVersion >= Nv.MinOverrideDriver;
            var dlssCapable = pc.Gpu.IsRtx;

            if (dlssCapable && overridesAllowed)
            {
                plan.Driver.Add(new DriverChange(Nv.SrOverride, Nv.On));
                plan.Driver.Add(new DriverChange(Nv.SrMode, Nv.SrModeGameControlled));
                // A preset picked in DLSS Swapper or the NVIDIA App is the player's call, not ours.
                if (Nv.IsPlayerPreset(pc.DriverPreset))
                {
                    plan.Kept.Add(Nv.SrPreset);
                }
                else
                {
                    plan.Driver.Add(new DriverChange(Nv.SrPreset, Nv.PresetK));
                }
            }
            else if (dlssCapable)
            {
                plan.Warnings.Add(Lang.T(
                    "Драйвер NVIDIA старий (" + NvDrs.FormatDriver(pc.Gpu.DriverVersion) + "). Онови його через NVIDIA App і запусти патчер ще раз, щоб увімкнути нову модель DLSS.",
                    "The NVIDIA driver is old (" + NvDrs.FormatDriver(pc.Gpu.DriverVersion) + "). Update it in the NVIDIA App and run the patcher again to enable the new DLSS model."));
            }

            var fgCapable = dlssCapable && pc.Gpu.RtxGeneration >= 4;
            plan.FrameGeneration = fgCapable && overridesAllowed && pc.RefreshHz >= MinRefreshForFrameGeneration;
            if (plan.FrameGeneration)
            {
                plan.Driver.Add(new DriverChange(Nv.FgOverride, Nv.On));
                plan.Driver.Add(new DriverChange(Nv.FgMode, Nv.FgModeOn));
                plan.Driver.Add(new DriverChange(Nv.FgMultiFrameCount, Nv.OneGeneratedFrame));
            }

            // Only with FG: the cap keeps generated output just under the refresh rate.
            // Without FG, frames above the refresh rate still lower input latency, so no cap.
            if (plan.FrameGeneration)
            {
                plan.FrameCap = Math.Min(1000, (int)Math.Floor(pc.RefreshHz * 0.97));
                plan.Driver.Add(new DriverChange(Nv.FrameRateLimit, (uint)plan.FrameCap));
            }

            if (dlssCapable)
            {
                plan.Game = GamePreset.Build(pc, plan.FrameGeneration);
            }

            // Without DLSS, Intel XeFG is the only frame generation the card has, so keep it available.
            plan.SteamFlag = dlssCapable && pc.SteamPath != null;
            if (!plan.SteamFlag && dlssCapable)
            {
                plan.Warnings.Add(Lang.T("Steam не знайдено, тому параметр запуску не додано.", "Steam was not found, so the launch option was not added."));
            }

            DescribeForPlayer(plan, pc, fgCapable, overridesAllowed);

            if (plan.Driver.Count == 0 && !plan.SteamFlag)
            {
                plan.Supported = false;
                plan.BlockReason = dlssCapable
                    ? Lang.T("Патчеру нема що змінювати. ", "Nothing for the patcher to change. ") + string.Join(" ", plan.Warnings)
                    : Lang.T("Ця відеокарта без DLSS (не RTX), тож патчеру нема що змінювати. У грі вистав FSR або XeSS (Quality).",
                             "This card has no DLSS (not RTX), so there is nothing for the patcher to change. In the game, pick FSR or XeSS (Quality).");
                return plan;
            }

            if (plan.FrameGeneration && pc.HardwareScheduling == false)
            {
                plan.Warnings.Add(Lang.T("Увімкни у Windows «Апаратне прискорення планування GPU» (Параметри → Дисплей → Графіка) і перезавантаж ПК, бо без нього генерація кадрів не працює.", "Turn on \"Hardware-accelerated GPU scheduling\" in Windows (Settings → Display → Graphics) and restart the PC — frame generation does not work without it."));
            }

            plan.InGame.Add(Lang.T("Графіку (DLSS, генерацію кадрів, Reflex, якість, ліміт FPS) патчер виставив сам. Змінювати під себе можна будь-що.", "The patcher already set the graphics (DLSS, frame generation, Reflex, quality, FPS limit). Change anything you like."));
            if (!plan.FrameGeneration && fgCapable)
            {
                plan.InGame.Add(Lang.T(
                    "DLSS Frame Generation лишається вимкненою: на моніторі до " + MinRefreshForFrameGeneration + " Гц вона зменшила б реальні кадри й додала затримки.",
                    "DLSS Frame Generation stays off: below " + MinRefreshForFrameGeneration + " Hz it would cut real frames and add latency."));
            }
            plan.InGame.Add("Display → VSync Enabled: Off.");
            plan.InGame.Add(Lang.T("Display → Lock Cursor to Window: Locked, особливо якщо в тебе два монітори.", "Display → Lock Cursor to Window: Locked, especially with two monitors."));
            return plan;
        }
    }
}
