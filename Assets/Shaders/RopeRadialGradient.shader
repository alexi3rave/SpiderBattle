Shader "WormCrawler/RopeRadialGradient"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _CoreColor ("Core Color", Color) = (1,1,1,1)
        _EdgeColor ("Edge Color", Color) = (0.2,0.65,1,0.9)
        _CoreWidthFraction ("Core Width Fraction", Range(0,1)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _CoreColor;
                float4 _EdgeColor;
                float _CoreWidthFraction;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half2 uv = IN.uv;

                // For LineRenderer, uv.y typically maps across the ribbon width.
                half v = saturate(uv.y);
                half radial = abs(v - 0.5h) * 2.0h; // 0 at center, 1 at edges

                half core = saturate(_CoreWidthFraction);
                half t = (core >= 0.999h) ? 0.0h : smoothstep(core, 1.0h, radial);

                half4 grad = lerp(_CoreColor, _EdgeColor, t);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);

                half4 col = grad * tex * IN.color;
                return col;
            }
            ENDHLSL
        }
    }

    // Built-in RP fallback SubShader (CG, no URP dependency)
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            float4 _CoreColor;
            float4 _EdgeColor;
            float _CoreWidthFraction;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half v = saturate(i.uv.y);
                half radial = abs(v - 0.5) * 2.0;
                half core = saturate(_CoreWidthFraction);
                half t = (core >= 0.999) ? 0.0 : smoothstep(core, 1.0, radial);
                fixed4 grad = lerp(_CoreColor, _EdgeColor, t);
                fixed4 tex = tex2D(_BaseMap, i.uv);
                return grad * tex * i.color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
