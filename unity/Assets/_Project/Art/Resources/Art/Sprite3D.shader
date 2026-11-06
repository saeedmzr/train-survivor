Shader "TrainSurvivor/Sprite3D"
{
    // Billboarded sprites living in the 3D world: alpha blended, depth-writing, unlit.
    // One material for the whole atlas; per-renderer UV rect / tint / flash via MaterialPropertyBlock.
    Properties
    {
        _BaseMap ("Atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Flash ("Flash", Range(0,1)) = 0
        _UVRect ("UV Rect", Vector) = (0,0,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.02
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Off

        Pass
        {
            Name "SpriteForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _UVRect;
                float _Flash;
                float _Cutoff;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv * _UVRect.zw + _UVRect.xy;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(c.a - _Cutoff);

                half3 rgb = c.rgb * _Color.rgb;
                rgb = lerp(rgb, half3(1.0h, 0.95h, 0.85h), _Flash * 0.45h);
                return half4(rgb, c.a * _Color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
