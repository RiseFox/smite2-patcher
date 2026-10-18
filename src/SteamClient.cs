using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Smite2Patcher
{
    internal static class SteamClient
    {
        public const string AppId = "2437170";

        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string? FindSteamPath()
        {
            var candidates = new List<string?>();
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                candidates.Add(key?.GetValue("SteamPath") as string);
            }
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
            {
                candidates.Add(key?.GetValue("InstallPath") as string);
            }

            return candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path!.Replace('/', '\\')))
                .FirstOrDefault(path => File.Exists(Path.Combine(path, "steam.exe")));
        }

        /// <summary>One localconfig.vdf per Steam account that has signed in on this PC.</summary>
        public static List<string> LocalConfigs(string steamPath)
        {
            var userdata = Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userdata))
            {
                return new List<string>();
            }
            return Directory.GetDirectories(userdata)
                .Select(directory => Path.Combine(directory, "config", "localconfig.vdf"))
                .Where(File.Exists)
                .ToList();
        }

        public static string AccountOf(string localConfig) =>
            Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(localConfig)) ?? "");

        public static bool IsGameInstalled(string steamPath)
        {
            var libraries = new List<string> { steamPath };
            var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFile))
            {
                try
                {
                    var root = Vdf.Parse(File.ReadAllText(libraryFile));
                    var folders = root.Child("libraryfolders")?.Children ?? new List<VdfNode>();
                    libraries.AddRange(folders.Select(folder => folder.Child("path")?.Value).Where(path => path != null).Select(path => path!));
                }
                catch (FormatException)
                {
                }
            }
            return libraries.Any(library => File.Exists(Path.Combine(library, "steamapps", "appmanifest_" + AppId + ".acf")));
        }

        /// <summary>Set only by the end-to-end test, which must never close the real Steam.</summary>
        public static Func<bool>? RunningOverride;

        public static bool IsRunning() => RunningOverride?.Invoke() ?? Process.GetProcessesByName("steam").Length > 0;

        /// <summary>Asks Steam to exit normally and waits; Steam rewrites localconfig.vdf on exit.</summary>
        public static bool Shutdown(string steamPath, TimeSpan timeout)
        {
            if (!IsRunning())
            {
                return true;
            }
            Process.Start(new ProcessStartInfo(Path.Combine(steamPath, "steam.exe"), "-shutdown") { UseShellExecute = false })?.Dispose();
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!IsRunning())
                {
                    // Give Steam's final config flush a moment to finish.
                    Thread.Sleep(1500);
                    return true;
                }
                Thread.Sleep(500);
            }
            return !IsRunning();
        }

        public static void Start(string steamPath) =>
            Process.Start(new ProcessStartInfo(Path.Combine(steamPath, "steam.exe")) { UseShellExecute = true })?.Dispose();

        public static void WriteText(string path, string text)
        {
            var temporary = path + ".smite2-patcher.tmp";
            File.WriteAllText(temporary, text, Utf8NoBom);
            File.Copy(temporary, path, overwrite: true);
            File.Delete(temporary);
        }
    }
}
