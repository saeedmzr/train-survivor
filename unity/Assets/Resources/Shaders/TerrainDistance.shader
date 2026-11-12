Shader "TrainSurvivor/TerrainDistance"
{
    Properties
    {
        [MainTexture] _BaseMap("Gravel", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _TerrainTint("Packed yard tint", Color) = (1,1,1,1)
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.25
        _SpecColor("Specular", Color) = (0.2,0.2,0.2,1)
        _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal scale", Float) = 1
        _OcclusionMap("Occlusion", 2D) = "white" {}
        _OcclusionStrength("Occlusion strength", Float) = 1
        _EmissionMap("Emission", 2D) = "white" {}
        _EmissionColor("Emission color", Color) = (0,0,0,0)
        _Cutoff("Cutoff", Float) = 0.5
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _MainTex("Base map", 2D) = "white" {}
        [HideInInspector] _Color("Base color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "TerrainForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitPassVertex
            #pragma fragment TerrainFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_instancing

            // Reuse URP's actual Lit surface/lighting/shadow path, not an unlit painted yard.
            // No fog keyword: this terrain-only blend must work even when URP strips fog variants.
            #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR 1
            #define InitializeStandardLitSurfaceData TerrainBaseSurface
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #undef InitializeStandardLitSurfaceData
            half4 _TerrainTint;
            half4 _TerrainHorizonColor;
            void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surfaceData)
            {
                TerrainBaseSurface(uv, surfaceData);
                surfaceData.albedo *= _TerrainTint.rgb;
            }
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            void TerrainFragment(Varyings input, out half4 color : SV_Target0)
            {
                LitPassFragment(input, color);
                // Per-fragment camera-relative depth, not interpolated colour on a giant quad.
                // Keep warm textured gravel near the convoy; finish well before the far clip.
                float depth = -TransformWorldToView(input.positionWS).z;
                float haze = smoothstep(55.0, 180.0, depth);
                color.rgb = lerp(color.rgb, _TerrainHorizonColor.rgb, haze);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    Fallback Off
}
