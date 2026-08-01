using Prism.Core;
using UnityEngine;

namespace Prism.Worlds
{
    /// <summary>
    /// Where a Concept World takes you.
    ///
    /// The Atrium is a highland at dawn — that is home, and it stays. But putting all eleven worlds
    /// on that same plateau was the single biggest reason the renders were dull: the phenomena were
    /// different every time and the ROOM never was, so entering a world felt like rearranging the
    /// table rather than going anywhere.
    ///
    /// A spec is plain data driving one parameterised shader (<c>Prism/WorldShell</c>). A world
    /// picks a preset and adjusts it; the base class does the rest. The point is that eleven worlds
    /// authored by eleven different hands land in eleven distinguishable places without any of them
    /// writing a shader.
    /// </summary>
    public struct WorldShellSpec
    {
        public Color Zenith, Nadir, Horizon;
        public float HorizonAmount;

        public float Stars, StarScale;

        public float Nebula, NebulaScale;
        public Color NebulaTint;

        public float Structure, StructureScale;
        public Color StructureTint;

        public float Drift;

        /// <summary>Radius of the enclosing sphere, metres. Far enough to read as sky, near enough to feel enclosing.</summary>
        public float Radius;

        /// <summary>Ambient light this place casts on the world's own geometry.</summary>
        public Color Ambient;

        // -----------------------------------------------------------------
        // presets
        //
        // Each is a real place, not a colour scheme. The numbers were chosen by rendering them.
        // -----------------------------------------------------------------

        /// <summary>Neutral fallback: a dim lit volume. Any world that overrides nothing still gets a room.</summary>
        public static WorldShellSpec Studio => new WorldShellSpec
        {
            Zenith = new Color(0.075f, 0.082f, 0.105f),
            Nadir = new Color(0.020f, 0.021f, 0.028f),
            Horizon = new Color(0.13f, 0.13f, 0.16f),
            HorizonAmount = 0.55f,
            Nebula = 0.10f, NebulaTint = new Color(0.22f, 0.24f, 0.34f), NebulaScale = 1.2f,
            Drift = 0.010f, Radius = 9f, StarScale = 46f,
            Ambient = new Color(0.16f, 0.17f, 0.21f),
        };

        /// <summary>Deep space, off the plane of the galaxy. Stars carry it; the nebula is a whisper.</summary>
        public static WorldShellSpec DeepSpace => new WorldShellSpec
        {
            Zenith = new Color(0.012f, 0.016f, 0.032f),
            Nadir = new Color(0.005f, 0.006f, 0.014f),
            Horizon = new Color(0.03f, 0.04f, 0.08f),
            HorizonAmount = 0.20f,
            Stars = 0.62f, StarScale = 40f,
            // Rendered at 0.55 this read as a flat purple sky with stars on it. A real dark-sky
            // field is mostly black; the nebula has to be a structure you notice second, not the
            // ground colour.
            Nebula = 0.20f, NebulaTint = new Color(0.32f, 0.17f, 0.50f), NebulaScale = 2.1f,
            Drift = 0.006f, Radius = 14f,
            Ambient = new Color(0.10f, 0.11f, 0.16f),
        };

        /// <summary>Inside a living thing: warm, crowded, wet. Heavy structure, no stars, slow drift.</summary>
        public static WorldShellSpec Cytoplasm => new WorldShellSpec
        {
            Zenith = new Color(0.115f, 0.055f, 0.070f),
            Nadir = new Color(0.045f, 0.020f, 0.030f),
            Horizon = new Color(0.20f, 0.09f, 0.10f),
            HorizonAmount = 0.70f,
            Nebula = 0.45f, NebulaTint = new Color(0.34f, 0.13f, 0.16f), NebulaScale = 2.2f,
            Structure = 0.60f, StructureTint = new Color(0.55f, 0.27f, 0.29f), StructureScale = 9.0f,
            Drift = 0.030f, Radius = 8f, StarScale = 46f,
            Ambient = new Color(0.22f, 0.13f, 0.14f),
        };

        /// <summary>A vacuum that is not empty. Near-black, with slow luminous filament.</summary>
        public static WorldShellSpec QuantumVacuum => new WorldShellSpec
        {
            Zenith = new Color(0.020f, 0.030f, 0.055f),
            Nadir = new Color(0.006f, 0.010f, 0.022f),
            Horizon = new Color(0.05f, 0.09f, 0.14f),
            HorizonAmount = 0.35f,
            Stars = 0.16f, StarScale = 64f,
            Nebula = 0.40f, NebulaTint = new Color(0.10f, 0.34f, 0.42f), NebulaScale = 1.1f,
            Structure = 0.30f, StructureTint = new Color(0.16f, 0.44f, 0.52f), StructureScale = 5.5f,
            Drift = 0.018f, Radius = 11f,
            Ambient = new Color(0.11f, 0.16f, 0.20f),
        };

        /// <summary>Night over a lit grid: dark above, cold light below, circuitry in the walls.</summary>
        public static WorldShellSpec Datascape => new WorldShellSpec
        {
            Zenith = new Color(0.014f, 0.020f, 0.036f),
            Nadir = new Color(0.030f, 0.055f, 0.075f),
            Horizon = new Color(0.06f, 0.16f, 0.21f),
            HorizonAmount = 0.85f,
            // At scale 7.5 the ridged noise read as a flooded cave, not a city. Pushing the
            // frequency up until the filaments are thinner than the gaps between them is what turns
            // the same function from "organic" into "built".
            Structure = 0.45f, StructureTint = new Color(0.16f, 0.52f, 0.62f), StructureScale = 22f,
            Nebula = 0.10f, NebulaTint = new Color(0.08f, 0.16f, 0.26f), NebulaScale = 2.0f,
            Stars = 0.20f, StarScale = 70f,
            Drift = 0.026f, Radius = 10f,
            Ambient = new Color(0.12f, 0.18f, 0.22f),
        };

        /// <summary>High atmosphere: a planet's air seen from just outside it.</summary>
        public static WorldShellSpec Stratosphere => new WorldShellSpec
        {
            Zenith = new Color(0.020f, 0.038f, 0.090f),
            Nadir = new Color(0.075f, 0.090f, 0.120f),
            Horizon = new Color(0.32f, 0.30f, 0.34f),
            HorizonAmount = 1.05f,
            Stars = 0.30f, StarScale = 44f,
            Nebula = 0.20f, NebulaTint = new Color(0.24f, 0.26f, 0.34f), NebulaScale = 2.6f,
            Drift = 0.022f, Radius = 13f,
            Ambient = new Color(0.19f, 0.20f, 0.24f),
        };

        /// <summary>Deep time: a warm dim basin, dust in the light, geology in the walls.</summary>
        public static WorldShellSpec DeepTime => new WorldShellSpec
        {
            Zenith = new Color(0.060f, 0.055f, 0.048f),
            Nadir = new Color(0.026f, 0.022f, 0.018f),
            Horizon = new Color(0.19f, 0.14f, 0.09f),
            HorizonAmount = 0.90f,
            // Strata, not soup: a low frequency across the sphere with a much higher one on top
            // gives beds and partings instead of one undifferentiated brown.
            Structure = 0.55f, StructureTint = new Color(0.42f, 0.29f, 0.17f), StructureScale = 14f,
            Nebula = 0.16f, NebulaTint = new Color(0.20f, 0.15f, 0.10f), NebulaScale = 1.1f,
            Drift = 0.004f, Radius = 12f, StarScale = 46f,
            Ambient = new Color(0.20f, 0.17f, 0.13f),
        };

        /// <summary>A resonant hall: dark, with standing patterns in the air.</summary>
        public static WorldShellSpec Resonator => new WorldShellSpec
        {
            Zenith = new Color(0.040f, 0.026f, 0.062f),
            Nadir = new Color(0.016f, 0.010f, 0.026f),
            Horizon = new Color(0.14f, 0.07f, 0.19f),
            HorizonAmount = 0.65f,
            Structure = 0.50f, StructureTint = new Color(0.42f, 0.20f, 0.50f), StructureScale = 6.5f,
            Nebula = 0.26f, NebulaTint = new Color(0.20f, 0.10f, 0.30f), NebulaScale = 1.4f,
            Drift = 0.045f, Radius = 10f, StarScale = 46f,
            Ambient = new Color(0.16f, 0.12f, 0.20f),
        };

        /// <summary>A workshop at night: warm lamplight, stone and iron, no sky.</summary>
        public static WorldShellSpec Workshop => new WorldShellSpec
        {
            Zenith = new Color(0.038f, 0.032f, 0.028f),
            Nadir = new Color(0.055f, 0.042f, 0.030f),
            Horizon = new Color(0.24f, 0.16f, 0.09f),
            HorizonAmount = 0.95f,
            Structure = 0.34f, StructureTint = new Color(0.30f, 0.20f, 0.12f), StructureScale = 4.2f,
            Drift = 0.006f, Radius = 9f, StarScale = 46f,
            Ambient = new Color(0.22f, 0.18f, 0.14f),
        };

        /// <summary>Clean abstract space for mathematics: cool, quiet, a faint lattice.</summary>
        public static WorldShellSpec Manifold => new WorldShellSpec
        {
            Zenith = new Color(0.045f, 0.055f, 0.080f),
            Nadir = new Color(0.018f, 0.022f, 0.034f),
            Horizon = new Color(0.10f, 0.13f, 0.19f),
            HorizonAmount = 0.60f,
            Structure = 0.26f, StructureTint = new Color(0.26f, 0.34f, 0.46f), StructureScale = 9.0f,
            Nebula = 0.10f, NebulaTint = new Color(0.16f, 0.20f, 0.30f), NebulaScale = 1.8f,
            Drift = 0.012f, Radius = 11f, StarScale = 46f,
            Ambient = new Color(0.17f, 0.19f, 0.24f),
        };

        /// <summary>Firelight and open country at dusk — the human scale.</summary>
        public static WorldShellSpec Hearth => new WorldShellSpec
        {
            Zenith = new Color(0.045f, 0.048f, 0.075f),
            Nadir = new Color(0.048f, 0.032f, 0.024f),
            Horizon = new Color(0.30f, 0.16f, 0.08f),
            HorizonAmount = 1.10f,
            Stars = 0.34f, StarScale = 42f,
            Nebula = 0.14f, NebulaTint = new Color(0.20f, 0.14f, 0.12f), NebulaScale = 2.2f,
            Drift = 0.014f, Radius = 13f,
            Ambient = new Color(0.20f, 0.16f, 0.14f),
        };

        /// <summary>Inside a body: dark, warm, pulsing red-violet, dense tissue.</summary>
        public static WorldShellSpec Interior => new WorldShellSpec
        {
            Zenith = new Color(0.090f, 0.030f, 0.048f),
            Nadir = new Color(0.038f, 0.012f, 0.022f),
            Horizon = new Color(0.22f, 0.06f, 0.10f),
            HorizonAmount = 0.80f,
            Nebula = 0.42f, NebulaTint = new Color(0.36f, 0.09f, 0.18f), NebulaScale = 1.9f,
            Structure = 0.55f, StructureTint = new Color(0.52f, 0.17f, 0.27f), StructureScale = 11f,
            Drift = 0.040f, Radius = 8f, StarScale = 46f,
            Ambient = new Color(0.22f, 0.11f, 0.14f),
        };

        /// <summary>Push every emissive term by <paramref name="k"/>. Used to swell a place on a discovery.</summary>
        public WorldShellSpec Brightened(float k)
        {
            var s = this;
            s.Zenith *= k; s.Nadir *= k; s.Horizon *= k;
            s.Nebula *= k; s.Structure *= k;
            s.Ambient *= k;
            return s;
        }

        public void Apply(Material m)
        {
            m.SetColor("_Zenith", Zenith);
            m.SetColor("_Nadir", Nadir);
            m.SetColor("_Horizon", Horizon);
            m.SetFloat("_HorizonAmt", HorizonAmount);
            m.SetFloat("_Stars", Stars);
            m.SetFloat("_StarScale", StarScale <= 0f ? 46f : StarScale);
            m.SetFloat("_NebulaAmt", Nebula);
            m.SetColor("_NebulaTint", NebulaTint);
            m.SetFloat("_NebulaFreq", NebulaScale <= 0f ? 1.7f : NebulaScale);
            m.SetFloat("_Structure", Structure);
            m.SetColor("_StructTint", StructureTint);
            m.SetFloat("_StructFreq", StructureScale <= 0f ? 3.1f : StructureScale);
            m.SetFloat("_Drift", Drift);
        }
    }

    /// <summary>
    /// Builds and swaps world shells.
    ///
    /// Static because two unrelated kinds of world need it: every <see cref="PrismWorldBase"/>
    /// subclass, and <c>OrbitalWorld</c>, which predates that base class and is still wired
    /// directly into the session. Duplicating the logic would have guaranteed they drifted.
    /// </summary>
    public static class WorldShell
    {
        static Scenery.PrismEnvironment _scenery;
        static float _atriumBackdrop = -1f;

        /// <summary>An inside-out sphere carrying <paramref name="spec"/>, parented to <paramref name="parent"/>.</summary>
        public static GameObject Build(Transform parent, WorldShellSpec spec, float yOffset = 0f)
        {
            var mat = Aesthetic.PrismMaterials.New(Aesthetic.PrismMaterials.WorldShell);
            spec.Apply(mat);

            var go = new GameObject("WorldShell");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Aesthetic.PrismMesh.Icosphere(3);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.transform.localScale = Vector3.one * (spec.Radius <= 0f ? 9f : spec.Radius);
            // Scenery, never an obstacle: no collider, and it never blocks a reach.
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);
            return go;
        }

        /// <summary>
        /// Hide the Atrium's highland for the duration of a world.
        ///
        /// You cannot be in deep space and on a plateau at the same time, and trying to be both was
        /// what made every world render look like the same field with different props on it.
        /// </summary>
        public static void TakeOver(WorldShellSpec spec)
        {
            if (_scenery == null) _scenery = Object.FindAnyObjectByType<Scenery.PrismEnvironment>();
            if (_scenery == null) return;
            if (_atriumBackdrop < 0f) _atriumBackdrop = _scenery.BackdropLuma;

            _scenery.SetVisible(false);

            // Prism_Compose blends "darken and saturate" against a bright backdrop with "emit"
            // against a dark one. Every shell is dark, so reporting that honestly is what makes the
            // world's own geometry glow instead of going muddy.
            var z = spec.Zenith;
            float luma = 0.2126f * z.r + 0.7152f * z.g + 0.0722f * z.b;
            Shader.SetGlobalFloat("_PrismBackdropLuma", Mathf.Clamp01(luma * 2.2f));
        }

        /// <summary>Give the highland back on the way out.</summary>
        public static void HandBack()
        {
            if (_scenery == null) return;
            _scenery.SetVisible(true);
            Shader.SetGlobalFloat("_PrismBackdropLuma",
                                  _atriumBackdrop >= 0f ? _atriumBackdrop : 0.62f);
        }
    }
}
