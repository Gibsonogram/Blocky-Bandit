using UnityEngine;

// The danger overlay's authored look. One asset lives at Resources/VisionOverlayStyle and every
// level's VisionOverlayRenderer loads it, so editing it from any level's Inspector (or from the
// asset itself) applies to every level. The two things the player can change - overall opacity
// and indicator size - are separate saved preferences (SaveManager / the Visual settings screen);
// indicator shapes are chosen by the actor prefabs.
[CreateAssetMenu(menuName = "Blocky Bandit/Vision Overlay Style", fileName = "VisionOverlayStyle")]
public sealed class VisionOverlayStyle : ScriptableObject
{
    [Header("Rendering")]
    [Tooltip("Template material for the overlay mesh. The renderer derives its fill and outline " +
             "materials from this one, so the shader here is the overlay's shader. Falls back to " +
             "Resources/VisionTileTint when empty.")]
    public Material tintMaterial;

    [Tooltip("Sorting layer for the tint. Must be below the actors and above the floor.")]
    public string sortingLayer = "WalkInFront";

    public int sortingOrder = 5;

    [Header("Colors (Apollo palette)")]
    // #E8C170: a tile that will arm a mine, start a chase or count down to a shot.
    public Color warningColor = new Color(0.9098039f, 0.75686276f, 0.4392157f, 1f);
    // #A53030: a tile the actor's own rules will kill on next turn.
    public Color lethalColor = new Color(0.64705884f, 0.1882353f, 0.1882353f, 1f);

    [Header("Outline")]
    [Tooltip("Colour of the fixed one-art-pixel outline. Its alpha is the outline's share of the " +
             "overlay's opacity, so 1 keeps the outline as strong as the fill.")]
    public Color outlineColor = new Color(0.06f, 0.05f, 0.07f, 1f);

    [Header("Player defaults")]
    // One opacity for the whole overlay: the fill and the outline are both scaled by it, so this
    // is the only strength control the designer or the player ever touches - the colours above
    // carry the hue. These two are read live (VisionOverlayDefaults), so tuning them here sets
    // what the Visual settings screen starts on and what its Reset restores; a player's saved
    // preference still wins over them.
    [Tooltip("Default overlay opacity (the Visual settings screen's starting value).")]
    [Range(0f, 1f)] public float defaultOpacity = 0.21f;

    [Tooltip("Default indicator size in world units (one tile is 1 unit).")]
    [Range(0.1f, 0.25f)] public float defaultRadius = 0.237f;
}
