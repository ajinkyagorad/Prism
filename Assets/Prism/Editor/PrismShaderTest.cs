using Prism.Aesthetic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Prism.EditorTools
{
    /// <summary>
    /// Asks Unity whether the PRISM shaders actually compiled.
    ///
    /// This is the check to trust, and it is worth being precise about why the obvious ones are
    /// not enough:
    ///
    ///   Shader.Find returning non-null proves only that an ASSET exists. A shader with a syntax
    ///   error still imports, still has a name, and still hands back a Material — which then draws
    ///   nothing. With additive materials you would at least get magenta; PRISM's materials are
    ///   alpha-blended, so a broken shader fails to complete invisibility.
    ///
    ///   Grepping Logs/shadercompiler-*.log does not work in a -nographics batch run, because no
    ///   graphics device means no variant compilation and therefore no such log.
    ///
    /// ShaderUtil reads the import-time state directly, works headlessly, and reports the actual
    /// error text with line numbers.
    ///
    /// The remaining gap is honest and unavoidable here: this validates the compile for the
    /// EDITOR's platform. The Vulkan/Android variants are only really compiled by the APK build,
    /// which is why the build is still the final word.
    ///
    ///   PRISM &gt; Test Shaders
    /// </summary>
    public static class PrismShaderTest
    {
        [MenuItem("PRISM/Test Shaders", priority = 310)]
        public static void Run()
        {
            int failures = 0;
            int warnings = 0;

            // Core shaders PLUS everything the world modules declare. World shaders were written
            // by agents with no compiler, so they are the highest-risk shaders in the project —
            // testing only the core list would have left them completely unchecked.
            var names = new System.Collections.Generic.List<string>(PrismMaterials.AllShaders);
            foreach (var w in Prism.Worlds.WorldRegistry.AllWorldShaders())
                if (!names.Contains(w)) names.Add(w);

            foreach (var name in names)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogError($"[PRISM-SHADER] MISSING {name}");
                    failures++;
                    continue;
                }

                bool hasError = ShaderUtil.ShaderHasError(shader);
                bool supported = shader.isSupported;
                int messages = ShaderUtil.GetShaderMessageCount(shader);

                if (hasError || !supported)
                {
                    Debug.LogError($"[PRISM-SHADER] FAIL {name} " +
                                   $"(hasError={hasError} supported={supported} messages={messages})");
                    failures++;
                }
                else
                {
                    Debug.Log($"[PRISM-SHADER] pass {name} " +
                              $"(passes={shader.passCount} messages={messages})");
                }

                if (messages > 0)
                {
                    foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    {
                        string where = string.IsNullOrEmpty(m.file) ? "" : $" {m.file}:{m.line}";
                        if (m.severity == ShaderCompilerMessageSeverity.Error)
                            Debug.LogError($"[PRISM-SHADER]   error{where}: {m.message} {m.messageDetails}");
                        else
                        {
                            Debug.LogWarning($"[PRISM-SHADER]   warning{where}: {m.message}");
                            warnings++;
                        }
                    }
                }
            }

            if (failures == 0)
                Debug.Log($"[PRISM-SHADER] ALL {names.Count} SHADERS OK " +
                          $"({warnings} warning(s)) — editor platform only; the APK build validates Vulkan.");
            else
                Debug.LogError($"[PRISM-SHADER] {failures} SHADER FAILURE(S)");

            _failures = failures;
        }

        static int _failures;

        public static void RunFromCommandLine()
        {
            Run();
            EditorApplication.Exit(_failures == 0 ? 0 : 1);
        }
    }
}
