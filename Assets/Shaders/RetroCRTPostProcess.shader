Shader "Mimic/RetroCRTPostProcess"
{
    Properties
    {
        _Intensity("Intensity", Range(0, 1)) = 0.9
        _Curvature("CRT Bulge", Range(0, 1)) = 0.55
        _Vignette("Edge Fade", Range(0, 1)) = 0.38
        _ScanlineStrength("Scanline Strength", Range(0, 1)) = 0.18
        _ScanlineDensity("Scanline Density", Range(0.5, 2.5)) = 1.15
        _NoiseStrength("Tape Noise", Range(0, 0.25)) = 0.025
        _ChromaticAberration("Chromatic Aberration", Range(0, 2)) = 0.55
        _GlitchStrength("Glitch Strength", Range(0, 1)) = 0.4
        _ColorBleed("Color Bleed", Range(0, 1)) = 0.45
        _TapeWobble("Tape Wobble", Range(0, 1)) = 0.45
        _Desaturation("Desaturation", Range(0, 1)) = 0.16
        _Tint("CRT Tint", Color) = (0.92, 1.0, 0.88, 1.0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "Retro CRT VHS"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            SAMPLER(sampler_BlitTexture);

            float _Intensity;
            float _Curvature;
            float _Vignette;
            float _ScanlineStrength;
            float _ScanlineDensity;
            float _NoiseStrength;
            float _ChromaticAberration;
            float _GlitchStrength;
            float _ColorBleed;
            float _TapeWobble;
            float _Desaturation;
            float4 _Tint;

            float Hash12(float2 value)
            {
                float3 p = frac(float3(value.xyx) * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float2 ApplyCrtBulge(float2 uv)
            {
                float2 centered = uv * 2.0 - 1.0;
                float radiusSq = dot(centered, centered);
                centered *= 1.0 + radiusSq * _Curvature * 0.24;
                return centered * 0.5 + 0.5;
            }

            float2 ApplyTapeDamage(float2 uv, float time)
            {
                float slowWave = sin(uv.y * 42.0 + time * 2.3) * 0.0032;
                float fastWave = sin(uv.y * 150.0 - time * 9.0) * 0.0015;
                uv.x += (slowWave + fastWave) * _TapeWobble;

                float band = floor(uv.y * 95.0);
                float bandTime = floor(time * 11.0);
                float bandGate = step(1.0 - _GlitchStrength * 0.18, Hash12(float2(band, bandTime)));
                float bandShift = (Hash12(float2(band * 2.17, bandTime + 7.0)) - 0.5) * 0.07;
                uv.x += bandShift * bandGate * _GlitchStrength;

                float tearBand = floor(uv.y * 15.0);
                float tearGate = step(1.0 - _GlitchStrength * 0.08, Hash12(float2(tearBand, floor(time * 5.0))));
                uv.x += sin(uv.y * 80.0 + time * 24.0) * 0.018 * tearGate * _GlitchStrength;

                return uv;
            }

            half3 SampleScene(float2 uv)
            {
                float inside = step(0.0, uv.x) * step(0.0, uv.y) * step(uv.x, 1.0) * step(uv.y, 1.0);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, saturate(uv)).rgb * inside;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 originalUv = input.texcoord;
                float2 pixel = originalUv * _ScreenParams.xy;
                float time = _Time.y;

                float2 warpedUv = ApplyCrtBulge(originalUv);
                warpedUv = ApplyTapeDamage(warpedUv, time);

                float2 centered = originalUv * 2.0 - 1.0;
                float2 chromaOffset = centered * _ChromaticAberration * 0.0045;

                half3 original = SampleScene(originalUv);
                half3 color;
                color.r = SampleScene(warpedUv + chromaOffset).r;
                color.g = SampleScene(warpedUv).g;
                color.b = SampleScene(warpedUv - chromaOffset).b;

                half3 bleedA = SampleScene(warpedUv + float2(_BlitTexture_TexelSize.x * 2.5, 0.0));
                half3 bleedB = SampleScene(warpedUv - float2(_BlitTexture_TexelSize.x * 2.5, 0.0));
                half3 bleed = (bleedA + bleedB) * 0.5;
                color = lerp(color, half3(bleed.r, color.g, bleed.b), _ColorBleed * 0.22);

                float scan = pow(abs(sin(pixel.y * 3.14159265 * _ScanlineDensity)), 0.65);
                color *= 1.0 - _ScanlineStrength * (1.0 - scan);

                float grillePhase = frac(pixel.x / 3.0);
                half3 grille = grillePhase < 0.3333 ? half3(1.08, 0.88, 0.88) : (grillePhase < 0.6666 ? half3(0.88, 1.04, 0.88) : half3(0.88, 0.90, 1.08));
                color *= lerp(half3(1.0, 1.0, 1.0), grille, 0.10 * _Intensity);

                float luma = dot(color, half3(0.299, 0.587, 0.114));
                float noise = Hash12(pixel + floor(time * 30.0) * float2(43.7, 17.3)) - 0.5;
                color += noise * _NoiseStrength * (0.35 + luma * 0.65);

                color = lerp(color, luma.xxx, _Desaturation);
                color *= _Tint.rgb;
                color = (color - 0.5) * 1.08 + 0.5;

                float vignette = 1.0 - smoothstep(0.32, 1.25, dot(centered, centered)) * _Vignette;
                color *= vignette;

                float flicker = 1.0 + (Hash12(float2(floor(time * 28.0), 13.0)) - 0.5) * 0.045 * _Intensity;
                color *= flicker;

                color = saturate(color);
                return half4(lerp(original, color, _Intensity), 1.0);
            }
            ENDHLSL
        }
    }
}
