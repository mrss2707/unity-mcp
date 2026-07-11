using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Handles file search operations including symbol reference finding across the project.
    /// </summary>
    [McpForUnityTool("find_in_file")]
    public static class FindInFile
    {
        /// <summary>
        /// Main handler for find_in_file actions.
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            var p = new ToolParams(@params);

            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
            {
                return new ErrorResponse(actionResult.ErrorMessage);
            }
            string action = actionResult.Value.ToLowerInvariant();

            switch (action)
            {
                case "find_references":
                {
                    string symbolName = p.GetRequired("symbolName");
                    string scope = p.Get("scope", "Assets");

                    var scripts = AssetDatabase.FindAssets("t:MonoScript",
                        new[] { scope });
                    var results = new List<object>();

                    foreach (var guid in scripts)
                    {
                        string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (!scriptPath.EndsWith(".cs")) continue;

                        var lines = File.ReadAllLines(scriptPath);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].Contains(symbolName))
                            {
                                results.Add(new
                                {
                                    file = scriptPath,
                                    line = i + 1,
                                    content = lines[i].Trim()
                                });
                            }
                        }
                    }

                    return new SuccessResponse(
                        $"Found {results.Count} references to '{symbolName}'",
                        new { symbolName, count = results.Count, references = results });
                }

                default:
                    return new ErrorResponse(
                        $"Unknown action: '{action}'. Supported actions: find_references.");
            }
        }
    }
}
