Shader "Light Path/Repair Stage"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 顶面运行时网格以顶点色显示光线、目标和机关。
            struct Input { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Output { float4 positionCS : SV_POSITION; half4 color : COLOR; };
            Output Vertex(Input input)
            {
                Output output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }
            half4 Fragment(Output input) : SV_Target { return input.color; }
            ENDHLSL
        }
    }
}
