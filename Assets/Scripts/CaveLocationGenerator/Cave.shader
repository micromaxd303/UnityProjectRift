Shader "Custom/Triplanar"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Scale ("Texture Scale", Float) = 1.0
        _Sharpness ("Blend Sharpness", Range(1, 16)) = 4.0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float _Scale;
                float _Sharpness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Геометрическая нормаль для triplanar
                float3 worldPosDdx = ddx(IN.worldPos);
                float3 worldPosDdy = ddy(IN.worldPos);
                float3 flatNormal = normalize(cross(worldPosDdy, worldPosDdx));

                // Triplanar blend по flat нормали
                float3 n = abs(flatNormal);
                n = pow(n, _Sharpness);
                n /= (n.x + n.y + n.z);

                float3 pos = IN.worldPos * _Scale;

                half4 texX = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, pos.yz);
                half4 texY = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, pos.xz);
                half4 texZ = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, pos.xy);

                half4 color = texX * n.x + texY * n.y + texZ * n.z;

                // Сглаженная нормаль для освещения
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(normalize(IN.worldNormal), mainLight.direction));
                float3 lighting = mainLight.color * NdotL + unity_AmbientSky.rgb;

                return half4(color.rgb * lighting, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}