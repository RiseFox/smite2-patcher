using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Smite2Patcher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // --lang en|uk (first argument) picks the language for this run only, e.g. for screenshots.
            if (args.Length > 1 && args[0] == "--lang")
            {
                Lang.Ukrainian = args[1] == "uk";
                args = args.Skip(2).ToArray();
            }
            if (args.Length > 0)
            {
                return Diagnostics.Run(args);
            }
            Application.Run(new MainForm());
            return 0;
        }
    }

    /// <summary>
    /// Developer checks, all read-only for the real game profile and Steam files:
    ///   --plan OUT                 detected PC, the plan and current values
    ///   --selftest-nv OUT          write/read/delete on a throwaway driver profile
    ///   --test-steam IN COPY OUT   patch a copy of localconfig.vdf
    ///   --screenshot PNG           render the window to an image
    /// </summary>
    internal static class Diagnostics
    {
        private const string TestExe = "smite2-patcher-selftest.exe";

        public static int Run(string[] args)
        {
            var report = new StringBuilder();
            var code = 0;
            try
            {
                switch (args[0])
                {
                    case "--plan":
                        Plan(report);
                        break;
                    case "--selftest-nv":
                        code = SelfTestNv(report);
                        break;
                    case "--test-steam":
                        code = TestSteam(args[1], args[2], report);
                        break;
                    case "--e2e-test":
                        code = EndToEnd(report);
                        break;
                    case "--simulate":
                        Simulate(report);
                        break;
                    case "--test-save":
                        code = TestSave(args[1], report);
                        break;
                    case "--check-log":
                        Describe(LastGame.Read(args[1]), report);
                        break;
                    case "--screenshot":
                        Screenshot(args[1], null);
                        return 0;
                    case "--preview":
                        // --preview "NVIDIA GeForce RTX 4070" 144 1920 1080 out.png
                        Screenshot(args[5], FriendPc(args[1], int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4])));
                        return 0;
                    default:
                        report.AppendLine("Unknown option " + args[0]);
                        code = 2;
                        break;
                }
            }
            catch (Exception error)
            {
                report.AppendLine("ERROR " + error.GetType().Name + ": " + error.Message);
                code = 1;
            }

            var outIndex = args[0] == "--test-steam" ? 3 : args[0] == "--check-log" || args[0] == "--test-save" ? 2 : 1;
            var outPath = args.Length > outIndex ? args[outIndex] : null;
            if (outPath != null)
            {
                File.WriteAllText(outPath, report.ToString(), SteamClient.Utf8NoBom);
            }
            return code;
        }

        private static void Plan(StringBuilder report)
        {
            var pc = SystemProbe.Capture();
            var plan = PatchPlan.Build(pc);
            report.AppendLine("GPU: " + pc.Gpu.Name + " | NVIDIA=" + pc.Gpu.IsNvidia + " RTX=" + pc.Gpu.IsRtx + " gen=" + pc.Gpu.RtxGeneration);
            report.AppendLine("Driver: " + (pc.Gpu.DriverVersion > 0 ? NvDrs.FormatDriver(pc.Gpu.DriverVersion) : "?") + (pc.DriverError != null ? " error=" + pc.DriverError : ""));
            report.AppendLine("Display: " + pc.Width + "x" + pc.Height + " @" + pc.RefreshHz + " Hz | HAGS=" + (pc.HardwareScheduling?.ToString() ?? "default"));
            report.AppendLine("Steam: " + (pc.SteamPath ?? "-") + " running=" + pc.SteamRunning + " | game installed=" + pc.GameInstalled + " running=" + pc.GameRunning);
            report.AppendLine("Supported: " + plan.Supported + (plan.BlockReason != null ? " (" + plan.BlockReason + ")" : ""));
            report.AppendLine("FG=" + plan.FrameGeneration + " cap=" + plan.FrameCap + " steamFlag=" + plan.SteamFlag);
            plan.Summary.ForEach(line => report.AppendLine("  + " + line));
            plan.Warnings.ForEach(line => report.AppendLine("  ! " + line));
            plan.InGame.ForEach(line => report.AppendLine("  game: " + line));

            if (plan.Supported)
            {
                var ids = new List<uint> { Nv.SrOverride, Nv.SrMode, Nv.SrPreset, Nv.FgOverride, Nv.FgMode, Nv.FgMultiFrameCount, Nv.FrameRateLimit };
                var current = NvDrs.Read(Nv.GameExe, ids, out string profileName);
                report.AppendLine("Current profile \"" + profileName + "\":");
                foreach (var setting in current)
                {
                    var planned = plan.Driver.FirstOrDefault(change => change.Id == setting.Id);
                    report.AppendLine(string.Format("  0x{0:X8} existed={1} value={2} -> {3}",
                        setting.Id, setting.Existed, setting.Value, planned != null ? planned.Value.ToString() : "unchanged"));
                }
                var state = PatchEngine.ReadState(pc, plan);
                report.AppendLine("State: driverApplied=" + state.DriverApplied + " steamApplied=" + state.SteamApplied + " backup=" + state.BackupExists);
            }

            Describe(pc.LastGame, report);

            foreach (var edit in PatchEngine.SteamEdits(pc))
            {
                report.AppendLine("Steam edit " + SteamClient.AccountOf(edit.ConfigPath) + ":");
                report.AppendLine("  now:  " + (edit.Current ?? "<none>"));
                report.AppendLine("  next: " + edit.Next);
                report.AppendLine("  removed: " + edit.Removed.Count);
            }
        }

        private static int SelfTestNv(StringBuilder report)
        {
            var ids = new List<uint> { Nv.SrOverride, Nv.FrameRateLimit };
            var failures = 0;
            void Expect(bool condition, string what)
            {
                report.AppendLine((condition ? "PASS " : "FAIL ") + what);
                failures += condition ? 0 : 1;
            }

            var before = NvDrs.Read(TestExe, ids, out string name0);
            Expect(name0 == "" && before.All(item => !item.Existed), "test exe has no profile yet");

            NvDrs.Write(TestExe, new List<NvWrite> { new NvWrite(Nv.SrOverride, 1, false), new NvWrite(Nv.FrameRateLimit, 123, false) });
            var written = NvDrs.Read(TestExe, ids, out string name1);
            Expect(name1 == "SMITE 2 Patcher", "fallback profile created: " + name1);
            Expect(written[0].Existed && written[0].Value == 1 && written[1].Existed && written[1].Value == 123, "values written and read back");

            NvDrs.Write(TestExe, new List<NvWrite> { new NvWrite(Nv.SrOverride, 0, true), new NvWrite(Nv.FrameRateLimit, 0, true) });
            var removed = NvDrs.Read(TestExe, ids, out _);
            Expect(removed.All(item => !item.Existed), "values removed");

            NvDrs.DeleteFallbackProfile(TestExe);
            NvDrs.Read(TestExe, ids, out string name2);
            Expect(name2 == "", "fallback profile deleted");

            var guard = false;
            try
            {
                NvDrs.DeleteFallbackProfile(Nv.GameExe);
            }
            catch (NvDrsException)
            {
                guard = true;
            }
            Expect(guard, "refuses to delete the driver's SMITE 2 profile");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Full apply → apply again → restore cycle against a throwaway driver profile
        /// and copies of two Steam configs. The real Steam is reported as closed and
        /// is never touched.
        /// </summary>
        private static int EndToEnd(StringBuilder report)
        {
            var failures = 0;
            void Expect(bool condition, string what)
            {
                report.AppendLine((condition ? "PASS " : "FAIL ") + what);
                failures += condition ? 0 : 1;
            }

            var realSteam = SteamClient.FindSteamPath() ?? throw new InvalidOperationException("Steam not found");
            var root = Path.Combine(Path.GetTempPath(), "smite2-patcher-e2e-" + Guid.NewGuid().ToString("N"));
            var steam = Path.Combine(root, "steam");
            var originals = new Dictionary<string, string>();
            foreach (var config in SteamClient.LocalConfigs(realSteam).Take(2))
            {
                var copy = Path.Combine(steam, "userdata", SteamClient.AccountOf(config), "config", "localconfig.vdf");
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(config, copy);
                originals[copy] = File.ReadAllText(copy);
            }

            var saveCopy = Path.Combine(root, "game", "HWGameUserSettings.sav");
            var iniCopy = Path.Combine(root, "game", "GameUserSettings.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(saveCopy)!);
            File.Copy(GameSettings.SavePath, saveCopy);
            File.Copy(GameSettings.IniPath, iniCopy);
            var saveOriginal = File.ReadAllBytes(saveCopy);
            var iniOriginal = File.ReadAllBytes(iniCopy);
            GameSettings.SaveOverride = saveCopy;
            GameSettings.IniOverride = iniCopy;

            PatchEngine.Executable = TestExe;
            BackupData.FolderOverride = Path.Combine(root, "backup");
            SteamClient.RunningOverride = () => false;
            PatchEngine.GameRunningOverride = () => false;
            try
            {
                var pc = SystemProbe.Capture();
                pc.SteamPath = steam;
                var plan = PatchPlan.Build(pc);
                report.AppendLine("plan: driver settings=" + plan.Driver.Count + " steam=" + plan.SteamFlag + " accounts=" + originals.Count);
                Expect(!PatchEngine.ReadState(pc, plan).FullyApplied, "starts unpatched");

                PatchEngine.Apply(pc, plan, true, line => report.AppendLine("  apply: " + line));
                var state = PatchEngine.ReadState(pc, plan);
                Expect(state.FullyApplied && state.BackupExists, "applied (driver=" + state.DriverApplied + ", steam=" + state.SteamApplied + ")");
                foreach (var copy in originals.Keys)
                {
                    var options = Vdf.GetLaunchOptions(File.ReadAllText(copy), SteamClient.AppId, out _);
                    Expect(options == LaunchArgs.XeFgFlag, "account " + SteamClient.AccountOf(copy) + " launch options: " + options);
                }
                var patchedSave = SettingsSave.Parse(File.ReadAllBytes(saveCopy));
                Expect(plan.Game != null && plan.Game.Matches(patchedSave), "game save holds the preset");
                var patchedIni = GameSettings.ReadScalability(File.ReadAllText(iniCopy));
                Expect(plan.Game != null && plan.Game.IniValues().All(pair => patchedIni.TryGetValue(pair.Key, out int level) && level == pair.Value),
                    "GameUserSettings.ini holds the preset");

                var firstBackup = File.ReadAllText(BackupData.FilePath);
                PatchEngine.Apply(pc, plan, true, line => report.AppendLine("  apply again: " + line));
                Expect(File.ReadAllText(BackupData.FilePath) == firstBackup, "second apply keeps the original backup");

                // Upgrade case: the same PC on a 120 Hz screen no longer wants FG or a cap.
                var allIds = plan.Driver.Select(change => change.Id).ToList();
                var slowScreen = SystemProbe.Capture();
                slowScreen.SteamPath = steam;
                slowScreen.RefreshHz = 120;
                var smallerPlan = PatchPlan.Build(slowScreen);
                Expect(smallerPlan.Driver.All(change => change.Id != Nv.FrameRateLimit), "120 Hz plan has no frame cap");
                Expect(!PatchEngine.ReadState(slowScreen, smallerPlan).FullyApplied, "earlier cap shows as not applied");
                PatchEngine.Apply(slowScreen, smallerPlan, true, line => report.AppendLine("  apply 120 Hz: " + line));
                var afterUpgrade = NvDrs.Read(TestExe, allIds, out _);
                var dropped = allIds.Where(id => smallerPlan.Driver.All(change => change.Id != id)).ToList();
                Expect(dropped.Count > 0 && afterUpgrade.Where(setting => dropped.Contains(setting.Id)).All(setting => !setting.Existed),
                    "cap and FG removed again (" + dropped.Count + " settings)");
                Expect(PatchEngine.ReadState(slowScreen, smallerPlan).FullyApplied, "120 Hz plan fully applied");

                PatchEngine.Restore(pc, true, line => report.AppendLine("  restore: " + line));
                Expect(!File.Exists(BackupData.FilePath), "backup removed after restore");
                var after = NvDrs.Read(TestExe, allIds, out _);
                Expect(after.All(setting => !setting.Existed), "driver values removed again");
                foreach (var pair in originals)
                {
                    Expect(File.ReadAllText(pair.Key) == pair.Value, "account " + SteamClient.AccountOf(pair.Key) + " config identical after restore");
                }
                Expect(File.ReadAllBytes(saveCopy).SequenceEqual(saveOriginal), "game save identical after restore");
                Expect(File.ReadAllBytes(iniCopy).SequenceEqual(iniOriginal), "GameUserSettings.ini identical after restore");
            }
            finally
            {
                try
                {
                    NvDrs.DeleteFallbackProfile(TestExe);
                }
                catch (NvDrsException error)
                {
                    report.AppendLine("cleanup: " + error.Message);
                }
                PatchEngine.Executable = Nv.GameExe;
                GameSettings.SaveOverride = null;
                GameSettings.IniOverride = null;
                BackupData.FolderOverride = null;
                SteamClient.RunningOverride = null;
                PatchEngine.GameRunningOverride = null;
                Directory.Delete(root, recursive: true);
            }
            return failures == 0 ? 0 : 1;
        }

        private static void Describe(LastGame? game, StringBuilder report)
        {
            if (game == null)
            {
                report.AppendLine("Last game: no log");
                return;
            }
            report.AppendLine("Last game " + game.When.ToString("s") + ": adapter=" + game.Adapter + " nvidia=" + game.AdapterIsNvidia +
                              " provider=" + (game.SwapChainProvider.Length > 0 ? game.SwapChainProvider : "-") +
                              " xefgFlag=" + game.XeFgFlagObeyed + " xefgOwns=" + game.XeFgOwnsFrames +
                              " dlss=" + (game.DlssActive ? game.DlssInput + "->" + game.DlssOutput : "no") +
                              " preset=" + (game.SrPresetApplied > 0 ? LastGame.PresetLetter(game.SrPresetApplied) : "-") +
                              " fg=" + game.FrameGenerationUsed);
        }

        /// <summary>Plans for typical friend PCs, without reading or writing anything.</summary>
        private static void Simulate(StringBuilder report)
        {
            var cases = new[]
            {
                ("NVIDIA GeForce RTX 4070", 4, 165, 61742u),
                ("NVIDIA GeForce RTX 4070 SUPER", 4, 144, 61742u),
                ("NVIDIA GeForce RTX 4070 Laptop GPU", 4, 165, 61742u),
                ("NVIDIA GeForce RTX 3060 Ti", 3, 144, 61742u),
                ("NVIDIA GeForce RTX 2080 SUPER", 2, 144, 61742u),
                ("NVIDIA GeForce RTX 4090", 4, 240, 61742u),
                ("NVIDIA GeForce RTX 4060 Laptop GPU", 4, 120, 61742u),
                ("NVIDIA GeForce RTX 5060", 5, 100, 61742u),
                ("NVIDIA GeForce RTX 4070 Laptop GPU", 4, 90, 61742u),
                ("NVIDIA GeForce RTX 5060 Laptop GPU", 5, 144, 61742u),
                ("NVIDIA GeForce RTX 5050", 5, 240, 61742u),
                ("NVIDIA GeForce RTX 3080", 3, 144, 61742u),
                ("NVIDIA GeForce RTX 4060", 4, 60, 56694u),
                ("NVIDIA GeForce GTX 1660 SUPER", 0, 75, 61742u),
            };
            foreach (var (name, generation, refresh, driver) in cases)
            {
                var pc = new SystemSnapshot
                {
                    Gpu = new GpuInfo { Name = name, IsNvidia = true, IsRtx = name.Contains("RTX"), RtxGeneration = generation, DriverVersion = driver },
                    RefreshHz = refresh,
                    Height = refresh >= 240 ? 1440 : 1080,
                    SteamPath = @"C:\Steam",
                };
                var plan = PatchPlan.Build(pc);
                report.AppendLine("== " + name + " @" + refresh + " Hz, driver " + NvDrs.FormatDriver(driver));
                report.AppendLine("   driver: " + string.Join(", ", plan.Driver.Select(change => string.Format("0x{0:X8}={1}", change.Id, change.Value))));
                report.AppendLine("   FG=" + plan.FrameGeneration + " cap=" + plan.FrameCap + " steamFlag=" + plan.SteamFlag);
                plan.Warnings.ForEach(line => report.AppendLine("   ! " + line));
                if (plan.Game != null)
                {
                    report.AppendLine("   preset: " + plan.Game.TierText + (plan.Game.Laptop ? " (laptop)" : "") + " | " +
                                      string.Join(", ", plan.Game.SaveValues().Select(pair => pair.Key.Replace("Quality", "") + "=" + (pair.Value ?? "-"))));
                }
            }
        }

        /// <summary>
        /// Checks the save writer against files the game itself wrote while settings were
        /// changed in the menu (see docs/settings-map.md). The folder holds your own captures:
        /// HWGameUserSettings.before.sav, .after3-fgoff.sav (FG turned off) and .after2.sav
        /// (two menu passes), plus GameUserSettings.before.ini and .after2.ini.
        /// </summary>
        private static int TestSave(string folder, StringBuilder report)
        {
            var failures = 0;
            void Expect(bool condition, string what)
            {
                report.AppendLine((condition ? "PASS " : "FAIL ") + what);
                failures += condition ? 0 : 1;
            }
            byte[] Read(string name) => File.ReadAllBytes(Path.Combine(folder, name));

            var before = Read("HWGameUserSettings.before.sav");
            Expect(SettingsSave.Parse(before).ToBytes().SequenceEqual(before), "unchanged round trip is byte-identical");

            var fgOff = SettingsSave.Parse(before);
            fgOff.Set("DLSSFrameGen", null);
            var gameFgOff = SettingsSave.Parse(Read("HWGameUserSettings.after3-fgoff.sav"));
            Expect(SettingsSave.Parse(fgOff.ToBytes()).MapSection().SequenceEqual(gameFgOff.MapSection()), "FG Off map matches the game's own write");

            // First menu pass (DLSS Quality, 144 FPS, Reflex On, Shadows/Textures High), then the second.
            var pass1 = SettingsSave.Parse(before);
            pass1.Set("DLSS", "1");
            pass1.Set("MaxFPS", "5");
            pass1.Set("ReflexMode", null);
            pass1.Set("ShadowQuality", null);
            pass1.Set("TextureQuality", null);
            var pass2 = SettingsSave.Parse(pass1.ToBytes());
            pass2.Set("DLSS", "2");
            pass2.Set("EffectsQuality", "1");
            pass2.Set("MaxFPS", "6");
            pass2.Set("ViewDistanceQuality", null);
            pass2.Set("TextureQuality", "1");
            pass2.Set("ShadowQuality", "1");
            pass2.Set("ReflexMode", "2");
            var gamePass2 = SettingsSave.Parse(Read("HWGameUserSettings.after2.sav"));
            Expect(SettingsSave.Parse(pass2.ToBytes()).MapSection().SequenceEqual(gamePass2.MapSection()), "second menu pass matches the game's own write");

            var ini = File.ReadAllText(Path.Combine(folder, "GameUserSettings.before.ini"));
            var iniPass2 = GameSettings.SetScalability(ini, new Dictionary<string, int>
            {
                { "sg.ViewDistanceQuality", 2 }, { "sg.ShadowQuality", 1 }, { "sg.EffectsQuality", 1 },
            });
            var gameIni = File.ReadAllText(Path.Combine(folder, "GameUserSettings.after2.ini"));
            Expect(iniPass2 == gameIni.Replace(";METADATA=(Diff=true, UseCommands=true)\r\n", ""), "ini edit matches the game's values and line endings");
            return failures == 0 ? 0 : 1;
        }

        private static int TestSteam(string input, string copy, StringBuilder report)
        {
            var original = File.ReadAllText(input);
            var current = Vdf.GetLaunchOptions(original, SteamClient.AppId, out bool found);
            var next = LaunchArgs.Patch(current, out List<string> removed);
            var patched = Vdf.SetLaunchOptions(original, SteamClient.AppId, next);
            File.WriteAllText(copy, patched, SteamClient.Utf8NoBom);
            var check = Vdf.GetLaunchOptions(File.ReadAllText(copy), SteamClient.AppId, out _);

            report.AppendLine("app found: " + found);
            report.AppendLine("before: " + (current ?? "<none>"));
            report.AppendLine("after:  " + next);
            report.AppendLine("removed tokens: " + removed.Count);
            report.AppendLine("re-read matches: " + (check == next));

            // Everything outside the edited value must be untouched.
            var prefix = 0;
            while (prefix < original.Length && prefix < patched.Length && original[prefix] == patched[prefix])
            {
                prefix++;
            }
            var suffix = 0;
            while (suffix < original.Length - prefix && suffix < patched.Length - prefix &&
                   original[original.Length - 1 - suffix] == patched[patched.Length - 1 - suffix])
            {
                suffix++;
            }
            report.AppendLine("changed region: original " + (original.Length - prefix - suffix) + " chars, patched " + (patched.Length - prefix - suffix) + " chars");
            report.AppendLine("idempotent: " + (LaunchArgs.Patch(next, out _) == next));
            return check == next ? 0 : 1;
        }

        private static SystemSnapshot FriendPc(string gpu, int refresh, int width, int height)
        {
            var real = SystemProbe.Capture();
            var match = System.Text.RegularExpressions.Regex.Match(gpu, @"RTX\s*(\d)0\d0");
            return new SystemSnapshot
            {
                Gpu = new GpuInfo
                {
                    Name = gpu,
                    IsNvidia = true,
                    IsRtx = gpu.Contains("RTX"),
                    RtxGeneration = match.Success ? int.Parse(match.Groups[1].Value) : 0,
                    DriverVersion = real.Gpu.DriverVersion,
                },
                RefreshHz = refresh,
                Width = width,
                Height = height,
                SteamPath = real.SteamPath,
                GameInstalled = true,
            };
        }

        private static void Screenshot(string path, SystemSnapshot? snapshot)
        {
            using (var form = new MainForm(snapshot))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-3000, -3000);
                form.ShowInTaskbar = false;
                form.Show();
                for (var i = 0; i < 20; i++)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(50);
                }
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(path, ImageFormat.Png);
                }
                form.Close();
            }
        }
    }
}
