// PrismSky — the real sky, on the inside of a large sphere.
//
// Replaces the boundless white void the atrium used to be. That void was faithful to the brief
// on paper ("an immense, calm, white space with no obvious walls or floor boundary") and wrong
// in a headset: with no horizon, no ground and no depth cues there is nothing for the eye to
// measure anything against, so every floating object reads as a flat blob at an unknowable
// distance. A sky and a horizon cost nothing and give the space scale.
//
// The colour is computed from real Rayleigh and Mie scattering — see PrismAtmosphere.hlsl.
// That is not showing off: PRISM's whole claim is that what a learner sees is computed from
// the physics rather than illustrated, and the sky being blue for the actual reason is of a
// piece with the field contours being real equipotentials.

Shader "Prism/Sky"
{
    Properties
    {
        _Exposure ("Exposure", Range(0.1, 8)) = 1.6
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" }

        Pass
        {
            Name "Sky"
            Cull Front
            ZWrite Off
            ZTest LEqual

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Include/PrismOptics.hlsl"
            #include "Include/PrismAtmosphere.hlsl"

            float _Exposure;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
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

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float3 col = Prism_SkyColour(dir) + Prism_SunDisc(dir);

                col *= _Exposure;

                // Filmic-ish shoulder so the sun and the horizon glow roll off instead of
                // clipping to a flat white disc.
                col = col / (1.0 + col * 0.6);
                col = pow(max(col, 0.0), 1.0 / 1.15);

                // A gradient this smooth over this many pixels bands badly without a dither.
                Prism_Deband(col, i.pos.xy, _Time.y * 60.0);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
