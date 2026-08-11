// The original's rotating minimap, as a fragment shader.
//
// kart_demo_draw_original_minimap_camera walks every pixel of the panel, casts a
// ray from the camera at the map image's z = 0 plane, and samples the image where
// it lands. That is a per-pixel job with no interpolation in it, so it ports to a
// fragment shader one line at a time rather than becoming a textured quad — a
// quad would interpolate the projection across its corners and bend the map.
//
// The uniforms are the camera basis and position the C computes in map space.
Shader "OrangeCarrrrr/Minimap3D"
{
    Properties
    {
        _MainTex ("Map", 2D) = "black" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;

            // Image size in pixels, and the camera the C code builds.
            float2 _MapSize;
            float3 _CameraPosition;
            float3 _CameraRight;
            float3 _CameraBack;
            float3 _CameraUp;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // screen_x = 1 - 2 * (x + 0.5) / width in the C, which is the
                // reversal the displayed panel carries; screen_z counts up the
                // panel, and uv.y already does.
                float screenX = 1.0 - 2.0 * i.uv.x;
                float screenZ = 2.0 * i.uv.y - 1.0;

                float3 ray = -_CameraBack
                           + _CameraRight * screenX
                           + _CameraUp * screenZ;

                // Where the ray meets the image plane. A ray that never does —
                // above the horizon — clamps to the image's edge, which is the
                // D3DTADDRESS_CLAMP the original's TexProperty asks for.
                float distance = ray.z != 0.0 ? -_CameraPosition.z / ray.z : -1.0;
                float2 hit = float2(0.0, 0.0);
                if (distance > 0.0)
                {
                    hit = float2(
                        floor(_CameraPosition.x + ray.x * distance),
                        floor(_MapSize.y - (_CameraPosition.y + ray.y * distance)));
                }

                hit = clamp(hit, float2(0.0, 0.0), _MapSize - 1.0);

                // Sampled at the texel centre, and with v flipped: the C indexes
                // the bitmap's rows from the top.
                float2 uv = (hit + 0.5) / _MapSize;
                uv.y = 1.0 - uv.y;

                fixed4 map = tex2D(_MainTex, uv);

                // The original's BLENDFUNCTION is {AC_SRC_OVER, 0, 77, 0}: the
                // last field is the alpha format, and it is not AC_SRC_ALPHA, so
                // GDI ignores the source's per-pixel alpha entirely and blends
                // the whole map at the constant 77/255. Nine of the thirteen map
                // PNGs carry an alpha channel, so multiplying by it here — which
                // is what a normal textured blit does — makes the map fade out
                // twice and come up far weaker than the original's.
                return fixed4(map.rgb, 1.0) * _Color * i.color;
            }
            ENDCG
        }
    }

    Fallback Off
}
