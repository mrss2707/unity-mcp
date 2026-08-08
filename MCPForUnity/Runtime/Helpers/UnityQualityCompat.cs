using System.Reflection;
using UnityEngine;

namespace MCPForUnity.Runtime.Helpers
{
    // Part of MCP for Unity's compat-shim family. See UnityCompatShims.cs in this
    // folder for the full list of shims, the audit policy, and the reflection pattern.
    /// <summary>
    /// Version-compatible wrapper for the QualitySettings global mipmap limit.
    ///
    /// Currently covered:
    ///   - QualitySettings.masterTextureLimit        (the only spelling before 2022.2; obsolete from Unity 6)
    ///   - QualitySettings.globalTextureMipmapLimit  (2022.2+ rename)
    ///
    /// Reflection rather than <c>#if</c> because the two spellings overlap: on 2022.2–2023.x both
    /// exist, on 2021.3 only the old one, and on Unity 6 the old one raises CS0618. A version
    /// bracket would have to be exactly right at both ends; probing cannot be.
    /// </summary>
    public static class UnityQualityCompat
    {
        private static PropertyInfo _mipmapLimit;
        private static bool _probed;

        private static PropertyInfo MipmapLimitProp
        {
            get
            {
                if (!_probed)
                {
                    _probed = true;
                    _mipmapLimit = typeof(QualitySettings).GetProperty(
                        "globalTextureMipmapLimit",
                        BindingFlags.Public | BindingFlags.Static)
                        ?? typeof(QualitySettings).GetProperty(
                            "masterTextureLimit",
                            BindingFlags.Public | BindingFlags.Static);
                }
                return _mipmapLimit;
            }
        }

        /// <summary>
        /// Reads the global texture mipmap limit (0 = full resolution, 1 = half, …).
        /// Returns <c>null</c> if neither spelling is available in this Unity version.
        /// </summary>
        public static int? GetGlobalTextureMipmapLimit()
        {
            var prop = MipmapLimitProp;
            if (prop == null || !prop.CanRead) return null;
            try { return (int)prop.GetValue(null); }
            catch { return null; }
        }

        /// <summary>
        /// Writes the global texture mipmap limit. Returns <c>false</c> if the property is
        /// unavailable in this Unity version.
        /// </summary>
        public static bool TrySetGlobalTextureMipmapLimit(int value)
        {
            var prop = MipmapLimitProp;
            if (prop == null || !prop.CanWrite) return false;
            try
            {
                prop.SetValue(null, value);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
