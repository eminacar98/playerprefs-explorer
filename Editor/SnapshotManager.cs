using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Acar.PlayerPrefsExplorer.Editor
{
    /// <summary>
    /// Saves and restores named PlayerPrefs snapshots as JSON files in
    /// &lt;ProjectRoot&gt;/UserSettings/PlayerPrefsExplorer/Snapshots (per-user, usually not committed).
    /// Snapshots only contain non-Unity keys.
    /// </summary>
    internal static class SnapshotManager
    {
        static string Folder => Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, "UserSettings", "PlayerPrefsExplorer", "Snapshots");

        static string PathFor(string name) => Path.Combine(Folder, name + ".json");

        public static string DefaultName() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);

        /// <summary>Makes a user-entered name safe to use as a file name (and as a menu path).</summary>
        public static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' }).ToArray();
            var chars = (name ?? "").Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        public static string[] List()
        {
            if (!Directory.Exists(Folder)) return Array.Empty<string>();
            return Directory.GetFiles(Folder, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static bool Exists(string name) => File.Exists(PathFor(name));

        /// <summary>Saves the current non-Unity keys under the given name.</summary>
        public static void Save(string name)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathFor(name), JsonIO.Serialize(PrefsReader.ReadEntries(false, out _)));
        }

        /// <summary>Replaces all current non-Unity keys with the snapshot's keys.</summary>
        public static void Restore(string name)
        {
            var entries = JsonIO.Deserialize(File.ReadAllText(PathFor(name)), out _);
            PrefEntry.WriteAll(entries, replace: true);
        }

        public static void Rename(string oldName, string newName) => File.Move(PathFor(oldName), PathFor(newName));

        public static void Delete(string name) => File.Delete(PathFor(name));
    }
}
