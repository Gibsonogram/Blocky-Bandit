using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditorUIScaler
{
    /// <summary>
    /// Scales text inside the Unity *Editor* itself (not game / runtime UI).
    ///
    /// Two systems are handled:
    ///  1. IMGUI  - Inspector, Hierarchy, Project, Console, toolbars, most built-in windows.
    ///              Done by scaling GUIStyle font sizes (EditorStyles + built-in editor skins).
    ///  2. UI Toolkit - newer editor windows. Done by scaling TextElement font sizes.
    ///
    /// Everything that is changed is recorded first, so "Restore" is exact and
    /// re-applying never compounds (1.5x stays 1.5x, it never becomes 2.25x).
    /// </summary>
    [InitializeOnLoad]
    public static class EditorUIScaler
    {
        private const string EnabledKey = "UnityEditorUIScaler.Enabled";
        private const string ScaleKey = "UnityEditorUIScaler.Scale";
        private const string HeightsKey = "UnityEditorUIScaler.ScaleHeights";
        // Set while applying. If it is still set on the next launch, the last apply
        // crashed the editor, so auto-apply is switched off instead of crash-looping.
        private const string GuardKey = "UnityEditorUIScaler.ApplyInProgress";

        public const float MinScale = 0.75f;
        public const float MaxScale = 2.5f;
        public const float DefaultScale = 1.5f;

        private const double RefreshIntervalSeconds = 1.5;

        private struct StyleState
        {
            public int FontSize;
            public float FixedHeight;
        }

        private struct ElementState
        {
            public StyleLength InlineFontSize;
            public float BaseFontSize;
        }

        private static readonly Dictionary<GUIStyle, StyleState> imguiOriginals =
            new Dictionary<GUIStyle, StyleState>();

        private static readonly Dictionary<VisualElement, ElementState> uitkOriginals =
            new Dictionary<VisualElement, ElementState>();

        private static readonly Stack<VisualElement> traversal = new Stack<VisualElement>();
        private static readonly List<VisualElement> staleElements = new List<VisualElement>();

        private static bool initialized;
        private static double nextRefreshTime;

        // NOTE: keep this constructor trivial. It runs during editor/domain load, when
        // touching GUI styles or windows is not safe. All real work is deferred.
        static EditorUIScaler()
        {
            AssemblyReloadEvents.beforeAssemblyReload += RestoreInternal;
            EditorApplication.update += OnUpdate;
            EditorApplication.delayCall += Initialize;
        }

        // ------------------------------------------------------------------ settings

        public static bool Enabled
        {
            get { return EditorPrefs.GetBool(EnabledKey, false); }
        }

        public static float Scale
        {
            get { return Mathf.Clamp(EditorPrefs.GetFloat(ScaleKey, DefaultScale), MinScale, MaxScale); }
            set { EditorPrefs.SetFloat(ScaleKey, Mathf.Clamp(value, MinScale, MaxScale)); }
        }

        /// <summary>Also scale fixed control heights used by GUILayout styles (buttons, fields).</summary>
        public static bool ScaleHeights
        {
            get { return EditorPrefs.GetBool(HeightsKey, false); }
            set { EditorPrefs.SetBool(HeightsKey, value); }
        }

        // ------------------------------------------------------------------ public API

        public static void SetEnabled(bool on)
        {
            EditorPrefs.SetBool(EnabledKey, on);
            if (on) Apply();
            else Restore();
        }

        public static void Apply()
        {
            EditorPrefs.SetBool(GuardKey, true);
            try
            {
                float scale = Scale;
                ApplyImgui(scale, ScaleHeights);
                ApplyUitk(scale);
                InternalEditorUtility.RepaintAllViews();
            }
            finally
            {
                EditorPrefs.SetBool(GuardKey, false);
            }
        }

        public static void Restore()
        {
            RestoreInternal();
            InternalEditorUtility.RepaintAllViews();
        }

        // ------------------------------------------------------------------ lifecycle

        private static void Initialize()
        {
            initialized = true;

            if (EditorPrefs.GetBool(GuardKey, false))
            {
                EditorPrefs.SetBool(GuardKey, false);
                EditorPrefs.SetBool(EnabledKey, false);
                Debug.LogWarning("Editor UI Scaler: the previous apply did not finish, so scaling was " +
                                 "switched off. Re-enable it from Tools > Editor UI Scaler.");
                return;
            }

            if (Enabled) Apply();
        }

        private static void OnUpdate()
        {
            if (!initialized || !Enabled) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            double now = EditorApplication.timeSinceStartup;
            if (now < nextRefreshTime) return;
            nextRefreshTime = now + RefreshIntervalSeconds;

            // Picks up windows / inspectors / styles created after the last apply.
            float scale = Scale;
            ApplyImgui(scale, ScaleHeights);
            ApplyUitk(scale);
        }

        // Restores originals without touching the saved "Enabled" preference, so the
        // scale can be re-applied after a script reload. Without this, styles that
        // survive the reload would be scaled a second time.
        private static void RestoreInternal()
        {
            RestoreImgui();
            RestoreUitk();
        }

        // ------------------------------------------------------------------ IMGUI

        private static void ApplyImgui(float scale, bool scaleHeights)
        {
            foreach (GUIStyle style in CollectImguiStyles())
                ScaleStyle(style, scale, scaleHeights);
        }

        private static void ScaleStyle(GUIStyle style, float scale, bool scaleHeights)
        {
            StyleState original;
            if (!imguiOriginals.TryGetValue(style, out original))
            {
                original = new StyleState { FontSize = style.fontSize, FixedHeight = style.fixedHeight };
                imguiOriginals[style] = original;
            }

            int baseSize = original.FontSize > 0 ? original.FontSize : DefaultFontSize(style);
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(baseSize * scale));

            if (original.FixedHeight > 0f)
                style.fixedHeight = scaleHeights ? original.FixedHeight * scale : original.FixedHeight;
        }

        private static void RestoreImgui()
        {
            foreach (KeyValuePair<GUIStyle, StyleState> pair in imguiOriginals)
            {
                if (pair.Key == null) continue;
                pair.Key.fontSize = pair.Value.FontSize;
                pair.Key.fixedHeight = pair.Value.FixedHeight;
            }
            imguiOriginals.Clear();
        }

        private static int DefaultFontSize(GUIStyle style)
        {
            Font font = style.font;
            if (font != null && font.fontSize > 0) return font.fontSize;
            return Application.platform == RuntimePlatform.OSXEditor ? 11 : 12;
        }

        private static List<GUIStyle> CollectImguiStyles()
        {
            var result = new List<GUIStyle>(512);
            var seen = new HashSet<GUIStyle>();

            // EditorStyles.* (label, boldLabel, miniLabel, textField, foldout, ...)
            foreach (PropertyInfo prop in typeof(EditorStyles).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (prop.PropertyType != typeof(GUIStyle) || prop.GetIndexParameters().Length != 0)
                    continue;
                try { AddStyle(prop.GetValue(null, null) as GUIStyle, seen, result); }
                catch (Exception) { /* some getters are only valid inside OnGUI; skip them */ }
            }

            // Built-in editor skins (named styles + the large customStyles table).
            foreach (EditorSkin skinId in Enum.GetValues(typeof(EditorSkin)))
            {
                GUISkin skin = null;
                try { skin = EditorGUIUtility.GetBuiltinSkin(skinId); }
                catch (Exception) { }
                if (skin == null) continue;

                AddStyle(skin.box, seen, result);
                AddStyle(skin.button, seen, result);
                AddStyle(skin.toggle, seen, result);
                AddStyle(skin.label, seen, result);
                AddStyle(skin.textField, seen, result);
                AddStyle(skin.textArea, seen, result);
                AddStyle(skin.window, seen, result);
                AddStyle(skin.scrollView, seen, result);

                GUIStyle[] custom = skin.customStyles;
                if (custom == null) continue;
                for (int i = 0; i < custom.Length; i++)
                    AddStyle(custom[i], seen, result);
            }

            return result;
        }

        private static void AddStyle(GUIStyle style, HashSet<GUIStyle> seen, List<GUIStyle> result)
        {
            if (style != null && seen.Add(style))
                result.Add(style);
        }

        // ------------------------------------------------------------------ UI Toolkit

        private static void ApplyUitk(float scale)
        {
            PruneDetachedElements();

            EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                try
                {
                    EditorWindow window = windows[i];
                    if (window == null) continue;

                    VisualElement root = window.rootVisualElement;
                    // No panel yet = window not shown (e.g. hidden tab). It is picked up on a later refresh.
                    if (root == null || root.panel == null) continue;

                    ScaleTree(root, scale);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Editor UI Scaler: skipped a window (" + e.Message + ")");
                }
            }
        }

        // Iterative (no recursion), and walks `hierarchy` so that internal parts of
        // controls (field labels, toggle text, etc.) are included.
        private static void ScaleTree(VisualElement root, float scale)
        {
            traversal.Clear();
            traversal.Push(root);

            while (traversal.Count > 0)
            {
                VisualElement element = traversal.Pop();

                VisualElement.Hierarchy h = element.hierarchy;
                for (int i = 0; i < h.childCount; i++)
                    traversal.Push(h[i]);

                // Only elements that actually draw text.
                if (!(element is TextElement)) continue;

                ElementState state;
                if (!uitkOriginals.TryGetValue(element, out state))
                {
                    float resolved = element.resolvedStyle.fontSize;
                    if (float.IsNaN(resolved) || resolved <= 0f) continue; // not laid out yet
                    state = new ElementState
                    {
                        InlineFontSize = element.style.fontSize,
                        BaseFontSize = resolved
                    };
                    uitkOriginals[element] = state;
                }

                element.style.fontSize = state.BaseFontSize * scale;
            }

            traversal.Clear();
        }

        private static void RestoreUitk()
        {
            foreach (KeyValuePair<VisualElement, ElementState> pair in uitkOriginals)
            {
                if (pair.Key != null && pair.Key.panel != null)
                    pair.Key.style.fontSize = pair.Value.InlineFontSize;
            }
            uitkOriginals.Clear();
        }

        private static void PruneDetachedElements()
        {
            staleElements.Clear();
            foreach (KeyValuePair<VisualElement, ElementState> pair in uitkOriginals)
            {
                if (pair.Key == null || pair.Key.panel == null)
                    staleElements.Add(pair.Key);
            }
            for (int i = 0; i < staleElements.Count; i++)
                uitkOriginals.Remove(staleElements[i]);
            staleElements.Clear();
        }

        // ------------------------------------------------------------------ menu

        [MenuItem("Tools/Editor UI Scaler/Open Window")]
        private static void OpenWindow()
        {
            EditorWindow.GetWindow<EditorUIScalerWindow>("UI Scaler");
        }

        [MenuItem("Tools/Editor UI Scaler/Enable (saved scale)")]
        private static void MenuEnable()
        {
            SetEnabled(true);
        }

        [MenuItem("Tools/Editor UI Scaler/Disable and Restore Defaults")]
        private static void MenuDisable()
        {
            SetEnabled(false);
        }
    }

    public class EditorUIScalerWindow : EditorWindow
    {
        private float pendingScale;
        private bool pendingHeights;

        private void OnEnable()
        {
            minSize = new Vector2(320f, 260f);
            pendingScale = EditorUIScaler.Scale;
            pendingHeights = EditorUIScaler.ScaleHeights;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Unity Editor UI Scaler", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Scales text in the Unity Editor itself (Inspector, Hierarchy, Project, Console, " +
                "UI Toolkit windows). It does not touch game or runtime UI.\n\n" +
                "Some Editor controls use hard-coded row heights, so very large scales can clip text.",
                MessageType.Info);

            EditorGUILayout.Space();
            pendingScale = EditorGUILayout.Slider("Text scale", pendingScale,
                EditorUIScaler.MinScale, EditorUIScaler.MaxScale);
            pendingHeights = EditorGUILayout.Toggle(
                new GUIContent("Scale control heights", "Also grow fixed-height buttons/fields that use GUILayout."),
                pendingHeights);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply"))
                {
                    float scale = pendingScale;
                    bool heights = pendingHeights;
                    // Deferred: never change GUIStyles in the middle of an OnGUI pass.
                    EditorApplication.delayCall += () =>
                    {
                        EditorUIScaler.Scale = scale;
                        EditorUIScaler.ScaleHeights = heights;
                        EditorUIScaler.SetEnabled(true);
                    };
                }

                if (GUILayout.Button("Restore Defaults"))
                    EditorApplication.delayCall += () => EditorUIScaler.SetEnabled(false);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                EditorUIScaler.Enabled
                    ? "Status: active at " + EditorUIScaler.Scale.ToString("0.00") + "x"
                    : "Status: off",
                EditorStyles.miniLabel);
        }
    }
}
