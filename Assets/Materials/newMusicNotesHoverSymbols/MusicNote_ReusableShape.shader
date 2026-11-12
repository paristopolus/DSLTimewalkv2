Shader "Custom/MusicNote/ReusableShape"
{
    Properties
    {
        _BaseColor ("Note Color", Color) = (1, 0.81, 0.055, 1)
        _ShapeColor ("Shape Color", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _ShapeTex ("Shape PNG (Transparent)", 2D) = "black" {}
        _ShapeCenter ("Shape Center (Local XY)", Vector) = (0, 0, 0, 0)
        _ShapeSize ("Shape Width and Height (Local Units)", Vector) = (0.0035, 0.0035, 0, 0)
        _ShapeRotation ("Shape Rotation (Degrees)", Range(-180, 180)) = 0
        _Opacity ("Overall Opacity", Range(0, 1)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_ShapeTex);
            SAMPLER(sampler_ShapeTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShapeColor;
                float4 _ShapeCenter;
                float4 _ShapeSize;
                float _ShapeRotation;
                float _Opacity;
                half _Smoothness;
            CBUFFER_END
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 localXY : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 fogAndVertexLight : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogAndVertexLight = half4(ComputeFogFactor(positions.positionCS.z),
                    VertexLighting(positions.positionWS, output.normalWS));
                output.localXY = input.positionOS.xy;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float angle = radians(_ShapeRotation);
                float c = cos(angle);
                float s = sin(angle);
                float2 p = input.localXY - _ShapeCenter.xy;
                float2 rotated = float2(c * p.x + s * p.y, -s * p.x + c * p.y);
                float2 size = max(abs(_ShapeSize.xy), float2(0.000001, 0.000001));
                float2 uv = rotated / size + 0.5;
                float inside = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
                half shapeMask = SAMPLE_TEXTURE2D(_ShapeTex, sampler_ShapeTex, uv).a * inside * _ShapeColor.a;
                half3 rgb = lerp(_BaseColor.rgb, _ShapeColor.rgb, shapeMask);
                half opacity = saturate(_Opacity * _BaseColor.a);
                InputData lightingInput = (InputData)0;
                lightingInput.positionWS = input.positionWS;
                lightingInput.normalWS = NormalizeNormalPerPixel(input.normalWS);
                lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lightingInput.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lightingInput.vertexLighting = input.fogAndVertexLight.yzw;
                lightingInput.bakedGI = SampleSH(lightingInput.normalWS);
                lightingInput.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lightingInput.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = rgb;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = opacity;

                half4 color = UniversalFragmentPBR(lightingInput, surface);
                color.rgb = MixFog(color.rgb, input.fogAndVertexLight.x);
                return half4(color.rgb, opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
