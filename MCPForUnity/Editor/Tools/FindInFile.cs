using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
                    return FindReferences(@params, p);

                default:
                    return new ErrorResponse(
                        $"Unknown action: '{action}'. Supported actions: find_references.");
            }
        }

        private static object FindReferences(JObject @params, ToolParams p)
        {
            var symbolResult = p.GetRequired("symbolName", "'symbolName' parameter is required.");
            if (!symbolResult.IsSuccess)
            {
                return new ErrorResponse(symbolResult.ErrorMessage);
            }

            string symbolName = symbolResult.Value;
            string scope = p.Get("scope", "Assets");
            int maxResults = Math.Max(1, p.GetInt("maxResults") ?? p.GetInt("max_results") ?? 200);
            var pagination = PaginationRequest.FromParams(@params, defaultPageSize: Math.Min(50, maxResults));
            pagination.PageSize = Math.Max(1, Math.Min(pagination.PageSize, maxResults));
            pagination.Cursor = Math.Max(0, pagination.Cursor);

            int neededForPage = Math.Min(maxResults, pagination.Cursor + pagination.PageSize + 1);
            var references = new List<object>();
            int matchedCount = 0;
            int scannedFileCount = 0;
            bool truncatedByMaxResults = false;
            var boundaryRegex = new Regex($@"(?<![A-Za-z0-9_]){Regex.Escape(symbolName)}(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);

            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { scope }))
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!scriptPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

                scannedFileCount++;
                bool inBlockComment = false;
                int lineNumber = 0;

                foreach (string line in File.ReadLines(scriptPath))
                {
                    lineNumber++;
                    string searchable = StripCommentsAndStrings(line, ref inBlockComment);
                    if (!boundaryRegex.IsMatch(searchable)) continue;

                    matchedCount++;
                    if (matchedCount > maxResults)
                    {
                        truncatedByMaxResults = true;
                        break;
                    }

                    if (matchedCount > pagination.Cursor && references.Count < pagination.PageSize)
                    {
                        references.Add(new
                        {
                            file = scriptPath,
                            line = lineNumber,
                            content = line.Trim()
                        });
                    }

                    if (matchedCount >= neededForPage)
                    {
                        break;
                    }
                }

                if (truncatedByMaxResults || matchedCount >= neededForPage)
                {
                    break;
                }
            }

            bool hasMore = truncatedByMaxResults || matchedCount > pagination.Cursor + references.Count;
            int? nextCursor = hasMore ? pagination.Cursor + references.Count : (int?)null;
            int totalCount = truncatedByMaxResults ? maxResults : matchedCount;

            return new SuccessResponse(
                $"Found {totalCount} references to '{symbolName}'",
                new
                {
                    symbolName,
                    count = totalCount,
                    references,
                    cursor = pagination.Cursor,
                    nextCursor,
                    pageSize = pagination.PageSize,
                    hasMore,
                    totalCount,
                    scannedFileCount,
                    truncatedByMaxResults
                });
        }

        private static string StripCommentsAndStrings(string line, ref bool inBlockComment)
        {
            var chars = line.ToCharArray();
            bool inString = false;
            bool inChar = false;
            bool verbatimString = false;

            for (int i = 0; i < chars.Length; i++)
            {
                char current = chars[i];
                char next = i + 1 < chars.Length ? chars[i + 1] : '\0';

                if (inBlockComment)
                {
                    chars[i] = ' ';
                    if (current == '*' && next == '/')
                    {
                        chars[i + 1] = ' ';
                        i++;
                        inBlockComment = false;
                    }
                    continue;
                }

                if (inString)
                {
                    chars[i] = ' ';
                    if (verbatimString && current == '"' && next == '"')
                    {
                        chars[i + 1] = ' ';
                        i++;
                    }
                    else if (current == '"' && (verbatimString || !IsEscaped(line, i)))
                    {
                        inString = false;
                        verbatimString = false;
                    }
                    continue;
                }

                if (inChar)
                {
                    chars[i] = ' ';
                    if (current == '\'' && !IsEscaped(line, i))
                    {
                        inChar = false;
                    }
                    continue;
                }

                if (current == '/' && next == '/')
                {
                    for (int j = i; j < chars.Length; j++) chars[j] = ' ';
                    break;
                }

                if (current == '/' && next == '*')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i++;
                    inBlockComment = true;
                    continue;
                }

                if (current == '@' && next == '"')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i++;
                    inString = true;
                    verbatimString = true;
                    continue;
                }

                if (current == '"')
                {
                    chars[i] = ' ';
                    inString = true;
                    continue;
                }

                if (current == '\'')
                {
                    chars[i] = ' ';
                    inChar = true;
                }
            }

            return new string(chars);
        }

        private static bool IsEscaped(string line, int index)
        {
            int slashCount = 0;
            for (int i = index - 1; i >= 0 && line[i] == '\\'; i--)
            {
                slashCount++;
            }
            return slashCount % 2 == 1;
        }
    }
}
