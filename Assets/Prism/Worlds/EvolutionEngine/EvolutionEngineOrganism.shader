// EvolutionEngineOrganism — one organism of the population, as a soft camera-facing blob.
//
// Colour is not a material property here; it is PER-VERTEX, baked on the CPU in
// EvolutionOrganismField.Rebuild from PrismPalette.Spectral(trait01) — see that file for the
// colour law. Vertex alpha carries each organism's fade level (birth fade-in, death fade-out) and
// is multiplied into the final alpha rather than treated as a second density term.
//
// Geometrically this is Prism/Mote with the uniform _Tint replaced by a per-vertex colour: same
// soft radial puff on a quad, same Prism_Compose backdrop-aware composition, for the same reason
// Mote has it — an organism is a small volume of light, not a textured sprite, and it must read
// against a bright sky, a dark valley floor, or passthrough alike.

Shader "Prism/EvolutionEngineOrganism"
{
    Properties
    {
        _Density  ("Density", Range(0,3))          = 1.0
        _Softness ("Edge softness", Range(0.05,1)) = 0.55
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "EvolutionEngineOrganism"
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

            float _Density, _Softness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float4 color    : COLOR0;
                float3 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.uv       = v.uv;
                o.color    = v.color;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Radial falloff from the quad centre, exactly Prism/Mote's puff: a quadratic core
                // inside a soft shoulder, which reads as a small volume rather than a flat sprite.
                float2 d = i.uv * 2.0 - 1.0;
                float  r = length(d);
                float  puff = 1.0 - smoothstep(1.0 - _Softness, 1.0, r);
                if (puff <= 0.001) return fixed4(0, 0, 0, 0);

                float core = pow(puff, 3.0);

                // Organisms brighten faintly near the learner's hand, the same proximity cue every
                // material in PRISM carries — even though this world's whole rule is that a hand
                // may never MOVE one. Noticing it is not touching it.
                float hand = Prism_HandProximity(i.worldPos);

                float3 tint = i.color.rgb * (0.82 + 0.35 * core);
                tint = lerp(tint, saturate(tint * 1.2), hand);

                float dens = (core * 0.75 + puff * 0.25) * _Density * (1.0 + hand * 0.3);

                float3 rgb; float a;
                Prism_Compose(tint, dens, puff * 0.5, core * 0.3 * (1.0 + hand), rgb, a);

                // i.color.a is this organism's own fade level: 0 at the instant of birth or the
                // end of its death fade, 1 while fully present.
                a = saturate(a * i.color.a);
                Prism_Deband(rgb, i.pos.xy, _Time.y * 60.0);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }

    Fallback Off
}
