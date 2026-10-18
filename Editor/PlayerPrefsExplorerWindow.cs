using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Acar.PlayerPrefsExplorer.Editor
{
    /// <summary>Editor window to browse, edit, snapshot, and export PlayerPrefs.</summary>
    public sealed class PlayerPrefsExplorerWindow : EditorWindow
    {
        enum SortColumn { Key, Type }

        const float RowHeight = 22f, TypeWidth = 80f, ActionsWidth = 130f;
        static readonly Color LineColor = new Color(0f, 0f, 0f, 0.3f);
        static readonly Color PressedColor = new Color(0.24f, 0.49f, 0.9f, 0.45f); // readable in light and dark skins

        // Persisted across domain reloads.
        [SerializeField] bool showUnityKeys;
        [SerializeField] SortColumn sortColumn = SortColumn.Key;
        [SerializeField] bool sortAscending = true;

        readonly List<PrefEntry> allEntries = new List<PrefEntry>();     // everything read from storage
        readonly List<PrefEntry> visibleEntries = new List<PrefEntry>(); // after search filter and sort
        string search = "";
        string storageMessage;

        ListView listView;
        Label keyHeader, typeHeader, emptyLabel, statusLabel;

        VisualElement addForm;
        TextField addKeyField, addValueField;
        EnumField addTypeField;
        Label addError;

        // Inline name prompt used by snapshot Save and Rename.
        VisualElement promptRow;
        Label promptLabel;
        TextField promptField;
        Action<string> promptCallback;

        [MenuItem("Tools/PlayerPrefs Explorer")]
        static void Open() => GetWindow<PlayerPrefsExplorerWindow>("PlayerPrefs Explorer").minSize = new Vector2(520, 240);

        void OnEnable() => EditorApplication.playModeStateChanged += OnPlayModeChanged;
        void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        void OnFocus() => Refresh();

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
                Refresh();
        }

        // ---------------------------------------------------------------- UI construction

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.Add(BuildToolbar());
            root.Add(addForm = BuildAddForm());
            root.Add(promptRow = BuildPromptRow());
            root.Add(BuildHeader());

            listView = new ListView(visibleEntries, RowHeight, MakeRow, BindRow)
            {
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly
            };
            listView.style.flexGrow = 1;
            root.Add(listView);

            emptyLabel = new Label();
            emptyLabel.style.flexGrow = 1;
            emptyLabel.style.whiteSpace = WhiteSpace.Normal;
            emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            emptyLabel.style.opacity = 0.7f;
            root.Add(emptyLabel);

            statusLabel = new Label();
            statusLabel.style.paddingLeft = 6;
            statusLabel.style.paddingTop = statusLabel.style.paddingBottom = 3;
            statusLabel.style.borderTopWidth = 1;
            statusLabel.style.borderTopColor = LineColor;
            root.Add(statusLabel);

            Refresh();
            UpdateView(); // Refresh skips the view update when storage matches the (empty) cache
        }

        VisualElement BuildToolbar()
        {
            var searchField = new ToolbarSearchField();
            searchField.style.flexGrow = searchField.style.flexShrink = 1;
            searchField.style.width = StyleKeyword.Auto;
            searchField.style.minWidth = 80;
            searchField.RegisterValueChangedCallback(e => { search = e.newValue ?? ""; UpdateView(); });

            var unityToggle = new ToolbarToggle { text = "Show Unity Keys", value = showUnityKeys };
            ShowPressed(unityToggle, showUnityKeys);
            unityToggle.RegisterValueChangedCallback(e =>
            {
                showUnityKeys = e.newValue;
                ShowPressed(unityToggle, e.newValue);
                Refresh();
            });

            ToolbarButton snapshots = null;
            snapshots = new ToolbarButton(() => ShowSnapshotMenu(snapshots)) { text = "Snapshots ▾" };

            var toolbar = new Toolbar();
            toolbar.Add(searchField);
            toolbar.Add(new ToolbarButton(Refresh) { text = "Refresh" });
            toolbar.Add(new ToolbarButton(ShowAddForm) { text = "Add Key" });
            toolbar.Add(unityToggle);
            toolbar.Add(snapshots);
            toolbar.Add(new ToolbarButton(Export) { text = "Export" });
            toolbar.Add(new ToolbarButton(Import) { text = "Import" });
            toolbar.Add(new ToolbarButton(DeleteAll) { text = "Delete All" });
            return toolbar;
        }

        VisualElement BuildAddForm()
        {
            addKeyField = Grow(new TextField { tooltip = "Key" });
            addTypeField = Width(new EnumField(PrefType.Int) { tooltip = "Type" }, TypeWidth);
            addValueField = Grow(new TextField { tooltip = "Value (leave empty for the default)" });
            addError = new Label();
            addError.style.color = new Color(1f, 0.45f, 0.45f);
            OnEnter(addKeyField, AddKey);
            OnEnter(addValueField, AddKey);

            var form = Row(new Label("Key"), addKeyField, addTypeField, new Label("Value"), addValueField,
                new Button(AddKey) { text = "Add" }, new Button(HideForms) { text = "Cancel" }, addError);
            form.style.flexWrap = Wrap.Wrap;
            form.style.display = DisplayStyle.None;
            return form;
        }

        VisualElement BuildPromptRow()
        {
            promptLabel = new Label();
            promptField = Grow(new TextField());
            OnEnter(promptField, ConfirmPrompt);

            var row = Row(promptLabel, promptField,
                new Button(ConfirmPrompt) { text = "OK" }, new Button(HideForms) { text = "Cancel" });
            row.style.display = DisplayStyle.None;
            return row;
        }

        VisualElement BuildHeader()
        {
            keyHeader = Grow(new Label { tooltip = "Sort by key" });
            keyHeader.AddManipulator(new Clickable(() => SortBy(SortColumn.Key)));
            typeHeader = Width(new Label { tooltip = "Sort by type" }, TypeWidth);
            typeHeader.AddManipulator(new Clickable(() => SortBy(SortColumn.Type)));

            var header = Row(keyHeader, typeHeader, Grow(new Label("Value")), Width(new Label(), ActionsWidth));
            header.style.height = RowHeight;
            header.style.paddingLeft = 4;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = LineColor;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            return header;
        }

        VisualElement MakeRow()
        {
            // Rows are recycled, so handlers registered here read the current entry from userData.
            VisualElement row = null;
            PrefEntry Entry() => (PrefEntry)row.userData;

            var key = Grow(new Label { name = "key" });
            key.style.overflow = Overflow.Hidden;
            key.style.textOverflow = TextOverflow.Ellipsis;

            var type = Width(new EnumField(PrefType.Int) { name = "type" }, TypeWidth);
            type.RegisterValueChangedCallback(e => ChangeType(Entry(), (PrefType)e.newValue, type));

            var actions = Width(Row(
                new Button(() => Copy(Entry().ToGetCode())) { text = "Copy C#", tooltip = "Copy the PlayerPrefs.Get… line" },
                new Button(() => DeleteEntry(Entry())) { text = "Delete" }), ActionsWidth);

            row = Row(key, type, Grow(new VisualElement { name = "value" }), actions);
            row.style.paddingLeft = 4;
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                var entry = Entry();
                evt.menu.AppendAction("Copy as C#", _ => Copy(entry.ToGetCode()));
                evt.menu.AppendAction("Copy Set as C#", _ => Copy(entry.ToSetCode()));
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Delete", _ => DeleteEntry(entry));
            }));
            return row;
        }

        void BindRow(VisualElement row, int index)
        {
            var entry = visibleEntries[index];
            row.userData = entry;

            var key = row.Q<Label>("key");
            key.text = key.tooltip = entry.Key;
            row.Q<EnumField>("type").SetValueWithoutNotify(entry.Type);

            var host = row.Q("value");
            host.Clear();
            host.Add(CreateValueField(entry));
        }

        /// <summary>Creates a delayed field for the entry's type, so edits apply on Enter or focus loss.</summary>
        VisualElement CreateValueField(PrefEntry entry)
        {
            switch (entry.Type)
            {
                case PrefType.Int:
                    var intField = new IntegerField { isDelayed = true, value = entry.IntValue };
                    intField.RegisterValueChangedCallback(e => ApplyValue(entry, e.newValue.ToString(CultureInfo.InvariantCulture)));
                    return intField;
                case PrefType.Float:
                    var floatField = new FloatField { isDelayed = true, value = entry.FloatValue };
                    floatField.RegisterValueChangedCallback(e => ApplyValue(entry, PrefEntry.FormatFloat(e.newValue)));
                    return floatField;
                default:
                    if (IsMultiline(entry.Value))
                        return CreateMultilinePreview(entry);
                    var textField = new TextField { isDelayed = true, value = entry.Value };
                    textField.RegisterValueChangedCallback(e => ApplyValue(entry, e.newValue ?? ""));
                    return textField;
            }
        }

        /// <summary>
        /// Multi-line strings can't be edited in a single-line field, so the row shows a read-only
        /// one-line preview and an Edit… button that opens <see cref="StringEditorWindow"/>.
        /// </summary>
        VisualElement CreateMultilinePreview(PrefEntry entry)
        {
            var preview = Grow(new TextField
            {
                isReadOnly = true,
                value = Preview(entry.Value),
                tooltip = "Multi-line value. Use Edit… to change it."
            });
            var edit = new Button(() => StringEditorWindow.Open(entry.Key, entry.Value, position)) { text = "Edit…" };
            return Row(preview, edit);
        }

        const int PreviewMaxLength = 200;

        static bool IsMultiline(string value) => value != null && value.IndexOfAny(new[] { '\n', '\r' }) >= 0;

        /// <summary>Collapses line breaks (and the indentation around them) to single spaces and truncates.</summary>
        static string Preview(string value)
        {
            string flat = System.Text.RegularExpressions.Regex.Replace(value ?? "", @"\s*[\r\n]+\s*", " ").Trim();
            return flat.Length <= PreviewMaxLength ? flat : flat.Substring(0, PreviewMaxLength) + "…";
        }

        /// <summary>Re-reads storage in every open explorer window (used after saving from another window).</summary>
        internal static void RefreshAll()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<PlayerPrefsExplorerWindow>())
                window.Refresh();
        }

        // ---------------------------------------------------------------- Data and view

        /// <summary>Re-reads storage and updates the view if anything changed.</summary>
        void Refresh()
        {
            if (listView == null) return; // UI not built yet

            var entries = PrefsReader.ReadEntries(showUnityKeys, out storageMessage);

            // Skip the rebuild when nothing changed, so a focus-triggered refresh
            // doesn't destroy the field the user just clicked into.
            bool unchanged = entries.Count == allEntries.Count && entries.Zip(allEntries,
                (a, b) => a.Key == b.Key && a.Type == b.Type && a.Value == b.Value).All(same => same);
            if (unchanged) { UpdateStatus(); return; }
            allEntries.Clear();
            allEntries.AddRange(entries);
            UpdateView();
        }

        /// <summary>Applies search and sort to the loaded entries and rebuilds the list.</summary>
        void UpdateView()
        {
            if (listView == null) return;

            visibleEntries.Clear();
            visibleEntries.AddRange(allEntries.Where(e => e.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0));
            visibleEntries.Sort((a, b) =>
            {
                int result = sortColumn == SortColumn.Type ? a.Type.CompareTo(b.Type) : 0;
                if (result == 0) result = string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
                return sortAscending ? result : -result;
            });
            listView.Rebuild();

            keyHeader.text = "Key" + SortArrow(SortColumn.Key);
            typeHeader.text = "Type" + SortArrow(SortColumn.Type);
            UpdateStatus();
        }

        string SortArrow(SortColumn column) => sortColumn != column ? "" : sortAscending ? " ▲" : " ▼";

        void SortBy(SortColumn column)
        {
            sortAscending = sortColumn != column || !sortAscending;
            sortColumn = column;
            UpdateView();
        }

        void UpdateStatus()
        {
            bool empty = visibleEntries.Count == 0;
            listView.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            emptyLabel.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            emptyLabel.text = storageMessage ?? (allEntries.Count == 0
                ? "No PlayerPrefs keys yet. Use Add Key to create one."
                : $"No keys match \"{search}\".");

            string count = visibleEntries.Count == allEntries.Count
                ? $"{allEntries.Count} keys"
                : $"{visibleEntries.Count} of {allEntries.Count} keys";
            statusLabel.text = $"{count} · {PrefsReader.Company}/{PrefsReader.Product}";
        }

        // ---------------------------------------------------------------- Editing

        void ApplyValue(PrefEntry entry, string value)
        {
            if (entry.Value == value) return;
            entry.Value = value;
            entry.Write();
            PlayerPrefs.Save();
        }

        void ChangeType(PrefEntry entry, PrefType newType, EnumField field)
        {
            if (entry.Type == newType) return;

            if (!PrefEntry.TryNormalize(newType, entry.Value, out var converted))
            {
                converted = PrefEntry.DefaultValue(newType);
                string shown = converted.Length == 0 ? "an empty string" : converted;
                if (!EditorUtility.DisplayDialog("Change Type",
                        $"\"{entry.Value}\" can't be converted to {newType}.\n\nUse the default value ({shown}) instead?",
                        "Use Default", "Cancel"))
                {
                    field.SetValueWithoutNotify(entry.Type);
                    return;
                }
            }

            PlayerPrefs.DeleteKey(entry.Key);
            entry.Type = newType;
            entry.Value = converted;
            entry.Write();
            PlayerPrefs.Save();
            UpdateView(); // re-bind so the value field matches the new type
        }

        void ShowAddForm()
        {
            HideForms();
            addForm.style.display = DisplayStyle.Flex;
            FocusText(addKeyField);
        }

        void HideForms()
        {
            addForm.style.display = promptRow.style.display = DisplayStyle.None;
            addError.text = "";
            promptCallback = null;
        }

        void AddKey()
        {
            string key = addKeyField.value;
            var type = (PrefType)addTypeField.value;
            string raw = string.IsNullOrEmpty(addValueField.value) ? PrefEntry.DefaultValue(type) : addValueField.value;

            if (string.IsNullOrWhiteSpace(key))
                addError.text = "Key cannot be empty.";
            else if (PlayerPrefs.HasKey(key))
                addError.text = $"Key \"{key}\" already exists.";
            else if (!PrefEntry.TryNormalize(type, raw, out var value))
                addError.text = $"\"{raw}\" is not a valid {type}.";
            else
            {
                new PrefEntry(key, type, value).Write();
                PlayerPrefs.Save();
                addKeyField.value = addValueField.value = "";
                HideForms();
                Refresh();
            }
        }

        void DeleteEntry(PrefEntry entry)
        {
            if (!EditorUtility.DisplayDialog("Delete Key", $"Delete \"{entry.Key}\"?", "Delete", "Cancel")) return;
            PlayerPrefs.DeleteKey(entry.Key);
            PlayerPrefs.Save();
            Refresh();
        }

        void DeleteAll()
        {
            int count = allEntries.Count;
            if (count == 0) { Notify("No keys to delete"); return; }
            string note = showUnityKeys ? "Unity internal keys are included." : "Unity internal keys are kept.";
            if (!EditorUtility.DisplayDialog("Delete All",
                    $"Delete {count} key(s) from {PrefsReader.Company}/{PrefsReader.Product}?\n\n{note} This cannot be undone.",
                    $"Delete {count}", "Cancel"))
                return;

            foreach (var entry in allEntries)
                PlayerPrefs.DeleteKey(entry.Key);
            PlayerPrefs.Save();
            Refresh();
        }

        void Copy(string code)
        {
            EditorGUIUtility.systemCopyBuffer = code;
            Notify("Copied: " + code);
        }

        // ---------------------------------------------------------------- Snapshots

        void ShowSnapshotMenu(VisualElement anchor)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Save Snapshot..."), false,
                () => ShowPrompt("Snapshot name", SnapshotManager.DefaultName(), SaveSnapshot));
            menu.AddSeparator("");

            var names = SnapshotManager.List();
            if (names.Length == 0)
                menu.AddDisabledItem(new GUIContent("No snapshots yet"));
            foreach (var name in names)
            {
                menu.AddItem(new GUIContent($"{name}/Restore"), false, () => RestoreSnapshot(name));
                menu.AddItem(new GUIContent($"{name}/Rename..."), false,
                    () => ShowPrompt($"Rename \"{name}\" to", name, newName => RenameSnapshot(name, newName)));
                menu.AddItem(new GUIContent($"{name}/Delete"), false, () => DeleteSnapshot(name));
            }
            menu.DropDown(anchor.worldBound);
        }

        void SaveSnapshot(string rawName)
        {
            string name = SnapshotManager.Sanitize(rawName);
            if (name.Length == 0) name = SnapshotManager.DefaultName();
            if (SnapshotManager.Exists(name) && !EditorUtility.DisplayDialog("Overwrite Snapshot",
                    $"A snapshot named \"{name}\" already exists. Overwrite it?", "Overwrite", "Cancel"))
                return;
            if (TryRun("Save Snapshot", () => SnapshotManager.Save(name)))
                Notify($"Saved snapshot \"{name}\"");
        }

        void RestoreSnapshot(string name)
        {
            if (!EditorUtility.DisplayDialog("Restore Snapshot",
                    $"Restore \"{name}\"?\n\nThis replaces all current non-Unity keys.", "Restore", "Cancel"))
                return;
            TryRun("Restore Snapshot", () => SnapshotManager.Restore(name));
            Refresh();
        }

        void RenameSnapshot(string oldName, string rawName)
        {
            string newName = SnapshotManager.Sanitize(rawName);
            if (newName.Length == 0 || newName == oldName) return;
            if (SnapshotManager.Exists(newName))
                EditorUtility.DisplayDialog("Rename Snapshot", $"A snapshot named \"{newName}\" already exists.", "OK");
            else
                TryRun("Rename Snapshot", () => SnapshotManager.Rename(oldName, newName));
        }

        void DeleteSnapshot(string name)
        {
            if (EditorUtility.DisplayDialog("Delete Snapshot", $"Delete snapshot \"{name}\"?", "Delete", "Cancel"))
                TryRun("Delete Snapshot", () => SnapshotManager.Delete(name));
        }

        void ShowPrompt(string label, string initialValue, Action<string> onConfirm)
        {
            HideForms();
            promptRow.style.display = DisplayStyle.Flex;
            promptLabel.text = label;
            promptField.value = initialValue;
            promptCallback = onConfirm;
            FocusText(promptField);
        }

        void ConfirmPrompt()
        {
            var callback = promptCallback;
            HideForms();
            callback?.Invoke(promptField.value);
        }

        // ---------------------------------------------------------------- Export / Import

        void Export()
        {
            if (visibleEntries.Count == 0) { Notify("No keys to export"); return; }
            string path = EditorUtility.SaveFilePanel("Export PlayerPrefs", "",
                $"{SnapshotManager.Sanitize(PrefsReader.Product)}_PlayerPrefs.json", "json");
            if (string.IsNullOrEmpty(path)) return;

            if (TryRun("Export", () => File.WriteAllText(path, JsonIO.Serialize(visibleEntries))))
                Notify($"Exported {visibleEntries.Count} keys");
        }

        void Import()
        {
            string path = EditorUtility.OpenFilePanel("Import PlayerPrefs", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            List<PrefEntry> entries = null;
            int skipped = 0;
            if (!TryRun("Import", () => entries = JsonIO.Deserialize(File.ReadAllText(path), out skipped)))
                return;

            string skippedNote = skipped > 0 ? $"\n\n{skipped} invalid entries will be skipped." : "";
            int choice = EditorUtility.DisplayDialogComplex("Import PlayerPrefs",
                $"Import {entries.Count} key(s) from \"{Path.GetFileName(path)}\"?\n\n" +
                "Merge: overwrite matching keys, keep the rest.\n" +
                "Replace: delete all current non-Unity keys first." + skippedNote,
                "Merge", "Cancel", "Replace");
            if (choice == 1) return; // Cancel

            if (TryRun("Import", () => PrefEntry.WriteAll(entries, replace: choice == 2)))
                Notify($"Imported {entries.Count} keys");
            Refresh();
        }

        // ---------------------------------------------------------------- Helpers

        /// <summary>Runs an action and reports any exception in a dialog instead of throwing.</summary>
        static bool TryRun(string title, Action action)
        {
            try { action(); return true; }
            catch (Exception e) { EditorUtility.DisplayDialog(title + " Failed", e.Message, "OK"); return false; }
        }

        void Notify(string message) => ShowNotification(new GUIContent(message));

        /// <summary>Tints a toolbar toggle while it's on, since the default checked style is hard to see.</summary>
        static void ShowPressed(VisualElement toggle, bool pressed) =>
            toggle.style.backgroundColor = pressed ? new StyleColor(PressedColor) : new StyleColor(StyleKeyword.Null);

        static VisualElement Row(params VisualElement[] children)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            foreach (var child in children) row.Add(child);
            return row;
        }

        /// <summary>Makes the element share the remaining row width equally with other grown elements.</summary>
        static T Grow<T>(T element) where T : VisualElement
        {
            element.style.flexGrow = 1;
            element.style.flexBasis = 0;
            return element;
        }

        static T Width<T>(T element, float width) where T : VisualElement
        {
            element.style.width = width;
            return element;
        }

        /// <summary>Calls the action when Enter is pressed inside the text field.</summary>
        static void OnEnter(TextField field, Action action) =>
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) action();
            }, TrickleDown.TrickleDown);

        /// <summary>Focuses the inner text input once the field is laid out.</summary>
        static void FocusText(TextField field) =>
            field.schedule.Execute(() => (field.Q("unity-text-input") ?? field).Focus());
    }
}
