// PrismTrail — the path a body actually took, coloured by how fast it was going.
//
// This is the most pedagogically loaded material in the project, and the reason it
// exists is worth stating: colour here is SPEED. A moon on an eccentric orbit draws a
// trail that is violet and hurried at perigee and cyan and unhurried at apogee, every
// single time, before anyone has said the words "Kepler's second law" or "angular
// momentum". The learner sees the law before they have a name for it, which is the
// order this whole product insists on.
//
// The mesh is a ribbon rebuilt each frame by PrismTrailRibbon.
// UV convention: u = age (0 = newest, 1 = about to expire), v = across the ribbon.
// TEXCOORD1.x carries normalised speed at that sample.

Shader "Prism/Trail"
{
    Properties
    {
        _SpeedLo   ("Speed at cyan (m/s)", Float)      = 0.06
        _SpeedHi   ("Speed at violet (m/s)", Float)    = 0.34
        _Opacity   ("Opacity", Range(0,1))             = 0.8
        _HeadGain  ("Head brightness", Range(0,3))     = 1.6
        _EdgeSoft  ("Ribbon edge softness", Range(0.01,1)) = 0.55
        _GroundY   ("Ground plane Y", Float)           = -9999
        _GroundSoft("Ground soften (m)", Range(0.001,0.5)) = 0.06
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Trail"
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

            float _SpeedLo, _SpeedHi, _Opacity, _HeadGain, _EdgeSoft, _GroundY, _GroundSoft;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;   // x = age01, y = across01
                float2 uv2    : TEXCOORD1;   // x = speed (m/s)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float  speed  : TEXCOORD1;
                float  worldY : TEXCOORD2;
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
                o.speed  = v.uv2.x;
                o.worldY = mul(unity_ObjectToWorld, v.vertex).y;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Speed -> position on the spectral ramp. Cyan is slow, violet is fast.
                float s01 = saturate((i.speed - _SpeedLo) / max(_SpeedHi - _SpeedLo, 1e-5));
                float3 tint = Prism_Spectral(s01);

                // Trails are short-lived explanatory marks, not permanent geometry.
                float age  = saturate(i.uv.x);
                float life = pow(1.0 - age, 1.6);

                // Across the ribbon: a soft-edged stroke, brighter along its centreline.
                float across = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                float stroke = smoothstep(0.0, _EdgeSoft, across);
                float centre = pow(saturate(across), 3.0);

                // The newest few centimetres are brighter, so the eye is drawn to where the
                // body IS rather than where it has been.
                float head = exp(-age * 14.0) * _HeadGain;

                float dens = (stroke * 0.55 + centre * 0.45) * life * _Opacity;
                float spec = centre * head * 0.35;

                float3 rgb; float a;
                Prism_Compose(tint, dens, life * 0.25, spec, rgb, a);

                a *= Prism_PlaneFade(i.worldY, _GroundY, _GroundSoft);
                Prism_Deband(rgb, i.pos.xy, _Time.y * 60.0);
                return fixed4(rgb, saturate(a));
            }
            ENDCG
        }
    }

    Fallback Off
}
