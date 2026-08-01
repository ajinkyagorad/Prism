// Shared body for both PrismSeed passes. PRISM_INTERIOR is defined for the back-face
// pass. Include after UnityCG.cginc and PrismOptics.hlsl.

#ifndef PRISM_SEED_BODY_INCLUDED
#define PRISM_SEED_BODY_INCLUDED

float4 _Tint;
float  _Growth;
float  _Instability;
float  _Void;
float  _Phase;
float  _FilmNm;
float  _Density;
float  _EdgeGain;
float  _HaloPow;
float  _GroundY;
float  _GroundSoft;
float  _Hover;      // 0..1 the learner is reaching at this
float  _Held;       // 0..1 the learner has hold of this

struct appdata
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    float4 pos     : SV_POSITION;
    float3 objPos  : TEXCOORD0;
    float3 worldN  : TEXCOORD1;
    float3 worldV  : TEXCOORD2;   // fragment -> eye, unnormalised
    float3 worldPos: TEXCOORD3;
    float  worldY  : TEXCOORD4;
    UNITY_VERTEX_OUTPUT_STEREO
};

v2f vert(appdata v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_OUTPUT(v2f, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    // Breathing. Amplitude falls as the concept stabilises: an unresolved idea is
    // restless, a mastered one is still.
    float breath = Prism_Breathe(_Phase, 0.55) - 0.5;
    float amp    = lerp(0.045, 0.008, saturate(_Growth));
    float3 obj   = v.vertex.xyz * (1.0 + breath * amp);

    float3 world = mul(unity_ObjectToWorld, float4(obj, 1.0)).xyz;

    o.pos    = UnityObjectToClipPos(float4(obj, 1.0));
    o.objPos = v.vertex.xyz;
    o.worldN = UnityObjectToWorldNormal(v.normal);
    o.worldV = _WorldSpaceCameraPos - world;
    o.worldPos = world;
    o.worldY = world.y;
    return o;
}

fixed4 frag(v2f i) : SV_Target
{
    float3 V = normalize(i.worldV);
    float3 nSmooth = normalize(i.worldN);

#if defined(PRISM_INTERIOR)
    nSmooth = -nSmooth;                 // back faces point away from us
#endif

    // Understanding stabilises the geometry. See Prism_Crystallise.
    float3 N = Prism_Crystallise(nSmooth, _Growth, _Instability * 0.35);
    float  ndotv = dot(N, V);

    // Facet edges come free: inside a facet the quantised normal is constant, so its
    // screen-space derivative is ~0, and at a facet boundary it jumps. The crystal's
    // wireframe therefore EMERGES as the concept sharpens, with no extra geometry.
    float edge = saturate(length(fwidth(N)) * _EdgeGain) * smoothstep(0.15, 0.75, _Growth);

    // Thickness through the body: greatest where we look straight through the middle.
    float thick = pow(saturate(abs(ndotv)), 0.7);

    // Pearlescence on the facets. Real interference, so it shifts with viewing angle
    // the way a shell does and not the way a rainbow gradient does.
    float3 film = Prism_ThinFilm(_FilmNm, saturate(abs(ndotv)), 1.42);

    // COLOUR AND STRUCTURE ARE INDEPENDENT CHANNELS.
    //
    // An earlier version desaturated unmastered concepts toward the atrium's warm white,
    // on the theory that an idea has not "earned its colour" until it is understood. That
    // was wrong, and wrong in the worst possible place: on a learner's first run EVERY
    // concept is unmastered, so the entire opening scene was pale white blobs against a
    // warm white sky — unreadable, and the very first thing anyone saw.
    //
    // Hue now means WHICH DOMAIN a concept belongs to, which is knowable from the moment it
    // exists. Mastery drives structure only: facets, edges, interference, completeness.
    // Two orthogonal channels, each carrying exactly one thing.
    float3 tint = _Tint.rgb;
    // Interference only becomes legible once there are facets to carry it.
    tint = lerp(tint, tint * (0.72 + 0.56 * film), saturate(_Growth) * 0.75);

    // A hand nearby wakes the matter: the surface saturates and its dispersion widens, so a
    // concept answers an approach before it is touched.
    float hand = Prism_HandProximity(i.worldPos);
    float attention = saturate(max(hand, max(_Hover, _Held)));
    if (attention > 0.001)
    {
        float3 disp = Prism_Dispersion(0.6 * attention);
        tint *= (1.0 + disp * 0.5);
        tint = lerp(tint, saturate(tint * 1.18), attention);
    }

    float dens = _Density * thick;
    // Held concepts are denser and more present; hovered ones only slightly.
    dens *= 1.0 + _Held * 0.35 + attention * 0.15;
#if defined(PRISM_INTERIOR)
    dens *= 0.45;                       // the far wall reads as depth, not as surface
#endif

    float halo = Prism_Fresnel(ndotv, 0.0, _HaloPow);
    // Hover and grab read as a brightening contact rim rather than an outline object: on a
    // white world a thin bright hairline is the one thing that always reads.
    float spec = edge * 0.55 + pow(halo, 1.6) * attention * 0.9;
#if defined(PRISM_INTERIOR)
    spec *= 0.25;
#endif

    float3 rgb; float a;
    Prism_Compose(tint, dens, halo, spec, rgb, a);

    // A Question is an absence. It does not add light on white — it takes it away, and
    // keeps only a thin iridescent boundary so you can see where the missing thing is.
    if (_Void > 0.001)
    {
        float3 voidRgb = lerp(PRISM_VIOLET * 0.30, film * PRISM_LAVENDER, edge * 0.8 + halo * 0.5);
        rgb = lerp(rgb, voidRgb, _Void);
        a   = lerp(a, saturate(0.30 + halo * 0.55 + edge * 0.6), _Void);
    }

    // Incompleteness: the surface of an unknown idea has holes in it.
    a *= Prism_Completeness(i.objPos * 6.0, _Growth, 1.0);

    // Never a hard cut line where a concept meets the ground.
    a *= Prism_PlaneFade(i.worldY, _GroundY, _GroundSoft);

    a = saturate(a * _Tint.a);
    Prism_Deband(rgb, i.pos.xy, _Time.y * 60.0);
    return fixed4(rgb, a);
}

#endif // PRISM_SEED_BODY_INCLUDED
