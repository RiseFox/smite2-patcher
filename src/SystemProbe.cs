using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Smite2Patcher
{
    internal sealed class GpuInfo
    {
        public string Name = "";
        public bool IsNvidia;
        public bool IsRtx;

        /// <summary>2 for RTX 20xx ... 5 for RTX 50xx; 0 when unknown.</summary>
        public int RtxGeneration;

        public uint DriverVersion;
    }

    internal sealed class SystemSnapshot
    {
        public GpuInfo Gpu = new GpuInfo();
        public string? DriverError;

        /// <summary>DLSS-SR preset already in the game's driver profile; 0 when none is set.</summary>
        public uint DriverPreset;

        /// <summary>Newest DLSS Super Resolution in NVIDIA's own store, which the driver override loads; "" when none.</summary>
        public string NvidiaDlssVersion = "";

        public int RefreshHz;
        public int Width;
        public int Height;

        /// <summary>True/false when Windows stores the choice explicitly; null means the OS default.</summary>
        public bool? HardwareScheduling;

        public string? SteamPath;
        public bool GameInstalled;
        public bool GameRunning;
        public bool SteamRunning;
        public LastGame? LastGame;
    }

    internal static class SystemProbe
    {
        public static SystemSnapshot Capture()
        {
            var snapshot = new SystemSnapshot { Gpu = ReadGpu() };
            if (snapshot.Gpu.IsNvidia)
            {
                try
                {
                    snapshot.Gpu.DriverVersion = NvDrs.DriverVersion();
                }
                catch (Exception error) when (error is NvDrsException || error is DllNotFoundException || error is EntryPointNotFoundException)
                {
                    snapshot.DriverError = error.Message;
                }
            }
            if (snapshot.DriverError == null && snapshot.Gpu.DriverVersion > 0)
            {
                try
                {
                    var preset = NvDrs.Read(PatchEngine.Executable, new[] { Nv.SrPreset }, out _)[0];
                    snapshot.DriverPreset = preset.Existed ? preset.Value : 0;
                }
                catch (NvDrsException)
                {
                }
            }

            if (snapshot.Gpu.IsRtx)
            {
                snapshot.NvidiaDlssVersion = ReadNvidiaDlssVersion();
            }

            ReadPrimaryDisplay(snapshot);
            snapshot.HardwareScheduling = ReadHardwareScheduling();
            snapshot.SteamPath = SteamClient.FindSteamPath();
            snapshot.GameInstalled = snapshot.SteamPath != null && SteamClient.IsGameInstalled(snapshot.SteamPath);
            snapshot.GameRunning = IsRunning("Hemingway-Win64-Shipping") || IsRunning("Hemingway");
            snapshot.SteamRunning = SteamClient.IsRunning();
            try
            {
                snapshot.LastGame = LastGame.Read();
            }
            catch (IOException)
            {
            }
            return snapshot;
        }

        public static bool IsRunning(string processName) => Process.GetProcessesByName(processName).Length > 0;

        private static string ReadNvidiaDlssVersion()
        {
            try
            {
                var store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NVIDIA", "NGX", "models", "dlss", "versions");
                if (!Directory.Exists(store))
                {
                    return "";
                }
                var newest = Directory.GetDirectories(store)
                    .Select(dir => (Dir: dir, Number: long.TryParse(Path.GetFileName(dir), out long number) ? number : -1))
                    .Where(item => item.Number > 0)
                    .OrderByDescending(item => item.Number)
                    .FirstOrDefault();
                if (newest.Dir == null)
                {
                    return "";
                }
                var binary = Directory.GetFiles(newest.Dir, "*.bin", SearchOption.AllDirectories).FirstOrDefault();
                var version = binary != null ? LastGame.FileVersion(binary) : "";
                // Folder names pack the version as major << 16 | minor << 8 | patch (20318480 = 310.9.16).
                return version.Length > 0 ? version : (newest.Number >> 16) + "." + ((newest.Number >> 8) & 0xFF) + "." + (newest.Number & 0xFF);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return "";
            }
        }

        private static GpuInfo ReadGpu()
        {
            var best = new GpuInfo();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController"))
                {
                    foreach (var item in searcher.Get().Cast<ManagementObject>())
                    {
                        var name = (item["Name"] as string ?? "").Trim();
                        var vendor = item["AdapterCompatibility"] as string ?? "";
                        var nvidia = name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     vendor.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!nvidia && best.Name.Length > 0)
                        {
                            continue;
                        }

                        var candidate = new GpuInfo { Name = name, IsNvidia = nvidia };
                        candidate.IsRtx = nvidia && name.IndexOf("RTX", StringComparison.OrdinalIgnoreCase) >= 0;
                        var match = Regex.Match(name, @"RTX\s*(\d)0\d0", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            candidate.RtxGeneration = int.Parse(match.Groups[1].Value);
                        }

                        // Prefer an NVIDIA RTX adapter over an iGPU or an older NVIDIA card.
                        if (!best.IsNvidia || (candidate.IsRtx && candidate.RtxGeneration > best.RtxGeneration))
                        {
                            best = candidate;
                        }
                    }
                }
            }
            catch (ManagementException)
            {
            }
            return best;
        }

        private static void ReadPrimaryDisplay(SystemSnapshot snapshot)
        {
            var mode = new DevMode { dmSize = (short)Marshal.SizeOf(typeof(DevMode)) };
            if (EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
            {
                snapshot.RefreshHz = mode.dmDisplayFrequency;
                snapshot.Width = mode.dmPelsWidth;
                snapshot.Height = mode.dmPelsHeight;
            }
        }

        private static bool? ReadHardwareScheduling()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
            {
                if (key?.GetValue("HwSchMode") is int value)
                {
                    return value == 2;
                }
            }
            return null;
        }

        private const int EnumCurrentSettings = -1;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNumber, ref DevMode devMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
    }
}
