using System;
using System.Collections.Generic;
using System.IO;

namespace Smite2Patcher
{
    /// <summary>
    /// What the most recent SMITE 2 session actually used, read from its log:
    /// the GPU, whether DLSS started, who owned the swap chain, and FG.
    /// </summary>
    internal sealed class LastGame
    {
        public DateTime When;
        public string Adapter = "";
        public bool AdapterIsNvidia;
        public string SwapChainProvider = "";
        public bool XeFgFlagObeyed;
        public string DlssInput = "";
        public string DlssOutput = "";
        public int SrPresetApplied = -1;
        public bool FrameGenerationUsed;

        public bool DlssActive => DlssOutput.Length > 0;
        public bool XeFgOwnsFrames => SwapChainProvider.IndexOf("XeFG", StringComparison.OrdinalIgnoreCase) >= 0;

        public static string DefaultLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMITE2Alpha", "Saved", "Logs", "Hemingway.log");

        public static LastGame? Read(string? path = null)
        {
            path = path ?? DefaultLogPath;
            if (!File.Exists(path))
            {
                return null;
            }

            var game = new LastGame { When = File.GetLastWriteTime(path) };
            var adapters = new Dictionary<string, string>();
            string? chosen = null;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    int index;
                    if ((index = line.IndexOf("Found D3D12 adapter ", StringComparison.Ordinal)) >= 0)
                    {
                        // Found D3D12 adapter 0: NVIDIA GeForce RTX 4070 (VendorId: 10de, ...
                        var rest = line.Substring(index + 20);
                        var colon = rest.IndexOf(':');
                        var details = rest.IndexOf(" (", StringComparison.Ordinal);
                        if (colon > 0 && details > colon + 2)
                        {
                            adapters[rest.Substring(0, colon)] = rest.Substring(colon + 2, details - colon - 2);
                        }
                    }
                    else if ((index = line.IndexOf("Chosen D3D12 Adapter Id = ", StringComparison.Ordinal)) >= 0)
                    {
                        chosen = line.Substring(index + 26).Trim();
                    }
                    else if ((index = line.IndexOf("Found a custom swapchain provider: '", StringComparison.Ordinal)) >= 0)
                    {
                        var start = index + 36;
                        var end = line.IndexOf('\'', start);
                        if (end > start)
                        {
                            game.SwapChainProvider = line.Substring(start, end - start);
                        }
                    }
                    else if (line.IndexOf("XeFG override swap chain disabled by command line option", StringComparison.Ordinal) >= 0)
                    {
                        game.XeFgFlagObeyed = true;
                    }
                    else if ((index = line.IndexOf("NGX DLSS Feature  SrcRect=[0x0->", StringComparison.Ordinal)) >= 0 &&
                             line.IndexOf("Creating", StringComparison.Ordinal) >= 0)
                    {
                        game.DlssInput = Between(line, "SrcRect=[0x0->", "]");
                        game.DlssOutput = Between(line, "DestRect=[0x0->", "]");
                    }
                    else if ((index = line.IndexOf("Feature [SuperSampling] Override Reported: ModelPreset, Value: ", StringComparison.Ordinal)) >= 0 &&
                             line.IndexOf("Applied: Yes", StringComparison.Ordinal) >= 0)
                    {
                        var digits = line.Substring(index + 63);
                        var comma = digits.IndexOf(',');
                        if (comma > 0 && int.TryParse(digits.Substring(0, comma), out int preset))
                        {
                            game.SrPresetApplied = preset;
                        }
                    }
                    else if (line.IndexOf("DLSS-G interpolation state changed from disabled to enabled", StringComparison.Ordinal) >= 0)
                    {
                        game.FrameGenerationUsed = true;
                    }
                }
            }

            if (chosen != null && adapters.TryGetValue(chosen, out string? name))
            {
                game.Adapter = name;
            }
            game.AdapterIsNvidia = game.Adapter.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;
            return game;
        }

        public static string PresetLetter(int value) => value >= 1 && value <= 26 ? ((char)('A' + value - 1)).ToString() : value.ToString();

        private static string Between(string text, string start, string end)
        {
            var from = text.IndexOf(start, StringComparison.Ordinal);
            if (from < 0)
            {
                return "";
            }
            from += start.Length;
            var to = text.IndexOf(end, from, StringComparison.Ordinal);
            return to > from ? text.Substring(from, to - from).Replace('x', '×') : "";
        }
    }
}
