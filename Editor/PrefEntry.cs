using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Acar.PlayerPrefsExplorer.Editor
{
    /// <summary>The three value types PlayerPrefs can store.</summary>
    internal enum PrefType { Int, Float, String }

    /// <summary>
    /// A single PlayerPrefs entry. The value is always kept as a normalized,
    /// invariant-culture string so it can be shown, exported, and compared uniformly.
    /// </summary>
    internal sealed class PrefEntry
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        static readonly string[] TypeNames = { "int", "float", "string" }; // indexed by PrefType

        public readonly string Key;
        public PrefType Type;
        public string Value;

        public PrefEntry(string key, PrefType type, string value)
        {
            Key = key;
            Type = type;
            Value = value;
        }

        public int IntValue => int.TryParse(Value, NumberStyles.Integer, Invariant, out var v) ? v : 0;
        public float FloatValue => float.TryParse(Value, NumberStyles.Float, Invariant, out var v) ? v : 0f;

        /// <summary>
        /// Reads a key through the PlayerPrefs API. Raw storage doesn't reliably say which
        /// type a key has, so the type is resolved with sentinel default values.
        /// </summary>
        public static PrefEntry Read(string key)
        {
            int i = PlayerPrefs.GetInt(key, int.MinValue);
            if (i != int.MinValue) return new PrefEntry(key, PrefType.Int, i.ToString(Invariant));

            float f = PlayerPrefs.GetFloat(key, float.NaN);
            if (!float.IsNaN(f)) return new PrefEntry(key, PrefType.Float, FormatFloat(f));

            return new PrefEntry(key, PrefType.String, PlayerPrefs.GetString(key, ""));
        }

        /// <summary>Writes this entry to PlayerPrefs (without calling Save).</summary>
        public void Write()
        {
            switch (Type)
            {
                case PrefType.Int: PlayerPrefs.SetInt(Key, IntValue); break;
                case PrefType.Float: PlayerPrefs.SetFloat(Key, FloatValue); break;
                default: PlayerPrefs.SetString(Key, Value ?? ""); break;
            }
        }

        /// <summary>Writes entries and saves. With <paramref name="replace"/>, all current non-Unity keys are deleted first.</summary>
        public static void WriteAll(IEnumerable<PrefEntry> entries, bool replace)
        {
            if (replace)
                foreach (var key in PrefsReader.ReadKeys(false, out _))
                    PlayerPrefs.DeleteKey(key);

            foreach (var entry in entries)
            {
                PlayerPrefs.DeleteKey(entry.Key); // so a key that changes type doesn't keep stale data
                entry.Write();
            }
            PlayerPrefs.Save();
        }

        /// <summary>Parses text as the given type and returns its canonical string form.</summary>
        public static bool TryNormalize(PrefType type, string text, out string normalized)
        {
            text ??= "";
            normalized = type switch
            {
                // NumberStyles.Integer/Float allow surrounding whitespace; strings are kept verbatim.
                PrefType.Int => int.TryParse(text, NumberStyles.Integer, Invariant, out var i) ? i.ToString(Invariant) : null,
                PrefType.Float => float.TryParse(text, NumberStyles.Float, Invariant, out var f)
                                  && !float.IsNaN(f) && !float.IsInfinity(f) ? FormatFloat(f) : null,
                _ => text
            };
            return normalized != null;
        }

        public static string DefaultValue(PrefType type) => type == PrefType.String ? "" : "0";

        /// <summary>Round-trippable, culture-independent float formatting ("0.5", never "0,5").</summary>
        public static string FormatFloat(float value) => value.ToString("R", Invariant);

        public static string TypeName(PrefType type) => TypeNames[(int)type];

        public static bool TryParseTypeName(string name, out PrefType type)
        {
            int index = Array.IndexOf(TypeNames, name);
            type = index >= 0 ? (PrefType)index : PrefType.String;
            return index >= 0;
        }

        /// <summary>C# line that reads this key, e.g. <c>PlayerPrefs.GetInt("coins", 0);</c></summary>
        public string ToGetCode() => Type switch
        {
            PrefType.Int => $"PlayerPrefs.GetInt({Quote(Key)}, 0);",
            PrefType.Float => $"PlayerPrefs.GetFloat({Quote(Key)}, 0f);",
            _ => $"PlayerPrefs.GetString({Quote(Key)}, \"\");"
        };

        /// <summary>C# line that writes the current value, e.g. <c>PlayerPrefs.SetInt("coins", 150);</c></summary>
        public string ToSetCode() => Type switch
        {
            PrefType.Int => $"PlayerPrefs.SetInt({Quote(Key)}, {Value});",
            PrefType.Float => $"PlayerPrefs.SetFloat({Quote(Key)}, {Value}f);",
            _ => $"PlayerPrefs.SetString({Quote(Key)}, {Quote(Value)});"
        };

        /// <summary>Returns a C# string literal for the text.</summary>
        static string Quote(string text) => "\"" + (text ?? "")
            .Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
    }
}
