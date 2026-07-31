using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Prism.Worlds
{
    /// <summary>
    /// Finds every Concept World in the build, by reflection.
    ///
    /// Deliberately NOT a hand-maintained list. Ten world modules were built in parallel by
    /// independent agents, each owning one folder and forbidden from touching shared files — so a
    /// central registry that every one of them had to edit would have been the single guaranteed
    /// merge conflict in the whole arrangement. Discovering subclasses instead means a world is
    /// registered by the act of existing, and adding an eleventh needs no edit here at all.
    ///
    /// The scan runs once and is cached. It is not free, but it happens at scene build time and
    /// once per session, not per frame.
    /// </summary>
    public static class WorldRegistry
    {
        static List<Type> _types;

        /// <summary>Every concrete PrismWorldBase subclass in the loaded assemblies.</summary>
        public static IReadOnlyList<Type> WorldTypes
        {
            get
            {
                if (_types != null) return _types;

                _types = new List<Type>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    // Only our own assemblies can contain worlds; scanning the whole domain
                    // including UnityEngine and mscorlib is slow and pointless.
                    var name = asm.GetName().Name;
                    if (!name.StartsWith("Assembly-CSharp") && !name.StartsWith("Prism")) continue;

                    Type[] found;
                    try { found = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException e)
                    {
                        // A partially-loadable assembly still yields the types that did load.
                        found = e.Types.Where(t => t != null).ToArray();
                    }

                    foreach (var t in found)
                        if (t != null && !t.IsAbstract && typeof(PrismWorldBase).IsAssignableFrom(t))
                            _types.Add(t);
                }

                _types.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                return _types;
            }
        }

        /// <summary>
        /// Read a world's declared data without running it.
        ///
        /// Used by the concept seeder and the shader registration, both of which need to know what
        /// a world contributes before any scene exists. A temporary GameObject is created and
        /// destroyed; the world never enters, never builds geometry, and never ticks.
        /// </summary>
        public static void InspectAll(Action<PrismWorldBase> visit)
        {
            foreach (var type in WorldTypes)
            {
                GameObject probe = null;
                try
                {
                    probe = new GameObject($"~probe_{type.Name}") { hideFlags = HideFlags.HideAndDontSave };
                    var world = probe.AddComponent(type) as PrismWorldBase;
                    if (world != null) visit(world);
                }
                catch (Exception e)
                {
                    // One malformed module must not stop the other nine from being registered.
                    Debug.LogError($"[PRISM] Could not inspect world '{type.Name}': {e.Message}");
                }
                finally
                {
                    if (probe != null)
                    {
                        if (Application.isPlaying) UnityEngine.Object.Destroy(probe);
                        else UnityEngine.Object.DestroyImmediate(probe);
                    }
                }
            }
        }

        /// <summary>Every shader name any world creates at runtime, deduplicated.</summary>
        public static string[] AllWorldShaders()
        {
            var set = new HashSet<string>();
            InspectAll(w =>
            {
                var shaders = w.Shaders;
                if (shaders == null) return;
                foreach (var s in shaders) if (!string.IsNullOrEmpty(s)) set.Add(s);
            });
            return set.ToArray();
        }

        /// <summary>Every concept every world contributes to the constellation.</summary>
        public static List<ConceptSpec> AllWorldConcepts()
        {
            var all = new List<ConceptSpec>();
            InspectAll(w =>
            {
                var concepts = w.Concepts;
                if (concepts == null) return;
                foreach (var c in concepts)
                    if (!string.IsNullOrEmpty(c.Id)) all.Add(c);
            });
            return all;
        }
    }
}
