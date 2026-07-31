// PrismVolumetric — cloud-like matter.
//
// For probability clouds, atmospheres, and the soft bodies of not-yet-approached
// knowledge. Rendered on a sphere: the chord length through the sphere along the view
// ray is solved analytically rather than marched, which costs three noise taps total
// and still gives correct silhouette softening and a genuine thickness gradient from
// limb to centre.
//
// This is the honest cheap answer to volumetrics on a mobile GPU. It cannot self-shadow
// or hold sharp internal structure — if a world needs that, it should use Prism/Gel and
// pay for the march.

Shader "Prism/Volumetric"
{
    Properties
    {
        _Tint      ("Tint", Color)                   = (0.780, 0.749, 0.937, 1)
        _EdgeTint  ("Limb tint", Color)              = (0.973, 0.706, 0.663, 1)
        _Density   ("Density", Range(0,4))           = 0.9
        _NoiseFreq ("Structure scale", Range(0.5,10)) = 2.4
        _Churn     ("Churn speed", Range(0,1))       = 0.12
        _Softness  ("Limb softness", Range(0.1,3))   = 1.3
        _Phase     ("Breath phase", Range(0,1))      = 0.0
        _GroundY   ("Ground plane Y", Float)         = -9999
        _GroundSoft("Ground soften (m)", Range(0.001,0.5)) = 0.1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Volumetric"
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

            float4 _Tint, _EdgeTint;
            float  _Density, _NoiseFreq, _Churn, _Softness, _Phase, _GroundY, _GroundSoft;

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
                float3 objDir : TEXCOORD1;
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

                float breath = (Prism_Breathe(_Phase, 0.22) - 0.5) * 0.03;
                float3 obj   = v.vertex.xyz * (1.0 + breath);
                float3 objCam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                float3 world  = mul(unity_ObjectToWorld, float4(obj, 1.0)).xyz;

                o.pos    = UnityObjectToClipPos(float4(obj, 1.0));
                o.objPos = obj;
                o.objDir = obj - objCam;
                o.worldN = UnityObjectToWorldNormal(v.normal);
                o.worldV = _WorldSpaceCameraPos - world;
                o.worldY = world.y;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 rd = normalize(i.objDir);

                // Analytic chord through the unit sphere: b is the perpendicular distance
                // from the centre to the view line, so the remaining half-chord is
                // sqrt(1 - b^2) and the full chord is twice that.
                float3 perp = i.objPos - rd * dot(i.objPos, rd);
                float  b2   = saturate(dot(perp, perp));
                float  chord = 2.0 * sqrt(max(1.0 - b2, 0.0));

                // Three taps along the chord give the cloud some interior structure without
                // a loop: entry, middle, exit.
                float t = _Time.y * _Churn;
                float3 mid = i.objPos + rd * (chord * 0.5);
                float n = Prism_FBM2(i.objPos * _NoiseFreq + t)        * 0.30
                        + Prism_FBM2(mid      * _NoiseFreq + t * 1.3)  * 0.45
                        + Prism_Value3D((mid + rd * chord * 0.4) * _NoiseFreq * 1.7 - t) * 0.25;

                float dens = saturate(1.0 - exp(-chord * n * _Density * _Softness));

                // Limb tinting: light that has travelled the least picks up the warm edge
                // colour, the deep centre keeps the body colour.
                float3 tint = lerp(_EdgeTint.rgb, _Tint.rgb, saturate(chord * 0.55));

                float3 N = normalize(i.worldN);
                float3 V = normalize(i.worldV);
                float  halo = Prism_Fresnel(saturate(dot(N, V)), 0.0, 2.0);

                float3 rgb; float a;
                Prism_Compose(tint, dens, halo * 0.6, 0.0, rgb, a);

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
