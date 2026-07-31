// Prism/MathMotionCurve - a curve, coloured by what it means.
//
// COLOUR LAW (the one this whole world runs on): hue encodes the SIGN of the derivative at each
// point - mint where the function is rising, coral where it is falling, warm-neutral exactly at
// a critical point. The same rule paints the function curve, its derivative twin, the tangent
// line, and the area swept while integrating, so the same hue appearing in more than one place at
// the same x is not decoration - it is the same number, shown twice. Colour is decided once, in
// C#, by MathMotionWorld.ColourForSign - the only place that does that maths. This shader carries
// whatever colour the vertex already has; it does not compute a ramp itself.
//
// Geometry is a round tube (the curves, the tangent) or a flat strip (the area fill), rebuilt
// every frame as the curve deforms by ColouredStripMesh. UV convention: u runs 0..1 along the
// path, v runs 0..1 across a tube or from baseline (0) to the curve (1) on a strip - so the
// across-ribbon stroke shaping below reads correctly on both.
//
// Structurally this is Prism/Trail with the age/head-brightening terms removed (a persistent
// curve has no "age" to fade) and the colour ramp replaced by a per-vertex COLOR input.

Shader "Prism/MathMotionCurve"
{
    Properties
    {
        _Opacity    ("Opacity", Range(0,1))                 = 0.85
        _EdgeSoft   ("Edge softness", Range(0.01,1))         = 0.6
        _CoreGain   ("Core brightness", Range(0,2))          = 1.0
        _GroundY    ("Ground plane Y", Float)                = -9999
        _GroundSoft ("Ground soften (m)", Range(0.001,0.5))  = 0.08
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "MathMotionCurve"
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

            float _Opacity, _EdgeSoft, _CoreGain, _GroundY, _GroundSoft;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float4 colour : COLOR;
                float2 uv     : TEXCOORD0;
                float  worldY : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos    = UnityObjectToClipPos(v.vertex);
                o.colour = v.color;
                o.uv     = v.uv;
                o.worldY = mul(unity_ObjectToWorld, v.vertex).y;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Across the ribbon/tube: a soft-edged stroke, brighter along its centreline -
                // identical shaping to Prism/Trail, minus the age fade a persistent curve has no
                // use for.
                float across = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                float stroke = smoothstep(0.0, _EdgeSoft, across);
                float core   = pow(saturate(across), 2.4);

                float3 tint = i.colour.rgb;
                float  dens = (stroke * 0.55 + core * 0.45) * _Opacity * i.colour.a;
                float  spec = core * 0.30 * _CoreGain;

                float3 rgb; float a;
                Prism_Compose(tint, dens, core * 0.2, spec, rgb, a);

                a *= Prism_PlaneFade(i.worldY, _GroundY, _GroundSoft);
                Prism_Deband(rgb, i.pos.xy, _Time.y * 60.0);
                return fixed4(rgb, saturate(a));
            }
            ENDCG
        }
    }

    Fallback Off
}
