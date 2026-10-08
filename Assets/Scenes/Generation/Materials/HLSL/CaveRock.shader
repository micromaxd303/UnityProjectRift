// Материал пещеры: зоны пол/стены/потолок, шумовые кляксы, цвета штампов (vertex color),
// трипланарная текстура с картой нормалей, PBR-освещение URP (доп. источники, тени, туман, SSAO).
//
// Vertex color (заполняет генератор, этап 2; включается флажком «Цвета штампов»):
//   R — ниша, G — колонна, B — пятачок, A — укрытие. 0 — нет штампа, 1 — полностью.
Shader "Custom/CaveRock"
{
    Properties
    {
        [Header(Zones)]
        _WallColor     ("Стены", Color) = (0.42, 0.33, 0.26, 1)
        _FloorColor    ("Пол", Color) = (0.36, 0.30, 0.25, 1)
        _CeilingColor  ("Потолок", Color) = (0.30, 0.24, 0.20, 1)
        _FloorThreshold("Пол: порог normal.y", Range(0, 1)) = 0.55
        _CeilThreshold ("Потолок: порог -normal.y", Range(0, 1)) = 0.6
        _ZoneBlend     ("Мягкость границы зон", Range(0.001, 0.5)) = 0.12
        _ZoneNoise     ("Шум границы зон", Range(0, 1)) = 0.25

        [Header(Blotches 1)]
        _Blotch1Color  ("Кляксы 1: цвет", Color) = (0.55, 0.22, 0.12, 1)
        _Blotch1Scale  ("Кляксы 1: частота, 1/м", Float) = 0.08
        _Blotch1Thr    ("Кляксы 1: порог (больше — реже)", Range(-0.6, 0.6)) = 0.2
        _Blotch1Soft   ("Кляксы 1: мягкость края", Range(0.001, 0.3)) = 0.05
        _Blotch1Amount ("Кляксы 1: сила", Range(0, 1)) = 1

        [Header(Blotches 2)]
        _Blotch2Color  ("Кляксы 2: цвет", Color) = (0.22, 0.30, 0.38, 1)
        _Blotch2Scale  ("Кляксы 2: частота, 1/м", Float) = 0.2
        _Blotch2Thr    ("Кляксы 2: порог (больше — реже)", Range(-0.6, 0.6)) = 0.3
        _Blotch2Soft   ("Кляксы 2: мягкость края", Range(0.001, 0.3)) = 0.03
        _Blotch2Amount ("Кляксы 2: сила", Range(0, 1)) = 0
        _NoiseSeed     ("Сид шума", Vector) = (13.7, 4.1, 27.3, 0)

        [Header(Stamps)]
        [Toggle(_STAMP_COLORS)] _StampColors ("Цвета штампов из vertex color", Float) = 0
        _NicheColor    ("Ниша (R)", Color) = (0.12, 0.09, 0.08, 1)
        _PillarColor   ("Колонна (G)", Color) = (0.45, 0.45, 0.45, 1)
        _PadColor      ("Пятачок (B)", Color) = (0.22, 0.22, 0.23, 1)
        _CoverColor    ("Укрытие (A)", Color) = (0.32, 0.28, 0.26, 1)
        _StampBlotches ("Кляксы поверх штампов", Range(0, 1)) = 0.3

        [Header(Texture)]
        _MainTex       ("Текстура (умножается на цвет)", 2D) = "white" {}
        _TexStrength   ("Сила текстуры", Range(0, 1)) = 1
        [Normal] _BumpMap ("Карта нормалей", 2D) = "bump" {}
        _BumpScale     ("Сила нормалей", Range(0, 2)) = 1
        _Scale         ("Масштаб текстур, 1/м", Float) = 0.25
        _Sharpness     ("Резкость трипланара", Range(1, 16)) = 4

        [Header(Surface)]
        [Toggle(_FLAT_SHADING)] _FlatShading ("Гранёное освещение", Float) = 1
        _FacetVariation("Разброс яркости граней", Range(0, 0.5)) = 0.08
        _Smoothness    ("Гладкость", Range(0, 1)) = 0.15
        _Metallic      ("Металличность", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _WallColor, _FloorColor, _CeilingColor;
            float _FloorThreshold, _CeilThreshold, _ZoneBlend, _ZoneNoise;

            half4 _Blotch1Color;
            float _Blotch1Scale, _Blotch1Thr, _Blotch1Soft, _Blotch1Amount;
            half4 _Blotch2Color;
            float _Blotch2Scale, _Blotch2Thr, _Blotch2Soft, _Blotch2Amount;
            float4 _NoiseSeed;

            half4 _NicheColor, _PillarColor, _PadColor, _CoverColor;
            float _StampBlotches;

            float4 _MainTex_ST;
            float _TexStrength;
            float4 _BumpMap_ST;
            float _BumpScale, _Scale, _Sharpness;

            float _FacetVariation, _Smoothness, _Metallic;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _STAMP_COLORS
            #pragma shader_feature_local_fragment _FLAT_SHADING

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  stamps     : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
            };

            // ---------------- шум ----------------

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx) * 2.0 - 1.0;
            }

            // Градиентный шум, примерно [-0.7, 0.7]
            float GradientNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                float n000 = dot(Hash33(i + float3(0, 0, 0)), f - float3(0, 0, 0));
                float n100 = dot(Hash33(i + float3(1, 0, 0)), f - float3(1, 0, 0));
                float n010 = dot(Hash33(i + float3(0, 1, 0)), f - float3(0, 1, 0));
                float n110 = dot(Hash33(i + float3(1, 1, 0)), f - float3(1, 1, 0));
                float n001 = dot(Hash33(i + float3(0, 0, 1)), f - float3(0, 0, 1));
                float n101 = dot(Hash33(i + float3(1, 0, 1)), f - float3(1, 0, 1));
                float n011 = dot(Hash33(i + float3(0, 1, 1)), f - float3(0, 1, 1));
                float n111 = dot(Hash33(i + float3(1, 1, 1)), f - float3(1, 1, 1));

                return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                            lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
            }

            float Fbm(float3 p)
            {
                float v = GradientNoise(p) * 0.6;
                v += GradientNoise(p * 2.03 + 17.1) * 0.28;
                v += GradientNoise(p * 4.11 + 41.7) * 0.12;
                return v;
            }

            float Blotch(float3 p, float scale, float thr, float soft)
            {
                float n = Fbm(p * scale);
                return smoothstep(thr - soft, thr + soft, n);
            }

            // ---------------- трипланар ----------------

            struct Triplanar
            {
                float2 uvX, uvY, uvZ;
                float3 blend;
                float3 axisSign;
            };

            Triplanar MakeTriplanar(float3 positionWS, float3 n)
            {
                Triplanar t;
                float3 b = pow(abs(n), _Sharpness);
                t.blend = b / (b.x + b.y + b.z);
                t.axisSign = step(0.0, n) * 2.0 - 1.0;

                float3 p = positionWS * _Scale;
                t.uvX = p.zy;
                t.uvY = p.xz;
                t.uvZ = p.xy;
                // Зеркалим, чтобы текстура не отражалась на противоположных сторонах
                t.uvX.x *= t.axisSign.x;
                t.uvY.x *= t.axisSign.y;
                t.uvZ.x *= -t.axisSign.z;
                return t;
            }

            half3 SampleAlbedo(Triplanar t)
            {
                half3 x = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(t.uvX, _MainTex)).rgb;
                half3 y = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(t.uvY, _MainTex)).rgb;
                half3 z = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(t.uvZ, _MainTex)).rgb;
                return x * t.blend.x + y * t.blend.y + z * t.blend.z;
            }

            // Whiteout blend (Ben Golus, «Normal Mapping for a Triplanar Shader»)
            float3 SampleNormalWS(Triplanar t, float3 n)
            {
                half3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(t.uvX, _BumpMap)), _BumpScale);
                half3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(t.uvY, _BumpMap)), _BumpScale);
                half3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(t.uvZ, _BumpMap)), _BumpScale);

                tx.x *= t.axisSign.x;
                ty.x *= t.axisSign.y;
                tz.x *= -t.axisSign.z;

                tx.z *= t.axisSign.x;
                ty.z *= t.axisSign.y;
                tz.z *= t.axisSign.z;

                float3 an = abs(n);
                tx = half3(tx.xy + n.zy, an.x * tx.z);
                ty = half3(ty.xy + n.xz, an.y * ty.z);
                tz = half3(tz.xy + n.xy, an.z * tz.z);

                return normalize(tx.zyx * t.blend.x + ty.xzy * t.blend.y + tz.xyz * t.blend.z);
            }

            // ---------------- проходы ----------------

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
            #if defined(_STAMP_COLORS)
                OUT.stamps = saturate(IN.color);
            #else
                OUT.stamps = 0;
            #endif
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half3 RockColor(float3 positionWS, float3 smoothN, float3 facetN, half4 stamps)
            {
                float3 sp = positionWS + _NoiseSeed.xyz;

                // Зоны по сглаженной нормали, граница слегка рваная
                float zoneNoise = Fbm(sp * 0.35) * _ZoneNoise;
                float ny = smoothN.y + zoneNoise;
                float floorW = smoothstep(_FloorThreshold - _ZoneBlend, _FloorThreshold + _ZoneBlend, ny);
                float ceilW  = smoothstep(_CeilThreshold - _ZoneBlend, _CeilThreshold + _ZoneBlend, -ny);

                half3 c = _WallColor.rgb;
                c = lerp(c, _FloorColor.rgb, floorW);
                c = lerp(c, _CeilingColor.rgb, ceilW);

                float b1 = Blotch(sp, _Blotch1Scale, _Blotch1Thr, _Blotch1Soft) * _Blotch1Amount;
                float b2 = Blotch(sp + 101.3, _Blotch2Scale, _Blotch2Thr, _Blotch2Soft) * _Blotch2Amount;

            #if defined(_STAMP_COLORS)
                // Штампы перекрашивают камень; кляксы проступают поверх с ослаблением
                half stampSum = saturate(stamps.r + stamps.g + stamps.b + stamps.a);
                half3 stampCol = (_NicheColor.rgb * stamps.r + _PillarColor.rgb * stamps.g
                                + _PadColor.rgb * stamps.b + _CoverColor.rgb * stamps.a)
                                / max(stamps.r + stamps.g + stamps.b + stamps.a, 1e-4);
                c = lerp(c, stampCol, stampSum);
                float blotchKeep = lerp(1.0, _StampBlotches, stampSum);
                b1 *= blotchKeep;
                b2 *= blotchKeep;
            #endif

                c = lerp(c, _Blotch1Color.rgb, b1);
                c = lerp(c, _Blotch2Color.rgb, b2);

                // Разброс яркости граней: хеш от нормали грани, у каждой грани свой оттенок
                float facet = frac(sin(dot(round(facetN * 64.0), float3(12.9898, 78.233, 37.719))) * 43758.5453);
                c *= 1.0 + (facet - 0.5) * 2.0 * _FacetVariation;

                return c;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 smoothN = normalize(IN.normalWS);
                float3 facetN = normalize(cross(ddy(IN.positionWS), ddx(IN.positionWS)));
                // Нормаль грани может смотреть внутрь камня при отражённых чанках — выравниваем по вершинной
                facetN *= sign(dot(facetN, smoothN) + 1e-5);

            #if defined(_FLAT_SHADING)
                float3 geomN = facetN;
            #else
                float3 geomN = smoothN;
            #endif

                Triplanar t = MakeTriplanar(IN.positionWS, geomN);

                half3 albedo = RockColor(IN.positionWS, smoothN, facetN, IN.stamps);
                albedo *= lerp(half3(1, 1, 1), SampleAlbedo(t), _TexStrength);

                float3 normalWS = SampleNormalWS(t, geomN);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = _Metallic;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);

            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - worldPos);
            #else
                float3 lightDir = _LightDirection;
            #endif

                // Только depth bias, без normal bias (как в прежнем Cave.shader)
                worldPos += lightDir * 0.05;
                OUT.positionCS = TransformWorldToHClip(worldPos);

            #if UNITY_REVERSED_Z
                OUT.positionCS.z = min(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                OUT.positionCS.z = max(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                OUT.positionCS.z += 0.001;
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half DepthFrag(Varyings IN) : SV_Target { return IN.positionCS.z; }
            ENDHLSL
        }

        // Нужен для SSAO (затемнение впадин) в режиме Depth Normals
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma shader_feature_local_fragment _FLAT_SHADING
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings DNVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DNFrag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
            #if defined(_FLAT_SHADING)
                float3 f = normalize(cross(ddy(IN.positionWS), ddx(IN.positionWS)));
                n = f * sign(dot(f, n) + 1e-5);
            #endif
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
            #else
                return half4(n, 0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
