Shader "BlockBlast/URP/CrystalNeonGlass"
{
    Properties
    {
        _MainTex            ("Sprite Texture", 2D) = "white" {}
        _Color              ("Tint", Color) = (1,1,1,1)

        _GlassAlpha         ("Glass Alpha", Range(0.35, 1.0)) = 0.92
        _RefractionStrength ("Refraction Strength", Range(0.0, 0.08)) = 0.006
        _DistortionScale    ("Distortion Scale", Range(0.5, 8.0)) = 2.5

        _InnerGlowStrength  ("Inner Glow Strength", Range(0.0, 2.0)) = 0.32
        _InnerGlowPower     ("Inner Glow Power", Range(0.5, 5.0)) = 1.6

        _SpecularStrength   ("Specular Strength", Range(0.0, 3.0)) = 1.90
        _SpecularSize       ("Specular Size", Range(0.01, 0.20)) = 0.06

        _FresnelStrength    ("Fresnel Strength", Range(0.0, 2.0)) = 1.20
        _FresnelPower       ("Fresnel Power", Range(0.5, 6.0)) = 2.9

        _PulseSpeed         ("Pulse Speed", Range(0.0, 4.0)) = 1.1
        _PulseAmount        ("Pulse Amount", Range(0.0, 0.6)) = 0.04
        _PulseOffset        ("Pulse Offset", Range(0, 6.28318)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "CrystalNeon"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _GlassAlpha;
                half   _RefractionStrength;
                half   _DistortionScale;
                half   _InnerGlowStrength;
                half   _InnerGlowPower;
                half   _SpecularStrength;
                half   _SpecularSize;
                half   _FresnelStrength;
                half   _FresnelPower;
                half   _PulseSpeed;
                half   _PulseAmount;
                half   _PulseOffset;
            CBUFFER_END

            float _MegaBangFlash;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half4  color       : COLOR;
                float4 screenPos   : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color       = IN.color * _Color;
                OUT.screenPos   = ComputeScreenPos(OUT.positionHCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(tex.a - 0.05);

                half3 baseColor = tex.rgb * IN.color.rgb;

                // Faux glass normal from UV center + tiny animated wave.
                half2 uvCentered = IN.uv - 0.5h;
                half wave = sin((_Time.y * 0.9h + uvCentered.x * 11.0h + uvCentered.y * 7.0h) * _DistortionScale);
                half2 distort = normalize(uvCentered + half2(0.0001h, 0.0001h)) * (wave * _RefractionStrength);

                // Background refraction (requires URP Opaque Texture ON).
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 0.0001);
                half3 refractedBg = SampleSceneColor(screenUV + distort);

                // Crystalline facets: layered diagonal cuts to avoid flat color blocks.
                half f1 = abs(frac(IN.uv.x * 7.0h + IN.uv.y * 4.2h) - 0.5h) * 2.0h;
                half f2 = abs(frac(IN.uv.x * 3.4h - IN.uv.y * 6.3h) - 0.5h) * 2.0h;
                half facetMask = saturate(1.0h - min(f1, f2));
                half3 facetTintA = half3(0.76h, 0.82h, 0.93h);
                half3 facetTintB = half3(0.55h, 0.66h, 0.83h);
                half3 facetTint  = lerp(facetTintA, facetTintB, facetMask);
                // Keep piece hue dominant; facets should modulate, not override.
                baseColor = lerp(baseColor, facetTint, 0.22h);

                // Inner glow (center weighted + pulse).
                half centerDist = saturate(length(uvCentered) * 2.0h);
                half innerMask  = pow(1.0h - centerDist, _InnerGlowPower);
                half pulse      = sin(_Time.y * _PulseSpeed + _PulseOffset) * 0.5h + 0.5h;
                half glowAmp    = _InnerGlowStrength * (1.0h + pulse * _PulseAmount);
                half3 innerGlow = baseColor * innerMask * glowAmp;

                // Sharp specular highlights near top-right crystal edges.
                half2 s1 = IN.uv - half2(0.78h, 0.80h);
                half2 s2 = IN.uv - half2(0.62h, 0.88h);
                half spec1 = pow(saturate(1.0h - dot(s1, s1) / (_SpecularSize * _SpecularSize)), 5.0h);
                half spec2 = pow(saturate(1.0h - dot(s2, s2) / ((_SpecularSize * 0.65h) * (_SpecularSize * 0.65h))), 8.0h);
                // Slightly tint highlights with base hue to avoid white-washing all pieces.
                half3 specular = (spec1 + spec2) * _SpecularStrength * lerp(baseColor, half3(1.0h, 1.0h, 1.0h), 0.35h);

                // Fresnel/rim via border distance (2D sprite-friendly approximation).
                half edgeDist = min(min(IN.uv.x, 1.0h - IN.uv.x), min(IN.uv.y, 1.0h - IN.uv.y));
                half rim = pow(saturate(1.0h - edgeDist * 2.0h), _FresnelPower) * _FresnelStrength;
                half3 fresnel = baseColor * rim;

                // Mix: refracted background + crystal color layers.
                half3 glassBase = lerp(baseColor, refractedBg, 0.18h);
                half3 finalRGB = saturate(glassBase + innerGlow + specular + fresnel);

                // Supports external flash pulse from VFXManager (optional global property).
                finalRGB = saturate(finalRGB + _MegaBangFlash * 0.25h);

                half finalA = tex.a * IN.color.a * _GlassAlpha;
                return half4(finalRGB, finalA);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
