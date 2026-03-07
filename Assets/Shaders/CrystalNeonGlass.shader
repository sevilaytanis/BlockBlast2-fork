// Block Blast — Glossy Plastic Block Shader (URP 2D)
// Görsel katmanlar (üstten alta):
//   1) Oval beyaz specular highlight — üst-merkez, geniş oval
//   2) Üst beyaz rim çizgisi — parlak kenar
//   3) Dikey gradient — üst aydınlık, alt biraz karanlık
//   4) Kenar vignette — kenarlara doğru koyulaşma (3D yuvarlak hissi)
//   5) Emission boost — bloom için parlaklık, per-block MaterialPropertyBlock'tan
//   6) Subtlbe pulse — canlılık için hafif nefes animasyonu
//
// Tüm "glass / refraction" kodu kaldırıldı — opaque texture gerekmez.
// URP Renderer ayarlarında "Opaque Texture" kapatılabilir.

Shader "BlockBlast/URP/CrystalNeonGlass"
{
    Properties
    {
        _MainTex            ("Sprite Texture", 2D) = "white" {}
        _Color              ("Tint", Color) = (1,1,1,1)

        // Opacity — 1.0 = tamamen opak plastik, 0.35 = yarı saydam cam
        _GlassAlpha         ("Opacity", Range(0.35, 1.0)) = 1.0

        // Specular highlight oval
        _SpecularStrength   ("Highlight Strength", Range(0.0, 3.0)) = 1.55
        _SpecularSize       ("Highlight Size", Range(0.05, 0.40)) = 0.14

        // Gradient & highlight softness
        _InnerGlowStrength  ("Emission Boost (MPB)", Range(0.0, 2.0)) = 0.45
        _InnerGlowPower     ("Highlight Softness", Range(0.5, 5.0)) = 2.0

        // Edge darkening — sahte 3D yuvarlaklık
        _FresnelStrength    ("Edge Darken Strength", Range(0.0, 2.0)) = 0.55
        _FresnelPower       ("Edge Darken Power", Range(0.5, 6.0)) = 2.2

        // Üst rim çizgisi
        _RefractionStrength ("Top Rim Strength", Range(0.0, 0.15)) = 0.045

        // Nefes animasyonu
        _PulseSpeed         ("Pulse Speed", Range(0.0, 4.0)) = 0.9
        _PulseAmount        ("Pulse Amount", Range(0.0, 0.6)) = 0.025
        _PulseOffset        ("Pulse Offset (MPB)", Range(0, 6.28318)) = 0.0

        // Eski uyumluluk için tutuldu, kullanılmıyor
        _DistortionScale    ("(unused)", Range(0.5, 8.0)) = 2.5
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

            // VFXManager tarafından global set edilen big-bang flaşı
            float _MegaBangFlash;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half4  color       : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color       = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Sprite shape mask — rounded rectangle alpha
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(tex.a - 0.05h);

                // Canlı ana renk (SpriteRenderer.color * material tint)
                half3 baseColor = tex.rgb * IN.color.rgb;

                // ── 1. Dikey gradient ─────────────────────────────────────────
                // UV.y = 0 alt, UV.y = 1 üst (Unity sprite koordinatları)
                half yGrad = IN.uv.y;
                // Üst: beyaza karışım (yumuşatılmış kuadratik, sadece üst %35'i etkiler)
                half topLift  = yGrad * yGrad * 0.30h;
                // Alt: ince koyulaşma
                half botDark  = (1.0h - yGrad) * 0.22h;
                half3 gradColor = lerp(baseColor, half3(1.0h, 1.0h, 1.0h), topLift * 0.55h)
                                  * (1.0h - botDark * 0.35h);

                // ── 2. Kenar vignette (sahte 3D yuvarlaklık) ─────────────────
                // Her kenardan minimum mesafe → 0 = kenar, 1 = merkez
                half edgeX = min(IN.uv.x, 1.0h - IN.uv.x) * 2.0h;
                half edgeY = min(IN.uv.y, 1.0h - IN.uv.y) * 2.0h;
                // Keskin kenar maskesi (min, daha agresif köşe koyulaşması)
                half edgeMask = pow(min(edgeX, edgeY), _FresnelPower);
                // Kenarlara doğru koyulaş
                half3 shadedColor = lerp(
                    gradColor * (1.0h - _FresnelStrength * 0.55h),
                    gradColor,
                    saturate(edgeMask + 0.25h));

                // ── 3. Oval specular highlight (ana Block Blast imzası) ───────
                // Elips merkezi: üst-orta-sol (~%38 X, ~%72 Y)
                // Elips boyutu: yatay 1.85x, dikey 1x — geniş ve yassı
                half2 hlUV = (IN.uv - half2(0.38h, 0.72h))
                           / (half2(1.85h, 1.0h) * _SpecularSize);
                half  hlDist    = dot(hlUV, hlUV);
                half  highlight = pow(saturate(1.0h - hlDist), _InnerGlowPower)
                                  * _SpecularStrength;

                // Hafif nefes efekti highlight üzerinde
                half pulse = sin(_Time.y * _PulseSpeed + _PulseOffset)
                             * _PulseAmount + 1.0h;
                highlight *= pulse;

                // ── 4. Üst rim çizgisi ────────────────────────────────────────
                // _RefractionStrength → rim genişliği (0.045 ≈ %4.5 UV)
                half rimW   = max(_RefractionStrength, 0.001h);
                half topRim = saturate((IN.uv.y - (1.0h - rimW)) / rimW);
                topRim *= edgeX;   // köşelerde söner
                // Rim rengi: temel renk ile beyaz arasında
                half3 rimColor = lerp(baseColor, half3(1.0h, 1.0h, 1.0h), 0.80h);

                // ── 5. Katmanları birleştir ───────────────────────────────────
                half3 finalRGB = shadedColor;
                // Oval highlight: shadedColor → beyaz arası lerp
                finalRGB = lerp(finalRGB, half3(1.0h, 1.0h, 1.0h), saturate(highlight));
                // Üst rim ekle
                finalRGB += rimColor * topRim * 0.45h;

                // ── 6. Emission boost (URP Bloom için) ───────────────────────
                // _InnerGlowStrength her blok için MaterialPropertyBlock'tan gelir
                // Varsayılan: 0.32 + 0.55 = 0.87 → %15 ekstra parlaklık (koyu zemin için)
                finalRGB *= (1.0h + _InnerGlowStrength * 0.18h);

                // Global big-bang flaşı (VFXManager)
                finalRGB = saturate(finalRGB + _MegaBangFlash * 0.25h);

                half finalA = tex.a * IN.color.a * _GlassAlpha;
                return half4(saturate(finalRGB), finalA);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
