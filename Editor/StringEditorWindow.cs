using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Acar.PlayerPrefsExplorer.Editor
{
    /// <summary>Utility window for editing a multi-line string value (e.g. JSON) of a single key.</summary>
    internal sealed class StringEditorWindow : EditorWindow
    {
        // Persisted across domain reloads, so the window survives script recompiles.
        [SerializeField] string key;
        [SerializeField] string draft;

        TextField textField;

        public static void Open(string key, string value, Rect ownerPosition)
        {
            var window = CreateInstance<StringEditorWindow>();
            window.key = key;
            window.draft = value ?? "";
            window.titleContent = new GUIContent($"Edit \"{key}\"");
            window.minSize = new Vector2(360, 240);

            // Center over the explorer window.
            var size = new Vector2(Mathf.Max(360, ownerPosition.width * 0.8f), Mathf.Max(240, ownerPosition.height * 0.8f));
            window.position = new Rect(ownerPosition.center - size / 2f, size);
            window.ShowUtility();
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 6;

            var keyLabel = new Label(key);
            keyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            keyLabel.style.marginBottom = 4;
            root.Add(keyLabel);

            // A ScrollView keeps long values scrollable on every supported Unity version
            // (TextField's own scrollbars are not available on 2021.3).
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            textField = new TextField { multiline = true, value = draft };
            textField.style.marginLeft = textField.style.marginRight = 0;
            textField.RegisterValueChangedCallback(e => draft = e.newValue ?? "");
            scroll.Add(textField);

            // Let the field fill the visible area so clicking anywhere in it starts editing.
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(e =>
                textField.style.minHeight = e.newRect.height);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 6;
            buttons.Add(new Button(Close) { text = "Cancel" });
            buttons.Add(new Button(Save) { text = "Save" });
            root.Add(buttons);

            textField.schedule.Execute(() => (textField.Q("unity-text-input") ?? textField).Focus());
        }

        void Save()
        {
            PlayerPrefs.SetString(key, draft ?? "");
            PlayerPrefs.Save();
            PlayerPrefsExplorerWindow.RefreshAll();
            Close();
        }
    }
}
