// PrismMote — one mote of the companion's attention.
//
// Written because Prism/Volumetric was the wrong tool and failed silently. That shader solves
// the chord through a UNIT SPHERE in object space; the companion's motes are camera-facing
// quads whose object-space positions are metres from their transform's origin, so `b2`
// saturated to 1, the chord came out 0, the density came out 0, and the entire companion
// rendered as nothing at all. Nobody would ever have found that by looking, because the
// symptom of a broken alpha-blended material is absence.
//
// A mote is a soft radial puff on a quad. That is all it needs to be.

Shader "Prism/Mote"
{
    Properties
    {
        _Tint     ("Tint", Color)               = (0.780, 0.749, 0.937, 1)
        _EdgeTint ("Edge tint", Color)          = (0.612, 0.878, 0.906, 1)
        _Density  ("Density", Range(0,3))       = 1.0
        _Softness ("Edge softness", Range(0.05,1)) = 0.55
        // Opt-in, and default OFF on purpose. A mesh with no colour stream leaves the COLOR input
        // undefined — some drivers hand back white, others (0,0,0,0), which would silently turn
        // every companion mote black on exactly one class of device. Only meshes that actually
        // write colours may switch this on.
        _VertexColour ("Use per-vertex colour", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Mote"
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

            float4 _Tint, _EdgeTint;
            float  _Density, _Softness, _VertexColour;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                // Per-mote colour and weight. A field of motes that all share one tint reads as
                // fog; letting each carry its own lets a single mesh show a quantity — orbital
                // speed, concentration, energy — without a draw call per particle.
                fixed4 colour : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                fixed4 colour   : COLOR;
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
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.colour   = v.colour;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Radial falloff from the quad centre. A quadratic core inside a soft shoulder
                // reads as a small volume of light rather than as a sprite.
                float2 d = i.uv * 2.0 - 1.0;
                float  r = length(d);
                float  puff = 1.0 - smoothstep(1.0 - _Softness, 1.0, r);
                if (puff <= 0.001) return fixed4(0, 0, 0, 0);

                float core = pow(puff, 3.0);

                // Motes brighten near the learner's hands, so the companion visibly reacts to
                // being reached toward.
                float hand = Prism_HandProximity(i.worldPos);

                float3 tint = lerp(_EdgeTint.rgb, _Tint.rgb, core);
                tint = lerp(tint, saturate(tint * 1.25), hand);
                float3 vc = lerp(float3(1, 1, 1), i.colour.rgb, _VertexColour);
                float  va = lerp(1.0, i.colour.a, _VertexColour);
                tint *= vc;

                float dens = (core * 0.75 + puff * 0.25) * _Density * (1.0 + hand * 0.4) * va;

                float3 rgb; float a;
                Prism_Compose(tint, dens, puff * 0.5, core * 0.35 * (1.0 + hand), rgb, a);

                a = saturate(a * _Tint.a);
                Prism_Deband(rgb, i.pos.xy, _Time.y * 60.0);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }

    Fallback Off
}
