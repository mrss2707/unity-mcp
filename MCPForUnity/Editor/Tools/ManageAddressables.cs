using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_addressables",
        Description = "Manage Addressable Assets",
        RequiresPolling = true, PollAction = "status")]
    public static class ManageAddressables
    {
        private static bool? _packageAvailable;
        private static Type _settingsType;
        private static Type _groupType;
        private static Type _entryType;
        private static Type _groupSchemaType;
        private static bool _reflectionInitAttempted;

        private static bool PackageAvailable
        {
            get
            {
                if (!_packageAvailable.HasValue)
                {
                    _packageAvailable = Type.GetType(
                        "UnityEditor.AddressableAssets." +
                        "AddressableAssetSettings, " +
                        "Unity.AddressableAssets.Editor") != null;
                }
                return _packageAvailable.Value;
            }
        }

        private static bool EnsureReflectionCache()
        {
            if (_reflectionInitAttempted)
                return _settingsType != null;

            _reflectionInitAttempted = true;

            try
            {
                _settingsType = Type.GetType(
                    "UnityEditor.AddressableAssets.AddressableAssetSettings, " +
                    "Unity.AddressableAssets.Editor");
                _groupType = Type.GetType(
                    "UnityEditor.AddressableAssets.AddressableAssetGroup, " +
                    "Unity.AddressableAssets.Editor");
                _entryType = Type.GetType(
                    "UnityEditor.AddressableAssets.AddressableAssetEntry, " +
                    "Unity.AddressableAssets.Editor");
                _groupSchemaType = Type.GetType(
                    "UnityEditor.AddressableAssets.Settings." +
                    "AddressableAssetGroupSchema, " +
                    "Unity.AddressableAssets.Editor");
            }
            catch
            {
                // Reflection initialization failed
            }

            return _settingsType != null;
        }

        public static object HandleCommand(JObject @params)
        {
            if (!PackageAvailable)
                return new ErrorResponse("PACKAGE_MISSING",
                    "Addressables package (com.unity.addressables) " +
                    "is not installed. Install it via Package Manager.");

            if (!EnsureReflectionCache())
                return new ErrorResponse("REFLECTION_FAILED",
                    "Failed to resolve Addressables types via reflection. " +
                    "Ensure the package is installed and assemblies are compiled.");

            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            string action = p.Get("action");
            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("'action' parameter is required.");

            try
            {
                return action.ToLowerInvariant() switch
                {
                    "create_group" => CreateGroup(p),
                    "assign_asset" => AssignAsset(p),
                    "remove_asset" => RemoveAsset(p),
                    "list_groups" => ListGroups(p),
                    "build_content" => BuildContent(p),
                    "get_dependency_chain" => GetDependencyChain(p),
                    "status" => GetBuildStatus(p),
                    _ => new ErrorResponse("UNKNOWN_ACTION",
                        $"Unknown action: {action}. Valid actions: " +
                        "create_group, assign_asset, remove_asset, " +
                        "list_groups, build_content, get_dependency_chain.")
                };
            }
            catch (TargetInvocationException tie)
            {
                return new ErrorResponse("REFLECTION_ERROR",
                    $"Addressables operation failed: {(tie.InnerException ?? tie).Message}");
            }
            catch (Exception ex)
            {
                return new ErrorResponse("OPERATION_ERROR", ex.Message);
            }
        }

        // ─────────────────────────────────────────────
        // Reflection helpers
        // ─────────────────────────────────────────────

        private static object GetSettings()
        {
            // Try AddressableAssetSettingsDefaultObject.Settings first
            var defaultObjectType = Type.GetType(
                "UnityEditor.AddressableAssets." +
                "AddressableAssetSettingsDefaultObject, " +
                "Unity.AddressableAssets.Editor");
            if (defaultObjectType != null)
            {
                var settingsProp = defaultObjectType.GetProperty("Settings",
                    BindingFlags.Public | BindingFlags.Static);
                if (settingsProp != null)
                {
                    var settings = settingsProp.GetValue(null);
                    if (settings != null)
                        return settings;
                }
            }

            // Fallback: search project for settings asset
            if (_settingsType != null)
            {
                var guids = AssetDatabase.FindAssets("t:AddressableAssetSettings");
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var obj = AssetDatabase.LoadAssetAtPath(path, _settingsType);
                    if (obj != null)
                        return obj;
                }
            }

            return null;
        }

        private static IList GetGroupsList(object settings)
        {
            var groupsProp = _settingsType.GetProperty("groups",
                BindingFlags.Public | BindingFlags.Instance);
            return groupsProp?.GetValue(settings) as IList;
        }

        private static string GetGroupName(object group)
        {
            var nameProp = _groupType.GetProperty("Name") ??
                           _groupType.GetProperty("name");
            return (string)nameProp?.GetValue(group);
        }

        private static IList GetEntriesList(object group)
        {
            var entriesProp = _groupType.GetProperty("Entries") ??
                              _groupType.GetProperty("entries");
            return entriesProp?.GetValue(group) as IList;
        }

        private static string GetEntryGuid(object entry)
        {
            var guidProp = _entryType.GetProperty("guid",
                BindingFlags.Public | BindingFlags.Instance);
            return (string)guidProp?.GetValue(entry);
        }

        private static string GetEntryAddress(object entry)
        {
            var addressProp = _entryType.GetProperty("address",
                BindingFlags.Public | BindingFlags.Instance);
            return (string)addressProp?.GetValue(entry);
        }

        private static object FindGroup(object settings, string groupName)
        {
            var groups = GetGroupsList(settings);
            if (groups == null)
                return null;

            foreach (var group in groups)
            {
                if (GetGroupName(group) == groupName)
                    return group;
            }

            return null;
        }

        private static void SetSettingsDirty(object settings)
        {
            var setDirtyMethod = _settingsType.GetMethod("SetDirty",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(UnityEngine.Object) }, null);
            setDirtyMethod?.Invoke(settings, new[] { settings });
        }

        // ─────────────────────────────────────────────
        // 1. create_group
        // ─────────────────────────────────────────────

        private static object CreateGroup(ToolParams p)
        {
            try
            {
                string groupName = p.Get("group_name") ?? p.Get("groupName");
                if (string.IsNullOrEmpty(groupName))
                    return new ErrorResponse("'group_name' parameter is required.");

                var settings = GetSettings();
                if (settings == null)
                    return new ErrorResponse("SETTINGS_NOT_FOUND",
                        "Addressable Asset Settings not found. " +
                        "Set up Addressables first via Window > Asset Management > Addressables.");

                // Check for duplicate group name
                if (FindGroup(settings, groupName) != null)
                    return new ErrorResponse("GROUP_EXISTS",
                        $"Group '{groupName}' already exists.");

                // Create an empty List<AddressableAssetGroupSchema> via reflection
                var schemaListType = typeof(System.Collections.Generic.List<>)
                    .MakeGenericType(_groupSchemaType);
                var schemas = Activator.CreateInstance(schemaListType);

                // Call CreateGroup(groupName, false, false, false, schemas)
                var createGroupMethod = _settingsType.GetMethod("CreateGroup",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(string), typeof(bool), typeof(bool),
                        typeof(bool), schemaListType }, null);

                if (createGroupMethod == null)
                {
                    // Try with different signature (object param for schemas)
                    createGroupMethod = _settingsType.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "CreateGroup" &&
                            m.GetParameters().Length == 5);
                }

                if (createGroupMethod == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "Cannot find CreateGroup method on AddressableAssetSettings.");

                var group = createGroupMethod.Invoke(settings,
                    new object[] { groupName, false, false, false, schemas });

                if (group == null)
                    return new ErrorResponse("CREATE_FAILED",
                        "CreateGroup returned null.");

                SetSettingsDirty(settings);
                AssetDatabase.SaveAssets();

                return new SuccessResponse(
                    $"Created Addressables group '{groupName}'.",
                    new { groupName });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("CREATE_GROUP_FAILED",
                    $"Failed to create group: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 2. assign_asset
        // ─────────────────────────────────────────────

        private static object AssignAsset(ToolParams p)
        {
            try
            {
                string assetPath = p.Get("asset_path") ?? p.Get("assetPath");
                if (string.IsNullOrEmpty(assetPath))
                    return new ErrorResponse("'asset_path' parameter is required.");

                assetPath = AssetPathUtility.SanitizeAssetPath(assetPath);
                if (assetPath == null)
                    return new ErrorResponse("Invalid path: contains traversal sequences.");

                string groupName = p.Get("group_name") ?? p.Get("groupName");
                if (string.IsNullOrEmpty(groupName))
                    return new ErrorResponse("'group_name' parameter is required.");

                string address = p.Get("address") ?? assetPath;
                string labelsStr = p.Get("labels");

                var settings = GetSettings();
                if (settings == null)
                    return new ErrorResponse("SETTINGS_NOT_FOUND",
                        "Addressable Asset Settings not found. " +
                        "Set up Addressables first via Window > Asset Management > Addressables.");

                var group = FindGroup(settings, groupName);
                if (group == null)
                    return new ErrorResponse("GROUP_NOT_FOUND",
                        $"Group '{groupName}' not found.");

                // Get asset GUID
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                    return new ErrorResponse("ASSET_NOT_FOUND",
                        $"No asset found at '{assetPath}'.");

                // Check if entry already exists
                var entries = GetEntriesList(group);
                if (entries != null)
                {
                    foreach (var entry in entries)
                    {
                        if (GetEntryGuid(entry) == guid)
                            return new ErrorResponse("ENTRY_EXISTS",
                                $"Asset '{assetPath}' is already assigned to group '{groupName}'.");
                    }
                }

                // Create entry via reflection: AddressableAssetGroup.CreateEntry(guid, address, null, false)
                var createEntryMethod = _groupType.GetMethod("CreateEntry",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(string), typeof(string), typeof(object),
                        typeof(bool) }, null);

                if (createEntryMethod == null)
                {
                    // Try alternate signature
                    createEntryMethod = _groupType.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "CreateEntry");
                }

                if (createEntryMethod == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "Cannot find CreateEntry method on AddressableAssetGroup.");

                var entry = createEntryMethod.Invoke(group,
                    new object[] { guid, address, null, false });

                if (entry == null)
                    return new ErrorResponse("CREATE_ENTRY_FAILED",
                        "CreateEntry returned null.");

                // Set labels if provided
                if (!string.IsNullOrEmpty(labelsStr))
                {
                    var labelsProp = _entryType.GetProperty("labels",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (labelsProp != null)
                    {
                        var currentLabels = labelsProp.GetValue(entry) as IList;
                        if (currentLabels != null)
                        {
                            foreach (var label in labelsStr.Split(','))
                            {
                                var trimmed = label.Trim();
                                if (!string.IsNullOrEmpty(trimmed))
                                    currentLabels.Add(trimmed);
                            }
                        }
                    }
                }

                SetSettingsDirty(settings);
                AssetDatabase.SaveAssets();

                return new SuccessResponse(
                    $"Assigned asset '{assetPath}' to group '{groupName}' " +
                    $"with address '{address}'.",
                    new { assetPath, groupName, address, guid });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("ASSIGN_FAILED",
                    $"Failed to assign asset: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 3. remove_asset
        // ─────────────────────────────────────────────

        private static object RemoveAsset(ToolParams p)
        {
            try
            {
                string assetPath = p.Get("asset_path") ?? p.Get("assetPath");
                string address = p.Get("address");
                string groupName = p.Get("group_name") ?? p.Get("groupName");

                if (string.IsNullOrEmpty(assetPath) && string.IsNullOrEmpty(address))
                    return new ErrorResponse(
                        "Either 'asset_path' or 'address' parameter is required.");

                var settings = GetSettings();
                if (settings == null)
                    return new ErrorResponse("SETTINGS_NOT_FOUND",
                        "Addressable Asset Settings not found.");

                string targetGuid = null;
                if (!string.IsNullOrEmpty(assetPath))
                {
                    targetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                    if (string.IsNullOrEmpty(targetGuid))
                        return new ErrorResponse("ASSET_NOT_FOUND",
                            $"No asset found at '{assetPath}'.");
                }

                // Determine which groups to search
                var groups = GetGroupsList(settings);
                if (groups == null || groups.Count == 0)
                    return new ErrorResponse("NO_GROUPS",
                        "No addressable groups found.");

                object targetEntry = null;
                object targetGroup = null;
                string foundAddress = null;
                string foundGuid = null;

                foreach (var group in groups)
                {
                    string currentGroupName = GetGroupName(group);

                    // If groupName specified, skip non-matching groups
                    if (!string.IsNullOrEmpty(groupName) &&
                        !string.Equals(groupName, currentGroupName,
                            StringComparison.Ordinal))
                        continue;

                    var groupEntries = GetEntriesList(group);
                    if (groupEntries == null) continue;

                    foreach (var entry in groupEntries)
                    {
                        var entryGuid = GetEntryGuid(entry);
                        var entryAddress = GetEntryAddress(entry);

                        bool matchesAddress = !string.IsNullOrEmpty(address) &&
                            string.Equals(entryAddress, address,
                                StringComparison.Ordinal);
                        bool matchesGuid = !string.IsNullOrEmpty(targetGuid) &&
                            string.Equals(entryGuid, targetGuid,
                                StringComparison.OrdinalIgnoreCase);

                        if (matchesAddress || matchesGuid)
                        {
                            targetEntry = entry;
                            targetGroup = group;
                            foundAddress = entryAddress;
                            foundGuid = entryGuid;
                            break;
                        }
                    }

                    if (targetEntry != null) break;
                }

                if (targetEntry == null)
                    return new ErrorResponse("ENTRY_NOT_FOUND",
                        "No addressable entry found matching the given criteria.");

                // Remove entry from group via reflection
                var removeEntryMethod = _groupType.GetMethod("RemoveEntry",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { _entryType }, null);

                if (removeEntryMethod == null)
                {
                    // Try RemoveEntry with object parameter
                    removeEntryMethod = _groupType.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "RemoveEntry" &&
                            m.GetParameters().Length == 1);
                }

                if (removeEntryMethod == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "Cannot find RemoveEntry method on AddressableAssetGroup.");

                removeEntryMethod.Invoke(targetGroup, new[] { targetEntry });

                SetSettingsDirty(settings);
                AssetDatabase.SaveAssets();

                return new SuccessResponse(
                    $"Removed asset '{foundAddress}' from addressables.",
                    new { address = foundAddress, guid = foundGuid });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("REMOVE_FAILED",
                    $"Failed to remove asset: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 4. list_groups
        // ─────────────────────────────────────────────

        private static object ListGroups(ToolParams p)
        {
            try
            {
                var settings = GetSettings();
                if (settings == null)
                    return new ErrorResponse("SETTINGS_NOT_FOUND",
                        "Addressable Asset Settings not found.");

                var groups = GetGroupsList(settings);
                var result = new JArray();

                if (groups != null)
                {
                    foreach (var group in groups)
                    {
                        var groupName = GetGroupName(group);
                        var entries = GetEntriesList(group);
                        int entryCount = entries?.Count ?? 0;

                        // Get schema types via reflection
                        var schemasArray = new JArray();
                        var schemasProp = _groupType.GetProperty("Schemas") ??
                                          _groupType.GetProperty("schemas");
                        if (schemasProp != null)
                        {
                            var schemas = schemasProp.GetValue(group) as IList;
                            if (schemas != null)
                            {
                                foreach (var schema in schemas)
                                {
                                    schemasArray.Add(schema?.GetType().Name ?? "Unknown");
                                }
                            }
                        }

                        result.Add(new JObject
                        {
                            ["name"] = groupName,
                            ["entryCount"] = entryCount,
                            ["schemaTypes"] = schemasArray
                        });
                    }
                }

                return new SuccessResponse(
                    $"Found {result.Count} addressable group(s).",
                    new { groups = result, count = result.Count });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("LIST_FAILED",
                    $"Failed to list groups: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 5. build_content
        // ─────────────────────────────────────────────

        private static object BuildContent(ToolParams p)
        {
            try
            {
                var settings = GetSettings();
                if (settings == null)
                    return new ErrorResponse("SETTINGS_NOT_FOUND",
                        "Addressable Asset Settings not found. " +
                        "Set up Addressables first via Window > Asset Management > Addressables.");

                string jobId = $"addr-build-{Guid.NewGuid():N}";

                // Store initial build state
                McpJobStateStore.SaveState(jobId, new JObject
                {
                    ["jobId"] = jobId,
                    ["status"] = "running",
                    ["startTime"] = DateTime.Now.ToString("O")
                });

                // Resolve BuildPlayerContent method
                var buildMethod = _settingsType.GetMethod("BuildPlayerContent",
                    BindingFlags.Public | BindingFlags.Static,
                    null, Type.EmptyTypes, null);

                if (buildMethod == null)
                {
                    // Try with BuildPlayerContentOptions parameter
                    var contentOptionsType = Type.GetType(
                        "UnityEditor.AddressableAssets.Build." +
                        "BuildPlayerContentOptions, " +
                        "Unity.AddressableAssets.Editor");
                    if (contentOptionsType != null)
                    {
                        buildMethod = _settingsType.GetMethod("BuildPlayerContent",
                            BindingFlags.Public | BindingFlags.Static,
                            null, new[] { contentOptionsType }, null);

                        if (buildMethod != null)
                        {
                            var options = Activator.CreateInstance(contentOptionsType);
                            EditorApplication.delayCall += () =>
                            {
                                try
                                {
                                    buildMethod.Invoke(null, new[] { options });
                                    McpJobStateStore.SaveState(jobId, new JObject
                                    {
                                        ["jobId"] = jobId,
                                        ["status"] = "completed",
                                        ["startTime"] = DateTime.Now.ToString("O"),
                                        ["endTime"] = DateTime.Now.ToString("O")
                                    });
                                }
                                catch (Exception ex)
                                {
                                    McpJobStateStore.SaveState(jobId, new JObject
                                    {
                                        ["jobId"] = jobId,
                                        ["status"] = "failed",
                                        ["error"] = ex.InnerException?.Message ?? ex.Message,
                                        ["startTime"] = DateTime.Now.ToString("O"),
                                        ["endTime"] = DateTime.Now.ToString("O")
                                    });
                                }
                            };
                        }
                    }
                }
                else
                {
                    // No-arg overload
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            buildMethod.Invoke(null, null);
                            McpJobStateStore.SaveState(jobId, new JObject
                            {
                                ["jobId"] = jobId,
                                ["status"] = "completed",
                                ["startTime"] = DateTime.Now.ToString("O"),
                                ["endTime"] = DateTime.Now.ToString("O")
                            });
                        }
                        catch (Exception ex)
                        {
                            McpJobStateStore.SaveState(jobId, new JObject
                            {
                                ["jobId"] = jobId,
                                ["status"] = "failed",
                                ["error"] = ex.InnerException?.Message ?? ex.Message,
                                ["startTime"] = DateTime.Now.ToString("O"),
                                ["endTime"] = DateTime.Now.ToString("O")
                            });
                        }
                    };
                }

                if (buildMethod == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "Cannot find BuildPlayerContent method on AddressableAssetSettings.");

                return new PendingResponse(
                    $"Addressables content build started (job {jobId}).",
                    pollIntervalSeconds: 2.0,
                    data: new { jobId });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("BUILD_FAILED",
                    $"Failed to build addressables content: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 6. get_dependency_chain
        // ─────────────────────────────────────────────

        private static object GetDependencyChain(ToolParams p)
        {
            try
            {
                string assetPath = p.Get("asset_path") ?? p.Get("assetPath");
                if (string.IsNullOrEmpty(assetPath))
                    return new ErrorResponse("'asset_path' parameter is required.");

                assetPath = AssetPathUtility.SanitizeAssetPath(assetPath);
                if (assetPath == null)
                    return new ErrorResponse("Invalid path: contains traversal sequences.");

                var settings = GetSettings();
                if (settings == null)
                    return new ErrorResponse("SETTINGS_NOT_FOUND",
                        "Addressable Asset Settings not found.");

                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                    return new ErrorResponse("ASSET_NOT_FOUND",
                        $"No asset found at '{assetPath}'.");

                // Find the entry in any group
                var groups = GetGroupsList(settings);
                object targetEntry = null;
                string entryAddress = null;

                if (groups != null)
                {
                    foreach (var group in groups)
                    {
                        var entries = GetEntriesList(group);
                        if (entries == null) continue;

                        foreach (var entry in entries)
                        {
                            var eGuid = GetEntryGuid(entry);
                            if (string.Equals(eGuid, guid,
                                StringComparison.OrdinalIgnoreCase))
                            {
                                targetEntry = entry;
                                entryAddress = GetEntryAddress(entry);
                                break;
                            }
                        }

                        if (targetEntry != null) break;
                    }
                }

                // Try using Addressables' GetDependencies if we have the entry
                if (targetEntry != null)
                {
                    var getDepsMethod = _settingsType.GetMethod("GetDependencies",
                        BindingFlags.Public | BindingFlags.Instance,
                        null, new[] { _entryType }, null);

                    if (getDepsMethod != null)
                    {
                        var depsResult = getDepsMethod.Invoke(settings,
                            new[] { targetEntry });
                        var depsList = depsResult as IList;
                        var depsArray = new JArray();

                        if (depsList != null)
                        {
                            foreach (var dep in depsList)
                            {
                                var depGuid = GetEntryGuid(dep);
                                var depAddress = GetEntryAddress(dep);
                                var depPath = !string.IsNullOrEmpty(depGuid)
                                    ? AssetDatabase.GUIDToAssetPath(depGuid)
                                    : null;

                                depsArray.Add(new JObject
                                {
                                    ["guid"] = depGuid ?? "",
                                    ["address"] = depAddress ?? "",
                                    ["path"] = depPath ?? ""
                                });
                            }
                        }

                        return new SuccessResponse(
                            $"Found {depsArray.Count} dependenc(ies) for " +
                            $"'{assetPath}'.",
                            new
                            {
                                assetPath,
                                address = entryAddress,
                                guid,
                                dependencies = depsArray
                            });
                    }
                }

                // Fallback: use AssetDatabase.GetDependencies
                string[] deps = AssetDatabase.GetDependencies(assetPath);
                var fallbackDeps = new JArray();
                foreach (var dep in deps)
                {
                    fallbackDeps.Add(new JObject
                    {
                        ["path"] = dep,
                        ["guid"] = AssetDatabase.AssetPathToGUID(dep) ?? ""
                    });
                }

                return new SuccessResponse(
                    $"Found {fallbackDeps.Count} dependenc(ies) for " +
                    $"'{assetPath}' (from AssetDatabase).",
                    new
                    {
                        assetPath,
                        address = entryAddress,
                        guid,
                        note = targetEntry == null
                            ? "Asset is not in any addressable group."
                            : null,
                        dependencies = fallbackDeps
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("DEPENDENCY_FAILED",
                    $"Failed to get dependency chain: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // Polling: status
        // ─────────────────────────────────────────────

        private static object GetBuildStatus(ToolParams p)
        {
            try
            {
                string jobId = p.Get("job_id") ?? p.Get("jobId");
                if (string.IsNullOrEmpty(jobId))
                    return new ErrorResponse("'job_id' parameter is required for polling.");

                var state = McpJobStateStore.LoadState<JObject>(jobId);
                if (state == null)
                    return new ErrorResponse("JOB_NOT_FOUND",
                        $"No build job found with ID: {jobId}");

                string status = state["status"]?.ToString();

                if (string.Equals(status, "running",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return new PendingResponse(
                        "Addressables content build is running...",
                        pollIntervalSeconds: 2.0,
                        data: state);
                }

                if (string.Equals(status, "completed",
                    StringComparison.OrdinalIgnoreCase))
                {
                    // Clean up state
                    McpJobStateStore.ClearState(jobId);
                    return new SuccessResponse(
                        "Addressables content build completed.", state);
                }

                if (string.Equals(status, "failed",
                    StringComparison.OrdinalIgnoreCase))
                {
                    string error = state["error"]?.ToString() ?? "Unknown error";
                    McpJobStateStore.ClearState(jobId);
                    return new ErrorResponse("BUILD_FAILED",
                        $"Addressables content build failed: {error}");
                }

                return new SuccessResponse(
                    $"Build status: {status}", state);
            }
            catch (Exception ex)
            {
                return new ErrorResponse("STATUS_FAILED",
                    $"Failed to get build status: {ex.Message}");
            }
        }
    }
}
