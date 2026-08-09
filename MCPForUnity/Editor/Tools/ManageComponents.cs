using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using MCPForUnity.Runtime.Helpers;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Tool for managing components on GameObjects.
    /// Actions: add, remove, set_property
    /// 
    /// This is a focused tool for component lifecycle operations.
    /// For reading component data, use the unity://scene/gameobject/{id}/components resource.
    /// </summary>
    [McpForUnityTool("manage_components")]
    public static class ManageComponents
    {
        /// <summary>
        /// Handles the manage_components command.
        /// </summary>
        /// <param name="params">Command parameters</param>
        /// <returns>Result of the component operation</returns>
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            string action = ParamCoercion.CoerceString(@params["action"], null)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("'action' parameter is required (add, remove, set_property).");
            }

            // Target resolution
            JToken targetToken = @params["target"];
            string searchMethod = ParamCoercion.CoerceString(@params["searchMethod"] ?? @params["search_method"], null);

            // Only the original three actions resolve a component through 'target'; the
            // inspection and listener actions address the GameObject by 'gameObjectPath', and
            // the Python schema does not send 'target' for them.
            bool needsTarget = action == "add" || action == "remove" || action == "set_property";
            if (needsTarget && targetToken == null)
            {
                return new ErrorResponse("'target' parameter is required.");
            }

            try
            {
                var p = new ToolParams(@params);

                switch (action)
                {
                    case "add":
                        return AddComponent(@params, targetToken, searchMethod);
                    case "remove":
                        return RemoveComponent(@params, targetToken, searchMethod);
                    case "set_property":
                        return SetProperty(@params, targetToken, searchMethod);
                    case "get_property":
                    {
                        bool includeInactive = p.GetBool("includeInactive");
                        var go = FindGameObject(p.GetRequired("gameObjectPath").Value, includeInactive);
                        string componentType = p.GetRequired("componentType").Value;
                        string propertyName = p.GetRequired("propertyName").Value;

                        var component = go.GetComponent(componentType);
                        if (component == null)
                            return new ErrorResponse("COMPONENT_NOT_FOUND",
                                $"Component '{componentType}' not found on '{go.name}'.");

                        var so = new SerializedObject(component);
                        var prop = so.FindProperty(propertyName);
                        if (prop == null)
                            return new ErrorResponse("PROPERTY_NOT_FOUND",
                                $"Property '{propertyName}' not found on {componentType}. " +
                                $"Available: {GetSerializedPropertyNames(so)}");

                        return new SuccessResponse("Property value",
                            SerializedPropertyToDict(prop));
                    }
                    case "list_all":
                    {
                        bool includeInactive = p.GetBool("includeInactive");
                        var go = FindGameObject(p.GetRequired("gameObjectPath").Value, includeInactive);
                        var pagination = PaginationRequest.FromParams(@params, defaultPageSize: 50);
                        pagination.PageSize = Math.Max(1, Math.Min(pagination.PageSize, 500));
                        var components = go.GetComponents<Component>()
                            .Where(c => c != null)
                            .Select(c => new {
                                type = c.GetType().Name,
                                fullName = c.GetType().FullName,
                                assemblyName = c.GetType().Assembly.GetName().Name
                            }).Cast<object>().ToList();
                        var page = PaginationResponse<object>.Create(components, pagination);
                        return new SuccessResponse("Components list", new
                        {
                            components = page.Items,
                            count = page.TotalCount,
                            totalCount = page.TotalCount,
                            pageSize = page.PageSize,
                            cursor = page.Cursor,
                            nextCursor = page.NextCursor,
                            hasMore = page.HasMore
                        });
                    }
                    case "add_simple_listener":
                        return AddPersistentListener(p, typed: false);

                    case "add_param_listener":
                        return AddPersistentListener(p, typed: true);

                    case "remove_listener":
                    {
                        bool includeInactive = p.GetBool("includeInactive");
                        var go = FindGameObject(p.GetRequired("gameObjectPath").Value, includeInactive);
                        string componentType = p.GetRequired("componentType").Value;
                        string eventName = p.GetRequired("eventName").Value;
                        int listenerIndex = p.GetInt("listenerIndex") ?? 0;

                        var component = go.GetComponent(componentType);
                        var so = new SerializedObject(component);
                        var eventProp = so.FindProperty(eventName);
                        if (eventProp == null)
                            return new ErrorResponse("EVENT_NOT_FOUND",
                                $"Event '{eventName}' not found on {componentType}.");

                        var rCalls = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
                        if (rCalls == null)
                            return new ErrorResponse("NOT_A_UNITY_EVENT",
                                $"Property '{eventName}' on {componentType} is not a UnityEvent.");
                        if (listenerIndex < 0 || listenerIndex >= rCalls.arraySize)
                            return new ErrorResponse("INDEX_OUT_OF_RANGE",
                                $"Listener index {listenerIndex} is out of range; {eventName} has {rCalls.arraySize} listener(s).");

                        Undo.RecordObject(component, "Remove persistent listener");
                        rCalls.DeleteArrayElementAtIndex(listenerIndex);
                        so.ApplyModifiedProperties();
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                        EditorUtility.SetDirty(component);
                        return new SuccessResponse(
                            $"Removed persistent listener at index {listenerIndex}",
                            new { listenerIndex, remaining = rCalls.arraySize });
                    }
                    case "get_listeners":
                    {
                        bool includeInactive = p.GetBool("includeInactive");
                        var go = FindGameObject(p.GetRequired("gameObjectPath").Value, includeInactive);
                        string componentType = p.GetRequired("componentType").Value;
                        string eventName = p.GetRequired("eventName").Value;

                        var component = go.GetComponent(componentType);
                        var so = new SerializedObject(component);
                        var eventProp = so.FindProperty(eventName);
                        if (eventProp == null)
                            return new ErrorResponse("EVENT_NOT_FOUND",
                                $"Event '{eventName}' not found on {componentType}.");

                        var listeners = new List<object>();
                        var gCalls = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
                        if (gCalls == null)
                            return new ErrorResponse("NOT_A_UNITY_EVENT",
                                $"Property '{eventName}' on {componentType} is not a UnityEvent.");

                        int gCount = gCalls.arraySize;
                        for (int i = 0; i < gCount; i++)
                        {
                            var call = gCalls.GetArrayElementAtIndex(i);
                            var target = call.FindPropertyRelative("m_Target").objectReferenceValue;
                            listeners.Add(new
                            {
                                index = i,
                                target = target?.name,
                                targetType = target?.GetType().Name,
                                method = call.FindPropertyRelative("m_MethodName").stringValue,
                                mode = ((PersistentListenerMode)call.FindPropertyRelative("m_Mode").intValue).ToString(),
                                callState = ((UnityEventCallState)call.FindPropertyRelative("m_CallState").intValue).ToString()
                            });
                        }

                        var pagination = PaginationRequest.FromParams(@params, defaultPageSize: 50);
                        pagination.PageSize = Math.Max(1, Math.Min(pagination.PageSize, 500));
                        var page = PaginationResponse<object>.Create(listeners, pagination);
                        return new SuccessResponse($"Found {gCount} listeners",
                            new
                            {
                                count = gCount,
                                listeners = page.Items,
                                totalCount = page.TotalCount,
                                pageSize = page.PageSize,
                                cursor = page.Cursor,
                                nextCursor = page.NextCursor,
                                hasMore = page.HasMore
                            });
                    }
                    default:
                        return new ErrorResponse($"Unknown action: '{action}'. Supported actions: add, remove, set_property, get_property, list_all, add_simple_listener, add_param_listener, remove_listener, get_listeners");
                }
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageComponents] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }

        #region Action Implementations

        private static object AddComponent(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentTypeName = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentTypeName))
            {
                return new ErrorResponse("'componentType' parameter is required for 'add' action.");
            }

            // Resolve component type using unified type resolver
            Type type = UnityTypeResolver.ResolveComponent(componentTypeName);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentTypeName}' not found. Use a fully-qualified name if needed.");
            }

            // Use ComponentOps for the actual operation
            Component newComponent = ComponentOps.AddComponent(targetGo, type, out string error);
            if (newComponent == null)
            {
                return new ErrorResponse(error ?? $"Failed to add component '{componentTypeName}'.");
            }

            // When adding VFX-related components (ParticleSystem, LineRenderer, TrailRenderer),
            // ensure the renderer has a material compatible with the active render pipeline.
            // Without this, newly added ParticleSystems in URP/HDRP projects get Unity's default
            // Built-in RP particle material, which renders as magenta.
            EnsureVfxRendererMaterial(targetGo, newComponent);

            // Set properties if provided
            JObject properties = @params["properties"] as JObject ?? @params["componentProperties"] as JObject;
            if (properties != null && properties.HasValues)
            {
                // Record for undo before modifying properties
                Undo.RecordObject(newComponent, "Modify Component Properties");
                SetPropertiesOnComponent(newComponent, properties);
            }

            EditorUtility.SetDirty(targetGo);
            MarkOwningSceneDirty(targetGo);

            return new
            {
                success = true,
                message = $"Component '{componentTypeName}' added to '{targetGo.name}'.",
                data = new
                {
                    instanceID = targetGo.GetInstanceIDCompat(),
                    componentType = type.FullName,
                    componentInstanceID = newComponent.GetInstanceIDCompat()
                }
            };
        }

        private static object RemoveComponent(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentTypeName = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentTypeName))
            {
                return new ErrorResponse("'componentType' parameter is required for 'remove' action.");
            }

            // Resolve component type using unified type resolver
            Type type = UnityTypeResolver.ResolveComponent(componentTypeName);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentTypeName}' not found.");
            }

            int? componentIndex = ParamCoercion.CoerceIntNullable(@params["componentIndex"] ?? @params["component_index"]);
            if (componentIndex.HasValue)
            {
                var components = targetGo.GetComponents(type);
                if (componentIndex.Value < 0 || componentIndex.Value >= components.Length)
                    return new ErrorResponse($"component_index {componentIndex.Value} out of range. Found {components.Length} '{componentTypeName}' component(s).");
                if (type == typeof(Transform) || type == typeof(RectTransform))
                    return new ErrorResponse("Cannot remove Transform or RectTransform components.");
                Undo.DestroyObjectImmediate(components[componentIndex.Value]);
                EditorUtility.SetDirty(targetGo);
                MarkOwningSceneDirty(targetGo);
                return new
                {
                    success = true,
                    message = $"Component '{componentTypeName}' (index {componentIndex.Value}) removed from '{targetGo.name}'.",
                    data = new { instanceID = targetGo.GetInstanceIDCompat(), componentIndex = componentIndex.Value }
                };
            }

            // Use ComponentOps for the actual operation (removes first instance)
            bool removed = ComponentOps.RemoveComponent(targetGo, type, out string error);
            if (!removed)
            {
                return new ErrorResponse(error ?? $"Failed to remove component '{componentTypeName}'.");
            }

            EditorUtility.SetDirty(targetGo);
            MarkOwningSceneDirty(targetGo);

            return new
            {
                success = true,
                message = $"Component '{componentTypeName}' removed from '{targetGo.name}'.",
                data = new
                {
                    instanceID = targetGo.GetInstanceIDCompat()
                }
            };
        }

        private static object SetProperty(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = FindTarget(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string componentType = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
            if (string.IsNullOrEmpty(componentType))
            {
                return new ErrorResponse("'componentType' parameter is required for 'set_property' action.");
            }

            // Resolve component type using unified type resolver
            Type type = UnityTypeResolver.ResolveComponent(componentType);
            if (type == null)
            {
                return new ErrorResponse($"Component type '{componentType}' not found.");
            }

            int? componentIndex = ParamCoercion.CoerceIntNullable(@params["componentIndex"] ?? @params["component_index"]);
            Component component;
            if (componentIndex.HasValue)
            {
                var components = targetGo.GetComponents(type);
                if (componentIndex.Value < 0 || componentIndex.Value >= components.Length)
                    return new ErrorResponse($"component_index {componentIndex.Value} out of range. Found {components.Length} '{componentType}' component(s).");
                component = components[componentIndex.Value];
            }
            else
            {
                component = targetGo.GetComponent(type);
            }
            if (component == null)
            {
                return new ErrorResponse($"Component '{componentType}' not found on '{targetGo.name}'.");
            }

            // Get property and value
            string propertyName = ParamCoercion.CoerceString(@params["property"], null);
            JToken valueToken = @params["value"];

            // Support both single property or properties object
            JObject properties = @params["properties"] as JObject;

            if (string.IsNullOrEmpty(propertyName) && (properties == null || !properties.HasValues))
            {
                return new ErrorResponse("Either 'property'+'value' or 'properties' object is required for 'set_property' action.");
            }

            var errors = new List<string>();

            try
            {
                Undo.RecordObject(component, $"Set property on {componentType}");

                if (!string.IsNullOrEmpty(propertyName) && valueToken != null)
                {
                    // Single property mode
                    var error = TrySetProperty(component, propertyName, valueToken);
                    if (error != null)
                    {
                        errors.Add(error);
                    }
                }

                if (properties != null && properties.HasValues)
                {
                    // Multiple properties mode
                    foreach (var prop in properties.Properties())
                    {
                        var error = TrySetProperty(component, prop.Name, prop.Value);
                        if (error != null)
                        {
                            errors.Add(error);
                        }
                    }
                }

                EditorUtility.SetDirty(component);
                MarkOwningSceneDirty(targetGo);

                if (errors.Count > 0)
                {
                    return new
                    {
                        success = false,
                        message = $"Some properties failed to set on '{componentType}'.",
                        data = new
                        {
                            instanceID = targetGo.GetInstanceIDCompat(),
                            errors = errors
                        }
                    };
                }

                return new
                {
                    success = true,
                    message = $"Properties set on component '{componentType}' on '{targetGo.name}'.",
                    data = new
                    {
                        instanceID = targetGo.GetInstanceIDCompat()
                    }
                };
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error setting properties on component '{componentType}': {e.Message}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// When a VFX-capable component is added (ParticleSystem, LineRenderer, TrailRenderer),
        /// ensures its renderer material is valid for the active render pipeline.
        /// This prevents magenta rendering in URP/HDRP projects where the default built-in
        /// particle/line materials use incompatible shaders.
        /// </summary>
        private static void EnsureVfxRendererMaterial(GameObject go, Component addedComponent)
        {
            Renderer renderer = null;

            if (addedComponent is ParticleSystem ps)
            {
                renderer = go.GetComponent<ParticleSystemRenderer>();

                // Apply sensible defaults so newly added ParticleSystems aren't oversized.
                // These are overridden by any subsequent particle_set_* calls.
                RendererHelpers.SetSensibleParticleDefaults(ps);
            }
            else if (addedComponent is Renderer r)
            {
                // Covers LineRenderer, TrailRenderer, and any other Renderer subclass
                renderer = r;
            }

            if (renderer != null)
            {
                var result = RendererHelpers.EnsureMaterial(renderer);
                if (result.MaterialReplaced)
                {
                    McpLog.Info($"[ManageComponents] Auto-assigned pipeline-compatible material to {renderer.GetType().Name} on '{go.name}' (reason: {result.ReplacementReason}).");
                }
            }
        }

        /// <summary>
        /// Marks the appropriate scene as dirty for the given GameObject.
        /// Handles both regular scenes and prefab stages.
        /// </summary>
        private static void MarkOwningSceneDirty(GameObject targetGo)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                EditorSceneManager.MarkSceneDirty(prefabStage.scene);
            }
            else
            {
                EditorSceneManager.MarkSceneDirty(targetGo.scene);
            }
        }

        private static GameObject FindTarget(JToken targetToken, string searchMethod)
        {
            if (targetToken == null)
                return null;

            // Try instance ID first
            if (targetToken.Type == JTokenType.Integer)
            {
                int instanceId = targetToken.Value<int>();
                return GameObjectLookup.FindById(instanceId);
            }

            string targetStr = targetToken.ToString();

            // Try parsing as instance ID
            if (int.TryParse(targetStr, out int parsedId))
            {
                var byId = GameObjectLookup.FindById(parsedId);
                if (byId != null)
                    return byId;
            }

            // Use GameObjectLookup for search
            return GameObjectLookup.FindByTarget(targetToken, searchMethod ?? "by_name", true);
        }

        private static void SetPropertiesOnComponent(Component component, JObject properties)
        {
            if (component == null || properties == null)
                return;

            var errors = new List<string>();
            foreach (var prop in properties.Properties())
            {
                var error = TrySetProperty(component, prop.Name, prop.Value);
                if (error != null)
                    errors.Add(error);
            }
            
            if (errors.Count > 0)
            {
                McpLog.Warn($"[ManageComponents] Some properties failed to set on {component.GetType().Name}: {string.Join(", ", errors)}");
            }
        }

        /// <summary>
        /// Attempts to set a property or field on a component.
        /// Delegates to ComponentOps.SetProperty for unified implementation.
        /// </summary>
        private static string TrySetProperty(Component component, string propertyName, JToken value)
        {
            if (component == null || string.IsNullOrEmpty(propertyName))
                return "Invalid component or property name";

            if (ComponentOps.SetProperty(component, propertyName, value, out string error))
            {
                return null; // Success
            }

            McpLog.Warn($"[ManageComponents] {error}");
            return error;
        }

        private static GameObject FindGameObject(string path, bool includeInactive = false)
        {
            var go = GameObjectLookup.FindByTarget(new JValue(path), "by_path", includeInactive)
                ?? GameObjectLookup.FindByTarget(new JValue(path), "by_name", includeInactive);
            if (go == null)
                throw new Exception($"GameObject not found at path: {path}");
            return go;
        }

        /// <summary>
        /// Adds a persistent (serialized, inspector-visible) listener to a UnityEvent.
        /// </summary>
        /// <remarks>
        /// Written against SerializedProperty on every Unity version. UnityEventTools looks like
        /// the right API but is not usable here: every one of its methods takes a compile-time
        /// UnityAction delegate, and this tool only ever has a method *name*. It has no
        /// SerializedProperty overloads and no GetPersistentEventCount/Target/MethodName at all —
        /// verified by reflection on 6000.5.3f1 — so the pre-2022.2 branch this replaces could
        /// never have compiled.
        ///
        /// The serialized layout (m_PersistentCalls.m_Calls with m_Target,
        /// m_TargetAssemblyTypeName, m_MethodName, m_Mode, m_Arguments, m_CallState) is identical
        /// on 2022.3.62f2 and 6000.5.3f1 and has been stable since UnityEvent was introduced.
        /// </remarks>
        private static object AddPersistentListener(ToolParams p, bool typed)
        {
            var go = FindGameObject(p.GetRequired("gameObjectPath").Value);
            string componentType = p.GetRequired("componentType").Value;
            string eventName = p.GetRequired("eventName").Value;
            var targetGo = FindGameObject(p.GetRequired("targetPath").Value);
            string methodName = p.GetRequired("methodName").Value;

            string paramType = typed ? p.GetRequired("paramType").Value : "void";
            string paramValue = typed ? p.GetRequired("paramValue").Value : null;

            var component = go.GetComponent(componentType);
            if (component == null)
                return new ErrorResponse("COMPONENT_NOT_FOUND",
                    $"Component '{componentType}' not found on '{go.name}'.");

            var so = new SerializedObject(component);
            var eventProp = so.FindProperty(eventName);
            if (eventProp == null)
                return new ErrorResponse("EVENT_NOT_FOUND",
                    $"Event '{eventName}' not found on {componentType}.");

            var calls = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
            if (calls == null)
                return new ErrorResponse("NOT_A_UNITY_EVENT",
                    $"Property '{eventName}' on {componentType} is not a UnityEvent.");

            if (!Enum.TryParse(paramType, ignoreCase: true, out PersistentListenerMode mode)
                || mode == PersistentListenerMode.EventDefined)
                return new ErrorResponse("INVALID_PARAM_TYPE",
                    $"Unknown paramType '{paramType}'. Valid values: int, float, string, bool, Object.");

            // The invoked method lives on a component, not on the GameObject — UnityEvent's
            // m_Target must be that component. Pointing it at the GameObject (what this used to
            // do) produces a listener the inspector shows as unresolved and that never fires.
            Type argumentType = mode switch
            {
                PersistentListenerMode.Int => typeof(int),
                PersistentListenerMode.Float => typeof(float),
                PersistentListenerMode.String => typeof(string),
                PersistentListenerMode.Bool => typeof(bool),
                PersistentListenerMode.Object => typeof(UnityEngine.Object),
                _ => null,
            };

            UnityEngine.Object callTarget = ResolveCallTarget(targetGo, methodName, argumentType, out Type declaredArgumentType);
            if (callTarget == null)
                return new ErrorResponse("METHOD_NOT_FOUND",
                    $"No component on '{targetGo.name}' declares a public method '{methodName}'"
                    + (argumentType == null ? " with no parameters." : $" taking a single {argumentType.Name}.")
                    + $" Components present: {string.Join(", ", targetGo.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name))}.");

            UnityEngine.Object objectArgument = null;
            if (mode == PersistentListenerMode.Object)
            {
                // Without paramObjectPath the argument used to default to the listener's own
                // target, which is almost never what the caller meant.
                string objectPath = p.Get("paramObjectPath") ?? paramValue;
                if (string.IsNullOrEmpty(objectPath))
                    return new ErrorResponse("MISSING_PARAMETER",
                        "paramType 'Object' needs 'paramObjectPath' naming the GameObject to pass.");
                objectArgument = FindGameObject(objectPath, p.GetBool("includeInactive"));
                if (objectArgument == null)
                    return new ErrorResponse("NOT_FOUND", $"GameObject not found at path: {objectPath}");
            }

            Undo.RecordObject(component, "Add persistent listener");
            int index = calls.arraySize;
            calls.InsertArrayElementAtIndex(index);
            var call = calls.GetArrayElementAtIndex(index);

            call.FindPropertyRelative("m_Target").objectReferenceValue = callTarget;
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue =
                callTarget.GetType().AssemblyQualifiedName;
            call.FindPropertyRelative("m_MethodName").stringValue = methodName;
            call.FindPropertyRelative("m_Mode").intValue = (int)mode;
            // A freshly inserted element defaults to UnityEventCallState.Off (0) — a listener that
            // is registered but never runs. Verified on both 2022.3.62f2 and 6000.5.3f1.
            call.FindPropertyRelative("m_CallState").intValue = (int)UnityEventCallState.RuntimeOnly;

            // InsertArrayElementAtIndex duplicates the preceding element, so every argument field
            // has to be cleared: otherwise listener #2 silently inherits #1's argument.
            var args = call.FindPropertyRelative("m_Arguments");
            args.FindPropertyRelative("m_IntArgument").intValue = 0;
            args.FindPropertyRelative("m_FloatArgument").floatValue = 0f;
            args.FindPropertyRelative("m_StringArgument").stringValue = string.Empty;
            args.FindPropertyRelative("m_BoolArgument").boolValue = false;
            args.FindPropertyRelative("m_ObjectArgument").objectReferenceValue = null;
            args.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue =
                declaredArgumentType == null ? string.Empty : declaredArgumentType.AssemblyQualifiedName;

            try
            {
                switch (mode)
                {
                    case PersistentListenerMode.Int:
                        args.FindPropertyRelative("m_IntArgument").intValue = int.Parse(paramValue);
                        break;
                    case PersistentListenerMode.Float:
                        args.FindPropertyRelative("m_FloatArgument").floatValue = float.Parse(paramValue);
                        break;
                    case PersistentListenerMode.String:
                        args.FindPropertyRelative("m_StringArgument").stringValue = paramValue;
                        break;
                    case PersistentListenerMode.Bool:
                        args.FindPropertyRelative("m_BoolArgument").boolValue = bool.Parse(paramValue);
                        break;
                    case PersistentListenerMode.Object:
                        args.FindPropertyRelative("m_ObjectArgument").objectReferenceValue = objectArgument;
                        break;
                }
            }
            catch (FormatException)
            {
                return new ErrorResponse("INVALID_PARAM_VALUE",
                    $"'{paramValue}' is not a valid {paramType}.");
            }

            // ApplyModifiedProperties registers its own Undo entry.
            so.ApplyModifiedProperties();
            // Listener edits on a prefab instance are discarded without this.
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorUtility.SetDirty(component);

            return new SuccessResponse(
                $"Added persistent listener #{index}: {callTarget.GetType().Name}.{methodName}({paramType})",
                new
                {
                    index,
                    target = callTarget.GetType().Name,
                    methodName,
                    mode = mode.ToString(),
                    callState = UnityEventCallState.RuntimeOnly.ToString()
                });
        }

        /// <summary>
        /// Finds the component on <paramref name="targetGo"/> that declares a public method
        /// matching <paramref name="methodName"/> and the listener's argument type. Falls back to
        /// the GameObject itself (built-ins like SetActive live there). Reports the parameter type
        /// as declared, which for Object listeners is the concrete type the inspector needs.
        /// </summary>
        private static UnityEngine.Object ResolveCallTarget(
            GameObject targetGo, string methodName, Type argumentType, out Type declaredArgumentType)
        {
            declaredArgumentType = null;

            var candidates = new List<UnityEngine.Object>(targetGo.GetComponents<Component>()
                .Where(c => c != null).Cast<UnityEngine.Object>()) { targetGo };

            foreach (var candidate in candidates)
            {
                foreach (var method in candidate.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name != methodName) continue;
                    var ps = method.GetParameters();

                    if (argumentType == null)
                    {
                        if (ps.Length == 0) return candidate;
                        continue;
                    }

                    if (ps.Length != 1) continue;
                    // An Object listener may take any UnityEngine.Object subclass; the value
                    // types must match exactly.
                    bool matches = argumentType == typeof(UnityEngine.Object)
                        ? typeof(UnityEngine.Object).IsAssignableFrom(ps[0].ParameterType)
                        : ps[0].ParameterType == argumentType;
                    if (matches)
                    {
                        declaredArgumentType = ps[0].ParameterType;
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static string GetSerializedPropertyNames(SerializedObject so)
        {
            var names = new List<string>();
            var prop = so.GetIterator();
            if (prop.Next(true))
            {
                do
                {
                    names.Add(prop.propertyPath);
                } while (prop.Next(false));
            }
            return string.Join(", ", names);
        }

        private static object SerializedPropertyToDict(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => (object)prop.intValue,
                SerializedPropertyType.Boolean => prop.boolValue,
                SerializedPropertyType.Float => prop.floatValue,
                SerializedPropertyType.String => prop.stringValue,
                SerializedPropertyType.Color => new { r = prop.colorValue.r, g = prop.colorValue.g, b = prop.colorValue.b, a = prop.colorValue.a },
                SerializedPropertyType.ObjectReference => prop.objectReferenceValue != null
                    ? new { name = prop.objectReferenceValue.name, type = prop.objectReferenceValue.GetType().Name }
                    : null,
                SerializedPropertyType.Vector2 => new { x = prop.vector2Value.x, y = prop.vector2Value.y },
                SerializedPropertyType.Vector3 => new { x = prop.vector3Value.x, y = prop.vector3Value.y, z = prop.vector3Value.z },
                SerializedPropertyType.Vector4 => new { x = prop.vector4Value.x, y = prop.vector4Value.y, z = prop.vector4Value.z, w = prop.vector4Value.w },
                SerializedPropertyType.Enum => prop.enumDisplayNames[prop.enumValueIndex],
                _ => $"Unsupported type: {prop.propertyType}"
            };
        }

        #endregion
    }
}
