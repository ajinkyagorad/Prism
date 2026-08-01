// Prism/AlgorithmCityBead — a single datum in a running computation.
//
// Solid ceramic body, structurally identical to Prism/Ceramic's lighting rig (same two-light
// studio, same wrapped diffuse, same specular hairline), with exactly one deliberate change:
// colour is not a uniform _Tint, it is READ PER VERTEX from UV1.x. That is the whole point of
// this shader existing rather than reusing Ceramic directly — up to a few dozen data beads are
// batched into ONE mesh by BeadField, so there is no per-object material to carry a per-object
// tint on. Colour has to travel with the vertex instead.
//
// COLOUR LAW: UV1.x is the datum's VALUE in [0,1], mapped onto the spectral ramp. A sorted run
// therefore reads as a smooth colour gradient with no label anywhere. UV1.y is a 0..1 "just
// touched by the algorithm" glow, set by the world for one comparison's worth of time and left to
// decay — it brightens the bead and adds a little extra specular energy, nothing more.

Shader "Prism/AlgorithmCityBead"
{
    Properties
    {
        _Luminance ("Self luminance", Range(0,1))       = 0.32
        _Wrap      ("Diffuse wrap", Range(0,1))         = 0.60
        _SpecPower ("Specular tightness", Range(8,256)) = 90
        _SpecGain  ("Specular gain", Range(0,2))        = 0.55
        _RimGain   ("Rim gain", Range(0,2))             = 0.45
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            Name "AlgorithmCityBead"
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "../../Aesthetic/Include/PrismOptics.hlsl"

            float _Luminance, _Wrap, _SpecPower, _SpecGain, _RimGain;

            // Same rig as Prism/Ceramic: key from upper right, cool fill from lower left, so every
            // solid body in PRISM shares one light no matter which shader draws it.
            static const float3 PRISM_KEY_DIR  = float3( 0.3363,  0.7495, -0.4997);
            static const float3 PRISM_FILL_DIR = float3(-0.7332,  0.3055,  0.6073);

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv1    : TEXCOORD1;   // x = value 0..1, y = glow 0..1
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float3 worldN  : TEXCOORD0;
                float3 worldV  : TEXCOORD1;
                float2 valueGl : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos     = UnityObjectToClipPos(v.vertex);
                o.worldN  = UnityObjectToWorldNormal(v.normal);
                o.worldV  = _WorldSpaceCameraPos - world;
                o.valueGl = v.uv1;
                return o;
            }

            float WrapLambert(float ndotl, float w)
            {
                return saturate((ndotl + w) / (1.0 + w));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldN);
                float3 V = normalize(i.worldV);
                float  ndotv = saturate(dot(N, V));

                float3 tint = Prism_Spectral(i.valueGl.x);
                float  glow = saturate(i.valueGl.y);

                float key  = WrapLambert(dot(N, PRISM_KEY_DIR),  _Wrap);
                float fill = WrapLambert(dot(N, PRISM_FILL_DIR), _Wrap);

                float3 lit = tint * (0.16
                           + key  * 0.72 * lerp(PRISM_WHITE, PRISM_GOLD, 0.35)
                           + fill * 0.34 * lerp(PRISM_WHITE, PRISM_LAVENDER, 0.55));

                // Self luminance plus the touch-glow is what makes an active comparison read as a
                // small flare rather than a colour swap that happened off-screen.
                lit += tint * (_Luminance + glow * 0.9) * 0.5;

                float3 H = normalize(PRISM_KEY_DIR + V);
                float  spec = pow(saturate(dot(N, H)), _SpecPower) * (_SpecGain + glow * 1.2);

                float rim = Prism_Fresnel(ndotv, 0.02, 4.0) * _RimGain;
                float3 rgb = lit + spec + rim * PRISM_CYAN * 0.5;

                Prism_Deband(rgb, i.pos.xy, _Time.y * 60.0);
                return fixed4(rgb, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
