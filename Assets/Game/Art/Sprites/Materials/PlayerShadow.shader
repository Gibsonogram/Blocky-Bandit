// Black silhouette copy of the player that only lands on pixels the walk-behind tilemap
// actually paints. Works with Custom/WalkBehindStencil: that shader marks stencil bit 128
// under the walk-behind tile art, and this one keeps a pixel only where that bit is set.
// Everything else is discarded, so the normal player sprite stays visible there.
//
// This is the URP 2D Sprite-Unlit-Default shader plus the stencil test and an alpha clip.
// Sprite renderer colour (the scene's 62% black), flipping and per-renderer texture all keep
// working because the sprite-lit vertex/fragment helpers are unchanged.
Shader "Custom/PlayerShadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _AlphaClip ("Alpha Clip", Range(0, 1)) = 0.5
        [MaterialToggle] _ZWrite ("ZWrite", Float) = 0

        // Legacy properties. They're here so that materials using this shader can gracefully fallback to the legacy sprite shader.
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            // Bit 7 of the stencil buffer is ours. Unity's SpriteMask uses the other bits
            // (one per sorting order), so the hole's fall mask never collides with this.
            // Keep (not Zero) so the mask survives for anything drawn after the silhouette.
            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp Equal
                Pass Keep
            }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _AlphaClip;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color *_Color * unity_SpriteColor;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 c = CommonUnlitFragment(input, input.color);
                // Transparent texels must not punch a hole in the stencil picture.
                clip(c.a - _AlphaClip);
                return c;
            }
            ENDHLSL
        }
    }
}
