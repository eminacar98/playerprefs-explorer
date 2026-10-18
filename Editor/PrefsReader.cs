using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace Acar.PlayerPrefsExplorer.Editor
{
    /// <summary>
    /// Discovers PlayerPrefs key names in the Editor's platform-specific storage.
    /// Only names are read here; values and types are resolved by <see cref="PrefEntry.Read"/>.
    /// </summary>
    internal static class PrefsReader
    {
        /// <summary>
        /// Keys Unity and its packages write for their own use, hidden unless "Show Unity Keys" is on.
        /// An entry ending in '*' matches as a prefix (case-insensitive); any other entry must match
        /// the key name exactly (case-sensitive). Add new patterns here.
        /// </summary>
        static readonly string[] HiddenUnityKeyPatterns =
        {
            "unity.*",                // editor/engine internals
            "unity_*",                // e.g. unity_connect.installation_id, unity_connect.session_id
            "UnityGraphicsQuality*",
            "Screenmanager *",        // window size, fullscreen mode, etc.
            "PT_Run",                 // Performance Testing package
            "PT_Settings",            // Performance Testing package
        };

        public static string Company => PlayerSettings.companyName;
        public static string Product => PlayerSettings.productName;

        /// <summary>
        /// Keys written through this tool since the last domain reload. They're merged into every read,
        /// so a new key shows up immediately even if the platform storage hasn't been flushed to disk yet.
        /// </summary>
        static readonly HashSet<string> WrittenKeys = new HashSet<string>();

        public static void NoteWritten(string key)
        {
            if (!string.IsNullOrEmpty(key)) WrittenKeys.Add(key);
        }

        public static bool IsUnityKey(string key)
        {
            foreach (var pattern in HiddenUnityKeyPatterns)
            {
                bool isPrefix = pattern.EndsWith("*", StringComparison.Ordinal);
                bool match = isPrefix
                    ? key.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase)
                    : string.Equals(key, pattern, StringComparison.Ordinal);
                if (match) return true;
            }
            return false;
        }

        /// <summary>Reads all keys and resolves their types and values.</summary>
        public static List<PrefEntry> ReadEntries(bool includeUnityKeys, out string message)
        {
            var entries = new List<PrefEntry>();
            foreach (var key in ReadKeys(includeUnityKeys, out message))
                entries.Add(PrefEntry.Read(key));
            return entries;
        }

        /// <summary>
        /// Returns the key names currently in storage. Never throws: if storage is missing
        /// or unreadable, an empty list is returned and <paramref name="message"/> explains why.
        /// </summary>
        public static List<string> ReadKeys(bool includeUnityKeys, out string message)
        {
            message = null;
            var raw = new List<string>();
            try
            {
                // Flush pending writes so on-disk storage matches the in-memory state.
                PlayerPrefs.Save();
                if (!ReadPlatformKeys(raw))
                    message = $"No PlayerPrefs storage found yet at:\n{StorageLocation}";
            }
            catch (Exception e)
            {
                raw.Clear();
                message = $"Could not read PlayerPrefs storage at:\n{StorageLocation}\n\n{e.Message}";
            }

            raw.AddRange(WrittenKeys); // deleted ones are dropped by the HasKey check below
            if (message != null && WrittenKeys.Count > 0 && raw.Exists(PlayerPrefs.HasKey))
                message = null; // storage not on disk yet, but we know keys exist

            var keys = new List<string>();
            var seen = new HashSet<string>();
            foreach (var key in raw)
            {
                // HasKey filters out stale storage entries the API no longer knows about.
                if (string.IsNullOrEmpty(key) || !seen.Add(key) || !PlayerPrefs.HasKey(key)) continue;
                if (includeUnityKeys || !IsUnityKey(key)) keys.Add(key);
            }
            return keys;
        }

        static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

#if UNITY_EDITOR_WIN
        static string RegistryPath => $@"Software\Unity\UnityEditor\{Company}\{Product}";
        static string StorageLocation => @"HKEY_CURRENT_USER\" + RegistryPath;

        // Unity stores each key as a registry value named "<key>_h<hash>".
        static readonly System.Text.RegularExpressions.Regex HashSuffix =
            new System.Text.RegularExpressions.Regex(@"_h\d+$");

        static bool ReadPlatformKeys(List<string> keys)
        {
            using (var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath))
            {
                if (reg == null) return false;
                foreach (var name in reg.GetValueNames())
                    keys.Add(HashSuffix.Replace(name, ""));
            }
            return true;
        }
#elif UNITY_EDITOR_OSX
        static string StorageLocation =>
            Path.Combine(Home, "Library", "Preferences", $"unity.{Company}.{Product}.plist");

        static string Domain => $"unity.{Company}.{Product}";

        static bool ReadPlatformKeys(List<string> keys)
        {
            // macOS writes the .plist file lazily (via cfprefsd), so right after PlayerPrefs.Save()
            // the file can be seconds out of date. `defaults` asks cfprefsd directly and is current.
            string xml = RunTool("/usr/bin/defaults", $"export \"{Domain}\" -", out _);
            if (xml == null)
            {
                // Fallback: read the file itself. It's usually binary; plutil converts it to XML.
                if (!File.Exists(StorageLocation)) return false;
                xml = RunTool("/usr/bin/plutil", $"-convert xml1 -o - \"{StorageLocation}\"", out int exitCode)
                      ?? throw new IOException($"plutil exited with code {exitCode}.");
            }

            // Top-level <dict> alternates <key>name</key> and a value element.
            var dict = LoadXml(new StringReader(xml)).SelectSingleNode("/plist/dict");
            if (dict == null) return true;
            foreach (XmlNode node in dict.ChildNodes)
                if (node.Name == "key")
                    keys.Add(node.InnerText);
            return true;
        }

        /// <summary>Runs a command-line tool and returns its stdout, or null if it exits with an error.</summary>
        static string RunTool(string path, string arguments, out int exitCode)
        {
            var info = new System.Diagnostics.ProcessStartInfo(path, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true, // keep "domain does not exist" out of the Editor log
                CreateNoWindow = true
            };
            using (var process = System.Diagnostics.Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
                return exitCode == 0 ? output : null;
            }
        }
#elif UNITY_EDITOR_LINUX
        static string StorageLocation =>
            Path.Combine(Home, ".config", "unity3d", Company, Product, "prefs");

        static bool ReadPlatformKeys(List<string> keys)
        {
            if (!File.Exists(StorageLocation)) return false;

            // Format: <unity_prefs><pref name="coins" type="int">150</pref>...</unity_prefs>
            using (var reader = new StreamReader(StorageLocation))
            {
                foreach (XmlNode node in LoadXml(reader).SelectNodes("//pref"))
                    keys.Add(node.Attributes?["name"]?.Value);
            }
            return true;
        }
#else
        static string StorageLocation => "(unsupported Editor platform)";
        static bool ReadPlatformKeys(List<string> keys) => false;
#endif

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        /// <summary>Loads XML without resolving DTDs (plists reference an Apple DTD URL).</summary>
        static XmlDocument LoadXml(TextReader text)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(text, settings))
                doc.Load(reader);
            return doc;
        }
#endif
    }
}
