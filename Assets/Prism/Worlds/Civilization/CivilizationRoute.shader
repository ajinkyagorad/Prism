// Prism/CivilizationRoute — a trade route, as a price gradient.
//
// Drawn along the tube the learner's own hand traced between two settlements. Two things ride
// on this one surface, and both are computed quantities, not decoration:
//
//   the GRADIENT   _TintA at u=0 fades to _TintB at u=1 — the same spectral surplus-to-shortage
//                  colour each settlement already wears. The learner sees the price differential
//                  that is actually driving trade laid out along the route itself, and watches
//                  the two ends drift toward the same colour as trade levels them.
//
//   the PACKETS    two independent streams, one travelling toward B and one toward A, each
//                  scaled by the real flow of goods TradeSim computed in that direction this
//                  frame. A route carrying grain one way and ore the other shows both streams at
//                  once; a route with nothing worth moving shows almost none.
//
// _Starved dims and stalls the packets and desaturates the gradient toward the deep note — the
// same visual grammar Prism/Flow uses for a contradiction, borrowed here for "the cost of this
// path currently eats the whole price gap".
//
// UV convention (from PrismMesh.Tube): u runs 0..1 along the route, v runs 0..1 around it.

Shader "Prism/CivilizationRoute"
{
    Properties
    {
        _TintA    ("Tint at A", Color)                 = (0.612, 0.878, 0.906, 1)
        _TintB    ("Tint at B", Color)                 = (0.443, 0.361, 0.612, 1)
        _FlowPos  ("Flow A->B (units/s)", Float)       = 0
        _FlowNeg  ("Flow B->A (units/s)", Float)       = 0
        _FlowRef  ("Flow that reads as brisk", Float)  = 3.0
        _Starved  ("Starved (cost exceeds gap)", Range(0,1)) = 0.0
        _Packets  ("Packets along curve", Range(1,12)) = 5
        _Speed    ("Packet speed", Range(0,3))         = 0.5
        _Width    ("Packet width", Range(0.02,0.5))    = 0.18
        _CoreGain ("Core opacity", Range(0,1))         = 0.55
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "CivilizationRoute"
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

            float4 _TintA, _TintB;
            float  _FlowPos, _FlowNeg, _FlowRef, _Starved, _Packets, _Speed, _Width, _CoreGain;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float3 worldN : TEXCOORD1;
                float3 worldV : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 obj = v.vertex.xyz;

                // A starved route shivers gently in place — strain, not decoration.
                if (_Starved > 0.001)
                {
                    float s = sin(_Time.y * 30.0 + v.uv.x * 37.0) * 0.5
                            + sin(_Time.y * 47.0 + v.uv.x * 23.0) * 0.5;
                    obj += v.normal * s * _Starved * 0.010;
                }

                float3 world = mul(unity_ObjectToWorld, float4(obj, 1.0)).xyz;
                o.pos    = UnityObjectToClipPos(float4(obj, 1.0));
                o.uv     = v.uv;
                o.worldN = UnityObjectToWorldNormal(v.normal);
                o.worldV = _WorldSpaceCameraPos - world;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldN);
                float3 V = normalize(i.worldV);
                float  ndotv = saturate(dot(N, V));

                // The gradient: the price differential, laid out along the route itself.
                float3 tint = lerp(_TintA.rgb, _TintB.rgb, i.uv.x);
                if (_Starved > 0.001) tint = lerp(tint, PRISM_VIOLET, saturate(_Starved) * 0.7);

                float posAmt = saturate(_FlowPos / max(_FlowRef, 1e-3));
                float negAmt = saturate(_FlowNeg / max(_FlowRef, 1e-3));
                float speed  = _Speed * lerp(1.0, 0.06, saturate(_Starved));

                // Stream toward B (increasing u).
                float uPos = i.uv.x * _Packets - _Time.y * speed;
                float dPos = abs(frac(uPos) - 0.5) / max(_Width, 1e-3);
                float packetPos = exp(-dPos * dPos * 2.0) * posAmt;

                // Stream toward A (decreasing u) — independent, so two-way trade on one route
                // reads as two-way motion, not as a cancelled average.
                float uNeg = i.uv.x * _Packets + _Time.y * speed;
                float dNeg = abs(frac(uNeg) - 0.5) / max(_Width, 1e-3);
                float packetNeg = exp(-dNeg * dNeg * 2.0) * negAmt;

                float packet = packetPos + packetNeg;

                // Across the tube: a bright filament core, soft shoulders.
                float across = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                float core   = pow(saturate(across), 2.4);

                float dens = (core * _CoreGain + packet * 0.6) * lerp(1.0, 0.35, saturate(_Starved));
                float halo = Prism_Fresnel(ndotv, 0.0, 2.2) * across;
                float spec = packet * core * 0.5 * (1.0 - _Starved);

                float3 rgb; float a;
                Prism_Compose(tint, dens, halo, spec, rgb, a);

                a = saturate(a * lerp(_TintA.a, _TintB.a, i.uv.x));
                Prism_Deband(rgb, i.pos.xy, _Time.y * 60.0);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }

    Fallback Off
}
