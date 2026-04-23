Shader "ProceduralWorld/Terrain"
{
    Properties
    {
        _BiomeTex0 ("Biome Texture 0", 2D) = "white" {}
        _BiomeTex1 ("Biome Texture 1", 2D) = "white" {}
        _BiomeTex2 ("Biome Texture 2", 2D) = "white" {}
        _BiomeTex3 ("Biome Texture 3", 2D) = "white" {}
        _BiomeTex4 ("Biome Texture 4", 2D) = "white" {}
        _BiomeTex5 ("Biome Texture 5", 2D) = "white" {}
        _BiomeTex6 ("Biome Texture 6", 2D) = "white" {}
        _BiomeTex7 ("Biome Texture 7", 2D) = "white" {}
        _BiomeScale0 ("Biome Texture Size 0", Float) = 24
        _BiomeScale1 ("Biome Texture Size 1", Float) = 24
        _BiomeScale2 ("Biome Texture Size 2", Float) = 24
        _BiomeScale3 ("Biome Texture Size 3", Float) = 24
        _BiomeScale4 ("Biome Texture Size 4", Float) = 24
        _BiomeScale5 ("Biome Texture Size 5", Float) = 24
        _BiomeScale6 ("Biome Texture Size 6", Float) = 24
        _BiomeScale7 ("Biome Texture Size 7", Float) = 24
        _BiomeLayerCount ("Biome Layer Count", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        // ── Forward Lit ─────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 biomeData  : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float2 biomeData   : TEXCOORD2;
                float4 vertexColor : COLOR;
            };

            TEXTURE2D(_BiomeTex0); SAMPLER(sampler_BiomeTex0);
            TEXTURE2D(_BiomeTex1); SAMPLER(sampler_BiomeTex1);
            TEXTURE2D(_BiomeTex2); SAMPLER(sampler_BiomeTex2);
            TEXTURE2D(_BiomeTex3); SAMPLER(sampler_BiomeTex3);
            TEXTURE2D(_BiomeTex4); SAMPLER(sampler_BiomeTex4);
            TEXTURE2D(_BiomeTex5); SAMPLER(sampler_BiomeTex5);
            TEXTURE2D(_BiomeTex6); SAMPLER(sampler_BiomeTex6);
            TEXTURE2D(_BiomeTex7); SAMPLER(sampler_BiomeTex7);

            float _BiomeScale0;
            float _BiomeScale1;
            float _BiomeScale2;
            float _BiomeScale3;
            float _BiomeScale4;
            float _BiomeScale5;
            float _BiomeScale6;
            float _BiomeScale7;
            float _BiomeLayerCount;

            float3 SampleTriplanar(TEXTURE2D_PARAM(tex, samplerTex), float3 positionWS, float3 normalWS, float worldSize)
            {
                float safeWorldSize = max(worldSize, 0.1);
                float3 blend = pow(abs(normalWS), 4.0);
                blend /= max(blend.x + blend.y + blend.z, 0.0001);

                float2 uvX = positionWS.zy / safeWorldSize;
                float2 uvY = positionWS.xz / safeWorldSize;
                float2 uvZ = positionWS.xy / safeWorldSize;

                float3 sampleX = SAMPLE_TEXTURE2D(tex, samplerTex, uvX).rgb;
                float3 sampleY = SAMPLE_TEXTURE2D(tex, samplerTex, uvY).rgb;
                float3 sampleZ = SAMPLE_TEXTURE2D(tex, samplerTex, uvZ).rgb;

                return sampleX * blend.x + sampleY * blend.y + sampleZ * blend.z;
            }

            int ResolveBiomeIndex(float rawIndex)
            {
                int layerCount = max(1, (int)round(_BiomeLayerCount));
                return clamp((int)round(rawIndex), 0, layerCount - 1);
            }

            float3 SampleBiomeAlbedo(int biomeIndex, float3 positionWS, float3 normalWS)
            {
                float3 albedo = SampleTriplanar(TEXTURE2D_ARGS(_BiomeTex0, sampler_BiomeTex0), positionWS, normalWS, _BiomeScale0);

                if (biomeIndex == 1) albedo = SampleTriplanar(TEXTURE2D_ARGS(_BiomeTex1, sampler_BiomeTex1), positionWS, normalWS, _BiomeScale1);
                else if (biomeIndex == 2) albedo = SampleTriplanar(TEXTURE2D_ARGS(_BiomeTex2, sampler_BiomeTex2), positionWS, normalWS, _BiomeScale2);
                else if (biomeIndex == 3) albedo = SampleTriplanar(TEXTURE2D_ARGS(_BiomeTex3, sampler_BiomeTex3), positionWS, normalWS, _BiomeScale3);
                else if (biomeIndex == 4) albedo = SampleTriplanar(TEXTURE2D_ARGS(_BiomeTex4, sampler_BiomeTex4), positionWS, normalWS, _BiomeScale4);
                else if (biomeIndex == 5) albedo = SampleTriplanar(TEXTURE2D_ARGS(_BiomeTex5, sampler_BiomeTex5), positionWS, normalWS, _BiomeScale5);
                else if (biomeIndex == 6) albedo = SampleTriplanar(TEXTURE2D_ARGS(_BiomeTex6, sampler_BiomeTex6), positionWS, normalWS, _BiomeScale6);
                else if (biomeIndex >= 7) albedo = SampleTriplanar(TEXTURE2D_ARGS(_BiomeTex7, sampler_BiomeTex7), positionWS, normalWS, _BiomeScale7);

                return albedo;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS  = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.biomeData   = IN.biomeData;
                OUT.vertexColor = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                int biomeIndex = ResolveBiomeIndex(IN.biomeData.x);
                float3 sampledAlbedo = SampleBiomeAlbedo(biomeIndex, IN.positionWS, normalWS);

                float hasVertexTint = step(0.001, dot(IN.vertexColor.rgb, float3(1.0, 1.0, 1.0)));
                float3 biomeTint = lerp(float3(1.0, 1.0, 1.0), IN.vertexColor.rgb, hasVertexTint);
                float3 albedo = sampledAlbedo * biomeTint;

                Light light = GetMainLight();
                float NdotL = saturate(dot(normalWS, light.direction));
                float3 ambient = SampleSH(normalWS) * 0.35;
                float3 direct = light.color.rgb * (NdotL * 0.85 + 0.15);
                float3 color = albedo * (ambient + direct);

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ── Shadow Caster ────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest  LEqual
            ColorMask 0
            Cull  Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(posWS, normWS, _LightDirection));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack Off
}
