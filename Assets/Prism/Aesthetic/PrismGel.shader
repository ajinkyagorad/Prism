// PrismGel — translucent gel with visible internal currents.
//
// For matter that has an inside worth seeing: a planet's interior, cytoplasm, a
// probability cloud held in the hand, the medium a field lives in.
//
// The interior is a short parallax raymarch through an object-space noise field, six
// steps, two octaves. That is not a volumetric renderer and is not trying to be — it
// buys the one thing a flat translucent surface can never fake, which is parallax:
// the currents inside slide correctly against the surface as the learner moves their
// head, and that alone is what makes the material read as having a volume.
//
// Refraction is analytic. There is no GrabPass: on Quest a grab breaks single-pass
// instanced stereo and costs a full resolve, and at these opacities nobody can tell
// the difference between refracting the real background and refracting the interior.

Shader "Prism/Gel"
{
    Properties
    {
        _Tint       ("Tint", Color)                       = (0.612, 0.878, 0.906, 1)
        _DeepTint   ("Deep tint", Color)                  = (0.443, 0.361, 0.612, 1)
        _Density    ("Optical density", Range(0,4))        = 1.4
        _NoiseFreq  ("Current scale", Range(0.5,12))       = 3.2
        _FlowSpeed  ("Current speed", Range(0,2))          = 0.35
        _FlowAxis   ("Current axis (object space)", Vector) = (0,1,0,0)
        _Stretch    ("Current stretch along axis", Range(0,0.95)) = 0.65
        _Dispersion ("Chromatic dispersion", Range(0,1))   = 0.35
        _FilmNm     ("Thin film thickness (nm)", Range(0,900)) = 300
        _RimGain    ("Rim gain", Range(0,2))               = 0.8
        _Phase      ("Breath phase", Range(0,1))           = 0.0
        _GroundY    ("Ground plane Y", Float)              = -9999
        _GroundSoft ("Ground soften (m)", Range(0.001,0.5)) = 0.08
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Gel"
            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Include/PrismOptics.hlsl"

            // Six steps is the budget on XR2 Gen 2 when this material can cover a third of
            // the screen. Raising it is the first thing to try if the interior looks banded,
            // and the first thing to cut if frame time slips.
            #define PRISM_GEL_STEPS 6

            float4 _Tint, _DeepTint, _FlowAxis;
            float  _Density, _NoiseFreq, _FlowSpeed, _Stretch, _Dispersion;
            float  _FilmNm, _RimGain, _Phase, _GroundY, _GroundSoft;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float3 objPos : TEXCOORD0;
                float3 objDir : TEXCOORD1;   // eye -> surface, object space
                float3 worldN : TEXCOORD2;
                float3 worldV : TEXCOORD3;
                float  worldY : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float breath = (Prism_Breathe(_Phase, 0.33) - 0.5) * 0.018;
                float3 obj   = v.vertex.xyz * (1.0 + breath);
                float3 world = mul(unity_ObjectToWorld, float4(obj, 1.0)).xyz;
                float3 objCam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;

                o.pos    = UnityObjectToClipPos(float4(obj, 1.0));
                o.objPos = obj;
                o.objDir = obj - objCam;
                o.worldN = UnityObjectToWorldNormal(v.normal);
                o.worldV = _WorldSpaceCameraPos - world;
                o.worldY = world.y;
                return o;
            }

            // Currents rather than clouds: the noise field is compressed along the flow
            // axis, so structures elongate into streams instead of staying blobby.
            float CurrentDensity(float3 p, float3 axis, float t)
            {
                float along = dot(p, axis);
                float3 q = p - axis * along * _Stretch;
                q += axis * t;                              // advect
                return Prism_FBM2(q * _NoiseFreq);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldN);
                float3 V = normalize(i.worldV);
                float  ndotv = saturate(dot(N, V));

                float3 axis = normalize(_FlowAxis.xyz + float3(0, 1e-4, 0));
                float  t    = -_Time.y * _FlowSpeed;

                // March inward from the surface along the view ray, in object space so the
                // currents belong to the object and do not swim when it moves.
                float3 rd = normalize(i.objDir);
                float  h  = 2.0 / (float)PRISM_GEL_STEPS;

                // Dispersion: the three wavelengths take slightly different paths inward.
                float3 disp = Prism_Dispersion(_Dispersion) * 0.12;

                float3 accum = 0.0;
                float  trans = 1.0;

                [unroll]
                for (int s = 0; s < PRISM_GEL_STEPS; s++)
                {
                    float3 p = i.objPos + rd * (h * ((float)s + 0.5));
                    // Leave the body when we exit the unit volume.
                    float inside = 1.0 - smoothstep(0.85, 1.05, length(p));
                    if (inside <= 0.001) break;

                    float d = CurrentDensity(p, axis, t) * inside;
                    // Depth-tinted: shallow currents carry the surface tint, deep ones the
                    // deep tint. This is what gives the material apparent thickness.
                    float depth01 = (float)s / (float)PRISM_GEL_STEPS;
                    float3 c = lerp(_Tint.rgb, _DeepTint.rgb, depth01);
                    // Per-channel path difference stands in for chromatic separation.
                    c *= (1.0 + disp * depth01);

                    float ext = d * _Density * h;
                    accum += trans * c * ext;
                    trans *= exp(-ext * 1.6);               // Beer-Lambert
                }

                float dens = saturate(1.0 - trans);

                // Surface optics on top of the interior.
                float3 film = (_FilmNm > 1.0) ? Prism_ThinFilm(_FilmNm, ndotv, 1.34) : 1.0;
                float  rim  = Prism_Fresnel(ndotv, 0.02, 3.2) * _RimGain;

                float3 tint = (dens > 1e-4) ? accum / max(dens, 1e-4) : _Tint.rgb;
                tint *= (0.85 + 0.30 * film);

                float3 rgb; float a;
                Prism_Compose(tint, dens, rim, rim * 0.35, rgb, a);

                a *= Prism_PlaneFade(i.worldY, _GroundY, _GroundSoft);
                a  = saturate(a * _Tint.a);

                Prism_Deband(rgb, i.pos.xy, _Time.y * 60.0);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }

    Fallback Off
}
