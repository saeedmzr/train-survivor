Shader "TrainSurvivor/Sprite3DAdd"
{
    // Additive sibling of Sprite3D, for things that emit light rather than reflect it:
    // bullets, muzzle glow, impact bursts. Same quad/UV plumbing, no depth write so a
    // stack of overlapping tracers never punches holes in the scene.
    Properties
    {
        _BaseMap ("Atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Flash ("Flash", Range(0,1)) = 0
        _UVRect ("UV Rect", Vector) = (0,0,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.01
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

        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "SpriteAdditive"
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
                half a = c.a * _Color.a;
                clip(a - _Cutoff);
                half3 rgb = c.rgb * _Color.rgb * (1.0h + _Flash * 2.0h);
                return half4(rgb * a, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
