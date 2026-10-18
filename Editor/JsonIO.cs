using System;
using System.Collections.Generic;
using UnityEngine;

namespace Acar.PlayerPrefsExplorer.Editor
{
    /// <summary>
    /// Converts entries to and from the JSON format shared by export/import and snapshots:
    /// { "version": 1, "company": "...", "product": "...", "entries": [ { "key", "type", "value" } ] }
    /// </summary>
    internal static class JsonIO
    {
        const int FormatVersion = 1;

        // JsonUtility needs plain serializable wrapper classes with public fields.
        [Serializable]
        class EntryData
        {
            public string key;
            public string type;
            public string value;
        }

        [Serializable]
        class FileData
        {
            public int version;
            public string company;
            public string product;
            public List<EntryData> entries = new List<EntryData>();
        }

        public static string Serialize(IEnumerable<PrefEntry> entries)
        {
            var data = new FileData
            {
                version = FormatVersion,
                company = PrefsReader.Company,
                product = PrefsReader.Product
            };
            foreach (var e in entries)
                data.entries.Add(new EntryData { key = e.Key, type = PrefEntry.TypeName(e.Type), value = e.Value });
            return JsonUtility.ToJson(data, true);
        }

        /// <summary>
        /// Parses JSON into entries. Entries with an empty key, unknown type, or a value that
        /// doesn't match its type are skipped and counted. Throws FormatException for invalid files.
        /// </summary>
        public static List<PrefEntry> Deserialize(string json, out int skipped)
        {
            FileData data;
            try
            {
                data = JsonUtility.FromJson<FileData>(json);
            }
            catch (ArgumentException e)
            {
                throw new FormatException("The file is not valid JSON. " + e.Message);
            }
            if (data == null || data.version != FormatVersion)
                throw new FormatException($"Unsupported file format (expected \"version\": {FormatVersion}).");

            skipped = 0;
            var result = new List<PrefEntry>();
            foreach (var e in data.entries)
            {
                if (e == null || string.IsNullOrEmpty(e.key)
                    || !PrefEntry.TryParseTypeName(e.type, out var type)
                    || !PrefEntry.TryNormalize(type, e.value, out var value))
                {
                    skipped++;
                    continue;
                }
                result.Add(new PrefEntry(e.key, type, value));
            }
            return result;
        }
    }
}
