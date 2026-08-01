using System.Collections.Generic;
using Prism.Core;
using UnityEngine;

namespace Prism.Aesthetic
{
    /// <summary>
    /// Runtime material factory for the PRISM shader family.
    ///
    /// Every material in this project is created at runtime, which means every shader is found
    /// by name. Unity strips shaders that no scene references, so an unregistered shader
    /// resolves to null in a player build and the object renders magenta — or, with these
    /// alpha-blended materials, renders as nothing at all, which is far harder to diagnose.
    /// PRISM > Register Shaders For Build writes them all into Always Included Shaders, and
    /// <see cref="Get"/> shouts rather than shrugs if one is missing.
    /// </summary>
    public static class PrismMaterials
    {
        public const string Seed       = "Prism/Seed";
        public const string Ceramic    = "Prism/Ceramic";
        public const string Gel        = "Prism/Gel";
        public const string Field      = "Prism/Field";
        public const string Flow       = "Prism/Flow";
        public const string Trail      = "Prism/Trail";
        public const string Volumetric = "Prism/Volumetric";
        public const string Mote       = "Prism/Mote";
        public const string Sky        = "Prism/Sky";
        public const string Ground     = "Prism/Ground";
        public const string WorldShell = "Prism/WorldShell";

        /// <summary>Every shader the runtime looks up by name. The build registration reads this.</summary>
        public static readonly string[] AllShaders =
        {
            Seed, Ceramic, Gel, Field, Flow, Trail, Volumetric, Mote, Sky, Ground, WorldShell
        };

        static readonly Dictionary<string, Shader> _shaders = new Dictionary<string, Shader>();

        public static Shader Get(string shaderName)
        {
            if (_shaders.TryGetValue(shaderName, out var s) && s != null) return s;

            s = Shader.Find(shaderName);
            if (s == null)
            {
                Debug.LogError($"[PRISM] Shader '{shaderName}' not found. In a player build this " +
                               "means it was stripped: run PRISM > Register Shaders For Build. " +
                               "Nothing using this material will be visible.");
                return null;
            }
            _shaders[shaderName] = s;
            return s;
        }

        public static Material New(string shaderName)
        {
            var sh = Get(shaderName);
            var m = new Material(sh != null ? sh : Shader.Find("Unlit/Color"))
            {
                name = shaderName.Replace('/', '_')
            };
            return m;
        }

        // ---- semantic constructors ------------------------------------------
        // These exist so that the visual grammar is applied by code rather than remembered.
        // A caller asks for "a concept of this kind" and cannot accidentally give a Question
        // the appearance of a Fact.

        /// <summary>
        /// A concept's body. Kind chooses the shader family; mastery drives crystallisation.
        /// </summary>
        public static Material ForConcept(ConceptDefinition concept, float mastery,
                                          float misconception = 0f, float phase = 0f)
        {
            string shader = KnowledgeAppearance.ShaderFor(concept.kind);
            var m = New(shader);
            var tint = concept.Colour;

            m.SetColor("_Tint", tint);
            if (m.HasProperty("_Phase")) m.SetFloat("_Phase", phase);

            if (shader == Seed)
            {
                m.SetFloat("_Growth", Mathf.Clamp01(mastery));
                m.SetFloat("_Instability", Mathf.Clamp01(misconception));
                m.SetFloat("_Void", concept.kind == KnowledgeKind.Question ? 1f : 0f);
                // Film thickness varies per concept so no two crystals iridesce identically.
                m.SetFloat("_FilmNm", 300f + Mathf.Repeat(phase * 997f, 380f));
            }
            else if (shader == Volumetric)
            {
                m.SetColor("_EdgeTint", PrismPalette.Spectral(
                    Mathf.Repeat(PrismPalette.DomainHue(concept.domain) + 0.15f, 1f)));
                // An uncertain thing is diffuse; growing certainty tightens it.
                m.SetFloat("_Density", Mathf.Lerp(0.5f, 1.4f, mastery));
            }
            else if (shader == Gel)
            {
                m.SetColor("_DeepTint", PrismPalette.Violet);
                m.SetFloat("_Density", Mathf.Lerp(0.7f, 1.8f, mastery));
            }
            else if (shader == Flow)
            {
                m.SetFloat("_Strength", Mathf.Lerp(0.25f, 1f, mastery));
            }

            return m;
        }

        /// <summary>A relationship between two concepts.</summary>
        public static Material ForRelation(Relation relation, float strength, float phase = 0f)
        {
            var m = New(Flow);
            m.SetColor("_Tint", RelationColour(relation));
            m.SetFloat("_Strength", Mathf.Clamp01(strength));
            m.SetFloat("_Phase", phase);
            m.SetFloat("_Packets", PacketsFor(relation));
            return m;
        }

        /// <summary>
        /// Relations have fixed hues, for the same reason domains do: a learner who has seen a
        /// few analogies should be able to spot the next one by its colour before reading it.
        /// Analogy gets gold because it is the most valuable link in the product — the same shape
        /// turning up in different material is the whole thesis.
        /// </summary>
        public static Color RelationColour(Relation relation)
        {
            switch (relation)
            {
                case Relation.Analogy:      return PrismPalette.Gold;
                case Relation.Causes:       return PrismPalette.Coral;
                case Relation.Constrains:   return PrismPalette.Lavender;
                case Relation.Composes:     return PrismPalette.Mint;
                case Relation.Measures:     return PrismPalette.Cyan;
                case Relation.Generalises:  return PrismPalette.Lavender;
                case Relation.Instantiates: return PrismPalette.Mint;
                default:                    return PrismPalette.Cyan;
            }
        }

        static float PacketsFor(Relation relation)
        {
            switch (relation)
            {
                case Relation.Causes:     return 3f;   // few, deliberate, directional
                case Relation.Composes:   return 6f;
                case Relation.Analogy:    return 2f;   // slow and rare: an analogy is a big claim
                default:                  return 4f;
            }
        }

        /// <summary>A solid body: planets, instrument surfaces.</summary>
        public static Material CeramicBody(Color tint, float luminance = 0.35f, float filmNm = 0f)
        {
            var m = New(Ceramic);
            m.SetColor("_Tint", tint);
            m.SetFloat("_Luminance", luminance);
            m.SetFloat("_FilmNm", filmNm);
            return m;
        }

        public static Material TrailMaterial(float speedLo, float speedHi)
        {
            var m = New(Trail);
            m.SetFloat("_SpeedLo", speedLo);
            m.SetFloat("_SpeedHi", speedHi);
            return m;
        }

        /// <summary>Tell every ground-fading material where the ground is.</summary>
        public static void SetGround(Material m, float y, float soften = 0.08f)
        {
            if (m == null) return;
            if (m.HasProperty("_GroundY"))    m.SetFloat("_GroundY", y);
            if (m.HasProperty("_GroundSoft")) m.SetFloat("_GroundSoft", soften);
        }
    }
}
