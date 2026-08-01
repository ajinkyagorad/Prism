// PrismOptics.hlsl — the shared optical and colour law for every PRISM material.
//
// Built-in RP, targeting Quest 3 (Vulkan, single-pass instanced stereo, 72-90 Hz).
// Everything here is analytic. There is no grab pass, no full-screen post and no
// volumetric buffer: on this hardware those all cost more than they return, and a
// GrabPass additionally breaks single-pass stereo.
//
// -----------------------------------------------------------------------------
// THE ONE LAW THAT SHAPES EVERYTHING ELSE
// -----------------------------------------------------------------------------
// A luminous element must read against whatever is behind it, and PRISM's backdrop
// is not fixed: the atrium is an outdoor landscape with a bright sky and dark ground,
// and a world may be the learner's own room through passthrough. So there is no
// single blending trick that works, and in particular:
//
//   * plain ADDITIVE blending is invisible against a bright sky
//   * darkening-to-read is invisible against dark ground
//
// Every material therefore composes through Prism_Compose(), which blends between
// those two behaviours using _PrismBackdropLuma. Never write a plain additive pass,
// and never assume the background is light or dark.
//
// What survives on ANY backdrop, and so carries the important detail:
//   1. thin, bright specular HAIRLINES  (the only term allowed to exceed 1.0)
//   2. saturation, which no backdrop can wash out
//   3. silhouette and motion

#ifndef PRISM_OPTICS_INCLUDED
#define PRISM_OPTICS_INCLUDED

#define PRISM_TAU 6.28318530718
#define PRISM_PI  3.14159265359

// The three sampled wavelengths, nm. Used for real interference and dispersion,
// so these are physical values, not art-directed ones.
static const float3 PRISM_LAMBDA = float3(610.0, 550.0, 460.0);

// -----------------------------------------------------------------------------
// Palette law
// -----------------------------------------------------------------------------
// Colour encodes meaning; it does not decorate. Every material draws its hue from
// this one ramp so that the same hue always means the same thing across worlds.
// Restrained pastels against warm white: cyan, mint, lavender, coral, gold, violet.
static const float3 PRISM_CYAN     = float3(0.612, 0.878, 0.906);
static const float3 PRISM_MINT     = float3(0.678, 0.925, 0.808);
static const float3 PRISM_LAVENDER = float3(0.780, 0.749, 0.937);
static const float3 PRISM_CORAL    = float3(0.973, 0.706, 0.663);
static const float3 PRISM_GOLD     = float3(0.961, 0.859, 0.639);
static const float3 PRISM_VIOLET   = float3(0.443, 0.361, 0.612); // the one deep note
static const float3 PRISM_WHITE    = float3(0.988, 0.984, 0.973); // warm white, never 1,1,1

// Continuous spectral ramp over t in [0,1]. Piecewise-linear across six stops.
float3 Prism_Spectral(float t)
{
    t = saturate(t) * 5.0;
    float  i = floor(t);
    float  f = t - i;
    float3 a = PRISM_CYAN,  b = PRISM_MINT;
    if (i >= 1.0) { a = PRISM_MINT;     b = PRISM_LAVENDER; }
    if (i >= 2.0) { a = PRISM_LAVENDER; b = PRISM_CORAL;    }
    if (i >= 3.0) { a = PRISM_CORAL;    b = PRISM_GOLD;     }
    if (i >= 4.0) { a = PRISM_GOLD;     b = PRISM_VIOLET;   }
    return lerp(a, b, smoothstep(0.0, 1.0, f));
}

// How bright the world BEHIND a luminous element is. 1 = white void, 0 = night.
// Published by PrismEnvironment; see Prism_Compose.
float _PrismBackdropLuma;

// Compose a luminous element against whatever the backdrop happens to be.
//
// This used to be Prism_OnWhite, and it only knew one trick: saturate and darken the core, which
// is the correct way to read as "luminous" against a white void. The moment the atrium gained a
// sky and a landscape, that trick started working against us — an element that reads by being
// DARKER than its surroundings disappears the instant the surroundings are dark.
//
// So both behaviours exist and are blended by the backdrop:
//
//   bright backdrop  the core saturates and darkens        (stained glass)
//   dark backdrop    the core emits                        (a lit thing in a dim room)
//
// Specular hairlines are added in both cases; they are the one term allowed to exceed 1.0, and
// they are what reads against absolutely any background.
//
//   tint  the element's spectral hue
//   dens  0..1 optical density through the element (core > edge)
//   halo  0..1 wide fringe term
//   spec  specular hairline energy, may exceed 1
void Prism_Compose(float3 tint, float dens, float halo, float spec,
                   out float3 rgb, out float a)
{
    float bright = saturate(_PrismBackdropLuma);
    float d = saturate(dens);

    float3 onBright = tint * lerp(1.0, 0.62, d);
    onBright = lerp(onBright, tint * tint, d * 0.45);        // gamma-ish saturation push

    float3 onDark = tint * (0.55 + 1.35 * d);

    rgb = lerp(onDark, onBright, bright) + spec;

    // A translucent thing against a dark backdrop needs more alpha to register at all.
    a = saturate(d * lerp(1.10, 0.88, bright) + halo * 0.16);
}

// -----------------------------------------------------------------------------
// The learner's hands, available to every material
// -----------------------------------------------------------------------------
// Published once per frame by PrismHands via Shader.SetGlobalVector. Any material can
// therefore respond to a hand approaching WITHOUT the interaction layer having to know
// which materials exist or hold references to them.
//
// This is what makes "the concept itself is the interface" affordable: proximity feedback
// is a property of the matter, not a highlight object switched on by a controller script.
//
//   xyz = world position, w = 1 when tracked, 0 when not (so an untracked hand
//   contributes nothing rather than lighting up the world origin)
float4 _PrismHandL;
float4 _PrismHandR;
float  _PrismHandRange;      // metres over which a hand is felt

// 0 at range, 1 at the hand. Quadratic falloff so the influence is local and does not
// wash the whole scene.
float Prism_HandProximity(float3 worldPos)
{
    float range = max(_PrismHandRange, 1e-3);
    float dl = distance(worldPos, _PrismHandL.xyz);
    float dr = distance(worldPos, _PrismHandR.xyz);
    float l = saturate(1.0 - dl / range) * _PrismHandL.w;
    float r = saturate(1.0 - dr / range) * _PrismHandR.w;
    float p = max(l, r);
    return p * p;
}

// -----------------------------------------------------------------------------
// Fresnel / thin film / dispersion
// -----------------------------------------------------------------------------
float Prism_Fresnel(float ndotv, float f0, float power)
{
    return f0 + (1.0 - f0) * pow(saturate(1.0 - abs(ndotv)), power);
}

// Real two-beam thin-film interference. thicknessNm is the physical film thickness,
// cosI the cosine of the incidence angle, ior the film index.
// This is what makes a surface read as pearlescent rather than rainbow-tinted:
// a monotonic Fresnel reads as plastic, interference reads as shell.
float3 Prism_ThinFilm(float thicknessNm, float cosI, float ior)
{
    float sinI2 = saturate(1.0 - cosI * cosI);
    float sinT2 = sinI2 / max(ior * ior, 1e-4);
    float cosT  = sqrt(saturate(1.0 - sinT2));

    float  opd   = 2.0 * ior * thicknessNm * cosT;              // optical path difference, nm
    float3 phase = (PRISM_TAU * opd) / PRISM_LAMBDA + PRISM_PI; // external reflection adds pi
    return 0.5 + 0.5 * cos(phase);
}

// Chromatic dispersion: the three wavelengths refract by slightly different amounts.
// Returns per-channel scalar offsets to apply along a refraction direction.
float3 Prism_Dispersion(float strength)
{
    // Normalised inverse wavelength, so blue bends most. Cauchy would be more exact;
    // the linear term is indistinguishable at these thicknesses and costs two ops.
    float3 inv = (550.0 / PRISM_LAMBDA - 1.0);
    return inv * strength;
}

// -----------------------------------------------------------------------------
// Dither / debanding
// -----------------------------------------------------------------------------
// Interleaved gradient noise. Pastel gradients across large soft volumes band very
// visibly on the Quest 3 panel; every PRISM surface must dither before output.
float Prism_IGN(float2 pix, float frame)
{
    pix += 5.588238 * frame;
    return frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715))));
}

void Prism_Deband(inout float3 rgb, float2 pix, float frame)
{
    rgb += (Prism_IGN(pix, frame) - 0.5) * (1.0 / 255.0) * 1.6;
}

// -----------------------------------------------------------------------------
// Noise (for internal currents and cloud matter)
// -----------------------------------------------------------------------------
float Prism_Hash13(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float Prism_Value3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(Prism_Hash13(i + float3(0, 0, 0)), Prism_Hash13(i + float3(1, 0, 0)), f.x),
                     lerp(Prism_Hash13(i + float3(0, 1, 0)), Prism_Hash13(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(Prism_Hash13(i + float3(0, 0, 1)), Prism_Hash13(i + float3(1, 0, 1)), f.x),
                     lerp(Prism_Hash13(i + float3(0, 1, 1)), Prism_Hash13(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// Two octaves is the budget. A third is not visible through translucent matter.
float Prism_FBM2(float3 p)
{
    return Prism_Value3D(p) * 0.65 + Prism_Value3D(p * 2.17 + 11.3) * 0.35;
}

// -----------------------------------------------------------------------------
// Life
// -----------------------------------------------------------------------------
// "Structures breathe subtly." A shared breath so nothing in a scene is ever
// perfectly static, and a per-object phase so they never breathe in lockstep.
float Prism_Breathe(float phase, float rate)
{
    return sin(_Time.y * rate + phase * PRISM_TAU) * 0.5 + 0.5;
}

// -----------------------------------------------------------------------------
// Soft intersections
// -----------------------------------------------------------------------------
// Analytic fade against a known plane (the table, the atrium ground). Free, and it
// covers almost every case where geometry would otherwise show a hard cut line.
//   planeY   - world height of the surface
//   softness - metres over which to fade
float Prism_PlaneFade(float worldY, float planeY, float softness)
{
    return saturate((worldY - planeY) / max(softness, 1e-4));
}

// Depth-buffer fade, for intersections with geometry we do not know about
// (passthrough room mesh, other learners' constructions). Costs a depth texture:
// enable PRISM_DEPTHFADE and set Camera.depthTextureMode only where it earns it.
#if defined(PRISM_DEPTHFADE)
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
float Prism_DepthFade(float4 screenPos, float eyeDepth, float softness)
{
    float2 uv = screenPos.xy / max(screenPos.w, 1e-4);
    float  raw = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,
                                          UNITY_PROJ_COORD(screenPos));
    float  scene = LinearEyeDepth(raw);
    return saturate((scene - eyeDepth) / max(softness, 1e-4));
}
#endif

// -----------------------------------------------------------------------------
// Crystallisation
// -----------------------------------------------------------------------------
// The single most important function in PRISM's visual grammar.
//
// A concept that is not understood is smooth, indistinct and incomplete. A concept
// that IS understood has stable, faceted geometry. Rather than swapping meshes, we
// quantise the normal: every normal in a neighbourhood snaps to the same direction,
// which produces genuinely flat facets from one smooth sphere. Understanding
// literally stabilises the geometry, in one lerp, at no mesh cost.
//
//   growth 0 -> smooth, soft, unresolved
//   growth 1 -> hard crystalline facets
float3 Prism_Crystallise(float3 n, float growth, float jitter)
{
    // Quantise in a rotated basis; snapping the raw normal gives cube facets, which
    // read as low-poly rather than as crystal. R is orthonormal, so its transpose is
    // its inverse and is hardcoded rather than computed.
    const float3x3 R  = float3x3( 0.8047,  0.3106, -0.5058,
                                 -0.5058,  0.8047, -0.3106,
                                  0.3106,  0.5058,  0.8047);
    const float3x3 RT = float3x3( 0.8047, -0.5058,  0.3106,
                                  0.3106,  0.8047,  0.5058,
                                 -0.5058, -0.3106,  0.8047);
    float3 nr = mul(R, n);

    // Facet count rises with understanding: few broad faces first, finer structure later.
    // floor(x+0.5) rather than round() — round() is not dependable at #pragma target 3.0.
    float k = lerp(1.15, 5.5, saturate(growth));
    float3 q = floor(nr * k + 0.5);
    // A normal whose components all round to zero would collapse; bias it back out.
    q += (dot(abs(q), float3(1, 1, 1)) < 0.5) ? sign(nr + 1e-6) : float3(0, 0, 0);

    // Misconceptions are unstable structures that collapse under testing: the lattice
    // itself shivers, so the facets never settle.
    q += jitter * (float3(Prism_Hash13(q + floor(_Time.y * 11.0)),
                          Prism_Hash13(q + floor(_Time.y * 13.0) + 7.7),
                          Prism_Hash13(q + floor(_Time.y * 17.0) + 3.3)) - 0.5);

    float3 facet = normalize(mul(RT, q));
    return normalize(lerp(n, facet, saturate(growth)));
}

// Incompleteness: an unresolved concept is literally missing parts of itself.
// Returns a 0..1 coverage mask that closes as growth rises. Used to modulate alpha
// rather than to clip() — discard breaks early-z and costs more than it saves, and a
// soft-edged hole reads as "not yet resolved" better than a hard-edged one does.
float Prism_Completeness(float3 objPos, float growth, float scale)
{
    float n = Prism_FBM2(objPos * scale);
    // At growth 0 roughly a third of the surface is absent; at 1 it is whole.
    return smoothstep(0.0, 0.30, n - (1.0 - saturate(growth)) * 0.42);
}

#endif // PRISM_OPTICS_INCLUDED
