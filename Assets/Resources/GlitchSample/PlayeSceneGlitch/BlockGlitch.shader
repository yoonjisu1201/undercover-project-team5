Shader "Hidden/GlitchSample/BlockDamage"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _BlockDensity;
            float _BarDensity;
            float _MaxOffset;
            float _BarOpacity;
            float _Speed;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float RectMask(float2 uv, float2 minCorner, float2 maxCorner)
            {
                return step(minCorner.x, uv.x) * step(uv.x, maxCorner.x) *
                       step(minCorner.y, uv.y) * step(uv.y, maxCorner.y);
            }

            float RollingScanMask(float2 uv, float time, float speed, float offset, float width, float seed)
            {
                // 두 개의 파동을 섞어 주사선이 완벽한 직선으로 움직이지 않게 합니다.
                float wave =
                    sin(uv.x * 11.0 + time * (0.7 + seed * 0.1)) * 0.0035 +
                    sin(uv.x * 27.0 - time * 0.45 + seed) * 0.0015;
                float scanPosition = frac(time * speed + offset + wave);
                float scanDistance = abs(frac(uv.y - scanPosition + 0.5) - 0.5);

                // 중심부는 선명하게 유지하고 가장자리의 좁은 구간만 부드럽게 흐립니다.
                float scanBand = 1.0 - smoothstep(0.0, width, scanDistance);
                return scanBand * 0.8;
            }

            float3 Palette(float value)
            {
                if (value < 0.10) return float3(0.28, 0.86, 0.03); // green
                if (value < 0.20) return float3(0.35, 0.04, 0.48); // purple
                if (value < 0.30) return float3(0.95, 0.57, 0.04); // orange
                if (value < 0.40) return float3(0.02, 0.78, 0.82); // cyan
                if (value < 0.50) return float3(0.82, 0.88, 0.90); // white
                return float3(0.015, 0.018, 0.025); // black
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float intensity = saturate(_Intensity);
                // 연속 시간값을 사용해 패턴이 프레임 단위로 끊겨 바뀌지 않도록 합니다.
                float frame = _Time.y * max(_Speed, 0.01);
                // 해시 함수가 시간 변화를 크게 증폭하므로 블록 패턴에 전달할 시간은 추가로 감속합니다.
                float patternFrame = frame * 0.03;
                float2 uv = input.texcoord;

                // Tear the source image in coarse horizontal cells.
                float2 cell = floor(uv * float2(28.0, 18.0));
                float cellRandom = Hash21(cell + patternFrame * 0.17);
                float cellThreshold = 1.0 - _BlockDensity * 0.86;
                float cellActive = smoothstep(
                    cellThreshold,
                    min(1.0, cellThreshold + 0.12),
                    cellRandom);
                float cellOffset = (Hash21(cell + patternFrame * 0.17 + 23.7) * 2.0 - 1.0) * _MaxOffset * intensity;

                float row = floor(uv.y * 42.0);
                float rowOffset = (Hash21(float2(row, patternFrame * 0.31)) * 2.0 - 1.0) * _MaxOffset * 0.35 * intensity;
                float2 sampleUv = frac(uv + float2(cellOffset * cellActive + rowOffset, 0.0));

                // Small RGB separation keeps the damaged image from looking like a flat overlay.
                float split = 0.006 * intensity * (0.4 + cellActive);
                float red = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearRepeat, frac(sampleUv + float2(split, 0.0))).r;
                float green = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearRepeat, sampleUv).g;
                float blue = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearRepeat, frac(sampleUv - float2(split, 0.0))).b;
                float3 color = float3(red, green, blue);

                // Draw sparse, chunky horizontal damage bars over the image.
                float barMask = 0.0;
                float3 barColor = color;

                [unroll]
                for (int index = 0; index < 16; index++)
                {
                    float seed = patternFrame * 0.073 + index * 17.13;
                    float barThreshold = 1.0 - _BarDensity;
                    float alive = smoothstep(
                        barThreshold,
                        min(1.0, barThreshold + 0.12),
                        Hash21(float2(seed, 3.7)));
                    float y = Hash21(float2(seed, 7.1));
                    float height = lerp(0.010, 0.065, Hash21(float2(seed, 11.9))) * (0.7 + intensity * 0.9);
                    float x = floor(Hash21(float2(seed, 19.4)) * 12.0) / 12.0;
                    float width = lerp(0.045, 0.52, Hash21(float2(seed, 23.8)));
                    float mask = RectMask(uv, float2(x, y), float2(min(1.0, x + width), min(1.0, y + height))) * alive;
                    float segmentCut = smoothstep(
                        0.12,
                        0.28,
                        Hash21(float2(floor(uv.x * 18.0) + index, seed)));
                    mask *= lerp(0.55, 1.0, segmentCut);

                    float3 candidate = Palette(Hash21(float2(seed, 31.6)));
                    barColor = lerp(barColor, candidate, mask);
                    barMask = max(barMask, mask);
                }

                // Keep the broad horizontal bars, but remove the scattered vertical fragments.
                float3 finalColor = lerp(color, barColor, saturate(barMask * _BarOpacity));

                // 한 개의 주사선만 순환시켜 다음 선까지 화면 전체 높이만큼 간격을 확보합니다.
                float scanTime = _Time.y;
                float scanOffsetA = 0.00 + sin(scanTime * 0.21) * 0.024;
                float scanDarkness = RollingScanMask(
                    uv,
                    scanTime,
                    0.8,
                    scanOffsetA,
                    0.008,
                    1.3);
                scanDarkness = min(scanDarkness, 0.82);
                finalColor *= 1.0 - scanDarkness;

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
