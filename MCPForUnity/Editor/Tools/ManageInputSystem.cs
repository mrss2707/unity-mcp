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
                    _packageAvailable = UnityTypeResolver.ResolveComponent("InputActionAsset") != null;
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
            string path = p.Get("path");
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
                var asset = ScriptableObject.CreateInstance(_assetType);
                if (asset == null)
                    return new ErrorResponse("Failed to create InputActionAsset instance.");

                string guid;
                if (overwrite)
                {
                    var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (existing != null)
                    {
                        EditorUtility.CopySerialized(asset, existing);
                        UnityEngine.Object.DestroyImmediate(asset);
                        asset = existing;
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssets();
                        guid = AssetDatabase.AssetPathToGUID(path);
                    }
                    else
                    {
                        AssetDatabase.CreateAsset(asset, path);
                        AssetDatabase.SaveAssets();
                        guid = AssetDatabase.AssetPathToGUID(path);
                    }
                }
                else
                {
                    // Ensure directory exists
                    string dir = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                    {
                        string[] parts = dir.Replace('\\', '/').Split('/');
                        string current = parts[0];
                        for (int i = 1; i < parts.Length; i++)
                        {
                            string next = current + "/" + parts[i];
                            if (!AssetDatabase.IsValidFolder(next))
                                AssetDatabase.CreateFolder(current, parts[i]);
                            current = next;
                        }
                    }

                    AssetDatabase.CreateAsset(asset, path);
                    AssetDatabase.SaveAssets();
                    guid = AssetDatabase.AssetPathToGUID(path);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

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

                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssets();
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
            string path = p.Get("path");
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

                // Get action maps info
                var mapsInfo = GetActionMapsInfo(asset);
                var schemesInfo = GetControlSchemesInfo(asset);

                return new SuccessResponse(
                    $"Loaded InputActionAsset from '{path}'.",
                    new
                    {
                        path,
                        guid = AssetDatabase.AssetPathToGUID(path),
                        json,
                        actionMaps = mapsInfo,
                        controlSchemes = schemesInfo
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
            var pathResult = p.GetRequired("path", "'path' parameter is required.");
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

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            return new SuccessResponse(
                $"Added action map '{mapName}' to '{path}'.",
                new { path, mapName });
        }

        // ─────────────────────────────────────────────
        // 4. remove_action_map
        // ─────────────────────────────────────────────

        private static object RemoveActionMap(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter is required.");
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

                // Remove via RemoveActionMap method
                var removeMethod = _assetType.GetMethod("RemoveActionMap", BindingFlags.Public | BindingFlags.Instance);
                if (removeMethod != null)
                {
                    removeMethod.Invoke(asset, new[] { targetMap.Instance });
                }
                else
                {
                    // Fallback: direct array manipulation
                    var mActionMapsField = _assetType.GetField("m_ActionMaps", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mActionMapsField != null)
                    {
                        var currentMaps = (Array)mActionMapsField.GetValue(asset);
                        var newMaps = Array.CreateInstance(_mapType, currentMaps.Length - 1);
                        for (int i = 0, j = 0; i < currentMaps.Length; i++)
                        {
                            if (i != targetIndex)
                                newMaps.SetValue(currentMaps.GetValue(i), j++);
                        }
                        mActionMapsField.SetValue(asset, newMaps);
                    }
                }

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

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
            var pathResult = p.GetRequired("path", "'path' parameter is required.");
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

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            return new SuccessResponse(
                $"Added action '{actionName}' to map '{mapName}'.",
                new { path, mapName, actionName, actionType = actionType ?? "Value", controlLayout = controlLayout ?? "" });
        }

        // ─────────────────────────────────────────────
        // 6. remove_action
        // ─────────────────────────────────────────────

        private static object RemoveAction(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter is required.");
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
                // Use RemoveAction method on InputActionMap if available
                var removeMethod = _mapType.GetMethod("RemoveAction", BindingFlags.Public | BindingFlags.Instance);
                if (removeMethod != null)
                {
                    removeMethod.Invoke(map.Instance, new[] { action.Instance });
                }
                else
                {
                    // Fallback: direct array manipulation on m_Actions
                    var mActionsField = _mapType.GetField("m_Actions", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mActionsField != null)
                    {
                        var currentActions = (Array)mActionsField.GetValue(map.Instance);
                        var actionNameProp = _actionType.GetProperty("name");
                        int removeIdx = -1;
                        for (int i = 0; i < currentActions.Length; i++)
                        {
                            var a = currentActions.GetValue(i);
                            var n = (string)actionNameProp.GetValue(a);
                            if (n == actionName)
                            {
                                removeIdx = i;
                                break;
                            }
                        }

                        if (removeIdx >= 0)
                        {
                            var newActions = Array.CreateInstance(_actionType, currentActions.Length - 1);
                            for (int i = 0, j = 0; i < currentActions.Length; i++)
                            {
                                if (i != removeIdx)
                                    newActions.SetValue(currentActions.GetValue(i), j++);
                            }
                            mActionsField.SetValue(map.Instance, newActions);
                        }
                    }
                }

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

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
            var pathResult = p.GetRequired("path", "'path' parameter is required.");
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
                var nameProp = _actionType.GetProperty("name");
                nameProp.SetValue(action.Instance, newName);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

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
            var pathResult = p.GetRequired("path", "'path' parameter is required.");
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

            try
            {
                // Create InputControlScheme struct
                var scheme = Activator.CreateInstance(_controlSchemeType, new object[] { schemeName, bindingGroup });

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

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new SuccessResponse(
                    $"Added control scheme '{schemeName}'.",
                    new { path, schemeName, bindingGroup });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("ADD_SCHEME_FAILED", $"Failed to add control scheme: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 9. remove_control_scheme
        // ─────────────────────────────────────────────

        private static object RemoveControlScheme(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter is required.");
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
                // Try RemoveControlScheme method
                var removeMethod = _assetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "RemoveControlScheme");

                if (removeMethod != null)
                {
                    // Find the scheme first
                    var schemesProp = _assetType.GetProperty("controlSchemes");
                    var schemesObj = schemesProp.GetValue(asset);
                    var schemesArray = ExtractArrayFromReadOnlyArray(schemesObj);

                    object schemeToRemove = null;
                    var nameField = _controlSchemeType.GetProperty("name") ?? (MemberInfo)_controlSchemeType.GetField("name", BindingFlags.Public | BindingFlags.Instance);

                    if (schemesArray != null)
                    {
                        for (int i = 0; i < schemesArray.Length; i++)
                        {
                            var s = schemesArray.GetValue(i);
                            var n = nameField != null
                                ? (string)((nameField is PropertyInfo pi ? pi.GetValue(s) : ((FieldInfo)nameField).GetValue(s)))
                                : null;
                            if (n == schemeName)
                            {
                                schemeToRemove = s;
                                break;
                            }
                        }
                    }

                    if (schemeToRemove == null)
                        return new ErrorResponse($"Control scheme '{schemeName}' not found.");

                    removeMethod.Invoke(asset, new[] { schemeToRemove });
                }
                else
                {
                    // Fallback: direct array manipulation
                    var mControlSchemesField = _assetType.GetField("m_ControlSchemes", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mControlSchemesField == null)
                        return new ErrorResponse("Cannot remove control scheme: no supported API found.");

                    var current = (Array)mControlSchemesField.GetValue(asset);
                    if (current == null)
                        return new ErrorResponse("No control schemes found.");

                    var nameProp = _controlSchemeType.GetProperty("name") ?? (MemberInfo)_controlSchemeType.GetField("name", BindingFlags.Public | BindingFlags.Instance);
                    int removeIdx = -1;
                    for (int i = 0; i < current.Length; i++)
                    {
                        var s = current.GetValue(i);
                        var n = nameProp is PropertyInfo pi
                            ? (string)pi.GetValue(s)
                            : (string)((FieldInfo)nameProp).GetValue(s);
                        if (n == schemeName)
                        {
                            removeIdx = i;
                            break;
                        }
                    }

                    if (removeIdx < 0)
                        return new ErrorResponse($"Control scheme '{schemeName}' not found.");

                    var newArray = Array.CreateInstance(_controlSchemeType, current.Length - 1);
                    for (int i = 0, j = 0; i < current.Length; i++)
                    {
                        if (i != removeIdx)
                            newArray.SetValue(current.GetValue(i), j++);
                    }
                    mControlSchemesField.SetValue(asset, newArray);
                }

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

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
            var pathResult = p.GetRequired("path", "'path' parameter is required.");
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

            JToken bindingsToken = p.GetRaw("bindings");
            if (bindingsToken == null || bindingsToken.Type != JTokenType.Array)
                return new ErrorResponse("'bindings' parameter is required (array of binding objects).");

            var bindingsArray = (JArray)bindingsToken;

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

                foreach (var bindingToken in bindingsArray)
                {
                    if (bindingToken is not JObject bindingObj)
                        continue;

                    string bPath = bindingObj["path"]?.ToString() ?? "";
                    string bGroup = bindingObj["groups"]?.ToString() ?? bindingObj["group"]?.ToString();
                    string bName = bindingObj["name"]?.ToString();
                    string bInteractions = bindingObj["interactions"]?.ToString();
                    string bProcessors = bindingObj["processors"]?.ToString();

                    var error = AddBindingToAction(action, map, bPath, bGroup, bName, bInteractions, bProcessors);
                    if (error == null)
                    {
                        added++;
                        addedBindings.Add(new JObject
                        {
                            ["path"] = bPath,
                            ["groups"] = bGroup ?? "",
                            ["name"] = bName ?? ""
                        });
                    }
                }

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new SuccessResponse(
                    $"Added {added} binding(s) to action '{actionName}'.",
                    new { path, mapName, actionName, added, bindings = addedBindings });
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
            var pathResult = p.GetRequired("path", "'path' parameter is required.");
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

            JToken pathsToken = p.GetRaw("paths");
            string[] paths = null;
            if (pathsToken is JArray pathsArray)
                paths = pathsArray.Select(t => t.ToString()).ToArray();

            if ((indices == null || indices.Length == 0) && (paths == null || paths.Length == 0))
                return new ErrorResponse("Either 'indices' (array of integers) or 'paths' (array of binding paths) is required.");

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

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

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
            var pathResult = p.GetRequired("path", "'path' parameter is required.");
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
            JToken partsToken = p.GetRaw("parts");
            if (partsToken == null || partsToken.Type != JTokenType.Array)
                return new ErrorResponse("'parts' parameter is required (array of part binding objects with 'path' and 'name').");

            var partsArray = (JArray)partsToken;

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
                // Get the composite path based on type
                string compositePath = GetCompositePath(compositeType);
                if (compositePath == null)
                    return new ErrorResponse($"Unknown composite type: '{compositeType}'. Valid types: 2DVector, 1DAxis, ButtonWithOneModifier, ButtonWithTwoModifiers, Dpad.");

                // Create the composite binding entry
                var compositeBindingError = AddBindingToAction(action, map, compositePath, null, compositeName ?? compositeType, null, null, isComposite: true);
                if (compositeBindingError != null)
                    return new ErrorResponse("COMPOSITE_FAILED", $"Failed to add composite binding: {compositeBindingError}");

                // Create part bindings
                int partsAdded = 0;
                var addedParts = new JArray();

                foreach (var partToken in partsArray)
                {
                    if (partToken is not JObject partObj)
                        continue;

                    string partPath = partObj["path"]?.ToString();
                    string partName = partObj["name"]?.ToString();
                    string partGroups = partObj["groups"]?.ToString();

                    if (string.IsNullOrEmpty(partPath) || string.IsNullOrEmpty(partName))
                        continue;

                    var partError = AddBindingToAction(action, map, partPath, partGroups, partName, null, null, isPartOfComposite: true);
                    if (partError == null)
                    {
                        partsAdded++;
                        addedParts.Add(new JObject
                        {
                            ["name"] = partName,
                            ["path"] = partPath,
                            ["groups"] = partGroups ?? ""
                        });
                    }
                }

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                return new SuccessResponse(
                    $"Added composite '{compositeType}' with {partsAdded} part(s) to action '{actionName}'.",
                    new
                    {
                        path,
                        mapName,
                        actionName,
                        compositeType,
                        compositeName = compositeName ?? compositeType,
                        partsAdded,
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
        private static object AddActionMapToAsset(ScriptableObject asset, string mapName)
        {
            try
            {
                var addMethod = _assetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "AddActionMap" && m.GetParameters().Length == 1 &&
                                         m.GetParameters()[0].ParameterType == typeof(string));

                if (addMethod != null)
                {
                    var result = addMethod.Invoke(asset, new object[] { mapName });
                    return new SuccessResponse($"Added action map '{mapName}'.", new { mapName });
                }

                // Fallback: create InputActionMap via constructor and add via AddActionMap(InputActionMap) overload
                var mapCtor = _mapType.GetConstructor(new[] { typeof(string) });
                if (mapCtor != null)
                {
                    var newMap = mapCtor.Invoke(new object[] { mapName });

                    // Set m_Asset back-reference
                    var mAssetField = _mapType.GetField("m_Asset", BindingFlags.NonPublic | BindingFlags.Instance);
                    mAssetField?.SetValue(newMap, asset);

                    var addMapOverload = _assetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "AddActionMap" && m.GetParameters().Length == 1 &&
                                             m.GetParameters()[0].ParameterType == _mapType);

                    if (addMapOverload != null)
                    {
                        addMapOverload.Invoke(asset, new[] { newMap });
                        return new SuccessResponse($"Added action map '{mapName}'.", new { mapName });
                    }
                }

                // Last resort: direct m_ActionMaps array manipulation
                var mActionMapsField = _assetType.GetField("m_ActionMaps", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mActionMapsField == null)
                    return new ErrorResponse("Cannot add action map: no supported API found.");

                var mapCtorSimple = _mapType.GetConstructor(Type.EmptyTypes);
                if (mapCtorSimple == null)
                    return new ErrorResponse("Cannot add action map: no default constructor on InputActionMap.");

                var newMapDirect = mapCtorSimple.Invoke(null);

                var nameProp = _mapType.GetProperty("name");
                nameProp?.SetValue(newMapDirect, mapName);

                var mAssetFieldDirect = _mapType.GetField("m_Asset", BindingFlags.NonPublic | BindingFlags.Instance);
                mAssetFieldDirect?.SetValue(newMapDirect, asset);

                var currentMaps = (Array)mActionMapsField.GetValue(asset);
                var newMapsArray = Array.CreateInstance(_mapType, (currentMaps?.Length ?? 0) + 1);
                if (currentMaps != null)
                {
                    for (int i = 0; i < currentMaps.Length; i++)
                        newMapsArray.SetValue(currentMaps.GetValue(i), i);
                }
                newMapsArray.SetValue(newMapDirect, newMapsArray.Length - 1);
                mActionMapsField.SetValue(asset, newMapsArray);

                return new SuccessResponse($"Added action map '{mapName}'.", new { mapName });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("ADD_MAP_FAILED", $"Failed to add action map: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds an action to an action map via reflection.
        /// </summary>
        private static string AddActionToMap(InputActionMapResolver map, string actionName, string actionType, string controlLayout)
        {
            try
            {
                // Determine the action type enum value
                object actionTypeValue = null;
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

                // Try AddAction with type and layout parameters
                if (actionTypeValue != null || !string.IsNullOrEmpty(controlLayout))
                {
                    var addActionFull = _mapType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "AddAction" && m.GetParameters().Length >= 2);

                    if (addActionFull != null)
                    {
                        var parameters = addActionFull.GetParameters();
                        var args = new object[parameters.Length];
                        args[0] = actionName;

                        for (int i = 1; i < parameters.Length; i++)
                        {
                            if (parameters[i].ParameterType == _actionTypeEnum)
                                args[i] = actionTypeValue ?? Enum.GetValues(_actionTypeEnum).GetValue(0);
                            else if (parameters[i].ParameterType == typeof(string))
                                args[i] = controlLayout ?? (parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null);
                            else
                                args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
                        }

                        addActionFull.Invoke(map.Instance, args);
                        return null;
                    }
                }

                // Try simple AddAction(name)
                var addActionSimple = _mapType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "AddAction" && m.GetParameters().Length == 1 &&
                                         m.GetParameters()[0].ParameterType == typeof(string));

                if (addActionSimple != null)
                {
                    var newAction = addActionSimple.Invoke(map.Instance, new object[] { actionName });

                    // Set expectedControlLayout and type if provided
                    if (!string.IsNullOrEmpty(controlLayout))
                    {
                        var ctrlLayoutProp = _actionType.GetProperty("expectedControlType");
                        ctrlLayoutProp?.SetValue(newAction, controlLayout);
                    }

                    if (actionTypeValue != null)
                    {
                        var typeProp = _actionType.GetProperty("type");
                        typeProp?.SetValue(newAction, actionTypeValue);
                    }

                    return null;
                }

                // Fallback: direct m_Actions array manipulation
                var mActionsField = _mapType.GetField("m_Actions", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mActionsField == null)
                    return "Cannot add action: no supported API found.";

                var actionCtor = _actionType.GetConstructor(new[] { typeof(string) }) ??
                                 _actionType.GetConstructor(Type.EmptyTypes);
                if (actionCtor == null)
                    return "Cannot add action: no suitable constructor on InputAction.";

                object newActionObj;
                var actionNameProp = _actionType.GetProperty("name");

                if (actionCtor.GetParameters().Length == 1)
                {
                    newActionObj = actionCtor.Invoke(new object[] { actionName });
                }
                else
                {
                    newActionObj = actionCtor.Invoke(null);
                    actionNameProp?.SetValue(newActionObj, actionName);
                }

                if (actionTypeValue != null)
                {
                    var typeProp = _actionType.GetProperty("type");
                    typeProp?.SetValue(newActionObj, actionTypeValue);
                }

                if (!string.IsNullOrEmpty(controlLayout))
                {
                    var ctrlLayoutProp = _actionType.GetProperty("expectedControlType");
                    ctrlLayoutProp?.SetValue(newActionObj, controlLayout);
                }

                var currentActions = (Array)mActionsField.GetValue(map.Instance);
                var newActionsArray = Array.CreateInstance(_actionType, (currentActions?.Length ?? 0) + 1);
                if (currentActions != null)
                {
                    for (int i = 0; i < currentActions.Length; i++)
                        newActionsArray.SetValue(currentActions.GetValue(i), i);
                }
                newActionsArray.SetValue(newActionObj, newActionsArray.Length - 1);
                mActionsField.SetValue(map.Instance, newActionsArray);

                return null;
            }
            catch (Exception ex)
            {
                return ex.InnerException?.Message ?? ex.Message;
            }
        }

        /// <summary>
        /// Adds a single binding to an action within its map via reflection.
        /// </summary>
        private static string AddBindingToAction(InputActionResolver action, InputActionMapResolver map,
            string path, string groups, string name, string interactions, string processors,
            bool isComposite = false, bool isPartOfComposite = false)
        {
            try
            {
                var mBindingsField = _mapType.GetField("m_Bindings", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mBindingsField == null)
                    return "Cannot access binding storage on InputActionMap.";

                var currentBindings = (Array)mBindingsField.GetValue(map.Instance);
                int currentLen = currentBindings?.Length ?? 0;

                var newBindings = Array.CreateInstance(_bindingType, currentLen + 1);

                // Copy existing bindings
                if (currentBindings != null)
                {
                    for (int i = 0; i < currentLen; i++)
                        newBindings.SetValue(currentBindings.GetValue(i), i);
                }

                // Create new InputBinding
                var binding = Activator.CreateInstance(_bindingType);

                var pathProp = _bindingType.GetProperty("path");
                var actionProp = _bindingType.GetProperty("action");
                var groupsProp = _bindingType.GetProperty("groups");
                var nameProp = _bindingType.GetProperty("name");
                var interactionsProp = _bindingType.GetProperty("interactions");
                var processorsProp = _bindingType.GetProperty("processors");
                var isCompositeProp = _bindingType.GetProperty("isComposite");
                var isPartOfCompositeProp = _bindingType.GetProperty("isPartOfComposite");

                pathProp?.SetValue(binding, path);
                actionProp?.SetValue(binding, action.Name);
                groupsProp?.SetValue(binding, groups ?? "");
                nameProp?.SetValue(binding, name ?? "");
                interactionsProp?.SetValue(binding, interactions ?? "");
                processorsProp?.SetValue(binding, processors ?? "");
                isCompositeProp?.SetValue(binding, isComposite);
                isPartOfCompositeProp?.SetValue(binding, isPartOfComposite);

                newBindings.SetValue(binding, currentLen);
                mBindingsField.SetValue(map.Instance, newBindings);

                return null;
            }
            catch (Exception ex)
            {
                return ex.InnerException?.Message ?? ex.Message;
            }
        }

        /// <summary>
        /// Gets the composite control path for a known composite type name.
        /// </summary>
        private static string GetCompositePath(string compositeType)
        {
            return compositeType.ToLowerInvariant() switch
            {
                "2dvector" => "*/{Vector2}",
                "1daxis" => "*/{Axis}",
                "buttonwithonemodifier" => "*/{ButtonWithOneModifier}",
                "buttonwithtwomodifiers" => "*/{ButtonWithTwoModifiers}",
                "dpad" => "*/{Dpad}",
                _ => null
            };
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
