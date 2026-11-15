using UnityEditor;
using UnityEngine;

// The overlay's player-facing opacity and shape are stored in PlayerPrefs so the in-game
// Options menu and this Inspector always agree. Draw the regular scene fields, then a
// compact game-wide section wired to those same saved values.
[CustomEditor(typeof(VisionOverlayRenderer))]
public class VisionOverlayRendererEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Danger Overlay (Player Setting)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "These values are shared by every level and saved in PlayerPrefs. The in-game " +
            "Options menu is the authority; changing them here updates the same values.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();

        float opacity = EditorGUILayout.Slider("Opacity", SaveManager.OverlayOpacity, 0f, 1f);
        VisionOverlayShape shape = (VisionOverlayShape)EditorGUILayout.EnumPopup("Shape", SaveManager.OverlayShape);

        if (EditorGUI.EndChangeCheck())
        {
            // Match the in-game slider's whole-percent granularity so both controls agree.
            SaveManager.OverlayOpacity = Mathf.Round(opacity * 100f) * 0.01f;
            SaveManager.OverlayShape = shape;
            VisionOverlayRenderer.ApplySavedSettings();
        }
    }
}
