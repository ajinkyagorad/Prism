// PrismCeramic — frosted luminous ceramic.
//
// The default solid of the PRISM world: planets, instrument bodies, the atrium's
// suggested ground. Soft, warm, self-luminous, with pastel rather than grey shading.
//
// It carries its own two-light studio rig in world space instead of reading scene
// lights. That is deliberate: every scene here is generated from code and cannot be
// inspected before it ships, so a material whose look depends on correctly configured
// scene lighting is a material that will one day render flat grey on a headset with
// nobody around to notice. This rig is identical in the editor, in the simulator and
// on device.

Shader "Prism/Ceramic"
{
    Properties
    {
        _Tint       ("Tint", Color)                    = (0.988, 0.984, 0.973, 1)
        _Luminance  ("Self luminance", Range(0,1))     = 0.35
        _Wrap       ("Diffuse wrap", Range(0,1))       = 0.60
        _Subsurface ("Subsurface", Range(0,2))         = 0.75
        _SpecPower  ("Specular tightness", Range(8,256)) = 90
        _SpecGain   ("Specular gain", Range(0,2))      = 0.55
        _RimGain    ("Rim gain", Range(0,2))           = 0.45
        _FilmNm     ("Thin film thickness (nm)", Range(0, 900)) = 0
        _Phase      ("Breath phase", Range(0,1))       = 0.0
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            Name "Ceramic"
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Include/PrismOptics.hlsl"

            float4 _Tint;
            float  _Luminance, _Wrap, _Subsurface, _SpecPower, _SpecGain, _RimGain, _FilmNm, _Phase;

            // The rig. Key from upper right, cool fill from lower left, warm ambient.
            static const float3 PRISM_KEY_DIR  = float3( 0.3363,  0.7495, -0.4997);
            static const float3 PRISM_FILL_DIR = float3(-0.7332,  0.3055,  0.6073);

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float3 worldN : TEXCOORD0;
                float3 worldV : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float breath = (Prism_Breathe(_Phase, 0.4) - 0.5) * 0.006;
                float3 obj   = v.vertex.xyz * (1.0 + breath);
                float3 world = mul(unity_ObjectToWorld, float4(obj, 1.0)).xyz;

                o.pos    = UnityObjectToClipPos(float4(obj, 1.0));
                o.worldN = UnityObjectToWorldNormal(v.normal);
                o.worldV = _WorldSpaceCameraPos - world;
                return o;
            }

            // Wrapped diffuse: the terminator softens and wraps past 90 degrees, which is
            // what separates ceramic from painted plastic.
            float WrapLambert(float ndotl, float w)
            {
                return saturate((ndotl + w) / (1.0 + w));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldN);
                float3 V = normalize(i.worldV);
                float  ndotv = saturate(dot(N, V));

                float key  = WrapLambert(dot(N, PRISM_KEY_DIR),  _Wrap);
                float fill = WrapLambert(dot(N, PRISM_FILL_DIR), _Wrap);

                // Shadows are pastel, never grey: the key side warms toward gold and the
                // shadow side cools toward lavender.
                float3 lit = _Tint.rgb * (0.16
                           + key  * 0.72 * lerp(PRISM_WHITE, PRISM_GOLD, 0.35)
                           + fill * 0.34 * lerp(PRISM_WHITE, PRISM_LAVENDER, 0.55));

                // Back-scatter through a thin shell: light that entered elsewhere and left here.
                float sss = pow(saturate(dot(V, -PRISM_KEY_DIR)), 3.0) * (1.0 - ndotv);
                lit += _Tint.rgb * PRISM_CORAL * sss * _Subsurface * 0.5;

                // Self luminance is what makes the material read as lit-from-within in a
                // world that has no dark side.
                lit += _Tint.rgb * _Luminance * 0.5;

                // Thin, tight highlights. On a white world these hairlines are the only
                // thing allowed to exceed 1.0, and they are what sells curved glass.
                float3 H = normalize(PRISM_KEY_DIR + V);
                float  spec = pow(saturate(dot(N, H)), _SpecPower) * _SpecGain;

                // Optional pearlescence for biological or shell-like surfaces.
                if (_FilmNm > 1.0)
                {
                    float3 film = Prism_ThinFilm(_FilmNm, ndotv, 1.38);
                    lit *= (0.80 + 0.40 * film);
                }

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
