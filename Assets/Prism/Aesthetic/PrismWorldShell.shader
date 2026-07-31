// PrismWorldShell — the inside of the place a Concept World happens in.
//
// The first eleven renders all looked the same, and it was not the fault of any one world: every
// one of them put a few small objects on the identical grey highland under the identical dawn sky.
// The content differed and the PLACE never did, so nothing felt like arriving anywhere.
//
// This is one shader, driven entirely by parameters, rendered on the inside of a sphere around the
// world. Four independent layers stack into very different rooms:
//
//   gradient   zenith-to-nadir wash, always on, sets the mood in one line
//   stars      hashed point field with real colour temperature spread  (deep space)
//   nebula     domain-warped FBM in a second tint                     (space, weather, mind)
//   structure  ridged veining that reads as tissue, circuitry or caustics
//
// Deep space is stars + faint nebula. The inside of a cell is heavy structure, no stars. A quantum
// vacuum is near-black with a slow nebula and nothing else. Same 90 lines each time.
//
// Drawn with Cull Front and ZTest Always in the Background queue so it is behind everything the
// world builds, and so it does not fight the depth of a sphere the learner can walk through.

Shader "Prism/WorldShell"
{
    Properties
    {
        _Zenith    ("Zenith", Color)          = (0.05, 0.06, 0.11, 1)
        _Nadir     ("Nadir", Color)           = (0.02, 0.02, 0.04, 1)
        _Horizon   ("Horizon band", Color)    = (0.10, 0.12, 0.20, 1)
        _HorizonAmt("Horizon strength", Range(0,2)) = 0.0

        _Stars     ("Star density", Range(0,1))     = 0.0
        _StarScale ("Star field scale", Float)      = 46
        _NebulaAmt ("Nebula", Range(0,2))           = 0.0
        _NebulaTint("Nebula tint", Color)           = (0.35, 0.22, 0.55, 1)
        _NebulaFreq("Nebula scale", Float)          = 1.7

        _Structure ("Structure", Range(0,2))        = 0.0
        _StructTint("Structure tint", Color)        = (0.45, 0.30, 0.35, 1)
        _StructFreq("Structure scale", Float)       = 3.1
        _Drift     ("Drift speed", Float)           = 0.02
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Shell"
            Cull Front              // we are inside the sphere
            ZWrite Off
            ZTest Always            // always behind the world's own content
            Blend Off

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Include/PrismOptics.hlsl"

            float4 _Zenith, _Nadir, _Horizon, _NebulaTint, _StructTint;
            float  _HorizonAmt, _Stars, _StarScale, _NebulaAmt, _NebulaFreq;
            float  _Structure, _StructFreq, _Drift;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;      // object-space direction, so the shell rotates with the world
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(v.vertex.xyz);
                return o;
            }

            float2 Hash22(float2 p)
            {
                float3 q = float3(dot(p, float2(127.1, 311.7)),
                                  dot(p, float2(269.5, 183.3)),
                                  dot(p, float2(419.2, 371.9)));
                return frac(sin(q.xy) * 43758.5453);
            }

            // Direction -> a seamless-enough 2D chart. Dominant-axis projection: cheap, and the
            // slight density change at the cube seams is invisible against a star field.
            float2 Chart(float3 d)
            {
                float3 a = abs(d);
                if (a.x >= a.y && a.x >= a.z) return d.yz / a.x + 8.0;
                if (a.y >= a.z)               return d.xz / a.y + 24.0;
                return d.xy / a.z + 40.0;
            }

            // One star per cell, with a hashed brightness so most cells hold nothing. Real stars
            // are not uniform white: hot ones run blue, cool ones amber, and that spread is most of
            // what makes a procedural sky stop looking like noise.
            float3 StarField(float3 d, float density)
            {
                float2 uv   = Chart(d) * _StarScale;
                float2 cell = floor(uv);
                float2 f    = uv - cell;

                float3 acc = 0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 o = float2(x, y);
                        float2 h = Hash22(cell + o);
                        float2 h2 = Hash22(cell + o + 57.3);
                        // h2.x gates existence: only the brightest tail survives.
                        float mag = h2.x;
                        if (mag < 1.0 - density * 0.5) continue;

                        float2 p = o + h - f;
                        float r  = dot(p, p);
                        float i0 = exp(-r * 130.0) * pow(saturate((mag - (1.0 - density * 0.5)) /
                                                                  max(density * 0.5, 1e-3)), 2.2);
                        // Colour temperature from a second hash, blue-white through amber.
                        float3 tc = lerp(float3(0.62, 0.74, 1.00), float3(1.00, 0.82, 0.60),
                                         saturate(h2.y * 1.3 - 0.15));
                        // A slow scintillation, different per star. Stops the field looking printed.
                        float tw = 0.82 + 0.18 * sin(_Time.y * (0.6 + h.x * 2.2) + h.y * 30.0);
                        acc += tc * i0 * tw;
                    }
                }
                return acc;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float t = _Time.y * _Drift;

                // Vertical wash, plus an optional band at the horizon for worlds that want a ground
                // plane implied rather than drawn.
                float up = d.y * 0.5 + 0.5;
                float3 col = lerp(_Nadir.rgb, _Zenith.rgb, smoothstep(0.0, 1.0, up));
                float band = exp(-abs(d.y) * 6.0);
                col = lerp(col, _Horizon.rgb, band * _HorizonAmt);

                if (_NebulaAmt > 0.001)
                {
                    // Domain warping: FBM whose input is displaced by another FBM. One extra lookup
                    // turns bland cloud into filament and void, which is what actual nebulae look
                    // like and what a flat FBM never does.
                    float3 q = d * _NebulaFreq + float3(0, t * 0.3, 0);
                    float3 w = float3(Prism_FBM2(q), Prism_FBM2(q + 5.2), Prism_FBM2(q + 9.1)) - 0.5;
                    float n  = Prism_FBM2(q + w * 1.8);
                    n = pow(saturate(n * 1.35), 2.0);
                    col += _NebulaTint.rgb * n * _NebulaAmt;
                }

                if (_Stars > 0.001)
                {
                    // Stars sit behind the nebula, so dense cloud hides them.
                    col += StarField(d, _Stars);
                }

                if (_Structure > 0.001)
                {
                    // Ridged noise: abs() folded about the midpoint makes creases instead of blobs,
                    // which is what reads as membrane, vein, filament or circuit trace.
                    float3 q = d * _StructFreq + float3(t * 0.5, -t * 0.31, t * 0.22);
                    float v = 1.0 - abs(Prism_FBM2(q) * 2.0 - 1.0);
                    float v2 = 1.0 - abs(Prism_FBM2(q * 2.3 + 17.0) * 2.0 - 1.0);
                    float s = pow(v, 3.4) * 0.7 + pow(v2, 4.5) * 0.3;
                    col += _StructTint.rgb * s * _Structure;
                }

                col = col / (1.0 + col * 0.55);
                Prism_Deband(col, i.pos.xy, _Time.y * 60.0);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
