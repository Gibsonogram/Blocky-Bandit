using UnityEditor;
using UnityEngine;

// The overlay's look is one shared asset and its player option is one shared PlayerPrefs
// value, so both sections here are game-wide even though the component lives in a level scene.
[CustomEditor(typeof(VisionOverlayRenderer))]
public class VisionOverlayRendererEditor : Editor
{
    private const string StylePath = "Assets/Game/Resources/VisionOverlayStyle.asset";

    public override void OnInspectorGUI()
    {
        DrawGlobalStyle();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Player Settings (Visual Screen)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Both values are saved in PlayerPrefs and shared by every level. The in-game " +
            "Visual settings screen is the authority; changing them here updates the same values. " +
            "Reset returns them to VisionOverlayDefaults.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();

        float opacity = EditorGUILayout.Slider("Danger Overlay Opacity", SaveManager.OverlayOpacity, 0f, 1f);
        float radius = EditorGUILayout.Slider("Danger Overlay Size", SaveManager.OverlayRadius,
            VisionOverlayDefaults.MinRadius, VisionOverlayDefaults.MaxRadius);

        if (EditorGUI.EndChangeCheck())
        {
            // Match the in-game slider's hundredth granularity so both controls agree.
            SaveManager.OverlayOpacity = Mathf.Round(opacity * 100f) * 0.01f;
            SaveManager.OverlayRadius = Mathf.Round(radius * 100f) * 0.01f;
            VisionOverlayRenderer.ApplySavedSettings();
        }
    }

    private void DrawGlobalStyle()
    {
        EditorGUILayout.LabelField("Global Overlay Style (All Levels)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "These values live in Assets/Game/Resources/VisionOverlayStyle.asset. Editing them " +
            "here changes the danger overlay in every level.",
            MessageType.Info);

        VisionOverlayStyle style = AssetDatabase.LoadAssetAtPath<VisionOverlayStyle>(StylePath);
        if (style == null)
            style = Resources.Load<VisionOverlayStyle>(VisionOverlayRenderer.StyleResourceName);

        if (style == null)
        {
            EditorGUILayout.HelpBox(
                "The global style asset is missing. Create it to make overlay edits apply to every level.",
                MessageType.Warning);

            if (GUILayout.Button("Create Global Overlay Style"))
            {
                style = ScriptableObject.CreateInstance<VisionOverlayStyle>();
                AssetDatabase.CreateAsset(style, StylePath);
                AssetDatabase.SaveAssets();
                VisionOverlayRenderer.ReloadStyle();
            }

            if (style == null) return;
        }

        var styleObject = new SerializedObject(style);
        styleObject.Update();

        SerializedProperty property = styleObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.propertyPath == "m_Script" || property.propertyPath == "m_Name") continue;
            EditorGUILayout.PropertyField(property, true);
        }

        if (styleObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(style);
            VisionOverlayRenderer.ApplyStyleGlobally();
        }
    }
}

// Edits made directly on the style asset (rather than through a level's overlay Inspector)
// also need to rebake any overlay currently running.
[CustomEditor(typeof(VisionOverlayStyle))]
public class VisionOverlayStyleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(target);
            VisionOverlayRenderer.ApplyStyleGlobally();
        }
    }
}
