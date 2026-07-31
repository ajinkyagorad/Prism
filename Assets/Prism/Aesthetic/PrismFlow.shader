// PrismFlow — a relationship, as a current.
//
// Runs along a runtime-generated tube between two concepts. Energy packets travel the
// curve; their rate and brightness rise when the relationship is causally active, so a
// constellation at rest is calm and a constellation being reasoned about is busy.
//
// _Tension is the counterpart. When a learner pulls together two concepts that are NOT
// related, the current does not simply fail to appear — packets stall, collide and the
// tube shivers. Contradiction produces spatial tension rather than an error sound.
//
// UV convention: u runs 0..1 along the curve, v runs 0..1 across it.

Shader "Prism/Flow"
{
    Properties
    {
        _Tint     ("Tint", Color)                    = (0.678, 0.925, 0.808, 1)
        _Strength ("Relationship strength", Range(0,1)) = 0.7
        _Pulse    ("Causally active", Range(0,1))    = 0.0
        _Tension  ("Contradiction", Range(0,1))      = 0.0
        _Packets  ("Packets along curve", Range(1,12)) = 4
        _Speed    ("Packet speed", Range(0,3))       = 0.55
        _Width    ("Packet width", Range(0.02,0.5))  = 0.16
        _CoreGain ("Core opacity", Range(0,1))       = 0.5
        _Phase    ("Phase offset", Range(0,1))       = 0.0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Flow"
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

            float4 _Tint;
            float  _Strength, _Pulse, _Tension, _Packets, _Speed, _Width, _CoreGain, _Phase;

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

                // Tension makes the tube shiver in place. Frequency is high enough to read as
                // strain rather than as animation.
                if (_Tension > 0.001)
                {
                    float s = sin(_Time.y * 34.0 + v.uv.x * 41.0) * 0.5
                            + sin(_Time.y * 53.0 + v.uv.x * 27.0) * 0.5;
                    obj += v.normal * s * _Tension * 0.012;
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

                // Packets. When contradicted they stall, so the travel term is damped toward
                // a standing pattern instead of a running one.
                float speed = _Speed * lerp(1.0, 0.08, saturate(_Tension));
                float u = i.uv.x * _Packets - _Time.y * speed + _Phase * 7.7;
                float d = abs(frac(u) - 0.5) / max(_Width, 1e-3);
                float packet = exp(-d * d * 2.0);

                // Active relationships emit more often and brighter.
                packet *= lerp(0.45, 1.0, saturate(_Pulse));
                float rate = 1.0 + _Pulse * 0.8;
                packet *= rate;

                // Across the tube: a bright filament core, soft shoulders.
                float across = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                float core   = pow(saturate(across), 2.4);

                float3 tint = _Tint.rgb;
                if (_Tension > 0.001)
                {
                    // Contradiction desaturates toward the deep note and loses its colour.
                    tint = lerp(tint, PRISM_VIOLET, saturate(_Tension) * 0.8);
                }

                float dens = (core * _CoreGain + packet * 0.55) * _Strength;
                float halo = Prism_Fresnel(ndotv, 0.0, 2.2) * across;
                float spec = packet * core * 0.5 * (1.0 - _Tension);

                float3 rgb; float a;
                Prism_Compose(tint, dens, halo, spec, rgb, a);

                a = saturate(a * _Tint.a);
                Prism_Deband(rgb, i.pos.xy, _Time.y * 60.0);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }

    Fallback Off
}
