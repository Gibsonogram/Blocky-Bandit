Shader "Custom/GridOverlay"
{
    Properties
    {
        _GridColor ("Grid Color", Color) = (1, 1, 1, 0.3)
        _LineThickness ("Line Thickness", Float) = 0.04
        _TileSize ("Tile Size", Float) = 1.0
        _GridOffset ("Grid Offset", Vector) = (0.5, 0.5, 0, 0)
        _Opacity ("Opacity", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "GridOverlay"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GridColor;
                float  _LineThickness;
                float  _TileSize;
                float4 _GridOffset;
                float  _Opacity;
            CBUFFER_END

            float4x4 _ClipToWorld;

            float4 Frag(Varyings input) : SV_Target
            {
                float2 ndc = input.texcoord * 2.0 - 1.0;
                float4 worldPos = mul(_ClipToWorld, float4(ndc, 0.0, 1.0));
                float2 wp = worldPos.xy / worldPos.w;

                float2 local = wp - _GridOffset.xy;
                float2 cell  = frac(local / _TileSize) * _TileSize;
                float2 lineDist = min(cell, _TileSize - cell);

                float half_t = _LineThickness * 0.5;
                float onLine = saturate(step(lineDist.x, half_t) + step(lineDist.y, half_t));

                float4 col = _GridColor;
                col.a *= onLine * _Opacity;
                return col;
            }
            ENDHLSL
        }
    }
}
