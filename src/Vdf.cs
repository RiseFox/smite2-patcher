using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Smite2Patcher
{
    internal sealed class VdfNode
    {
        public string Key = "";
        public string? Value;
        public List<VdfNode>? Children;

        // Raw span of a string value between its quotes, used for in-place edits.
        public int ValueStart;
        public int ValueEnd;

        // Index of the closing brace of a block.
        public int BlockClose;

        public VdfNode? Child(string key) =>
            Children?.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Minimal Valve KeyValues reader. Edits are spliced into the original text so
    /// the rest of Steam's file stays byte-for-byte as it was.
    /// </summary>
    internal static class Vdf
    {
        private static readonly string[] AppsPath = { "UserLocalConfigStore", "Software", "Valve", "Steam", "apps" };

        public static VdfNode Parse(string text)
        {
            var parser = new Parser(text);
            var root = new VdfNode { Children = new List<VdfNode>() };
            parser.ParseList(root, topLevel: true);
            return root;
        }

        public static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>Returns null when the app has no LaunchOptions entry.</summary>
        public static string? GetLaunchOptions(string text, string appId, out bool appFound)
        {
            var app = FindApps(Parse(text))?.Child(appId);
            appFound = app?.Children != null;
            return app?.Child("LaunchOptions")?.Value;
        }

        public static string SetLaunchOptions(string text, string appId, string launchOptions)
        {
            var apps = FindApps(Parse(text)) ?? throw new InvalidOperationException("Steam config has no apps section.");
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var escaped = Escape(launchOptions);
            var app = apps.Child(appId);

            if (app?.Children == null)
            {
                var indent = IndentOf(text, apps.BlockClose) + "\t";
                var block = indent + "\"" + appId + "\"" + newline +
                            indent + "{" + newline +
                            indent + "\t\"LaunchOptions\"\t\t\"" + escaped + "\"" + newline +
                            indent + "}" + newline;
                return text.Insert(LineStart(text, apps.BlockClose), block);
            }

            var existing = app.Child("LaunchOptions");
            if (existing?.Value != null)
            {
                return text.Substring(0, existing.ValueStart) + escaped + text.Substring(existing.ValueEnd);
            }

            var line = IndentOf(text, app.BlockClose) + "\t\"LaunchOptions\"\t\t\"" + escaped + "\"" + newline;
            return text.Insert(LineStart(text, app.BlockClose), line);
        }

        /// <summary>Removes the whole LaunchOptions line, restoring an app that never had one.</summary>
        public static string RemoveLaunchOptions(string text, string appId)
        {
            var existing = FindApps(Parse(text))?.Child(appId)?.Child("LaunchOptions");
            if (existing?.Value == null)
            {
                return text;
            }
            var start = LineStart(text, existing.ValueStart);
            var end = text.IndexOf('\n', existing.ValueEnd);
            return text.Remove(start, (end < 0 ? text.Length : end + 1) - start);
        }

        private static VdfNode? FindApps(VdfNode root)
        {
            VdfNode? node = root;
            foreach (var key in AppsPath)
            {
                node = node?.Child(key);
            }
            return node?.Children != null ? node : null;
        }

        private static int LineStart(string text, int index)
        {
            var newline = text.LastIndexOf('\n', Math.Max(0, index - 1));
            return newline + 1;
        }

        private static string IndentOf(string text, int index)
        {
            var start = LineStart(text, index);
            var end = start;
            while (end < index && (text[end] == '\t' || text[end] == ' '))
            {
                end++;
            }
            return text.Substring(start, end - start);
        }

        private sealed class Parser
        {
            private readonly string text;
            private int position;

            public Parser(string text) => this.text = text;

            public void ParseList(VdfNode parent, bool topLevel)
            {
                var children = parent.Children!;
                while (true)
                {
                    SkipTrivia();
                    if (position >= text.Length)
                    {
                        if (!topLevel)
                        {
                            throw new FormatException("Unexpected end of Steam config.");
                        }
                        return;
                    }

                    if (text[position] == '}')
                    {
                        if (topLevel)
                        {
                            throw new FormatException("Unexpected '}' in Steam config.");
                        }
                        parent.BlockClose = position;
                        position++;
                        return;
                    }

                    var key = ReadToken(out _, out _);
                    SkipTrivia();
                    if (position < text.Length && text[position] == '{')
                    {
                        position++;
                        var node = new VdfNode { Key = key, Children = new List<VdfNode>() };
                        ParseList(node, topLevel: false);
                        children.Add(node);
                    }
                    else
                    {
                        var value = ReadToken(out int start, out int end);
                        children.Add(new VdfNode { Key = key, Value = value, ValueStart = start, ValueEnd = end });
                    }
                }
            }

            private string ReadToken(out int rawStart, out int rawEnd)
            {
                if (position >= text.Length)
                {
                    throw new FormatException("Unexpected end of Steam config.");
                }

                if (text[position] != '"')
                {
                    rawStart = position;
                    while (position < text.Length && !char.IsWhiteSpace(text[position]) &&
                           text[position] != '{' && text[position] != '}' && text[position] != '"')
                    {
                        position++;
                    }
                    rawEnd = position;
                    return text.Substring(rawStart, rawEnd - rawStart);
                }

                position++;
                rawStart = position;
                var value = new StringBuilder();
                while (position < text.Length && text[position] != '"')
                {
                    if (text[position] == '\\' && position + 1 < text.Length)
                    {
                        var next = text[position + 1];
                        value.Append(next == 'n' ? '\n' : next == 't' ? '\t' : next);
                        position += 2;
                    }
                    else
                    {
                        value.Append(text[position]);
                        position++;
                    }
                }
                if (position >= text.Length)
                {
                    throw new FormatException("Unterminated string in Steam config.");
                }
                rawEnd = position;
                position++;
                return value.ToString();
            }

            private void SkipTrivia()
            {
                while (position < text.Length)
                {
                    var character = text[position];
                    if (char.IsWhiteSpace(character))
                    {
                        position++;
                    }
                    else if (character == '/' && position + 1 < text.Length && text[position + 1] == '/')
                    {
                        while (position < text.Length && text[position] != '\n')
                        {
                            position++;
                        }
                    }
                    else if (character == '[')
                    {
                        // Conditional tags such as [$WIN32].
                        while (position < text.Length && text[position] != ']')
                        {
                            position++;
                        }
                        position++;
                    }
                    else
                    {
                        return;
                    }
                }
            }
        }
    }

    /// <summary>SMITE 2 launch-option rules for Steam.</summary>
    internal static class LaunchArgs
    {
        /// <summary>Stops Intel XeFG from taking over the swap chain; the game logs that it obeys this switch.</summary>
        public const string XeFgFlag = "-XeFGOverrideSwapChainDisabled";

        // This SMITE 2 build ignores both; older tools left long, useless copies behind.
        private static readonly string[] IgnoredPrefixes = { "-ini:Engine:", "-ExecCmds=" };

        public static List<string> Split(string options)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            foreach (var character in options)
            {
                if (character == '"')
                {
                    quoted = !quoted;
                }
                if (char.IsWhiteSpace(character) && !quoted)
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }
                current.Append(character);
            }
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
            }
            return tokens;
        }

        public static string Patch(string? current, out List<string> removed)
        {
            removed = new List<string>();
            var kept = new List<string>();
            var hasFlag = false;
            foreach (var token in Split(current ?? ""))
            {
                if (IgnoredPrefixes.Any(prefix => token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    removed.Add(token);
                }
                else if (string.Equals(token, XeFgFlag, StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasFlag)
                    {
                        kept.Add(XeFgFlag);
                    }
                    hasFlag = true;
                }
                else
                {
                    kept.Add(token);
                }
            }
            if (!hasFlag)
            {
                kept.Add(XeFgFlag);
            }
            return string.Join(" ", kept);
        }

        public static bool HasFlag(string? options) =>
            Split(options ?? "").Any(token => string.Equals(token, XeFgFlag, StringComparison.OrdinalIgnoreCase));
    }
}
