namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Service for resolving paths to required tools and supporting user overrides
    /// </summary>
    public interface IPathResolverService
    {
        /// <summary>
        /// Gets the uvx package manager path (respects override if set)
        /// </summary>
        /// <returns>Path to the uvx executable, or null if not found</returns>
        string GetUvxPath();

        /// <summary>
        /// Gets the Claude CLI path (respects override if set)
        /// </summary>
        /// <returns>Path to the claude executable, or null if not found</returns>
        string GetClaudeCliPath();

        /// <summary>
        /// Gets the PaiCode CLI path (respects override if set)
        /// </summary>
        /// <returns>Path to the paicode executable, or null if not found</returns>
        string GetPaiCodeCliPath();

        /// <summary>
        /// Checks if Python is detected on the system
        /// </summary>
        /// <returns>True if Python is found</returns>
        bool IsPythonDetected();

        /// <summary>
        /// Checks if Claude CLI is detected on the system
        /// </summary>
        /// <returns>True if Claude CLI is found</returns>
        bool IsClaudeCliDetected();

        /// <summary>
        /// Checks if PaiCode CLI is detected on the system
        /// </summary>
        /// <returns>True if PaiCode CLI is found</returns>
        bool IsPaiCodeCliDetected();

        /// <summary>
        /// Sets an override for the uvx path
        /// </summary>
        /// <param name="path">Path to override with</param>
        void SetUvxPathOverride(string path);

        /// <summary>
        /// Sets an override for the Claude CLI path
        /// </summary>
        /// <param name="path">Path to override with</param>
        void SetClaudeCliPathOverride(string path);

        /// <summary>
        /// Sets an override for the PaiCode CLI path
        /// </summary>
        /// <param name="path">Path to override with</param>
        void SetPaiCodeCliPathOverride(string path);

        /// <summary>
        /// Clears the uvx path override
        /// </summary>
        void ClearUvxPathOverride();

        /// <summary>
        /// Clears the Claude CLI path override
        /// </summary>
        void ClearClaudeCliPathOverride();

        /// <summary>
        /// Clears the PaiCode CLI path override
        /// </summary>
        void ClearPaiCodeCliPathOverride();

        /// <summary>
        /// Gets whether a uvx path override is active
        /// </summary>
        bool HasUvxPathOverride { get; }

        /// <summary>
        /// Gets whether a Claude CLI path override is active
        /// </summary>
        bool HasClaudeCliPathOverride { get; }

        /// <summary>
        /// Gets whether a PaiCode CLI path override is active
        /// </summary>
        bool HasPaiCodeCliPathOverride { get; }

        /// <summary>
        /// Gets whether the uvx path used a fallback from override to system path
        /// </summary>
        bool HasUvxPathFallback { get; }

        /// <summary>
        /// Validates the provided uv executable by running "--version" and parsing the output.
        /// </summary>
        /// <param name="uvPath">Absolute or relative path to the uv/uvx executable.</param>
        /// <param name="version">Parsed version string if successful.</param>
        /// <returns>True when the executable runs and returns a uv version string.</returns>
        bool TryValidateUvxExecutable(string uvPath, out string version);
    }
}
