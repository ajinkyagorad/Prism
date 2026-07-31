// PrismField — invisible influence, made visible honestly.
//
// Drawn on a disc lying in the orbital plane. The contours are not decorative rings:
// they are equipotentials of the real gravitational potential of the bodies actually
// in the scene,
//
//     phi(p) = - sum_i  mu_i / |p - x_i|
//
// contoured at equal intervals of phi. Because phi goes as 1/r, equal intervals bunch
// the lines toward each mass automatically — the picture is dense where the field is
// steep and open where it is weak, for the same reason the real thing is. Nobody has
// to art-direct that, and a learner who later meets a textbook potential diagram is
// looking at something they have already held.
//
// Contour width is derived from the screen-space derivative of phi, so lines stay one
// pixel wide at every scale and never moire.
//
// NOTE for a future move to URP: _Bodies must stay OUTSIDE any UnityPerMaterial cbuffer.
// Material.SetVectorArray cannot reach the SRP Batcher's per-material buffer, and
// "tidying" the array into it would silently zero the whole field.

Shader "Prism/Field"
{
    Properties
    {
        _Reveal      ("Reveal", Range(0,1))               = 1.0
        _ContourStep ("Potential per contour", Float)     = 0.0018
        _LineWidth   ("Line width (px)", Range(0.5,4))    = 1.4
        _Drift       ("Contour drift (inward)", Range(0,2)) = 0.35
        _Softening   ("Softening radius (m)", Range(0.001,0.2)) = 0.02
        _GradRef     ("Field strength reference", Float)  = 0.4
        _FadeInner   ("Inner fade (m)", Range(0,0.5))     = 0.0
        _FadeOuter   ("Outer fade (m)", Range(0.05,4))    = 0.9
        _Opacity     ("Opacity", Range(0,1))              = 0.55
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Field"
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Include/PrismOptics.hlsl"

            #define PRISM_MAX_BODIES 8

            float4 _Bodies[PRISM_MAX_BODIES];   // xyz = world position, w = mu (m^3/s^2)
            int    _BodyCount;
            float4 _Centre;                     // xyz = disc centre, for the radial fade

            float _Reveal, _ContourStep, _LineWidth, _Drift, _Softening;
            float _GradRef, _FadeInner, _FadeOuter, _Opacity;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // Gravitational potential, and the magnitude of its gradient (the field).
            void PotentialAndField(float3 p, out float phi, out float gmag)
            {
                phi = 0.0;
                float3 g = 0.0;
                for (int b = 0; b < PRISM_MAX_BODIES; b++)
                {
                    if (b >= _BodyCount) break;
                    float3 d  = p - _Bodies[b].xyz;
                    float  r2 = dot(d, d) + _Softening * _Softening;   // Plummer softening
                    float  r  = sqrt(r2);
                    float  mu = _Bodies[b].w;
                    phi -= mu / r;
                    g   -= d * (mu / (r2 * r));
                }
                gmag = length(g);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float phi, gmag;
                PotentialAndField(i.worldPos, phi, gmag);

                // Equal intervals of potential. Drift makes the pattern creep inward, which
                // is the direction the field would actually take a released body.
                float v = phi / max(_ContourStep, 1e-6) + _Time.y * _Drift;

                // Constant-pixel-width lines from the screen-space derivative. Without this
                // the contours alias into moire wherever they bunch — which is exactly where
                // they matter most.
                float w = max(fwidth(v), 1e-5);
                float dist = abs(frac(v + 0.5) - 0.5);
                // 'line' is a reserved word in HLSL (a geometry-shader primitive type), so this
                // cannot be called what it obviously wants to be called.
                float contour = 1.0 - smoothstep(0.0, w * _LineWidth, dist);

                // Where lines bunch tighter than a pixel, stop drawing individual lines and
                // let the region read as a solid gradient instead. Honest and legible.
                float crowded = saturate(w * _LineWidth * 2.0 - 0.5);
                contour = lerp(contour, 0.55, crowded);

                // Colour encodes field strength: open cyan where it is weak, deep violet
                // where it is steep.
                float  strength = saturate(gmag / max(_GradRef, 1e-5));
                float3 tint = Prism_Spectral(0.05 + 0.85 * pow(strength, 0.45));

                float r = length(i.worldPos - _Centre.xyz);
                float fade = smoothstep(_FadeInner, _FadeInner + 0.04, r)
                           * (1.0 - smoothstep(_FadeOuter * 0.72, _FadeOuter, r));

                float dens = contour * fade * _Opacity * _Reveal;

                float3 rgb; float a;
                Prism_Compose(tint, dens, dens * 0.4, 0.0, rgb, a);

                Prism_Deband(rgb, i.pos.xy, _Time.y * 60.0);
                return fixed4(rgb, saturate(a));
            }
            ENDCG
        }
    }

    Fallback Off
}
