// PrismSeed — a concept, as matter.
//
// One shader covers the whole life of an idea. `_Growth` is the learner's actual
// understanding, and it drives geometry rather than just colour:
//
//   growth 0.0  a translucent seed. Smooth, indistinct, and literally incomplete:
//               parts of its surface do not exist yet.
//   growth 0.5  facets begin to resolve; the interior starts carrying light.
//   growth 1.0  a crystalline organism. Hard facets, bright edges, pearlescent film.
//
// There is no "Correct!" state. The object simply becomes coherent.
//
// Two other knowledge states ride on the same surface:
//   _Void        a Question. An iridescent absence that darkens rather than lights.
//   _Instability a Misconception. The facet lattice shivers and never settles, so the
//                structure reads as something that would collapse if leaned on.
//
// Drawn in two passes (back faces, then front) so a translucent solid has real
// interior depth. That ordering matters more than any amount of extra shading.

Shader "Prism/Seed"
{
    Properties
    {
        _Tint        ("Tint", Color)                     = (0.78, 0.749, 0.937, 1)
        _Growth      ("Growth (understanding)", Range(0,1)) = 0.0
        _Instability ("Instability (misconception)", Range(0,1)) = 0.0
        _Void        ("Void (open question)", Range(0,1)) = 0.0
        _Phase       ("Breath phase", Range(0,1))         = 0.0
        _FilmNm      ("Thin film thickness (nm)", Range(120, 900)) = 380
        _Density     ("Optical density", Range(0,2))      = 0.85
        _EdgeGain    ("Facet edge gain", Range(0,40))     = 14
        _HaloPow     ("Halo falloff", Range(1,8))         = 3.0
        _GroundY     ("Ground plane Y", Float)            = -9999
        _GroundSoft  ("Ground soften (m)", Range(0.001,0.5)) = 0.08
        _Hover       ("Reached at", Range(0,1))           = 0
        _Held        ("Held", Range(0,1))                  = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        // ---- pass 1: interior (back faces) -----------------------------------
        Pass
        {
            Name "SeedInterior"
            Cull Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_instancing
            #define PRISM_INTERIOR 1
            #include "UnityCG.cginc"
            #include "Include/PrismOptics.hlsl"
            #include "Include/PrismSeedBody.hlsl"
            ENDCG
        }

        // ---- pass 2: surface (front faces) -----------------------------------
        Pass
        {
            Name "SeedSurface"
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
            #include "Include/PrismSeedBody.hlsl"
            ENDCG
        }
    }

    Fallback Off
}
