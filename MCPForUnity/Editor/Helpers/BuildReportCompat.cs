using System.Collections.Generic;
using System.Linq;
using UnityEditor.Build.Reporting;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Version-compatible access to the per-file list on a <see cref="BuildReport"/>.
    ///
    /// <c>BuildReport.files</c> is the only spelling before 2022.1 and is [Obsolete] from 2022.1,
    /// where it is replaced by <c>GetFiles()</c>. Callers get a plain list either way.
    /// </summary>
    /// <remarks>
    /// This lives in Editor/ rather than alongside the Runtime/Helpers/Unity*Compat family because
    /// BuildReport is an editor-only type the runtime assembly cannot reference. The version
    /// bracket is safe to keep here — unlike the QualitySettings rename, the two spellings do not
    /// overlap ambiguously and the boundary is a single documented version.
    /// </remarks>
    public static class BuildReportCompat
    {
        public readonly struct FileEntry
        {
            public FileEntry(string path, ulong size, string role)
            {
                Path = path;
                Size = size;
                Role = role;
            }

            public string Path { get; }
            public ulong Size { get; }
            public string Role { get; }
        }

        public static List<FileEntry> GetFiles(BuildReport report)
        {
            if (report == null) return null;

#if UNITY_2022_1_OR_NEWER
            var files = report.GetFiles();
#else
            var files = report.files;
#endif
            return files?.Select(f => new FileEntry(f.path, f.size, f.role)).ToList();
        }
    }
}
