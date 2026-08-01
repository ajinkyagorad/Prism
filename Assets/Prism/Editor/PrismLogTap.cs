using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Prism.EditorTools
{
    /// <summary>
    /// Mirrors the console to <c>Logs/Prism.log</c>.
    ///
    /// Unity's own Editor.log is not dependable for this: launching a second editor process
    /// truncates it out from under the running one, and it is buried in a platform-specific
    /// location. This file only ever contains errors, warnings, and lines this project tagged as
    /// its own — which makes it the right thing to read when you cannot see the editor.
    /// </summary>
    [InitializeOnLoad]
    public static class PrismLogTap
    {
        const string Path = "Logs/Prism.log";
        static StreamWriter _writer;

        static PrismLogTap()
        {
            try
            {
                Directory.CreateDirectory("Logs");
                _writer = new StreamWriter(Path, append: true) { AutoFlush = true };
                _writer.WriteLine($"--- session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
                Application.logMessageReceivedThreaded -= OnLog;
                Application.logMessageReceivedThreaded += OnLog;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PRISM] Could not open the log tap: " + e.Message);
            }
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (_writer == null) return;

            bool interesting = type == LogType.Error || type == LogType.Exception
                            || type == LogType.Assert || type == LogType.Warning
                            || message.StartsWith("[PRISM");
            if (!interesting) return;

            try
            {
                _writer.WriteLine($"{DateTime.Now:HH:mm:ss} {type,-9} {message}");
                if (type == LogType.Exception || type == LogType.Error)
                {
                    var trimmed = (stack ?? "").Trim();
                    if (trimmed.Length > 0) _writer.WriteLine("    " + trimmed.Replace("\n", "\n    "));
                }
            }
            catch { /* a logger that throws is worse than a logger that misses a line */ }
        }
    }
}
