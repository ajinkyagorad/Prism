// PrismAtmosphere.hlsl — the sky, and everything that has to agree with the sky.
//
// Shared by PrismSky, PrismGround and any material that needs aerial perspective. Having one
// function own the sky colour is what stops distant terrain and the horizon disagreeing about
// what colour the air is, which is the single most common way a procedural outdoor scene
// stops reading as a real place.
//
// The scattering is real, not a gradient. Rayleigh coefficients are the physical ones for air
// at sea level, per metre:
//
//     beta_R = (5.8, 13.5, 33.1) e-6      proportional to 1/lambda^4
//
// which is *why* the sky is blue and the sun reddens at the horizon — both fall out of the
// wavelength dependence rather than being art-directed in. Mie scattering is wavelength
// neutral and forward-biased, and is what produces haze and the glow around the sun.
//
// Optical depth uses Kasten-Young air mass rather than a ray march: accurate to well under a
// percent down to the horizon, and about forty instructions cheaper.

#ifndef PRISM_ATMOSPHERE_INCLUDED
#define PRISM_ATMOSPHERE_INCLUDED

// Published by PrismEnvironment once per frame.
float4 _PrismSunDir;        // xyz = direction TO the sun, w = intensity
float4 _PrismSunColour;
float  _PrismTurbidity;     // 1 clear, 6 hazy
float  _PrismGroundLuma;    // how bright the ground bounce is

static const float3 PRISM_BETA_R = float3(5.8e-6, 13.5e-6, 33.1e-6);   // 1/m, Rayleigh
static const float  PRISM_BETA_M = 21.0e-6;                            // 1/m, Mie
static const float  PRISM_SCALE_R = 8000.0;                            // m, Rayleigh scale height
static const float  PRISM_SCALE_M = 1200.0;                            // m, Mie scale height

// Kasten-Young relative air mass. cosZ is the cosine of the zenith angle.
float Prism_AirMass(float cosZ)
{
    float z = degrees(acos(clamp(cosZ, -1.0, 1.0)));
    return 1.0 / (max(cosZ, 0.0) + 0.50572 * pow(max(96.07995 - z, 1e-3), -1.6364));
}

float Prism_RayleighPhase(float cosT)
{
    return (3.0 / (16.0 * PRISM_PI)) * (1.0 + cosT * cosT);
}

// Henyey-Greenstein. g = 0.76 is the standard fit for atmospheric aerosol.
float Prism_MiePhase(float cosT, float g)
{
    float g2 = g * g;
    float d = 1.0 + g2 - 2.0 * g * cosT;
    return (1.0 - g2) / (4.0 * PRISM_PI * pow(max(d, 1e-4), 1.5));
}

/// <summary>Sky radiance looking along a unit direction.</summary>
float3 Prism_SkyColour(float3 dir)
{
    float3 sun = normalize(_PrismSunDir.xyz);
    float cosT = dot(dir, sun);

    // Air mass along the view ray and along the sun ray.
    float amView = Prism_AirMass(dir.y);
    float amSun  = Prism_AirMass(sun.y);

    float turb = max(_PrismTurbidity, 1.0);
    float3 tauR = PRISM_BETA_R * PRISM_SCALE_R * amView;
    float  tauM = PRISM_BETA_M * PRISM_SCALE_M * amView * turb;

    // Transmittance of the sunlight before it scatters toward us.
    float3 sunTau = PRISM_BETA_R * PRISM_SCALE_R * amSun
                  + PRISM_BETA_M * PRISM_SCALE_M * amSun * turb;
    float3 sunTrans = exp(-sunTau);

    float3 scatterR = PRISM_BETA_R * PRISM_SCALE_R * Prism_RayleighPhase(cosT);
    float  scatterM = PRISM_BETA_M * PRISM_SCALE_M * Prism_MiePhase(cosT, 0.76) * turb;

    float3 total = scatterR + scatterM;
    float3 extinct = tauR + tauM;

    // Single-scattering with the standard (1 - e^-tau)/tau normalisation.
    float3 inscatter = total * (1.0 - exp(-extinct)) / max(extinct, 1e-6);
    float3 sky = inscatter * sunTrans * _PrismSunColour.rgb * _PrismSunDir.w;

    // Below the horizon the sky is replaced by ground bounce, blended softly so the horizon
    // line stays soft rather than becoming a hard seam.
    float belowness = saturate(-dir.y * 6.0);
    float3 ground = sky * 0.55 + float3(0.09, 0.085, 0.075) * _PrismGroundLuma;
    sky = lerp(sky, ground, belowness);

    // A little ambient so a night-ish sun never crushes to pure black.
    return sky + float3(0.012, 0.016, 0.024);
}

/// <summary>The sun's own disc, with limb darkening. Add to the sky, sky only.</summary>
float3 Prism_SunDisc(float3 dir)
{
    float3 sun = normalize(_PrismSunDir.xyz);
    float cosT = dot(dir, sun);

    // ~0.53 degrees of angular diameter, softened so it does not alias into a square.
    const float cosInner = 0.99996;
    const float cosOuter = 0.9997;
    float d = smoothstep(cosOuter, cosInner, cosT);
    if (d <= 0.0) return 0.0;

    // Limb darkening: the disc is dimmer at its edge.
    float r = saturate((1.0 - cosT) / (1.0 - cosInner));
    float limb = 0.6 + 0.4 * sqrt(saturate(1.0 - r * r));

    float3 trans = exp(-(PRISM_BETA_R * PRISM_SCALE_R + PRISM_BETA_M * PRISM_SCALE_M)
                       * Prism_AirMass(sun.y));
    return d * limb * trans * _PrismSunColour.rgb * _PrismSunDir.w * 12.0;
}

/// <summary>
/// Aerial perspective: fade a surface colour into the air between it and the eye. This is what
/// makes distance readable, and it is why the far hills sit behind the near ones instead of
/// looking like a painted backdrop.
///   dist  metres from eye to the surface
///   dir   unit direction from eye to the surface
/// </summary>
float3 Prism_Aerial(float3 colour, float dist, float3 dir)
{
    float turb = max(_PrismTurbidity, 1.0);
    float3 beta = PRISM_BETA_R * 55.0 + PRISM_BETA_M * 40.0 * turb;   // near-ground densities
    float3 trans = exp(-beta * dist);
    float3 air = Prism_SkyColour(dir);
    return colour * trans + air * (1.0 - trans);
}

/// <summary>
/// Ground mist: a shallow layer that thickens with distance and toward the ground plane, which
/// separates the plateau the learner stands on from the valley beyond it.
/// </summary>
float Prism_Mist(float worldY, float dist, float mistTop, float density)
{
    float height = saturate(1.0 - (worldY - (-2.0)) / max(mistTop, 0.01));
    float d = 1.0 - exp(-dist * density * (0.25 + 0.75 * height));
    return saturate(d * height);
}

#endif // PRISM_ATMOSPHERE_INCLUDED
