// PrismGround — the land the learner stands on.
//
// A calm highland: a level plateau underfoot, falling away into a mist-filled valley with
// ranges beyond it. Deliberately quiet. The constellation is the subject and the landscape is
// the room it is in, so this material is built to recede — low saturation, no busy detail, and
// aerial perspective doing most of the work of describing distance.
//
// No textures. Colour comes from slope and height, broken up by the same value noise the rest
// of the project uses, which means no tiling seams and nothing to download.

Shader "Prism/Ground"
{
    Properties
    {
        _Grass    ("Low ground", Color)          = (0.263, 0.318, 0.243, 1)
        _Rock     ("Rock", Color)                = (0.353, 0.345, 0.365, 1)
        _High     ("High ground", Color)         = (0.545, 0.545, 0.573, 1)
        _Plateau  ("Plateau underfoot", Color)   = (0.322, 0.361, 0.318, 1)
        _SlopeRock("Slope at which rock shows", Range(0.1,1)) = 0.62
        _SnowLine ("High ground begins (m)", Float) = 70
        _Detail   ("Detail strength", Range(0,1)) = 0.35
        _MistTop  ("Mist top (m)", Float)         = 34
        _MistDens ("Mist density", Range(0,0.02)) = 0.0040
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            Name "Ground"
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Include/PrismOptics.hlsl"
            #include "Include/PrismAtmosphere.hlsl"

            float4 _Grass, _Rock, _High, _Plateau;
            float  _SlopeRock, _SnowLine, _Detail, _MistTop, _MistDens;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;   // x = normalised radius from the learner
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldN   : TEXCOORD1;
                float  radial   : TEXCOORD2;
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
                o.worldN   = UnityObjectToWorldNormal(v.normal);
                o.radial   = v.uv.x;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldN);
                float3 toEye = _WorldSpaceCameraPos - i.worldPos;
                float dist = length(toEye);
                float3 viewDir = -toEye / max(dist, 1e-4);

                float height = i.worldPos.y;

                // ---- relief -------------------------------------------------------------
                // The terrain mesh is a few thousand triangles covering 900 m, so its own normals
                // describe only the largest landforms. Shaded with those alone the land has no
                // surface at all and tonemaps to flat grey bands — which is precisely what the
                // first renders showed. The detail has to come from the material.
                //
                // Perturb the normal by the gradient of the same value noise the terrain generator
                // uses, at three scales, each faded out by distance before it can alias. This is
                // bump mapping with a procedural, seamless, zero-download height field.
                float3 Ngeo = N;
                float relief = 0.0;
                // The finite difference must be taken over a SMALL fraction of a noise wavelength.
                // Sampling half a wavelength away returns an essentially uncorrelated value, so the
                // "gradient" is just noise, the perturbed normal points in a random direction, and
                // the hillside resolves into hard light and dark blotches. That is exactly what the
                // first render of this shader showed.
                const float Eps = 0.11;
                [unroll]
                for (int s = 0; s < 3; s++)
                {
                    // ~6 m boulders, ~1.4 m rubble, ~0.35 m gravel.
                    float freq  = (s == 0) ? 0.17  : ((s == 1) ? 0.72 : 2.9);
                    // Tangent of the slope each octave adds, before _Detail scales it.
                    float amp   = (s == 0) ? 0.55  : ((s == 1) ? 0.30 : 0.15);
                    // Each octave dies well before its features reach a pixel, so nothing aliases.
                    float fade  = saturate(1.0 - dist * freq * 0.022);
                    if (fade <= 0.001) continue;

                    float3 p = i.worldPos * freq;
                    float h  = Prism_Value3D(p);
                    float hx = Prism_Value3D(p + float3(Eps, 0, 0));
                    float hz = Prism_Value3D(p + float3(0, 0, Eps));
                    float2 g = float2(hx - h, hz - h) / Eps;      // order 1, a real slope
                    N = normalize(N + float3(-g.x, 0, -g.y) * amp * fade * _Detail * 1.6);
                    relief += (h - 0.5) * amp * fade * 1.8;
                }

                float slope = 1.0 - saturate(Ngeo.y);       // 0 flat, 1 vertical — landform, not bumps

                // ---- albedo -------------------------------------------------------------
                // Two scales of break-up. The coarse one varies the ground over tens of metres;
                // the fine one only matters within a few metres of the learner's feet.
                float macro = Prism_FBM2(i.worldPos * 0.018);
                float patch = Prism_FBM2(i.worldPos * 0.055 + 31.7);

                float3 albedo = lerp(_Grass.rgb, _Plateau.rgb, saturate(1.0 - i.radial * 3.0));
                // Sward and scree interleave rather than switching cleanly at one slope, and the
                // boundary follows the noise — a hard smoothstep on slope alone draws contour bands.
                float rockMask = smoothstep(_SlopeRock - 0.22, _SlopeRock + 0.12,
                                            slope + (patch - 0.5) * 0.30);
                albedo = lerp(albedo, _Rock.rgb, rockMask);
                albedo = lerp(albedo, _High.rgb,
                              smoothstep(_SnowLine, _SnowLine + 45.0, height + (macro - 0.5) * 34.0));

                // Hue drift, not just brightness. Damp ground reads greener and cooler in the
                // hollows, sun-bleached and warmer on the shoulders; equal-luminance grey noise is
                // what made the land look like static.
                float damp = saturate(0.5 - relief * 1.4) * (1.0 - rockMask);
                albedo = lerp(albedo, albedo * float3(0.82, 1.06, 0.92), damp * 0.55);
                albedo = lerp(albedo, albedo * float3(1.10, 1.03, 0.88), saturate(relief * 1.6) * 0.45);
                albedo *= 1.0 + (macro - 0.5) * 0.30 + relief * _Detail * 0.55;

                // Lit by the sun that lights the sky, with a wrapped terminator so the hills
                // stay soft, plus sky light from above.
                float3 sun = normalize(_PrismSunDir.xyz);
                float ndotl = saturate((dot(N, sun) + 0.35) / 1.35);

                // _PrismSunDir.w is tuned for the SKY, where it multiplies scattering coefficients
                // of order 1e-6. Feeding it straight into a surface albedo overexposes the ground by
                // more than an order of magnitude — every hillside blew out and tonemapped to a flat
                // brown, which is exactly what the first render showed. Ground irradiance needs its
                // own scale.
                const float GroundExposure = 0.055;
                float3 sunIrr = _PrismSunColour.rgb * (_PrismSunDir.w * GroundExposure);
                float3 sunLight = sunIrr * ndotl;

                // Sky light is already in scattered-radiance units, so it needs a gentler lift than
                // the sun term rather than the same one.
                float3 skyLight = Prism_SkyColour(float3(0, 1, 0)) * 2.6 * (0.50 + 0.50 * saturate(N.y));

                // One bounce off the sunlit ground nearby.
                //
                // Without it the inner wall of the valley — which faces the learner and away from
                // the sun, because the atrium deliberately looks toward it — received only the sky
                // term at its weakest and rendered as a near-black band across the middle of every
                // view. Real shadowed slopes are lit by the lit ground around them: they are dark,
                // not absent.
                //
                // Weighting this by (1 - N.y) was the obvious-looking choice and the wrong one. The
                // surface that needs the fill here is a GENTLE slope, so N.y is near 1 and that
                // weight cancels almost all of it. Ground bounce arrives from every direction, so
                // it gets no orientation term at all.
                float3 bounce = sunIrr * 0.22;

                float3 col = albedo * (sunLight + skyLight + bounce);

                // Distance and mist. Aerial perspective first, then the shallow valley mist on
                // top of it — the two describe different things and both are needed.
                col = Prism_Aerial(col, dist, viewDir);
                float mist = Prism_Mist(height, dist, _MistTop, _MistDens);
                col = lerp(col, Prism_SkyColour(normalize(float3(viewDir.x, 0.06, viewDir.z))), mist);

                col = col / (1.0 + col * 0.6);
                col = pow(max(col, 0.0), 1.0 / 1.15);

                Prism_Deband(col, i.pos.xy, _Time.y * 60.0);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
