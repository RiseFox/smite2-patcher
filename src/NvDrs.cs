using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Smite2Patcher
{
    /// <summary>NVIDIA driver-profile setting IDs and values (NvApiDriverSettings.h).</summary>
    internal static class Nv
    {
        public const string GameExe = "Hemingway-Win64-Shipping.exe";

        public const uint SrOverride = 0x10E41E01;
        public const uint SrMode = 0x10AFB768;
        public const uint SrPreset = 0x10E41DF3;
        public const uint FgOverride = 0x10E41E03;
        public const uint FgMode = 0x10308298;
        public const uint FgMultiFrameCount = 0x104D6667;
        public const uint FrameRateLimit = 0x10835002;

        public const uint On = 1;
        public const uint SrModeGameControlled = 3;
        public const uint PresetK = 11;
        public const uint FgModeOn = 2;
        public const uint OneGeneratedFrame = 1;

        // DLSS 4 overrides (preset K, FG multiplier) arrived in driver 572.
        public const uint MinOverrideDriver = 57200;
    }

    internal sealed class NvSetting
    {
        public uint Id;
        public bool Existed;
        public uint Value;
    }

    internal sealed class NvWrite
    {
        public NvWrite(uint id, uint value, bool remove)
        {
            Id = id;
            Value = value;
            Remove = remove;
        }

        public uint Id { get; }
        public uint Value { get; }
        public bool Remove { get; }
    }

    internal sealed class NvDrsException : Exception
    {
        public NvDrsException(string message) : base(message)
        {
        }
    }

    /// <summary>Thin wrapper over SmitePatchNv.dll, which sits next to the EXE.</summary>
    internal static class NvDrs
    {
        private const string Dll = "SmitePatchNv.dll";
        private const int TextLength = 512;

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int SmitePatchNv_DriverVersion(out uint version, StringBuilder error, uint errorLength);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int SmitePatchNv_Read(
            string executable, uint[] ids, uint count, [Out] uint[] existed, [Out] uint[] values,
            StringBuilder profileName, uint profileNameLength, StringBuilder error, uint errorLength);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int SmitePatchNv_Write(
            string executable, uint[] ids, uint[] values, uint[] remove, uint count,
            StringBuilder error, uint errorLength);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int SmitePatchNv_DeleteFallbackProfile(string executable, StringBuilder error, uint errorLength);

        /// <summary>Driver version as NVAPI reports it, e.g. 61742 for 617.42.</summary>
        public static uint DriverVersion()
        {
            var error = new StringBuilder(TextLength);
            Check(SmitePatchNv_DriverVersion(out uint version, error, TextLength), error);
            return version;
        }

        public static List<NvSetting> Read(string executable, IReadOnlyList<uint> ids, out string profileName)
        {
            var idArray = ids.ToArray();
            var existed = new uint[idArray.Length];
            var values = new uint[idArray.Length];
            var name = new StringBuilder(TextLength);
            var error = new StringBuilder(TextLength);
            Check(SmitePatchNv_Read(executable, idArray, (uint)idArray.Length, existed, values, name, TextLength, error, TextLength), error);
            profileName = name.ToString();
            return idArray.Select((id, index) => new NvSetting { Id = id, Existed = existed[index] != 0, Value = values[index] }).ToList();
        }

        public static void Write(string executable, IReadOnlyList<NvWrite> writes)
        {
            var error = new StringBuilder(TextLength);
            Check(SmitePatchNv_Write(
                executable,
                writes.Select(item => item.Id).ToArray(),
                writes.Select(item => item.Value).ToArray(),
                writes.Select(item => item.Remove ? 1u : 0u).ToArray(),
                (uint)writes.Count,
                error,
                TextLength), error);
        }

        public static void DeleteFallbackProfile(string executable)
        {
            var error = new StringBuilder(TextLength);
            Check(SmitePatchNv_DeleteFallbackProfile(executable, error, TextLength), error);
        }

        public static string FormatDriver(uint version) => (version / 100) + "." + (version % 100).ToString("00");

        private static void Check(int status, StringBuilder error)
        {
            if (status != 0)
            {
                throw new NvDrsException(error.Length > 0 ? error.ToString() : "NVAPI error " + status);
            }
        }
    }
}
