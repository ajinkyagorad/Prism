using System;

namespace Prism.Demos
{
    /// <summary>
    /// Marks a <see cref="ConceptDemo"/> as the pocket demonstration for a concept.
    ///
    /// Exists for the same reason WorldRegistry discovers worlds by reflection: the demonstration
    /// registry is a shared file, and ten module authors working in parallel cannot all edit one
    /// shared file without colliding. An attribute lets a demo live entirely inside its own world's
    /// folder and still be found — registration by declaration, not by editing a central list.
    ///
    ///     [ConceptDemoFor("diffusion")]
    ///     public class DiffusionDemo : ConceptDemo { ... }
    ///
    /// A concept with no marked demo and no world falls back to LatentDemo, which responds to the
    /// hand but does not pretend to teach. That fallback is deliberate: an honest gap is better
    /// than a decorative animation implying a lesson that does not exist.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ConceptDemoForAttribute : Attribute
    {
        public string ConceptId { get; }

        public ConceptDemoForAttribute(string conceptId)
        {
            ConceptId = conceptId;
        }
    }
}
