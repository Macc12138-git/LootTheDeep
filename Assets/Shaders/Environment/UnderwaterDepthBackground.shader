Shader "LootTheDeep/Environment/UnderwaterDepthBackground"
{
    Properties
    {
        [HDR] _ShallowTopColor ("Shallow Top", Color) = (0.208, 0.796, 0.91, 1)
        [HDR] _ShallowBottomColor ("Shallow Bottom", Color) = (0.086, 0.478, 0.655, 1)
        [HDR] _MidTopColor ("Mid Top", Color) = (0.086, 0.443, 0.624, 1)
        [HDR] _MidBottomColor ("Mid Bottom", Color) = (0.035, 0.247, 0.392, 1)
        [HDR] _DeepTopColor ("Deep Top", Color) = (0.027, 0.208, 0.337, 1)
        [HDR] _DeepBottomColor ("Deep Bottom", Color) = (0.012, 0.063, 0.137, 1)
        _MidPoint ("Mid Depth Point", Range(0.1, 0.9)) = 0.42
        _RayStrength ("Light Ray Strength", Range(0, 0.5)) = 0.16
        _ParticleStrength ("Particle Strength", Range(0, 1)) = 0.55
        _DitherStrength ("Dither Strength", Range(0, 2)) = 0.7
        _ReferenceDotSpacing ("Prototype Dot Spacing (World Units)", Float) = 2
        _ReferenceDotSize ("Prototype Dot Size (World Units)", Float) = 0.06
        _ReferenceDotStrength ("Prototype Dot Strength", Range(0, 1)) = 0.2
        _ReferenceResolution ("Reference Resolution", Vector) = (640, 360, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Background"
        }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Underwater Background"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 positionWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowTopColor;
                half4 _ShallowBottomColor;
                half4 _MidTopColor;
                half4 _MidBottomColor;
                half4 _DeepTopColor;
                half4 _DeepBottomColor;
                float _MidPoint;
                float _RayStrength;
                float _ParticleStrength;
                float _DitherStrength;
                float _ReferenceDotSpacing;
                float _ReferenceDotSize;
                float _ReferenceDotStrength;
                float4 _ReferenceResolution;
            CBUFFER_END

            float _OceanDepth01;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz).xy;
                return output;
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 345.45));
                value += dot(value, value + 34.345);
                return frac(value.x * value.y);
            }

            half3 DepthGradient(float vertical, float depth)
            {
                half3 topColor;
                half3 bottomColor;

                if (depth < _MidPoint)
                {
                    float blend = smoothstep(0.0, _MidPoint, depth);
                    topColor = lerp(_ShallowTopColor.rgb, _MidTopColor.rgb, blend);
                    bottomColor = lerp(_ShallowBottomColor.rgb, _MidBottomColor.rgb, blend);
                }
                else
                {
                    float blend = smoothstep(_MidPoint, 1.0, depth);
                    topColor = lerp(_MidTopColor.rgb, _DeepTopColor.rgb, blend);
                    bottomColor = lerp(_MidBottomColor.rgb, _DeepBottomColor.rgb, blend);
                }

                float gradient = smoothstep(0.0, 1.0, vertical);
                return lerp(bottomColor, topColor, gradient);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 resolution = max(_ReferenceResolution.xy, float2(1.0, 1.0));
                float2 pixelUV = (floor(input.uv * resolution) + 0.5) / resolution;
                float depth = saturate(_OceanDepth01);
                half3 color = DepthGradient(pixelUV.y, depth);

                // Broad surface rays remain subtle and fade naturally with depth.
                float rayCoordinate = pixelUV.x * 12.0 + pixelUV.y * 2.4 + sin(_Time.y * 0.055) * 0.24;
                float rayBands = pow(saturate(sin(rayCoordinate) * 0.5 + 0.5), 11.0);
                float secondBand = pow(saturate(sin(rayCoordinate * 0.61 + 2.1) * 0.5 + 0.5), 15.0);
                float rayVerticalFade = smoothstep(0.16, 0.96, pixelUV.y);
                float rayDepthFade = lerp(1.0, 0.06, smoothstep(0.0, 0.82, depth));
                color += half3(0.18, 0.72, 0.82) * (rayBands + secondBand * 0.55)
                    * rayVerticalFade * rayDepthFade * _RayStrength;

                // Sparse pixel-sized suspended particles drift upward slowly.
                float2 particlePosition = pixelUV * float2(96.0, 54.0);
                particlePosition.y += _Time.y * 0.32;
                float2 particleCell = floor(particlePosition);
                float particleRandom = Hash21(particleCell);
                float2 particleLocal = abs(frac(particlePosition) - 0.5);
                float particleShape = step(max(particleLocal.x, particleLocal.y), 0.085);
                float particle = step(0.982, particleRandom) * particleShape;
                float particleDepthFade = lerp(1.0, 0.35, depth);
                color += half3(0.34, 0.88, 0.94) * particle * _ParticleStrength * particleDepthFade;

                // Temporary movement reference, anchored in world space independently of camera coverage.
                float dotSpacing = max(_ReferenceDotSpacing, 0.01);
                float2 dotDistance = abs(frac(input.positionWS / dotSpacing + 0.5) - 0.5) * dotSpacing;
                float referenceDot = step(max(dotDistance.x, dotDistance.y), _ReferenceDotSize * 0.5);
                color = lerp(color, half3(0.65, 0.88, 0.95), referenceDot * _ReferenceDotStrength);

                // Ordered dithering preserves the pixel-art character in broad gradients.
                float2 pixelIndex = floor(input.uv * resolution);
                float checker = fmod(pixelIndex.x + pixelIndex.y * 2.0, 4.0) / 3.0 - 0.5;
                color += checker * (_DitherStrength / 255.0);

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
