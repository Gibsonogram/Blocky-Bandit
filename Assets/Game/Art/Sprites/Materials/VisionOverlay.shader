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

            // Visible tiles packed one bit per tile over [_GridMin, _GridMin + _GridSize).
            // A flat bitset replaces the old per-tile position array so that the fragment
            // shader costs a constant number of samples per pixel instead of O(tiles).
            StructuredBuffer<uint> _TileBits;
            float4 _GridMin;
            float4 _GridSize;

            // True when the given tile coordinate is a visible (danger) tile.
            bool TileVisible(int2 t)
            {
                int2 size = (int2)_GridSize.xy;
                int2 rel = t - (int2)_GridMin.xy;
                if (rel.x < 0 || rel.y < 0 || rel.x >= size.x || rel.y >= size.y)
                    return false;

                int index = rel.y * size.x + rel.x;
                return (_TileBits[index >> 5] & (1u << (index & 31))) != 0u;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 ndc = input.texcoord * 2.0 - 1.0;
                float4 worldPos = mul(_ClipToWorld, float4(ndc, 0.0, 1.0));
                float2 wp = worldPos.xy / worldPos.w;

                // Match GridUtils.WorldToGrid: round((world - offset) / tileSize).
                int2 tile = (int2)round((wp - _GridOffset.xy) / _TileSize);

                // The dot radius is far smaller than half a tile, so only the containing
                // tile and its immediate neighbours can ever cover this pixel.
                float bestDist = _DotRadius;
                bool hit = false;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int2 t = tile + int2(dx, dy);
                        if (!TileVisible(t)) continue;

                        float2 tileCenter = (float2)t * _TileSize + _GridOffset.xy;
                        float dist = length(wp - tileCenter);
                        if (dist < bestDist) { bestDist = dist; hit = true; }
                    }
                }

                if (!hit) return float4(0, 0, 0, 0);

                float alpha = 1.0 - smoothstep(_DotRadius * 0.7, _DotRadius, bestDist);
                return float4(_DotColor.rgb, _DotColor.a * alpha);
            }
            ENDHLSL
        }
    }
}
