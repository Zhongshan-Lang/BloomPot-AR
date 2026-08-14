Shader "BloomPot/Hydrangea Petal Random Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _ColorA("Blue Tint", Color) = (0.34, 0.45, 0.92, 1)
        _ColorB("Purple Tint", Color) = (0.67, 0.32, 0.86, 1)
        _BudColorA("Bud Yellow Green", Color) = (0.82, 0.88, 0.58, 1)
        _BudColorB("Bud Fresh Green", Color) = (0.62, 0.78, 0.40, 1)
        _BloomMaturity("Bloom Maturity", Range(0, 1)) = 1
        _WiltColor("Wilt Yellow Tint", Color) = (0.95, 0.78, 0.32, 1)
        _WiltAmount("Wilt Amount", Range(0, 1)) = 0
        _WiltStrength("Wilt Tint Strength", Range(0, 1)) = 0.35
        _WiltBrightness("Wilt Brightness", Range(0.5, 1)) = 1
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0, 2)) = 0.85
        _RoughnessMap("Roughness Map", 2D) = "white" {}
        _Smoothness("Smoothness", Range(0, 1)) = 1

        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _Cull("Cull", Float) = 0
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _AlphaClip("Alpha Clip", Float) = 0
        [HideInInspector] _SrcBlend("Src Blend", Float) = 1
        [HideInInspector] _DstBlend("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite("Z Write", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_RoughnessMap);
            SAMPLER(sampler_RoughnessMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ColorA;
                half4 _ColorB;
                half4 _BudColorA;
                half4 _BudColorB;
                half _BloomMaturity;
                half4 _WiltColor;
                half _WiltAmount;
                half _WiltStrength;
                half _WiltBrightness;
                half _BumpScale;
                half _Smoothness;
                half _Cutoff;
                half _Cull;
                half _Surface;
                half _AlphaClip;
                half _SrcBlend;
                half _DstBlend;
                half _ZWrite;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                half4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half4 color : COLOR;
                half fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half petalVariation = saturate(input.color.r);
                half3 budTint = lerp(_BudColorA.rgb, _BudColorB.rgb, petalVariation);
                half3 matureTint = lerp(_ColorA.rgb, _ColorB.rgb, petalVariation);
                half3 tint = lerp(budTint, matureTint, saturate(_BloomMaturity));
                half3 albedo = baseSample.rgb * tint * _BaseColor.rgb;
                half wilt = smoothstep(0.0h, 1.0h, saturate(_WiltAmount)) * saturate(_WiltStrength);
                half luminance = dot(albedo, half3(0.299h, 0.587h, 0.114h));
                half3 wiltAlbedo = max(luminance, 0.08h) * _WiltColor.rgb;
                albedo = lerp(albedo, wiltAlbedo, wilt);
                albedo *= saturate(_WiltBrightness);

                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv),
                    _BumpScale);
                half3 normalWS = normalize(input.normalWS);
                half3 tangentWS = normalize(input.tangentWS.xyz);
                half3 bitangentWS = input.tangentWS.w * cross(normalWS, tangentWS);
                normalWS = normalize(TransformTangentToWorld(
                    normalTS,
                    half3x3(tangentWS, bitangentWS, normalWS)));

                half roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, input.uv).r;
                half smoothness = saturate((1.0h - roughness) * _Smoothness);
                half3 viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half lightAttenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 halfDirection = SafeNormalize(mainLight.direction + viewDirectionWS);
                half specularPower = exp2(4.0h + smoothness * 7.0h);
                half specular = pow(saturate(dot(normalWS, halfDirection)), specularPower) * smoothness;

                half3 ambient = max(SampleSH(normalWS), half3(0.08h, 0.08h, 0.08h));
                half3 color = albedo * (ambient + mainLight.color * diffuse * lightAttenuation);
                color += mainLight.color * specular * lightAttenuation * 0.35h;
                color = MixFog(color, input.fogFactor);
                return half4(color, baseSample.a * _BaseColor.a);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
