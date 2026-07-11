using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// PaiCode configurator using the CLI-based registration (claude mcp add/remove).
    /// PaiCode shares Claude Code's CLI infrastructure, using the paicode binary
    /// which wraps the same underlying Claude Code CLI.
    /// </summary>
    public class PaiCodeConfigurator : ClaudeCliMcpConfigurator
    {
        public PaiCodeConfigurator() : base(new McpClient
        {
            name = "PaiCode",
            SupportsHttpTransport = true,
        })
        { }

        public override bool SupportsSkills => true;

        public override bool IsInstalled => MCPServiceLocator.Paths.IsPaiCodeCliDetected();

        internal override string ResolveCliPath()
        {
            return MCPServiceLocator.Paths.GetPaiCodeCliPath();
        }

        public override string GetSkillInstallPath()
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userHome, ".paicode", "skills", "unity-mcp-skill");
        }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Ensure PaiCode CLI is installed (paicode command)",
            "Click Configure to add UnityMCP via 'claude mcp add'",
            "The server will be automatically available in PaiCode",
            "Use Unregister to remove via 'claude mcp remove'"
        };
    }
}
