Shader "OrangeCarrrrr/ScreenLine"
{
    Properties
    {
        [HideInInspector] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ScreenLine"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // The original draws its grid and its track bounds with GDI pens of a
            // fixed pixel width, so a line stays the same thickness however far
            // away it is. A world-space quad cannot do that, so each segment is
            // submitted as a degenerate quad whose two corners carry both
            // endpoints, and the vertex stage expands it sideways by a fixed
            // number of pixels after projection.
            //
            // Segments are pre-clipped against the near plane on the CPU (see
            // ScreenLineBatch), so w is always positive here and the screen-space
            // direction is well defined.

            struct Attributes
            {
                float4 positionOS : POSITION;   // this endpoint
                float4 otherOS    : TANGENT;    // the segment's other endpoint
                float2 sideWidth  : TEXCOORD0;  // x: -1 or +1, y: width in pixels
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;

                float4 clipHere = TransformObjectToHClip(input.positionOS.xyz);
                float4 clipOther = TransformObjectToHClip(input.otherOS.xyz);

                float2 halfViewport = _ScreenParams.xy * 0.5;
                float2 screenHere = (clipHere.xy / clipHere.w) * halfViewport;
                float2 screenOther = (clipOther.xy / clipOther.w) * halfViewport;

                float2 delta = screenOther - screenHere;
                float length2 = dot(delta, delta);
                float2 direction = length2 > 1e-8 ? delta * rsqrt(length2) : float2(1.0, 0.0);
                float2 perpendicular = float2(-direction.y, direction.x);

                float2 offsetPixels = perpendicular * (input.sideWidth.x * input.sideWidth.y * 0.5);
                clipHere.xy += (offsetPixels / halfViewport) * clipHere.w;

                output.positionCS = clipHere;
                output.color = input.color;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                return half4(input.color.rgb, input.color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
