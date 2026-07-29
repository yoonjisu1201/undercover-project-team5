Shader "Undercover/Debug/CCTV Glitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CCTVGlitch"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;

            float Hash(float2 value)
            {
                return frac(sin(dot(value, float2(12.9898, 78.233))) * 43758.5453);
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float timeSlice = floor(_Time.y * 18.0);
                float blockRow = floor(input.texcoord.y * 18.0);
                float blockNoise = Hash(float2(blockRow, timeSlice));
                float burst = step(0.62, Hash(float2(timeSlice, floor(_Time.y * 3.0))));
                float horizontalShift = (blockNoise - 0.5) * 0.16 * burst;
                float2 uv = input.texcoord;
                uv.x = frac(uv.x + horizontalShift);

                float chroma = 0.009 + burst * 0.012;
                fixed red = tex2D(_MainTex, uv + float2(chroma, 0)).r;
                fixed green = tex2D(_MainTex, uv).g;
                fixed blue = tex2D(_MainTex, uv - float2(chroma, 0)).b;

                float scanline = 0.82 + 0.18 * step(0.5, frac(input.texcoord.y * 240.0));
                float dropout = step(0.035, Hash(float2(floor(input.texcoord.y * 90.0), timeSlice)));
                fixed4 color = fixed4(red, green, blue, 1.0) * scanline * dropout;
                color *= input.color;
                return color;
            }
            ENDCG
        }
    }
}
