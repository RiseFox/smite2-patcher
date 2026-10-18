using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Smite2Patcher
{
    [DataContract]
    internal sealed class DriverBackup
    {
        [DataMember] public uint Id;
        [DataMember] public bool Existed;
        [DataMember] public uint Value;
    }

    [DataContract]
    internal sealed class SteamBackup
    {
        [DataMember] public string ConfigPath = "";
        [DataMember] public bool HadLaunchOptions;
        [DataMember] public string LaunchOptions = "";
    }

    /// <summary>The state before the first patch. Later patches never overwrite an entry.</summary>
    [DataContract]
    internal sealed class BackupData
    {
        [DataMember] public int Version = 1;
        [DataMember] public string Created = "";
        [DataMember] public List<DriverBackup> Driver = new List<DriverBackup>();
        [DataMember] public List<SteamBackup> Steam = new List<SteamBackup>();

        // Copies of the game's two graphics files as they were before the first patch.
        [DataMember] public bool GameFiles;
        [DataMember] public string GameSavePath = "";
        [DataMember] public string GameIniPath = "";

        public static string GameSaveCopy => Path.Combine(Folder, "HWGameUserSettings.sav");

        public static string GameIniCopy => Path.Combine(Folder, "GameUserSettings.ini");

        /// <summary>Set only by the end-to-end test.</summary>
        public static string? FolderOverride;

        public static string Folder => FolderOverride ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMITE2-Patcher");

        public static string FilePath => Path.Combine(Folder, "backup.json");

        public static BackupData? Load()
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }
            using (var stream = File.OpenRead(FilePath))
            {
                var data = (BackupData?)new DataContractJsonSerializer(typeof(BackupData)).ReadObject(stream);
                if (data != null)
                {
                    data.Driver = data.Driver ?? new List<DriverBackup>();
                    data.Steam = data.Steam ?? new List<SteamBackup>();
                }
                return data;
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            var temporary = FilePath + ".tmp";
            using (var stream = File.Create(temporary))
            {
                new DataContractJsonSerializer(typeof(BackupData)).WriteObject(stream, this);
            }
            File.Copy(temporary, FilePath, overwrite: true);
            File.Delete(temporary);
        }

        /// <summary>Removes the backup and every copy kept next to it.</summary>
        public static void Delete()
        {
            if (Directory.Exists(Folder))
            {
                foreach (var file in Directory.GetFiles(Folder))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
