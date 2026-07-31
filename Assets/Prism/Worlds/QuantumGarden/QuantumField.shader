// Prism/QuantumField — the amplitude field made visible, honestly.
//
// Drawn on a flat quad lying in the detector screen's own plane. This is NOT a decorative
// gradient standing in for the interference pattern: every fragment runs the same discretised
// Fresnel-Kirchhoff sum that Prism/Worlds/QuantumGarden/TwoSlitSim.cs sums on the CPU to draw a
// detection sample, over the same open sub-sources, with the same real path-length phase. The
// picture and the physics are one computation evaluated twice, not a picture standing in for a
// computation.
//
//     psi(x) = sum over open sub-sources j :  (w_j / sqrt(r_j)) * exp(i * k * (d_j + r_j))
//
// where d_j is the distance from the source to sub-source j and r_j is the distance from
// sub-source j to the screen point x. |psi|^2 is the Born-rule intensity; arg(psi) is the phase.
//
// COLOUR ENCODING (the one this world uses everywhere a complex amplitude is shown):
//   hue        = phase of psi, mapped onto the spectral ramp. Two contributions that arrive
//                out of phase sit on opposite sides of the ramp; where they cancel, the pixel
//                goes dark REGARDLESS of hue, so the learner can see that the darkness at a
//                fringe minimum is a meeting of opposite phase, not an absence of light.
//   brightness = |psi|^2 / (the pattern's own current peak). A single-slit pattern is
//                genuinely dimmer than a two-slit one at the same peak reference — real light
//                gets through a second open aperture — so this is left honest rather than
//                renormalised per-configuration.
//
// y does not enter the sum (see TwoSlitSim's remarks on slits as line sources), so this field
// is honestly uniform up/down the screen: what varies is only ever the across-slit axis.

Shader "Prism/QuantumField"
{
    Properties
    {
        _Reveal      ("Reveal", Range(0,1))            = 0.0
        _K           ("Wavenumber (2*pi/lambda)", Float) = 300.0
        _SlitRelZ    ("Slit depth relative to screen (m)", Float) = -0.34
        _IntensityRef("Intensity reference (peak)", Float) = 1.0
        _Opacity     ("Opacity", Range(0,1))            = 0.75
        _HalfWidth   ("Screen half width (m)", Float)   = 0.26
        _HalfHeight  ("Screen half height (m)", Float)  = 0.16
        _EdgeSoften  ("Edge fade (m)", Float)            = 0.02
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "QuantumField"
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "../../Aesthetic/Include/PrismOptics.hlsl"

            // 2 slits x TwoSlitSim.SubSourcesPerSlit(9) = 18, with headroom. Kept OUTSIDE any
            // per-material cbuffer — see PrismField.shader's note on Material.SetVectorArray
            // and the SRP Batcher, which applies here identically.
            #define PRISM_MAX_SOURCES 20

            // xyz = (localX, distanceFromSourceToSubSource, weight); w unused.
            float4 _Sources[PRISM_MAX_SOURCES];
            int    _SourceCount;

            float _Reveal, _K, _SlitRelZ, _IntensityRef, _Opacity;
            float _HalfWidth, _HalfHeight, _EdgeSoften;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float3 objPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos    = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                o.objPos = v.vertex.xyz;
                return o;
            }

            // The same discretised sum TwoSlitSim.Amplitude computes on the CPU, evaluated at
            // one screen point. x is in the screen quad's own object space, i.e. metres, matching
            // the world-anchor-local coordinates the simulation itself works in.
            void Psi(float x, out float re, out float im)
            {
                re = 0.0; im = 0.0;
                for (int j = 0; j < PRISM_MAX_SOURCES; j++)
                {
                    if (j >= _SourceCount) break;
                    float4 src = _Sources[j];
                    float dx = x - src.x;
                    float r  = sqrt(dx * dx + _SlitRelZ * _SlitRelZ);
                    float amp = src.z / sqrt(max(r, 0.02));
                    float phase = _K * (src.y + r);

                    float s, c;
                    sincos(phase, s, c);
                    re += amp * c;
                    im += amp * s;
                }
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_SourceCount <= 0 || _Reveal <= 0.001) return fixed4(0, 0, 0, 0);

                float re, im;
                Psi(i.objPos.x, re, im);
                float intensity = re * re + im * im;

                float norm = saturate(intensity / max(_IntensityRef, 1e-6));
                // A gentle curve, not a linear one: linear leaves the wide dark stretches
                // between fringes looking flatly black instead of showing the faint spectral
                // colour that is genuinely there just below the peak.
                float shown = pow(norm, 0.55);

                float hue = atan2(im, re) / PRISM_TAU + 0.5;
                float3 tint = Prism_Spectral(hue);

                // Fade the last couple of centimetres so the field reads as a phenomenon filling
                // the screen rather than a rectangle with a printed edge.
                float ex = 1.0 - smoothstep(_HalfWidth - _EdgeSoften, _HalfWidth, abs(i.objPos.x));
                float ey = 1.0 - smoothstep(_HalfHeight - _EdgeSoften, _HalfHeight, abs(i.objPos.y));
                float edge = ex * ey;

                float dens = shown * _Opacity * _Reveal * edge;

                float3 rgb; float a;
                Prism_Compose(tint, dens, shown * 0.35 * edge, 0.0, rgb, a);

                Prism_Deband(rgb, i.pos.xy, _Time.y * 60.0);
                return fixed4(rgb, saturate(a));
            }
            ENDCG
        }
    }

    Fallback Off
}
