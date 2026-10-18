using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Smite2Patcher
{
    internal sealed class PatchState
    {
        public bool DriverApplied;
        public bool SteamApplied;
        public bool GameApplied;
        public bool BackupExists;
        public string ProfileName = "";

        public bool FullyApplied => DriverApplied && SteamApplied && GameApplied;
    }

    internal sealed class SteamEdit
    {
        public string ConfigPath = "";
        public string? Current;
        public string Next = "";
        public List<string> Removed = new List<string>();
    }

    internal static class PatchEngine
    {
        private static readonly TimeSpan SteamExitTimeout = TimeSpan.FromSeconds(90);

        /// <summary>The executable whose driver profile is patched; the end-to-end test swaps it.</summary>
        public static string Executable = Nv.GameExe;

        public static PatchState ReadState(SystemSnapshot pc, PatchPlan plan)
        {
            var state = new PatchState();
            BackupData? backup = null;
            try
            {
                backup = BackupData.Load();
                state.BackupExists = backup != null;
            }
            catch (Exception error) when (error is IOException || error is System.Runtime.Serialization.SerializationException)
            {
                state.BackupExists = true;
            }

            if (plan.Driver.Count == 0)
            {
                state.DriverApplied = true;
            }
            else
            {
                var current = NvDrs.Read(Executable, plan.Driver.Select(change => change.Id).ToList(), out state.ProfileName);
                state.DriverApplied = plan.Driver.All(change =>
                    current.Any(setting => setting.Id == change.Id && setting.Existed && setting.Value == change.Value));
            }
            if (backup != null && StaleDriverSettings(backup, plan).Count > 0)
            {
                state.DriverApplied = false;
            }

            state.SteamApplied = !plan.SteamFlag || SteamEdits(pc).Count == 0;
            // Once applied, later changes the player makes in the menu are theirs, not "unpatched".
            state.GameApplied = plan.Game == null || backup?.GameFiles == true;
            return state;
        }

        /// <summary>Launch-option edits still needed, one per Steam account.</summary>
        public static List<SteamEdit> SteamEdits(SystemSnapshot pc)
        {
            var edits = new List<SteamEdit>();
            if (pc.SteamPath == null)
            {
                return edits;
            }

            var configs = SteamClient.LocalConfigs(pc.SteamPath);
            var withGame = new List<(string Path, string? Options)>();
            foreach (var path in configs)
            {
                var options = Vdf.GetLaunchOptions(File.ReadAllText(path), SteamClient.AppId, out bool appFound);
                if (appFound)
                {
                    withGame.Add((path, options));
                }
            }

            // An account that never started SMITE 2 has no entry; only fall back to
            // all accounts when no account has played it yet.
            var targets = withGame.Count > 0 ? withGame : configs.Select(path => (path, (string?)null)).ToList();
            foreach (var (path, options) in targets)
            {
                var next = LaunchArgs.Patch(options, out List<string> removed);
                if (next != (options ?? ""))
                {
                    edits.Add(new SteamEdit { ConfigPath = path, Current = options, Next = next, Removed = removed });
                }
            }
            return edits;
        }

        public static void Apply(SystemSnapshot pc, PatchPlan plan, bool mayCloseSteam, Action<string> log)
        {
            EnsureGameClosed();
            var backup = LoadBackup() ?? new BackupData { Created = DateTime.Now.ToString("s") };

            if (plan.Driver.Count > 0)
            {
                var ids = plan.Driver.Select(change => change.Id).ToList();
                foreach (var setting in NvDrs.Read(Executable, ids, out _))
                {
                    if (backup.Driver.All(item => item.Id != setting.Id))
                    {
                        backup.Driver.Add(new DriverBackup { Id = setting.Id, Existed = setting.Existed, Value = setting.Value });
                    }
                }
                backup.Save();

                NvDrs.Write(Executable, plan.Driver.Select(change => new NvWrite(change.Id, change.Value, remove: false)).ToList());
                var written = NvDrs.Read(Executable, ids, out string profileName);
                if (!plan.Driver.All(change => written.Any(setting => setting.Id == change.Id && setting.Value == change.Value)))
                {
                    throw new InvalidOperationException(Lang.T("Драйвер NVIDIA не зберіг налаштування. Спробуй ще раз або онови драйвер.", "The NVIDIA driver did not save the settings. Try again or update the driver."));
                }
                log(Lang.T("✓ Профіль NVIDIA «" + profileName + "» оновлено.", "✓ NVIDIA profile \"" + profileName + "\" updated."));
            }

            // Settings an earlier run wrote that this plan no longer wants (for example the
            // frame cap 1.0.0 set on 90-144 Hz screens) go back to how they were before.
            var stale = StaleDriverSettings(backup, plan);
            if (stale.Count > 0)
            {
                NvDrs.Write(Executable, stale.Select(item => new NvWrite(item.Id, item.Value, remove: !item.Existed)).ToList());
                backup.Driver.RemoveAll(item => stale.Contains(item));
                backup.Save();
                log(stale.Any(item => item.Id == Nv.FrameRateLimit)
                    ? Lang.T("✓ Ліміт кадрів від попереднього запуску прибрано.", "✓ Frame cap from a previous run removed.")
                    : Lang.T("✓ Налаштування від попереднього запуску, які вже не потрібні, повернуто.", "✓ Settings from a previous run that are no longer needed were reverted."));
            }

            ApplyGame(plan, backup, log);

            if (!plan.SteamFlag)
            {
                return;
            }
            if (SteamEdits(pc).Count == 0)
            {
                log(Lang.T("✓ Steam: потрібний параметр запуску вже є.", "✓ Steam: the launch option is already there."));
                return;
            }

            WithSteamClosed(pc, mayCloseSteam, log, Lang.T("параметр запуску не додано", "the launch option was not added"), () =>
            {
                // Re-read after Steam exits: it rewrites localconfig.vdf on the way out.
                foreach (var edit in SteamEdits(pc))
                {
                    if (backup.Steam.All(item => !string.Equals(item.ConfigPath, edit.ConfigPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        backup.Steam.Add(new SteamBackup
                        {
                            ConfigPath = edit.ConfigPath,
                            HadLaunchOptions = edit.Current != null,
                            LaunchOptions = edit.Current ?? "",
                        });
                        var copy = Path.Combine(BackupData.Folder, "localconfig-" + SteamClient.AccountOf(edit.ConfigPath) + ".vdf");
                        if (!File.Exists(copy))
                        {
                            File.Copy(edit.ConfigPath, copy);
                        }
                    }
                    backup.Save();

                    WriteLaunchOptions(edit.ConfigPath, edit.Next);
                    log(Lang.T("✓ Steam: додано ", "✓ Steam: added ") + LaunchArgs.XeFgFlag +
                        (edit.Removed.Count > 0
                            ? Lang.T(", прибрано старі параметри, які гра ігнорує (", ", removed old options the game ignores (") + edit.Removed.Count + ")."
                            : "."));
                }
            });
        }

        public static void Restore(SystemSnapshot pc, bool mayCloseSteam, Action<string> log)
        {
            EnsureGameClosed();
            var backup = LoadBackup() ?? throw new InvalidOperationException(Lang.T("Нема що відкочувати: на цьому ПК патч ще не застосовували.", "Nothing to restore: the patch has not been applied on this PC."));

            if (backup.Driver.Count > 0)
            {
                NvDrs.Write(Executable, backup.Driver.Select(item => new NvWrite(item.Id, item.Value, remove: !item.Existed)).ToList());
                log(Lang.T("✓ Профіль NVIDIA повернуто, як було до патча.", "✓ NVIDIA profile restored to how it was before the patch."));
            }

            if (backup.GameFiles)
            {
                if (File.Exists(BackupData.GameSaveCopy))
                {
                    File.Copy(BackupData.GameSaveCopy, backup.GameSavePath, overwrite: true);
                }
                if (File.Exists(BackupData.GameIniCopy))
                {
                    File.Copy(BackupData.GameIniCopy, backup.GameIniPath, overwrite: true);
                }
                backup.GameFiles = false;
                backup.Save();
                log(Lang.T("✓ Графіку гри повернуто, як було до патча.", "✓ Game graphics restored to how they were before the patch."));
            }

            var steamRestored = backup.Steam.Count == 0;
            if (!steamRestored)
            {
                WithSteamClosed(pc, mayCloseSteam, log, Lang.T("параметри запуску Steam не повернуто", "Steam launch options were not restored"), () =>
                {
                    foreach (var item in backup.Steam.Where(entry => File.Exists(entry.ConfigPath)))
                    {
                        if (item.HadLaunchOptions)
                        {
                            WriteLaunchOptions(item.ConfigPath, item.LaunchOptions);
                        }
                        else
                        {
                            SteamClient.WriteText(item.ConfigPath, Vdf.RemoveLaunchOptions(File.ReadAllText(item.ConfigPath), SteamClient.AppId));
                        }
                    }
                    log(Lang.T("✓ Параметри запуску Steam повернуто.", "✓ Steam launch options restored."));
                    steamRestored = true;
                });
            }

            if (steamRestored)
            {
                BackupData.Delete();
            }
            else
            {
                backup.Driver.Clear();
                backup.Save();
            }
        }

        private static void ApplyGame(PatchPlan plan, BackupData backup, Action<string> log)
        {
            var preset = plan.Game;
            if (preset == null)
            {
                return;
            }
            var savePath = GameSettings.SavePath;
            var iniPath = GameSettings.IniPath;
            if (!File.Exists(savePath))
            {
                log(Lang.T("• Графіка гри: SMITE 2 тут ще не запускалась. Запусти її один раз, закрий і натисни «Оптимізувати» знову.", "• Game graphics: SMITE 2 has not been launched on this PC yet. Launch it once, close it and click \"Optimize\" again."));
                return;
            }

            SettingsSave save;
            try
            {
                save = SettingsSave.Parse(File.ReadAllBytes(savePath));
            }
            catch (FormatException)
            {
                log(Lang.T("• Графіка гри: файл налаштувань має незнайомий формат (мабуть, після оновлення гри), тому його не чіпали.", "• Game graphics: the settings file has an unknown format (probably after a game update), so it was left untouched."));
                return;
            }

            if (!backup.GameFiles)
            {
                Directory.CreateDirectory(BackupData.Folder);
                File.Copy(savePath, BackupData.GameSaveCopy, overwrite: true);
                if (File.Exists(iniPath))
                {
                    File.Copy(iniPath, BackupData.GameIniCopy, overwrite: true);
                }
                backup.GameFiles = true;
                backup.GameSavePath = savePath;
                backup.GameIniPath = iniPath;
                backup.Save();
            }

            foreach (var pair in preset.SaveValues())
            {
                save.Set(pair.Key, pair.Value);
            }
            var bytes = save.ToBytes();
            if (!preset.Matches(SettingsSave.Parse(bytes)))
            {
                throw new InvalidOperationException(Lang.T("Не вдалося підготувати налаштування гри.", "Could not prepare the game settings."));
            }
            WriteBytes(savePath, bytes);

            if (File.Exists(iniPath))
            {
                var raw = File.ReadAllBytes(iniPath);
                var bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
                var text = new System.Text.UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
                var updated = GameSettings.SetScalability(text, preset.IniValues());
                File.WriteAllText(iniPath + ".smite2-patcher.tmp", updated, new System.Text.UTF8Encoding(bom));
                File.Copy(iniPath + ".smite2-patcher.tmp", iniPath, overwrite: true);
                File.Delete(iniPath + ".smite2-patcher.tmp");
            }
            log(Lang.T("✓ Графіка гри: " + preset.TierText + " пресет, DLSS " + preset.DlssText + ".", "✓ Game graphics: " + preset.TierText + " preset, DLSS " + preset.DlssText + "."));
        }

        private static void WriteBytes(string path, byte[] bytes)
        {
            var temporary = path + ".smite2-patcher.tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Copy(temporary, path, overwrite: true);
            File.Delete(temporary);
        }

        private static List<DriverBackup> StaleDriverSettings(BackupData backup, PatchPlan plan) =>
            backup.Driver.Where(item => plan.Driver.All(change => change.Id != item.Id)).ToList();

        public static bool RestoreNeedsSteamClosed() => (LoadBackup()?.Steam.Count ?? 0) > 0;

        private static void WithSteamClosed(SystemSnapshot pc, bool mayCloseSteam, Action<string> log, string skippedText, Action action)
        {
            var steamPath = pc.SteamPath!;
            var wasRunning = SteamClient.IsRunning();
            if (wasRunning && !mayCloseSteam)
            {
                log(Lang.T("• Steam не закривали, тому ", "• Steam was left open, so ") + skippedText + ".");
                return;
            }
            if (wasRunning)
            {
                log(Lang.T("Закриваю Steam…", "Closing Steam…"));
                if (!SteamClient.Shutdown(steamPath, SteamExitTimeout))
                {
                    throw new InvalidOperationException(Lang.T("Steam не закрився. Закрий його сам (значок біля годинника → Вихід) і натисни ще раз.", "Steam did not close. Close it yourself (tray icon → Exit) and click again."));
                }
            }

            try
            {
                action();
            }
            finally
            {
                if (wasRunning)
                {
                    SteamClient.Start(steamPath);
                    log(Lang.T("Steam запущено знову.", "Steam started again."));
                }
            }
        }

        private static void WriteLaunchOptions(string configPath, string options)
        {
            var updated = Vdf.SetLaunchOptions(File.ReadAllText(configPath), SteamClient.AppId, options);
            SteamClient.WriteText(configPath, updated);
            var check = Vdf.GetLaunchOptions(File.ReadAllText(configPath), SteamClient.AppId, out _);
            if ((check ?? "") != options)
            {
                throw new InvalidOperationException(Lang.T("Не вдалося записати параметри запуску Steam.", "Could not write the Steam launch options."));
            }
        }

        /// <summary>Set only by the end-to-end test, which works on copies and may run while the game is open.</summary>
        public static Func<bool>? GameRunningOverride;

        private static void EnsureGameClosed()
        {
            var running = GameRunningOverride?.Invoke() ?? (SystemProbe.IsRunning("Hemingway-Win64-Shipping") || SystemProbe.IsRunning("Hemingway"));
            if (running)
            {
                throw new InvalidOperationException(Lang.T("Спершу закрий SMITE 2, потім натисни ще раз.", "Close SMITE 2 first, then click again."));
            }
        }

        private static BackupData? LoadBackup()
        {
            try
            {
                return BackupData.Load();
            }
            catch (System.Runtime.Serialization.SerializationException)
            {
                throw new InvalidOperationException(Lang.T("Файл резервної копії пошкоджено: ", "The backup file is damaged: ") + BackupData.FilePath);
            }
        }
    }
}
