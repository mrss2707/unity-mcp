using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_input_system", AutoRegister = false, Group = "input_system",
        Description = "Manage Unity Input System Action Assets")]
    public static class ManageInputSystem
    {
        private static bool? _packageAvailable;
        private static Type _assetType;
        private static Type _mapType;
        private static Type _actionType;
        private static Type _bindingType;
        private static Type _controlSchemeType;
        private static Type _actionTypeEnum;
        private static Type _actionMapExtensionsType;
        private static bool _reflectionInitAttempted;

        private static bool PackageAvailable
        {
            get
            {
                if (!_packageAvailable.HasValue)
                {
                    _packageAvailable = UnityTypeResolver.ResolveScriptableObject("InputActionAsset") != null;
                }
                return _packageAvailable.Value;
            }
        }

        /// <summary>
        /// Lazily initialises all cached reflection types for the Input System assembly.
        /// Returns true if all required types were resolved successfully.
        /// </summary>
        private static bool EnsureReflectionCache()
        {
            if (_reflectionInitAttempted)
                return _assetType != null;

            _reflectionInitAttempted = true;

            try
            {
                _assetType = Type.GetType("UnityEngine.InputSystem.InputActionAsset, Unity.InputSystem");
                _mapType = Type.GetType("UnityEngine.InputSystem.InputActionMap, Unity.InputSystem");
                _actionType = Type.GetType("UnityEngine.InputSystem.InputAction, Unity.InputSystem");
                _bindingType = Type.GetType("UnityEngine.InputSystem.InputBinding, Unity.InputSystem");
                _controlSchemeType = Type.GetType("UnityEngine.InputSystem.InputControlScheme, Unity.InputSystem");
                _actionTypeEnum = Type.GetType("UnityEngine.InputSystem.InputActionType, Unity.InputSystem");
                _actionMapExtensionsType = Type.GetType("UnityEngine.InputSystem.InputActionSetupExtensions, Unity.InputSystem");
            }
            catch
            {
                // Reflection initialization failed
            }

            return _assetType != null;
        }

        public static object HandleCommand(JObject @params)
        {
            if (!PackageAvailable)
                return new ErrorResponse("PACKAGE_MISSING",
                    "Unity Input System package (com.unity.inputsystem) " +
                    "is not installed. Install it via Package Manager.");

            if (!EnsureReflectionCache())
                return new ErrorResponse("REFLECTION_FAILED",
                    "Failed to resolve Input System types via reflection. " +
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
                    "create_asset" => CreateAsset(p),
                    "get_asset" => GetAsset(p),
                    "add_action_map" => AddActionMap(p),
                    "remove_action_map" => RemoveActionMap(p),
                    "add_action" => AddAction(p),
                    "remove_action" => RemoveAction(p),
                    "rename_action" => RenameAction(p),
                    "add_control_scheme" => AddControlScheme(p),
                    "remove_control_scheme" => RemoveControlScheme(p),
                    "add_bindings" => AddBindings(p),
                    "remove_bindings" => RemoveBindings(p),
                    "add_composite" => AddComposite(p),
                    _ => new ErrorResponse("UNKNOWN_ACTION",
                        $"Unknown action: {action}. Valid actions: create_asset, get_asset, add_action_map, remove_action_map, add_action, remove_action, rename_action, add_control_scheme, remove_control_scheme, add_bindings, remove_bindings, add_composite.")
                };
            }
            catch (TargetInvocationException tie)
            {
                return new ErrorResponse("REFLECTION_ERROR",
                    $"Input System operation failed: {(tie.InnerException ?? tie).Message}");
            }
            catch (Exception ex)
            {
                return new ErrorResponse("OPERATION_ERROR", ex.Message);
            }
        }

        // ─────────────────────────────────────────────
        // 1. create_asset
        // ─────────────────────────────────────────────

        private static object CreateAsset(ToolParams p)
        {
            string path = p.Get("assetPath");
            string mapName = p.Get("map_name") ?? p.Get("mapName");
            string actionName = p.Get("action_name") ?? p.Get("actionName");
            bool overwrite = p.GetBool("overwrite");

            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' parameter is required (e.g. 'Assets/Input/MyControls.inputactions').");

            // Normalize path
            path = AssetPathUtility.SanitizeAssetPath(path);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            if (!path.EndsWith(".inputactions", StringComparison.OrdinalIgnoreCase))
                path += ".inputactions";

            if (!overwrite)
            {
                var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (existing != null)
                    return new ErrorResponse($"InputActionAsset already exists at '{path}'. Set overwrite=true to replace it.");
            }

            try
            {
                // Ensure the directory exists before writing the file.
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                {
                    string[] parts = dir.Replace('\\', '/').Split('/');
                    string current = parts[0];
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string next = current + "/" + parts[i];
                        if (!AssetDatabase.IsValidFolder(next) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, parts[i])))
                            return new ErrorResponse("FOLDER_CREATE_FAILED",
                                $"Could not create folder '{next}'.");
                        current = next;
                    }
                }

                // Write the file, not an asset. AssetDatabase.CreateAsset would emit Unity YAML,
                // which InputActionImporter rejects — the old code produced a DefaultAsset with an
                // ImportLog and still reported success and a GUID, which is why every later action
                // failed with "No InputActionAsset found".
                string assetName = System.IO.Path.GetFileNameWithoutExtension(path);
                System.IO.File.WriteAllText(path, string.Format(EmptyAssetJsonFormat, assetName));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                var asset = LoadAsset(path);
                if (asset == null)
                    return new ErrorResponse("CREATE_FAILED",
                        $"'{path}' was written but did not import as an InputActionAsset.");

                string guid = AssetDatabase.AssetPathToGUID(path);

                // Optionally add initial action map and action
                if (!string.IsNullOrEmpty(mapName))
                {
                    var addMapResult = AddActionMapToAsset(asset, mapName);
                    if (addMapResult is ErrorResponse er)
                    {
                        return new ErrorResponse("CREATE_PARTIAL",
                            $"Asset created but failed to add action map: {er.Error}");
                    }

                    if (!string.IsNullOrEmpty(actionName))
                    {
                        var map = FindActionMap(asset, mapName);
                        if (map != null)
                        {
                            var addActionError = AddActionToMap(map, actionName, null, null);
                            if (addActionError != null)
                            {
                                return new ErrorResponse("CREATE_PARTIAL",
                                    $"Asset created with action map but failed to add action: {addActionError}");
                            }
                        }
                    }

                    var saveError = WriteAssetToDisk(asset, path);
                    if (saveError != null) return saveError;
                }

                return new SuccessResponse(
                    $"Created InputActionAsset at '{path}'.",
                    new { path, guid, hasInitialMap = !string.IsNullOrEmpty(mapName) });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("CREATE_FAILED", $"Failed to create InputActionAsset: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 2. get_asset
        // ─────────────────────────────────────────────

        private static object GetAsset(ToolParams p)
        {
            string path = p.Get("assetPath");
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' parameter is required.");

            path = AssetPathUtility.SanitizeAssetPath(path);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null || !_assetType.IsInstanceOfType(asset))
                return new ErrorResponse($"No InputActionAsset found at '{path}'.");

            try
            {
                bool includeJson = p.GetBool("includeJson", false);
                int jsonCursor = Math.Max(0, p.GetInt("jsonCursor") ?? 0);
                int jsonChunkSize = Math.Max(1, Math.Min(p.GetInt("jsonChunkSize") ?? 8192, 65536));
                var pagination = new PaginationRequest
                {
                    PageSize = p.GetInt("pageSize") ?? p.GetInt("page_size") ?? 50,
                    Cursor = p.GetInt("cursor") ?? 0
                };
                pagination.PageSize = Math.Max(1, Math.Min(pagination.PageSize, 500));

                // Get action maps info
                var mapsInfo = GetActionMapsInfo(asset);
                var schemesInfo = GetControlSchemesInfo(asset);
                var pagedMaps = PaginationResponse<JToken>.Create(mapsInfo.ToList(), pagination);

                string jsonChunk = null;
                int? jsonNextCursor = null;
                int jsonTotalChars = 0;
                if (includeJson)
                {
                    // Call ToJson() via reflection
                    var toJsonMethod = _assetType.GetMethod("ToJson", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (toJsonMethod == null)
                    {
                        // Fallback: try ToJson with parameters
                        toJsonMethod = _assetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(m => m.Name == "ToJson" && m.GetParameters().Length == 0);
                    }

                    string json = toJsonMethod != null
                        ? (string)toJsonMethod.Invoke(asset, null)
                        : SerializeAssetViaReflection(asset);
                    jsonTotalChars = json.Length;
                    if (jsonCursor < jsonTotalChars)
                    {
                        int length = Math.Min(jsonChunkSize, jsonTotalChars - jsonCursor);
                        jsonChunk = json.Substring(jsonCursor, length);
                        jsonNextCursor = jsonCursor + length < jsonTotalChars ? jsonCursor + length : (int?)null;
                    }
                }

                return new SuccessResponse(
                    $"Loaded InputActionAsset from '{path}'.",
                    new
                    {
                        path,
                        guid = AssetDatabase.AssetPathToGUID(path),
                        actionMapCount = mapsInfo.Count,
                        controlSchemeCount = schemesInfo.Count,
                        actionMaps = pagedMaps.Items,
                        controlSchemes = schemesInfo,
                        cursor = pagedMaps.Cursor,
                        nextCursor = pagedMaps.NextCursor,
                        pageSize = pagedMaps.PageSize,
                        hasMore = pagedMaps.HasMore,
                        totalCount = pagedMaps.TotalCount,
                        includeJson,
                        jsonChunk,
                        jsonCursor,
                        jsonNextCursor,
                        jsonTotalChars,
                        jsonChunkSize
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("GET_FAILED", $"Failed to read InputActionAsset: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 3. add_action_map
        // ─────────────────────────────────────────────

        private static object AddActionMap(ToolParams p)
        {
            var pathResult = p.GetRequired("assetPath", "'assetPath' parameter is required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string path = AssetPathUtility.SanitizeAssetPath(pathResult.Value);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            var nameResult = p.GetRequired("map_name", "'map_name' parameter is required.");
            if (!nameResult.IsSuccess)
                return new ErrorResponse(nameResult.ErrorMessage);

            string mapName = nameResult.Value;

            var asset = LoadAsset(path);
            if (asset == null)
                return new ErrorResponse($"No InputActionAsset found at '{path}'.");

            // Check for duplicate name
            if (FindActionMap(asset, mapName) != null)
                return new ErrorResponse($"Action map '{mapName}' already exists in this asset.");

            var error = AddActionMapToAsset(asset, mapName);
            if (error is ErrorResponse er)
                return er;

            var saveError = WriteAssetToDisk(asset, path);
            if (saveError != null) return saveError;

            return new SuccessResponse(
                $"Added action map '{mapName}' to '{path}'.",
                new { path, mapName });
        }

        // ─────────────────────────────────────────────
        // 4. remove_action_map
        // ─────────────────────────────────────────────

        private static object RemoveActionMap(ToolParams p)
        {
            var pathResult = p.GetRequired("assetPath", "'assetPath' parameter is required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string path = AssetPathUtility.SanitizeAssetPath(pathResult.Value);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            string mapName = p.Get("map_name") ?? p.Get("mapName");
            int? mapIndex = p.GetInt("map_index") ?? p.GetInt("mapIndex");

            if (string.IsNullOrEmpty(mapName) && !mapIndex.HasValue)
                return new ErrorResponse("Either 'map_name' or 'map_index' is required.");

            var asset = LoadAsset(path);
            if (asset == null)
                return new ErrorResponse($"No InputActionAsset found at '{path}'.");

            try
            {
                // Get the actionMaps property
                var actionMapsProp = _assetType.GetProperty("actionMaps");
                var actionMapsObj = actionMapsProp.GetValue(asset);

                // Extract the internal array from ReadOnlyArray<T>
                var mapsArray = ExtractArrayFromReadOnlyArray(actionMapsObj);
                if (mapsArray == null)
                    return new ErrorResponse("Failed to read action maps from asset.");

                InputActionMapResolver targetMap = null;
                int targetIndex = -1;

                for (int i = 0; i < mapsArray.Length; i++)
                {
                    var map = new InputActionMapResolver(mapsArray.GetValue(i), _mapType);
                    if ((!string.IsNullOrEmpty(mapName) && map.Name == mapName) ||
                        (mapIndex.HasValue && i == mapIndex.Value))
                    {
                        targetMap = map;
                        targetIndex = i;
                        break;
                    }
                }

                if (targetMap == null)
                {
                    string identifier = !string.IsNullOrEmpty(mapName) ? $"name '{mapName}'" : $"index {mapIndex}";
                    return new ErrorResponse($"Action map with {identifier} not found.");
                }

                // InputActionAsset has no instance RemoveActionMap either.
                var removeMap = SetupMethod("RemoveActionMap", _assetType, _mapType);
                if (removeMap == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "InputActionSetupExtensions.RemoveActionMap(asset, map) is not available.");

                removeMap.Invoke(null, new[] { asset, targetMap.Instance });

                var saveError = WriteAssetToDisk(asset, path);
                if (saveError != null) return saveError;

                return new SuccessResponse(
                    $"Removed action map '{targetMap.Name}' from '{path}'.",
                    new { path, mapName = targetMap.Name });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("REMOVE_MAP_FAILED", $"Failed to remove action map: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 5. add_action
        // ─────────────────────────────────────────────

        private static object AddAction(ToolParams p)
        {
            var pathResult = p.GetRequired("assetPath", "'assetPath' parameter is required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string path = AssetPathUtility.SanitizeAssetPath(pathResult.Value);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            var mapNameResult = p.GetRequired("map_name", "'map_name' parameter is required.");
            if (!mapNameResult.IsSuccess)
                return new ErrorResponse(mapNameResult.ErrorMessage);

            var actionNameResult = p.GetRequired("action_name", "'action_name' parameter is required.");
            if (!actionNameResult.IsSuccess)
                return new ErrorResponse(actionNameResult.ErrorMessage);

            string mapName = mapNameResult.Value;
            string actionName = actionNameResult.Value;
            string actionType = p.Get("action_type") ?? p.Get("actionType");
            string controlLayout = p.Get("control_layout") ?? p.Get("controlLayout") ?? p.Get("expected_control_type") ?? p.Get("expectedControlType");

            var asset = LoadAsset(path);
            if (asset == null)
                return new ErrorResponse($"No InputActionAsset found at '{path}'.");

            var map = FindActionMap(asset, mapName);
            if (map == null)
                return new ErrorResponse($"Action map '{mapName}' not found.");

            // Check for duplicate action name
            if (FindActionInMap(map, actionName) != null)
                return new ErrorResponse($"Action '{actionName}' already exists in map '{mapName}'.");

            string error = AddActionToMap(map, actionName, actionType, controlLayout);
            if (error != null)
                return new ErrorResponse("ADD_ACTION_FAILED", error);

            var saveError = WriteAssetToDisk(asset, path);
            if (saveError != null) return saveError;

            return new SuccessResponse(
                $"Added action '{actionName}' to map '{mapName}'.",
                new { path, mapName, actionName, actionType = actionType ?? "Value", controlLayout = controlLayout ?? "" });
        }

        // ─────────────────────────────────────────────
        // 6. remove_action
        // ─────────────────────────────────────────────

        private static object RemoveAction(ToolParams p)
        {
            var pathResult = p.GetRequired("assetPath", "'assetPath' parameter is required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string path = AssetPathUtility.SanitizeAssetPath(pathResult.Value);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            var mapNameResult = p.GetRequired("map_name", "'map_name' parameter is required.");
            if (!mapNameResult.IsSuccess)
                return new ErrorResponse(mapNameResult.ErrorMessage);

            var actionNameResult = p.GetRequired("action_name", "'action_name' parameter is required.");
            if (!actionNameResult.IsSuccess)
                return new ErrorResponse(actionNameResult.ErrorMessage);

            string mapName = mapNameResult.Value;
            string actionName = actionNameResult.Value;

            var asset = LoadAsset(path);
            if (asset == null)
                return new ErrorResponse($"No InputActionAsset found at '{path}'.");

            var map = FindActionMap(asset, mapName);
            if (map == null)
                return new ErrorResponse($"Action map '{mapName}' not found.");

            var action = FindActionInMap(map, actionName);
            if (action == null)
                return new ErrorResponse($"Action '{actionName}' not found in map '{mapName}'.");

            try
            {
                // InputActionMap has no RemoveAction. The extension takes the action itself and
                // also erases the bindings that referenced it — the private-array surgery this
                // replaces dropped the action and left its bindings behind, pointing at a name
                // nothing answered to.
                var removeAction = SetupMethod("RemoveAction", _actionType);
                if (removeAction == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "InputActionSetupExtensions.RemoveAction(action) is not available.");

                removeAction.Invoke(null, new[] { action.Instance });

                var saveError = WriteAssetToDisk(asset, path);
                if (saveError != null) return saveError;

                return new SuccessResponse(
                    $"Removed action '{actionName}' from map '{mapName}'.",
                    new { path, mapName, actionName });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("REMOVE_ACTION_FAILED", $"Failed to remove action: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 7. rename_action
        // ─────────────────────────────────────────────

        private static object RenameAction(ToolParams p)
        {
            var pathResult = p.GetRequired("assetPath", "'assetPath' parameter is required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string path = AssetPathUtility.SanitizeAssetPath(pathResult.Value);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            var mapNameResult = p.GetRequired("map_name", "'map_name' parameter is required.");
            if (!mapNameResult.IsSuccess)
                return new ErrorResponse(mapNameResult.ErrorMessage);

            var oldNameResult = p.GetRequired("old_name", "'old_name' parameter is required.");
            if (!oldNameResult.IsSuccess)
                return new ErrorResponse(oldNameResult.ErrorMessage);

            var newNameResult = p.GetRequired("new_name", "'new_name' parameter is required.");
            if (!newNameResult.IsSuccess)
                return new ErrorResponse(newNameResult.ErrorMessage);

            string mapName = mapNameResult.Value;
            string oldName = oldNameResult.Value;
            string newName = newNameResult.Value;

            var asset = LoadAsset(path);
            if (asset == null)
                return new ErrorResponse($"No InputActionAsset found at '{path}'.");

            var map = FindActionMap(asset, mapName);
            if (map == null)
                return new ErrorResponse($"Action map '{mapName}' not found.");

            var action = FindActionInMap(map, oldName);
            if (action == null)
                return new ErrorResponse($"Action '{oldName}' not found in map '{mapName}'.");

            // Check no duplicate
            if (oldName != newName && FindActionInMap(map, newName) != null)
                return new ErrorResponse($"Action '{newName}' already exists in map '{mapName}'.");

            try
            {
                // InputAction.name has no setter — renaming goes through
                // InputActionSetupExtensions.Rename, which also rewrites the action reference on
                // every binding that points at it.
                var rename = SetupMethod("Rename", _actionType, typeof(string));
                if (rename == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "InputActionSetupExtensions.Rename(action, string) is not available.");
                rename.Invoke(null, new object[] { action.Instance, newName });
                var saveError = WriteAssetToDisk(asset, path);
                if (saveError != null) return saveError;

                return new SuccessResponse(
                    $"Renamed action '{oldName}' to '{newName}' in map '{mapName}'.",
                    new { path, mapName, oldName, newName });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("RENAME_FAILED", $"Failed to rename action: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 8. add_control_scheme
        // ─────────────────────────────────────────────

        private static object AddControlScheme(ToolParams p)
        {
            var pathResult = p.GetRequired("assetPath", "'assetPath' parameter is required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string path = AssetPathUtility.SanitizeAssetPath(pathResult.Value);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            var nameResult = p.GetRequired("scheme_name", "'scheme_name' parameter is required.");
            if (!nameResult.IsSuccess)
                return new ErrorResponse(nameResult.ErrorMessage);

            string schemeName = nameResult.Value;
            string bindingGroup = p.Get("binding_group") ?? p.Get("bindingGroup") ?? schemeName;

            var asset = LoadAsset(path);
            if (asset == null)
                return new ErrorResponse($"No InputActionAsset found at '{path}'.");

            // Check for duplicate
            var existingSchemes = GetControlSchemesInfo(asset);
            if (existingSchemes != null && existingSchemes.Any(s =>
                string.Equals(s["name"]?.ToString(), schemeName, StringComparison.Ordinal)))
                return new ErrorResponse($"Control scheme '{schemeName}' already exists.");

            // InputControlScheme has a single constructor: (string name,
            // IEnumerable<DeviceRequirement> devices, string bindingGroup). A scheme with no device
            // requirements never activates, so the devices are not optional in practice.
            var requirementType = _controlSchemeType.GetNestedType("DeviceRequirement");
            if (requirementType == null)
                return new ErrorResponse("API_INCOMPATIBLE",
                    "InputControlScheme.DeviceRequirement could not be resolved on this Input System version.");

            var controlPathProp = requirementType.GetProperty("controlPath");
            var isOptionalProp = requirementType.GetProperty("isOptional");
            if (controlPathProp == null || !controlPathProp.CanWrite || isOptionalProp == null || !isOptionalProp.CanWrite)
                return new ErrorResponse("API_INCOMPATIBLE",
                    "InputControlScheme.DeviceRequirement does not expose writable controlPath/isOptional on this Input System version.");

            string[] requiredDevices = p.GetStringArray("requiredDevices") ?? Array.Empty<string>();
            string[] optionalDevices = p.GetStringArray("optionalDevices") ?? Array.Empty<string>();

            var requirements = Array.CreateInstance(requirementType, requiredDevices.Length + optionalDevices.Length);
            int slot = 0;
            foreach (var device in requiredDevices)
                requirements.SetValue(MakeRequirement(requirementType, controlPathProp, isOptionalProp, device, false), slot++);
            foreach (var device in optionalDevices)
                requirements.SetValue(MakeRequirement(requirementType, controlPathProp, isOptionalProp, device, true), slot++);

            try
            {
                var scheme = Activator.CreateInstance(_controlSchemeType, new object[] { schemeName, requirements, bindingGroup });

                // Add via AddControlScheme method on asset
                var addMethod = _assetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "AddControlScheme" && m.GetParameters().Length == 1);

                if (addMethod != null)
                {
                    addMethod.Invoke(asset, new[] { scheme });
                }
                else
                {
                    // Fallback: direct array manipulation
                    var mControlSchemesField = _assetType.GetField("m_ControlSchemes", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mControlSchemesField != null)
                    {
                        var current = (Array)mControlSchemesField.GetValue(asset);
                        var newArray = Array.CreateInstance(_controlSchemeType, (current?.Length ?? 0) + 1);
                        if (current != null)
                        {
                            for (int i = 0; i < current.Length; i++)
                                newArray.SetValue(current.GetValue(i), i);
                        }
                        newArray.SetValue(scheme, newArray.Length - 1);
                        mControlSchemesField.SetValue(asset, newArray);
                    }
                }

                var saveError = WriteAssetToDisk(asset, path);
                if (saveError != null) return saveError;

                return new SuccessResponse(
                    $"Added control scheme '{schemeName}'.",
                    new { path, schemeName, bindingGroup, requiredDevices, optionalDevices });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("ADD_SCHEME_FAILED", $"Failed to add control scheme: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds a boxed InputControlScheme.DeviceRequirement. The type is a struct, so the boxed
        /// instance is mutated through the property setters and stays boxed until it is placed in
        /// the requirements array.
        /// </summary>
        private static object MakeRequirement(Type requirementType, PropertyInfo controlPathProp,
            PropertyInfo isOptionalProp, string devicePath, bool isOptional)
        {
            object requirement = Activator.CreateInstance(requirementType);
            controlPathProp.SetValue(requirement, devicePath);
            isOptionalProp.SetValue(requirement, isOptional);
            return requirement;
        }

        // ─────────────────────────────────────────────
        // 9. remove_control_scheme
        // ─────────────────────────────────────────────

        private static object RemoveControlScheme(ToolParams p)
        {
            var pathResult = p.GetRequired("assetPath", "'assetPath' parameter is required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string path = AssetPathUtility.SanitizeAssetPath(pathResult.Value);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            string schemeName = p.Get("scheme_name") ?? p.Get("schemeName");
            if (string.IsNullOrEmpty(schemeName))
                return new ErrorResponse("'scheme_name' parameter is required.");

            var asset = LoadAsset(path);
            if (asset == null)
                return new ErrorResponse($"No InputActionAsset found at '{path}'.");

            try
            {
                // RemoveControlScheme(asset, name) is an extension, not an instance method, and
                // it takes the name directly — no need to locate the struct first.
                var removeScheme = SetupMethod("RemoveControlScheme", _assetType, typeof(string));
                if (removeScheme == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "InputActionSetupExtensions.RemoveControlScheme(asset, string) is not available.");

                if (FindControlSchemeIndex(asset, schemeName) < 0)
                    return new ErrorResponse("NOT_FOUND", $"Control scheme '{schemeName}' not found.");

                removeScheme.Invoke(null, new object[] { asset, schemeName });

                var saveError = WriteAssetToDisk(asset, path);
                if (saveError != null) return saveError;

                return new SuccessResponse(
                    $"Removed control scheme '{schemeName}'.",
                    new { path, schemeName });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("REMOVE_SCHEME_FAILED", $"Failed to remove control scheme: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 10. add_bindings
        // ─────────────────────────────────────────────

        private static object AddBindings(ToolParams p)
        {
            var pathResult = p.GetRequired("assetPath", "'assetPath' parameter is required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string path = AssetPathUtility.SanitizeAssetPath(pathResult.Value);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            var mapNameResult = p.GetRequired("map_name", "'map_name' parameter is required.");
            if (!mapNameResult.IsSuccess)
                return new ErrorResponse(mapNameResult.ErrorMessage);

            var actionNameResult = p.GetRequired("action_name", "'action_name' parameter is required.");
            if (!actionNameResult.IsSuccess)
                return new ErrorResponse(actionNameResult.ErrorMessage);

            string mapName = mapNameResult.Value;
            string actionName = actionNameResult.Value;

            // Accept an array of control-path strings, an array of binding objects, or the
            // singular `binding`. The Python tool declares list[str], so string entries are the
            // common case; objects allow per-binding overrides.
            JToken bindingsToken = p.GetRaw("bindings");
            JArray bindingsArray;
            if (bindingsToken is JArray array)
            {
                bindingsArray = array;
            }
            else if (!string.IsNullOrEmpty(p.Get("binding")))
            {
                bindingsArray = new JArray { p.Get("binding") };
            }
            else
            {
                return new ErrorResponse("MISSING_PARAMETER",
                    "'bindings' (array of control paths or binding objects) or 'binding' (single control path) is required.");
            }

            var asset = LoadAsset(path);
            if (asset == null)
                return new ErrorResponse($"No InputActionAsset found at '{path}'.");

            var map = FindActionMap(asset, mapName);
            if (map == null)
                return new ErrorResponse($"Action map '{mapName}' not found.");

            var action = FindActionInMap(map, actionName);
            if (action == null)
                return new ErrorResponse($"Action '{actionName}' not found in map '{mapName}'.");

            try
            {
                int added = 0;
                var addedBindings = new JArray();
                var skipped = new JArray();

                // Tool-level defaults, applied to every binding that does not override them.
                string defaultGroups = p.Get("groups");
                string defaultInteractions = p.Get("interactions");
                string defaultProcessors = p.Get("processors");

                foreach (var bindingToken in bindingsArray)
                {
                    string bPath, bGroup, bName, bInteractions, bProcessors;

                    if (bindingToken is JObject bindingObj)
                    {
                        bPath = bindingObj["path"]?.ToString();
                        bGroup = bindingObj["groups"]?.ToString() ?? bindingObj["group"]?.ToString() ?? defaultGroups;
                        bName = bindingObj["name"]?.ToString();
                        bInteractions = bindingObj["interactions"]?.ToString() ?? defaultInteractions;
                        bProcessors = bindingObj["processors"]?.ToString() ?? defaultProcessors;
                    }
                    else if (bindingToken.Type == JTokenType.String)
                    {
                        bPath = bindingToken.ToString();
                        bGroup = defaultGroups;
                        bName = null;
                        bInteractions = defaultInteractions;
                        bProcessors = defaultProcessors;
                    }
                    else
                    {
                        skipped.Add(new JObject { ["entry"] = bindingToken.ToString(), ["reason"] = "unsupported entry type" });
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(bPath))
                    {
                        skipped.Add(new JObject { ["entry"] = bindingToken.ToString(), ["reason"] = "empty control path" });
                        continue;
                    }

                    var error = AddBindingToAction(action, map, bPath, bGroup, bName, bInteractions, bProcessors);
                    if (error != null)
                    {
                        skipped.Add(new JObject { ["entry"] = bPath, ["reason"] = error.ToString() });
                        continue;
                    }

                    added++;
                    addedBindings.Add(new JObject
                    {
                        ["path"] = bPath,
                        ["groups"] = bGroup ?? "",
                        ["name"] = bName ?? "",
                        ["interactions"] = bInteractions ?? "",
                        ["processors"] = bProcessors ?? ""
                    });
                }

                if (added == 0)
                    return new ErrorResponse("NO_BINDINGS_ADDED",
                        $"None of the {bindingsArray.Count} supplied binding(s) could be added to '{actionName}'.",
                        new { path, mapName, actionName, skipped });

                var saveError = WriteAssetToDisk(asset, path);
                if (saveError != null) return saveError;

                return new SuccessResponse(
                    $"Added {added} binding(s) to action '{actionName}'.",
                    new { path, mapName, actionName, added, bindings = addedBindings, skipped });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("ADD_BINDINGS_FAILED", $"Failed to add bindings: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 11. remove_bindings
        // ─────────────────────────────────────────────

        private static object RemoveBindings(ToolParams p)
        {
            var pathResult = p.GetRequired("assetPath", "'assetPath' parameter is required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string path = AssetPathUtility.SanitizeAssetPath(pathResult.Value);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            var mapNameResult = p.GetRequired("map_name", "'map_name' parameter is required.");
            if (!mapNameResult.IsSuccess)
                return new ErrorResponse(mapNameResult.ErrorMessage);

            var actionNameResult = p.GetRequired("action_name", "'action_name' parameter is required.");
            if (!actionNameResult.IsSuccess)
                return new ErrorResponse(actionNameResult.ErrorMessage);

            string mapName = mapNameResult.Value;
            string actionName = actionNameResult.Value;

            // Optional filtering
            int[] indices = p.GetStringArray("indices")?.Select(s =>
            {
                int.TryParse(s, out int idx);
                return idx;
            }).Where(i => i >= 0).ToArray();

            // The tool's own vocabulary for binding paths is 'bindings' / 'binding', the same
            // names add_bindings uses; 'paths' was a third spelling the Python side never sent,
            // so this action could not be reached at all.
            string[] paths = p.GetStringArray("bindings") ?? p.GetStringArray("paths");
            string singleBinding = p.Get("binding");
            if ((paths == null || paths.Length == 0) && !string.IsNullOrEmpty(singleBinding))
                paths = new[] { singleBinding };

            if ((indices == null || indices.Length == 0) && (paths == null || paths.Length == 0))
                return new ErrorResponse("MISSING_PARAMETER",
                    "Removing bindings needs 'binding' (one path), 'bindings' (several) or "
                    + "'indices' (positions within the action's bindings).");

            var asset = LoadAsset(path);
            if (asset == null)
                return new ErrorResponse($"No InputActionAsset found at '{path}'.");

            var map = FindActionMap(asset, mapName);
            if (map == null)
                return new ErrorResponse($"Action map '{mapName}' not found.");

            var action = FindActionInMap(map, actionName);
            if (action == null)
                return new ErrorResponse($"Action '{actionName}' not found in map '{mapName}'.");

            try
            {
                // Get the map's binding array
                var mBindingsField = _mapType.GetField("m_Bindings", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mBindingsField == null)
                    return new ErrorResponse("Cannot access binding data on InputActionMap.");

                var currentBindings = (Array)mBindingsField.GetValue(map.Instance);
                if (currentBindings == null)
                    return new ErrorResponse("No bindings found.");

                // Find the action's binding range in the map's binding array
                var actionIdProp = _actionType.GetProperty("id");
                var actionId = (Guid)actionIdProp.GetValue(action.Instance);

                // Find the binding indices for this action using the action ID or name
                var bindingActionProp = _bindingType.GetProperty("action");
                var bindingIsCompositeProp = _bindingType.GetProperty("isComposite");
                var bindingIsPartOfCompositeProp = _bindingType.GetProperty("isPartOfComposite");
                var bindingPathProp = _bindingType.GetProperty("path");

                var actionBindings = new System.Collections.Generic.List<int>();
                for (int i = 0; i < currentBindings.Length; i++)
                {
                    var binding = currentBindings.GetValue(i);
                    var bindingAction = (string)bindingActionProp.GetValue(binding);
                    var isComposite = (bool)bindingIsCompositeProp.GetValue(binding);
                    var isPartOfComposite = (bool)bindingIsPartOfCompositeProp.GetValue(binding);

                    // Match by action ID (guid) or action name
                    bool matchesAction = bindingAction == actionName || bindingAction == actionId.ToString("D");

                    if (matchesAction || isComposite || isPartOfComposite)
                    {
                        // For composites, include the composite and its parts
                        actionBindings.Add(i);
                    }
                }

                if (actionBindings.Count == 0)
                    return new ErrorResponse($"No bindings found for action '{actionName}'.");

                // Determine which to remove based on filter
                var toRemove = new System.Collections.Generic.HashSet<int>();
                foreach (int idx in actionBindings)
                {
                    bool matchesIndices = indices != null && indices.Contains(idx);
                    bool matchesPaths = false;
                    if (paths != null)
                    {
                        var binding = currentBindings.GetValue(idx);
                        var bPath = (string)bindingPathProp.GetValue(binding);
                        matchesPaths = paths.Any(p => p == bPath);
                    }

                    if ((indices != null && matchesIndices) || (paths != null && matchesPaths))
                        toRemove.Add(idx);
                }

                if (toRemove.Count == 0)
                    return new ErrorResponse("No bindings matched the filter criteria.");

                // Remove from highest index to lowest
                var removeList = toRemove.OrderByDescending(i => i).ToList();

                // Create new array
                var newBindings = Array.CreateInstance(_bindingType, currentBindings.Length - removeList.Count);
                int writeIdx = 0;
                var removeSet = new System.Collections.Generic.HashSet<int>(removeList);
                for (int i = 0; i < currentBindings.Length; i++)
                {
                    if (!removeSet.Contains(i))
                        newBindings.SetValue(currentBindings.GetValue(i), writeIdx++);
                }
                mBindingsField.SetValue(map.Instance, newBindings);

                var saveError = WriteAssetToDisk(asset, path);
                if (saveError != null) return saveError;

                return new SuccessResponse(
                    $"Removed {toRemove.Count} binding(s) from action '{actionName}'.",
                    new { path, mapName, actionName, removed = toRemove.Count });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("REMOVE_BINDINGS_FAILED", $"Failed to remove bindings: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 12. add_composite
        // ─────────────────────────────────────────────

        private static object AddComposite(ToolParams p)
        {
            var pathResult = p.GetRequired("assetPath", "'assetPath' parameter is required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string path = AssetPathUtility.SanitizeAssetPath(pathResult.Value);
            if (path == null)
                return new ErrorResponse("Invalid path: contains traversal sequences.");

            var mapNameResult = p.GetRequired("map_name", "'map_name' parameter is required.");
            if (!mapNameResult.IsSuccess)
                return new ErrorResponse(mapNameResult.ErrorMessage);

            var actionNameResult = p.GetRequired("action_name", "'action_name' parameter is required.");
            if (!actionNameResult.IsSuccess)
                return new ErrorResponse(actionNameResult.ErrorMessage);

            string mapName = mapNameResult.Value;
            string actionName = actionNameResult.Value;

            string compositeType = p.Get("composite_type") ?? p.Get("compositeType");
            if (string.IsNullOrEmpty(compositeType))
                return new ErrorResponse("'composite_type' parameter is required (e.g. '2DVector', '1DAxis', 'ButtonWithOneModifier').");

            string compositeName = p.Get("composite_name") ?? p.Get("compositeName");

            // 'parts' accepts the natural {"up": "<Keyboard>/w", …} map as well as the array of
            // {name, path, groups} objects, because a composite part is exactly a name/path pair.
            JToken partsToken = p.GetRaw("parts");
            var parts = new List<(string Name, string Path, string Groups)>();
            if (partsToken is JObject partsObject)
            {
                foreach (var entry in partsObject)
                {
                    if (!string.IsNullOrEmpty(entry.Key) && entry.Value?.Type == JTokenType.String)
                        parts.Add((entry.Key, entry.Value.ToString(), null));
                }
            }
            else if (partsToken is JArray partsArrayToken)
            {
                foreach (var partToken in partsArrayToken)
                {
                    if (partToken is not JObject partObj) continue;
                    string partName = partObj["name"]?.ToString();
                    string partPath = partObj["path"]?.ToString();
                    if (!string.IsNullOrEmpty(partName) && !string.IsNullOrEmpty(partPath))
                        parts.Add((partName, partPath, partObj["groups"]?.ToString()));
                }
            }

            if (parts.Count == 0)
                return new ErrorResponse("MISSING_PARAMETER",
                    "'parts' is required: either {\"up\": \"<Keyboard>/w\", …} or "
                    + "[{\"name\": \"up\", \"path\": \"<Keyboard>/w\"}, …].");

            var asset = LoadAsset(path);
            if (asset == null)
                return new ErrorResponse($"No InputActionAsset found at '{path}'.");

            var map = FindActionMap(asset, mapName);
            if (map == null)
                return new ErrorResponse($"Action map '{mapName}' not found.");

            var action = FindActionInMap(map, actionName);
            if (action == null)
                return new ErrorResponse($"Action '{actionName}' not found in map '{mapName}'.");

            try
            {
                // AddCompositeBinding writes the composite header binding and returns a syntax
                // object whose With(name, binding, groups) appends each part in the right place.
                // The old code hand-rolled both, using a made-up composite path of "*/{Vector2}"
                // — the composite column holds the composite's *name* ("2DVector"), not a control
                // path — and appended parts as ordinary bindings.
                var addComposite = SetupMethod("AddCompositeBinding", _actionType,
                    typeof(string), typeof(string), typeof(string));
                if (addComposite == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "InputActionSetupExtensions.AddCompositeBinding is not available.");

                object syntax = addComposite.Invoke(null, new object[]
                {
                    action.Instance, compositeName ?? compositeType,
                    p.Get("interactions"), p.Get("processors")
                });
                if (syntax == null)
                    return new ErrorResponse("COMPOSITE_FAILED",
                        $"AddCompositeBinding('{compositeType}') returned nothing.");

                var with = syntax.GetType().GetMethod("With",
                    new[] { typeof(string), typeof(string), typeof(string), typeof(string) });
                if (with == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "CompositeSyntax.With(name, binding, groups, processors) is not available.");

                string defaultGroups = p.Get("groups");
                var addedParts = new JArray();
                foreach (var part in parts)
                {
                    string partGroups = part.Groups ?? defaultGroups;
                    // With returns a new struct each time; the composite it appends to is tracked
                    // by binding index inside the syntax value, so reassign rather than discard.
                    syntax = with.Invoke(syntax, new object[] { part.Name, part.Path, partGroups, null });
                    addedParts.Add(new JObject
                    {
                        ["name"] = part.Name,
                        ["path"] = part.Path,
                        ["groups"] = partGroups ?? ""
                    });
                }

                var saveError = WriteAssetToDisk(asset, path);
                if (saveError != null) return saveError;

                return new SuccessResponse(
                    $"Added composite '{compositeType}' with {parts.Count} part(s) to action '{actionName}'.",
                    new
                    {
                        path,
                        mapName,
                        actionName,
                        compositeType,
                        compositeName = compositeName ?? compositeType,
                        partsAdded = parts.Count,
                        parts = addedParts
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("ADD_COMPOSITE_FAILED", $"Failed to add composite: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // Reflection helpers
        // ─────────────────────────────────────────────

        /// <summary>
        /// Index of a control scheme by name, or -1. Used to tell "removed" from "was never there".
        /// </summary>
        private static int FindControlSchemeIndex(ScriptableObject asset, string schemeName)
        {
            var schemesObj = _assetType.GetProperty("controlSchemes")?.GetValue(asset);
            var schemes = ExtractArrayFromReadOnlyArray(schemesObj);
            if (schemes == null) return -1;

            var nameMember = _controlSchemeType.GetProperty("name")
                ?? (MemberInfo)_controlSchemeType.GetField("name", BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < schemes.Length; i++)
            {
                var scheme = schemes.GetValue(i);
                string name = nameMember is PropertyInfo pi
                    ? (string)pi.GetValue(scheme)
                    : (string)((FieldInfo)nameMember)?.GetValue(scheme);
                if (name == schemeName) return i;
            }
            return -1;
        }

        /// <summary>
        /// Loads an InputActionAsset at the given path via AssetDatabase.
        /// </summary>
        private static ScriptableObject LoadAsset(string path)
        {
            var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (obj != null && _assetType.IsInstanceOfType(obj))
                return obj;
            return null;
        }

        /// <summary>
        /// The smallest text InputActionImporter accepts. A fresh
        /// <c>ScriptableObject.CreateInstance&lt;InputActionAsset&gt;()</c> cannot be serialised at
        /// all — its map array is null and <c>ToJson()</c> throws ArgumentNullException — so new
        /// assets start from this rather than from an instance.
        /// </summary>
        private const string EmptyAssetJsonFormat =
            "{{\n    \"name\": \"{0}\",\n    \"maps\": [],\n    \"controlSchemes\": []\n}}";

        /// <summary>
        /// Persists an InputActionAsset back to its file. Returns an ErrorResponse on failure,
        /// or null on success.
        /// </summary>
        /// <remarks>
        /// A `.inputactions` file is JSON read by InputActionImporter, a ScriptedImporter — it is
        /// not a Unity YAML asset. EditorUtility.SetDirty + AssetDatabase.SaveAssets, which every
        /// mutating action used to call, writes nothing back through a ScriptedImporter: the edit
        /// lives only in the imported object and is discarded on the next reimport. The file has
        /// to be rewritten from ToJson() and reimported.
        /// </remarks>
        private static object WriteAssetToDisk(ScriptableObject asset, string path)
        {
            try
            {
                var toJson = _assetType.GetMethod("ToJson",
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (toJson == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "InputActionAsset.ToJson() is not available in this Input System version.");

                string json = (string)toJson.Invoke(asset, null);
                if (string.IsNullOrEmpty(json))
                    return new ErrorResponse("SERIALIZE_FAILED",
                        $"InputActionAsset at '{path}' serialised to nothing; refusing to overwrite it.");

                string previousJson = System.IO.File.Exists(path)
                    ? System.IO.File.ReadAllText(path)
                    : null;

                try
                {
                    System.IO.File.WriteAllText(path, json);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                    if (LoadAsset(path) == null)
                        throw new InvalidOperationException(
                            $"'{path}' no longer imports as an InputActionAsset after saving.");
                }
                catch
                {
                    if (previousJson != null)
                    {
                        System.IO.File.WriteAllText(path, previousJson);
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    }
                    throw;
                }

                return null;
            }
            catch (Exception ex)
            {
                return new ErrorResponse("SAVE_FAILED",
                    $"Failed to write InputActionAsset to '{path}': {(ex.InnerException ?? ex).Message}");
            }
        }

        /// <summary>
        /// Finds an action map by name within an InputActionAsset using reflection.
        /// </summary>
        private static InputActionMapResolver FindActionMap(ScriptableObject asset, string name)
        {
            var actionMapsProp = _assetType.GetProperty("actionMaps");
            if (actionMapsProp == null)
                return null;

            var actionMapsObj = actionMapsProp.GetValue(asset);
            var mapsArray = ExtractArrayFromReadOnlyArray(actionMapsObj);
            if (mapsArray == null)
                return null;

            for (int i = 0; i < mapsArray.Length; i++)
            {
                var map = new InputActionMapResolver(mapsArray.GetValue(i), _mapType);
                if (map.Name == name)
                    return map;
            }

            return null;
        }

        /// <summary>
        /// Finds an action by name within an action map using reflection.
        /// </summary>
        private static InputActionResolver FindActionInMap(InputActionMapResolver map, string actionName)
        {
            var actionsProp = _mapType.GetProperty("actions");
            if (actionsProp == null)
                return null;

            var actionsObj = actionsProp.GetValue(map.Instance);
            var actionsArray = ExtractArrayFromReadOnlyArray(actionsObj);
            if (actionsArray == null)
                return null;

            var nameProp = _actionType.GetProperty("name");

            for (int i = 0; i < actionsArray.Length; i++)
            {
                var action = actionsArray.GetValue(i);
                var n = (string)nameProp.GetValue(action);
                if (n == actionName)
                    return new InputActionResolver(action, _actionType);
            }

            return null;
        }

        /// <summary>
        /// Adds an action map to an InputActionAsset via reflection.
        /// </summary>
        /// <summary>
        /// Resolves a static method on InputActionSetupExtensions, the public API for editing
        /// action assets. Everything this tool mutates lives there as an extension method —
        /// InputActionAsset, InputActionMap and InputAction have no instance equivalents, which is
        /// what the reflection ladders this replaced were fruitlessly searching for.
        /// </summary>
        private static MethodInfo SetupMethod(string name, params Type[] parameterTypes)
        {
            return _actionMapExtensionsType?.GetMethod(name,
                BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null);
        }

        private static object AddActionMapToAsset(ScriptableObject asset, string mapName)
        {
            try
            {
                var addMap = SetupMethod("AddActionMap", _assetType, typeof(string));
                if (addMap == null)
                    return new ErrorResponse("API_INCOMPATIBLE",
                        "InputActionSetupExtensions.AddActionMap(asset, string) is not available.");

                addMap.Invoke(null, new object[] { asset, mapName });
                return new SuccessResponse($"Added action map '{mapName}'.", new { mapName });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("ADD_MAP_FAILED",
                    $"Failed to add action map: {(ex.InnerException ?? ex).Message}");
            }
        }

        /// <summary>
        /// Adds an action to an action map. Returns null on success, or an error message.
        /// </summary>
        private static string AddActionToMap(InputActionMapResolver map, string actionName, string actionType, string controlLayout)
        {
            object actionTypeValue = Enum.ToObject(_actionTypeEnum, 0);
            if (!string.IsNullOrEmpty(actionType))
            {
                try
                {
                    actionTypeValue = Enum.Parse(_actionTypeEnum, actionType, ignoreCase: true);
                }
                catch
                {
                    return $"Invalid action_type '{actionType}'. Valid values: Value, Button, PassThrough.";
                }
            }

            try
            {
                // AddAction(map, name, type, binding, interactions, processors, groups, expectedControlLayout)
                var addAction = SetupMethod("AddAction", _mapType, typeof(string), _actionTypeEnum,
                    typeof(string), typeof(string), typeof(string), typeof(string), typeof(string));
                if (addAction == null)
                    return "InputActionSetupExtensions.AddAction is not available in this Input System version.";

                addAction.Invoke(null, new object[]
                {
                    map.Instance, actionName, actionTypeValue, null, null, null, null,
                    string.IsNullOrEmpty(controlLayout) ? null : controlLayout
                });
                return null;
            }
            catch (Exception ex)
            {
                return (ex.InnerException ?? ex).Message;
            }
        }

        /// <summary>
        /// Adds a single binding to an action. Returns null on success, or an error message.
        /// </summary>
        /// <remarks>
        /// Goes through InputActionSetupExtensions.AddBinding, which appends to the owning map's
        /// binding array and fixes up the action's binding range. The private m_Bindings surgery
        /// this replaces did neither reliably.
        /// </remarks>
        private static string AddBindingToAction(InputActionResolver action, InputActionMapResolver map,
            string path, string groups, string name, string interactions, string processors,
            bool isComposite = false, bool isPartOfComposite = false)
        {
            try
            {
                // AddBinding(action, path, interactions, processors, groups)
                var addBinding = SetupMethod("AddBinding", _actionType,
                    typeof(string), typeof(string), typeof(string), typeof(string));
                if (addBinding == null)
                    return "InputActionSetupExtensions.AddBinding is not available in this Input System version.";

                object syntax = addBinding.Invoke(null, new object[]
                {
                    action.Instance, path,
                    string.IsNullOrEmpty(interactions) ? null : interactions,
                    string.IsNullOrEmpty(processors) ? null : processors,
                    string.IsNullOrEmpty(groups) ? null : groups
                });

                if (!string.IsNullOrEmpty(name) && syntax != null)
                {
                    var withName = syntax.GetType().GetMethod("WithName", new[] { typeof(string) });
                    withName?.Invoke(syntax, new object[] { name });
                }

                return null;
            }
            catch (Exception ex)
            {
                return (ex.InnerException ?? ex).Message;
            }
        }

        /// <summary>
        /// Extracts metadata about all action maps in an asset for the response.
        /// </summary>
        private static JArray GetActionMapsInfo(ScriptableObject asset)
        {
            var result = new JArray();

            try
            {
                var actionMapsProp = _assetType.GetProperty("actionMaps");
                if (actionMapsProp == null)
                    return result;

                var actionMapsObj = actionMapsProp.GetValue(asset);
                var mapsArray = ExtractArrayFromReadOnlyArray(actionMapsObj);
                if (mapsArray == null)
                    return result;

                var nameProp = _mapType.GetProperty("name");
                var actionsProp = _mapType.GetProperty("actions");

                for (int i = 0; i < mapsArray.Length; i++)
                {
                    var mapObj = mapsArray.GetValue(i);
                    var mapName = (string)nameProp.GetValue(mapObj);

                    var actionsObj = actionsProp.GetValue(mapObj);
                    var actionsArray = ExtractArrayFromReadOnlyArray(actionsObj);
                    int actionCount = actionsArray?.Length ?? 0;

                    result.Add(new JObject
                    {
                        ["name"] = mapName,
                        ["actionCount"] = actionCount,
                        ["index"] = i
                    });
                }
            }
            catch
            {
                // Best effort
            }

            return result;
        }

        /// <summary>
        /// Extracts metadata about all control schemes in an asset for the response.
        /// </summary>
        private static JArray GetControlSchemesInfo(ScriptableObject asset)
        {
            var result = new JArray();

            try
            {
                var schemesProp = _assetType.GetProperty("controlSchemes");
                if (schemesProp == null)
                    return result;

                var schemesObj = schemesProp.GetValue(asset);
                var schemesArray = ExtractArrayFromReadOnlyArray(schemesObj);
                if (schemesArray == null)
                    return result;

                var nameMember = _controlSchemeType.GetProperty("name") ?? (MemberInfo)_controlSchemeType.GetField("name", BindingFlags.Public | BindingFlags.Instance);
                var bindingGroupMember = _controlSchemeType.GetProperty("bindingGroup") ?? (MemberInfo)_controlSchemeType.GetField("bindingGroup", BindingFlags.Public | BindingFlags.Instance);

                for (int i = 0; i < schemesArray.Length; i++)
                {
                    var s = schemesArray.GetValue(i);
                    var name = nameMember != null
                        ? (nameMember is PropertyInfo pi ? (string)pi.GetValue(s) : (string)((FieldInfo)nameMember).GetValue(s))
                        : $"Scheme_{i}";
                    var bindingGroup = bindingGroupMember != null
                        ? (bindingGroupMember is PropertyInfo pi2 ? (string)pi2.GetValue(s) : (string)((FieldInfo)bindingGroupMember).GetValue(s))
                        : "";

                    result.Add(new JObject
                    {
                        ["name"] = name,
                        ["bindingGroup"] = bindingGroup,
                        ["index"] = i
                    });
                }
            }
            catch
            {
                // Best effort
            }

            return result;
        }

        /// <summary>
        /// Serializes an InputActionAsset to JSON via reflection on its fields.
        /// Used as fallback if ToJson() method is not available.
        /// </summary>
        private static string SerializeAssetViaReflection(ScriptableObject asset)
        {
            var mapsInfo = GetActionMapsInfo(asset);
            var schemesInfo = GetControlSchemesInfo(asset);

            var obj = new JObject
            {
                ["name"] = asset.name,
                ["actionMaps"] = mapsInfo,
                ["controlSchemes"] = schemesInfo
            };

            return obj.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>
        /// Extracts the internal array from a ReadOnlyArray{T} struct.
        /// </summary>
        private static Array ExtractArrayFromReadOnlyArray(object readOnlyArrayObj)
        {
            if (readOnlyArrayObj == null)
                return null;

            var type = readOnlyArrayObj.GetType();

            // Try m_Array field first (internal)
            var arrayField = type.GetField("m_Array", BindingFlags.NonPublic | BindingFlags.Instance);
            if (arrayField != null)
                return (Array)arrayField.GetValue(readOnlyArrayObj);

            // Try items or Items
            arrayField = type.GetField("items", BindingFlags.NonPublic | BindingFlags.Instance) ??
                         type.GetField("Items", BindingFlags.NonPublic | BindingFlags.Instance);
            if (arrayField != null)
                return (Array)arrayField.GetValue(readOnlyArrayObj);

            return null;
        }

        // ─────────────────────────────────────────────
        // Lightweight reflection wrappers
        // ─────────────────────────────────────────────

        /// <summary>
        /// Wraps an InputActionMap instance for reflection-based property access.
        /// </summary>
        private sealed class InputActionMapResolver
        {
            public object Instance { get; }
            private readonly Type _type;
            private PropertyInfo _nameProp;

            public InputActionMapResolver(object instance, Type type)
            {
                Instance = instance;
                _type = type;
                _nameProp = _type.GetProperty("name");
            }

            public string Name => _nameProp != null ? (string)_nameProp.GetValue(Instance) : null;
        }

        /// <summary>
        /// Wraps an InputAction instance for reflection-based property access.
        /// </summary>
        private sealed class InputActionResolver
        {
            public object Instance { get; }
            private readonly Type _type;
            private PropertyInfo _nameProp;

            public InputActionResolver(object instance, Type type)
            {
                Instance = instance;
                _type = type;
                _nameProp = _type.GetProperty("name");
            }

            public string Name => _nameProp != null ? (string)_nameProp.GetValue(Instance) : null;
        }
    }
}
