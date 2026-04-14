Shader "Custom/VisionOverlay"
{
    Properties
    {
        _DotColor   ("Dot Color",   Color)  = (1, 1, 0, 0.5)
        _DotRadius  ("Dot Radius",  Float)  = 0.18
        _TileSize   ("Tile Size",   Float)  = 1.0
        _GridOffset ("Grid Offset", Vector) = (0.5, 0.5, 0, 0)
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
            Name "VisionOverlay"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DotColor;
                float  _DotRadius;
                float  _TileSize;
                float4 _GridOffset;
            CBUFFER_END

            float4x4 _ClipToWorld;

            StructuredBuffer<float2> _TilePositions;
            int _TileCount;

            float4 Frag(Varyings input) : SV_Target
            {
                float2 ndc = input.texcoord * 2.0 - 1.0;
                float4 worldPos = mul(_ClipToWorld, float4(ndc, 0.0, 1.0));
                float2 wp = worldPos.xy / worldPos.w;

                for (int i = 0; i < _TileCount; i++)
                {
                    // centre of tile in world space
                    float2 tileCenter = _TilePositions[i] * _TileSize + _GridOffset.xy;
                    float dist = length(wp - tileCenter);
                    if (dist < _DotRadius)
                    {
                        float alpha = 1.0 - smoothstep(_DotRadius * 0.7, _DotRadius, dist);
                        return float4(_DotColor.rgb, _DotColor.a * alpha);
                    }
                }

                return float4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
