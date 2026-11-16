Shader "Custom/VisionTileTint"
{
    Properties
    {
        _MainTex ("Mask (R = fill, G = outline)", 2D) = "white" {}
        _OutlineColor ("Outline", Color) = (0, 0, 0, 1)
        _OverlayOpacity ("Overlay Opacity", Range(0, 1)) = 1

        // Driven from VisionOverlayRenderer: the mesh carries two submeshes over the same
        // triangles, so submesh 0 draws the fill and submesh 1 draws the outline over the top
        // of every fill.
        [HideInInspector] _UseOutline ("Use Outline Channel", Float) = 0
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        ZWrite Off
        Cull Off

        Pass
        {
            Name "VisionTileTint"
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _OutlineColor;
            float _OverlayOpacity;
            float _UseOutline;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            // No branch: the fill submesh reads the R channel and the vertex colour, the outline
            // submesh reads the G channel and the outline colour. One texel is never both, so
            // the two submeshes never fight over a pixel.
            float4 Frag(Varyings input) : SV_Target
            {
                float4 mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float coverage = lerp(mask.r, mask.g, _UseOutline);
                float4 color = lerp(input.color, _OutlineColor, _UseOutline);
                color.a *= coverage * _OverlayOpacity;
                return color;
            }
            ENDHLSL
        }
    }
}
