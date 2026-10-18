using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Smite2Patcher
{
    /// <summary>
    /// The graphics map in HWGameUserSettings.sav: GVAS property `SavedSettingsConfig`,
    /// MapProperty&lt;NameProperty, StrProperty&gt; with the UE 5.4+ tag (type tree, int32
    /// size, uint8 flags). Writes keep the rest of the file byte-for-byte; like the game,
    /// updated keys stay in place and new keys go to the end.
    /// </summary>
    internal sealed class SettingsSave
    {
        private static readonly byte[] Marker = Encoding.ASCII.GetBytes("\x14\0\0\0SavedSettingsConfig\0");

        private readonly byte[] data;
        private readonly int sizeOffset;
        private readonly int bodyEnd;
        private readonly int removedCount;

        public readonly List<KeyValuePair<string, string>> Entries;

        private SettingsSave(byte[] data, int sizeOffset, int bodyEnd, int removedCount, List<KeyValuePair<string, string>> entries)
        {
            this.data = data;
            this.sizeOffset = sizeOffset;
            this.bodyEnd = bodyEnd;
            this.removedCount = removedCount;
            Entries = entries;
        }

        public static SettingsSave Parse(byte[] data)
        {
            var start = IndexOf(data, Marker);
            if (start < 0)
            {
                throw new FormatException("SavedSettingsConfig not found");
            }
            var offset = start + Marker.Length;
            if (ReadString(data, ref offset) != "MapProperty")
            {
                throw new FormatException("SavedSettingsConfig is not a map");
            }
            var parameters = BitConverter.ToInt32(data, offset);
            offset += 4;
            var types = new List<string>();
            for (var i = 0; i < parameters; i++)
            {
                types.Add(ReadString(data, ref offset));
                if (BitConverter.ToInt32(data, offset) != 0)
                {
                    throw new FormatException("Unexpected nested map type");
                }
                offset += 4;
            }
            if (types.Count != 2 || types[0] != "NameProperty" || types[1] != "StrProperty")
            {
                throw new FormatException("Unexpected map types: " + string.Join(", ", types));
            }

            var sizeOffset = offset;
            var size = BitConverter.ToInt32(data, offset);
            offset += 5; // int32 size + uint8 flags
            var bodyStart = offset;
            var removed = BitConverter.ToInt32(data, offset);
            var count = BitConverter.ToInt32(data, offset + 4);
            offset += 8;
            var entries = new List<KeyValuePair<string, string>>();
            for (var i = 0; i < count; i++)
            {
                var key = ReadString(data, ref offset);
                var value = ReadString(data, ref offset);
                entries.Add(new KeyValuePair<string, string>(key, value));
            }
            if (removed != 0 || offset - bodyStart != size)
            {
                throw new FormatException("Settings map size does not match its contents");
            }
            return new SettingsSave(data, sizeOffset, offset, removed, entries);
        }

        /// <summary>The size field, flags and map body as parsed; compared with game-written files in tests.</summary>
        public byte[] MapSection()
        {
            var section = new byte[bodyEnd - sizeOffset];
            Array.Copy(data, sizeOffset, section, 0, section.Length);
            return section;
        }

        public string? Get(string key)
        {
            foreach (var entry in Entries)
            {
                if (entry.Key == key)
                {
                    return entry.Value;
                }
            }
            return null;
        }

        /// <summary>null removes the key, which the game reads as its default.</summary>
        public void Set(string key, string? value)
        {
            var index = Entries.FindIndex(entry => entry.Key == key);
            if (value == null)
            {
                if (index >= 0)
                {
                    Entries.RemoveAt(index);
                }
            }
            else if (index >= 0)
            {
                Entries[index] = new KeyValuePair<string, string>(key, value);
            }
            else
            {
                Entries.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        public byte[] ToBytes()
        {
            var body = new MemoryStream();
            body.Write(BitConverter.GetBytes(removedCount), 0, 4);
            body.Write(BitConverter.GetBytes(Entries.Count), 0, 4);
            foreach (var entry in Entries)
            {
                WriteString(body, entry.Key);
                WriteString(body, entry.Value);
            }

            var result = new MemoryStream();
            result.Write(data, 0, sizeOffset);
            result.Write(BitConverter.GetBytes((int)body.Length), 0, 4);
            result.WriteByte(data[sizeOffset + 4]);
            body.WriteTo(result);
            result.Write(data, bodyEnd, data.Length - bodyEnd);
            return result.ToArray();
        }

        private static string ReadString(byte[] data, ref int offset)
        {
            var length = BitConverter.ToInt32(data, offset);
            offset += 4;
            if (length == 0)
            {
                return "";
            }
            if (length < 0)
            {
                var text = Encoding.Unicode.GetString(data, offset, -length * 2);
                offset += -length * 2;
                return text.TrimEnd('\0');
            }
            var ascii = Encoding.UTF8.GetString(data, offset, length);
            offset += length;
            return ascii.TrimEnd('\0');
        }

        private static void WriteString(Stream stream, string value)
        {
            if (value.All(character => character < 128))
            {
                var bytes = Encoding.ASCII.GetBytes(value + "\0");
                stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                stream.Write(bytes, 0, bytes.Length);
            }
            else
            {
                var bytes = Encoding.Unicode.GetBytes(value + "\0");
                stream.Write(BitConverter.GetBytes(-(bytes.Length / 2)), 0, 4);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        private static int IndexOf(byte[] data, byte[] pattern)
        {
            for (var i = 0; i <= data.Length - pattern.Length; i++)
            {
                var match = true;
                for (var j = 0; j < pattern.Length && match; j++)
                {
                    match = data[i + j] == pattern[j];
                }
                if (match)
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>Paths of SMITE 2's graphics files and the ini side of a preset.</summary>
    internal static class GameSettings
    {
        // Set only by the end-to-end test.
        public static string? SaveOverride;
        public static string? IniOverride;

        private static string Saved => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMITE2Alpha", "Saved");

        public static string SavePath => SaveOverride ?? Path.Combine(Saved, "SaveGames", "HWGameUserSettings.sav");

        public static string IniPath => IniOverride ?? Path.Combine(Saved, "Config", "Windows", "GameUserSettings.ini");

        /// <summary>Sets keys inside [ScalabilityGroups], keeping every other line as it was.</summary>
        public static string SetScalability(string text, IReadOnlyDictionary<string, int> values)
        {
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            var trailingNewline = lines.Count > 0 && lines[lines.Count - 1].Length == 0;
            if (trailingNewline)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            var section = lines.FindIndex(line => line.Trim() == "[ScalabilityGroups]");
            if (section < 0)
            {
                lines.Insert(0, "[ScalabilityGroups]");
                lines.Insert(1, "");
                section = 0;
            }
            var end = section + 1;
            while (end < lines.Count && !lines[end].TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                end++;
            }

            var lastKey = section;
            var pending = values.ToDictionary(pair => pair.Key, pair => pair.Value);
            for (var i = section + 1; i < end; i++)
            {
                var equals = lines[i].IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }
                lastKey = i;
                var key = lines[i].Substring(0, equals).Trim();
                if (pending.TryGetValue(key, out int value))
                {
                    lines[i] = key + "=" + value;
                    pending.Remove(key);
                }
            }
            lines.InsertRange(lastKey + 1, pending.Select(pair => pair.Key + "=" + pair.Value));
            return string.Join(newline, lines) + (trailingNewline ? newline : "");
        }

        public static Dictionary<string, int> ReadScalability(string text)
        {
            var result = new Dictionary<string, int>();
            var inSection = false;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inSection = line == "[ScalabilityGroups]";
                    continue;
                }
                var equals = line.IndexOf('=');
                if (inSection && equals > 0 && int.TryParse(line.Substring(equals + 1), out int value))
                {
                    result[line.Substring(0, equals)] = value;
                }
            }
            return result;
        }
    }
}
