Shader "ProceduralWorld/Water"
{
    Properties
    {
        _ShallowColor ("Мелководье",        Color) = (0.28, 0.76, 0.93, 0.50)
        _DeepColor    ("Глубина",           Color) = (0.04, 0.20, 0.52, 0.90)
        _DepthFade    ("Дистанция глубины", Float) = 8.0
        _FresnelPower ("Fresnel",           Float) = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent"
        }

        // Прозрачное смешивание; не пишем в depth buffer
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        // ── Forward Lit ──────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float  _DepthFade;
                float  _FresnelPower;
            CBUFFER_END

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
                float4 screenPos  : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.screenPos  = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ── Глубина воды ─────────────────────────────────────────────
                // Берём глубину сцены (то, что ЗА водой) и сравниваем
                // с глубиной самой поверхности воды
                float2 screenUV  = IN.screenPos.xy / IN.screenPos.w;
                float  sceneEye  = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float  waterEye  = IN.screenPos.w;
                float  depth     = saturate((sceneEye - waterEye) / _DepthFade);

                // Мелководье (depth=0) → светло-голубой; глубина (depth=1) → тёмно-синий
                float4 color = lerp(_ShallowColor, _DeepColor, depth);

                // ── Fresnel ──────────────────────────────────────────────────
                // При взгляде под углом вода становится более непрозрачной
                float3 viewDir = normalize(GetCameraPositionWS() - IN.positionWS);
                float  fresnel = pow(1.0 - saturate(dot(normalize(IN.normalWS), viewDir)),
                                     _FresnelPower);
                color.a = saturate(color.a + fresnel * 0.25);

                // ── Освещение (мягкое) ────────────────────────────────────────
                Light  mainLight = GetMainLight();
                float  NdotL     = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                color.rgb *= mainLight.color.rgb * (NdotL * 0.35 + 0.65);

                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
