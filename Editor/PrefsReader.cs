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
        /// <summary>Prefixes of keys Unity writes for its own use.</summary>
        static readonly string[] UnityKeyPrefixes = { "unity.", "UnityGraphicsQuality", "Screenmanager " };

        public static string Company => PlayerSettings.companyName;
        public static string Product => PlayerSettings.productName;

        public static bool IsUnityKey(string key)
        {
            foreach (var prefix in UnityKeyPrefixes)
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
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

        static bool ReadPlatformKeys(List<string> keys)
        {
            if (!File.Exists(StorageLocation)) return false;

            // The plist is usually binary; plutil converts it to XML on stdout.
            var info = new System.Diagnostics.ProcessStartInfo("/usr/bin/plutil",
                $"-convert xml1 -o - \"{StorageLocation}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            string xml;
            using (var process = System.Diagnostics.Process.Start(info))
            {
                xml = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new IOException($"plutil exited with code {process.ExitCode}.");
            }

            // Top-level <dict> alternates <key>name</key> and a value element.
            var dict = LoadXml(new StringReader(xml)).SelectSingleNode("/plist/dict");
            if (dict == null) return true;
            foreach (XmlNode node in dict.ChildNodes)
                if (node.Name == "key")
                    keys.Add(node.InnerText);
            return true;
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
