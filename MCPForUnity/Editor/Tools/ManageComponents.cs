using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
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

            if (targetToken == null)
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
                        var go = FindGameObject(p.GetRequired("gameObjectPath").Value);
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
                        var go = FindGameObject(p.GetRequired("gameObjectPath").Value);
                        bool includeInactive = p.GetBool("includeInactive");
                        var components = go.GetComponents<Component>()
                            .Where(c => c != null)
                            .Select(c => new {
                                type = c.GetType().Name,
                                fullName = c.GetType().FullName,
                                assemblyName = c.GetType().Assembly.GetName().Name
                            }).ToList();
                        return new SuccessResponse("Components list", new { components });
                    }
                    case "add_simple_listener":
                    {
                        var go = FindGameObject(p.GetRequired("gameObjectPath").Value);
                        string componentType = p.GetRequired("componentType").Value;
                        string eventName = p.GetRequired("eventName").Value;
                        var targetObj = FindGameObject(p.GetRequired("targetPath").Value);
                        string methodName = p.GetRequired("methodName").Value;

                        var component = go.GetComponent(componentType);
                        var so = new SerializedObject(component);
                        var eventProp = so.FindProperty(eventName);
                        if (eventProp == null)
                            return new ErrorResponse("EVENT_NOT_FOUND",
                                $"Event '{eventName}' not found on {componentType}.");

                        int count;
#if UNITY_2022_2_OR_NEWER
                        var calls = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
                        count = calls.arraySize;
                        calls.InsertArrayElementAtIndex(count);
                        var newCall = calls.GetArrayElementAtIndex(count);
                        newCall.FindPropertyRelative("m_Target").objectReferenceValue = targetObj;
                        newCall.FindPropertyRelative("m_MethodName").stringValue = methodName;
                        newCall.FindPropertyRelative("m_Mode").intValue = 1; // EventDefined
                        newCall.FindPropertyRelative("m_Arguments").FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = "";
#else
                        count = UnityEventTools.GetPersistentEventCount(eventProp);
                        UnityEventTools.AddPersistentListener(eventProp);
                        UnityEventTools.RegisterPersistentListener(eventProp, count,
                            targetObj, methodName);
#endif
                        so.ApplyModifiedProperties();
                        return new SuccessResponse(
                            $"Added persistent listener #{count}: {methodName} on {targetObj.name}");
                    }
                    case "add_param_listener":
                    {
                        var go = FindGameObject(p.GetRequired("gameObjectPath").Value);
                        string componentType = p.GetRequired("componentType").Value;
                        string eventName = p.GetRequired("eventName").Value;
                        var targetObj = FindGameObject(p.GetRequired("targetPath").Value);
                        string methodName = p.GetRequired("methodName").Value;
                        string paramType = p.GetRequired("paramType").Value;
                        string paramValue = p.GetRequired("paramValue").Value;

                        var component = go.GetComponent(componentType);
                        var so = new SerializedObject(component);
                        var eventProp = so.FindProperty(eventName);
                        if (eventProp == null)
                            return new ErrorResponse("EVENT_NOT_FOUND",
                                $"Event '{eventName}' not found on {componentType}.");

                        int pCount;
#if UNITY_2022_2_OR_NEWER
                        var pCalls = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
                        pCount = pCalls.arraySize;
                        pCalls.InsertArrayElementAtIndex(pCount);
                        var pCall = pCalls.GetArrayElementAtIndex(pCount);
                        pCall.FindPropertyRelative("m_Target").objectReferenceValue = targetObj;
                        pCall.FindPropertyRelative("m_MethodName").stringValue = methodName;
                        pCall.FindPropertyRelative("m_Mode").intValue = 1;
                        var args = pCall.FindPropertyRelative("m_Arguments");
                        switch (paramType)
                        {
                            case "int":
                                args.FindPropertyRelative("m_IntArgument").intValue = int.Parse(paramValue);
                                args.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = "System.Int32, mscorlib";
                                break;
                            case "float":
                                args.FindPropertyRelative("m_FloatArgument").floatValue = float.Parse(paramValue);
                                args.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = "System.Single, mscorlib";
                                break;
                            case "string":
                                args.FindPropertyRelative("m_StringArgument").stringValue = paramValue;
                                args.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = "System.String, mscorlib";
                                break;
                            case "bool":
                                args.FindPropertyRelative("m_BoolArgument").boolValue = bool.Parse(paramValue);
                                args.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = "System.Boolean, mscorlib";
                                break;
                            case "Object":
                                args.FindPropertyRelative("m_ObjectArgument").objectReferenceValue = targetObj;
                                args.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = "UnityEngine.Object, UnityEngine";
                                break;
                            default:
                                throw new ArgumentException($"Unknown paramType: {paramType}");
                        }
#else
                        pCount = UnityEventTools.GetPersistentEventCount(eventProp);
                        UnityEventTools.AddPersistentListener(eventProp);
                        UnityEventTools.RegisterPersistentListener(eventProp, pCount,
                            targetObj, methodName);
                        UnityEventTools.RegisterPersistentListenerArgument(eventProp, pCount,
                            paramType switch
                            {
                                "int" => int.Parse(paramValue),
                                "float" => float.Parse(paramValue),
                                "string" => paramValue,
                                "bool" => bool.Parse(paramValue),
                                "Object" => targetObj,
                                _ => throw new ArgumentException($"Unknown paramType: {paramType}")
                            });
#endif
                        so.ApplyModifiedProperties();
                        return new SuccessResponse(
                            $"Added persistent typed listener #{pCount}: {methodName}({paramType})");
                    }
                    case "remove_listener":
                    {
                        var go = FindGameObject(p.GetRequired("gameObjectPath").Value);
                        string componentType = p.GetRequired("componentType").Value;
                        string eventName = p.GetRequired("eventName").Value;
                        int listenerIndex = p.GetInt("listenerIndex") ?? 0;

                        var component = go.GetComponent(componentType);
                        var so = new SerializedObject(component);
                        var eventProp = so.FindProperty(eventName);
                        if (eventProp == null)
                            return new ErrorResponse("EVENT_NOT_FOUND",
                                $"Event '{eventName}' not found on {componentType}.");

#if UNITY_2022_2_OR_NEWER
                        var rCalls = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
                        rCalls.DeleteArrayElementAtIndex(listenerIndex);
#else
                        UnityEventTools.RemovePersistentListener(eventProp, listenerIndex);
#endif
                        so.ApplyModifiedProperties();
                        return new SuccessResponse(
                            $"Removed persistent listener at index {listenerIndex}");
                    }
                    case "get_listeners":
                    {
                        var go = FindGameObject(p.GetRequired("gameObjectPath").Value);
                        string componentType = p.GetRequired("componentType").Value;
                        string eventName = p.GetRequired("eventName").Value;

                        var component = go.GetComponent(componentType);
                        var so = new SerializedObject(component);
                        var eventProp = so.FindProperty(eventName);
                        if (eventProp == null)
                            return new ErrorResponse("EVENT_NOT_FOUND",
                                $"Event '{eventName}' not found on {componentType}.");

                        int gCount;
                        var listeners = new List<object>();
#if UNITY_2022_2_OR_NEWER
                        var gCalls = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
                        gCount = gCalls.arraySize;
                        for (int i = 0; i < gCount; i++)
                        {
                            var call = gCalls.GetArrayElementAtIndex(i);
                            var target = call.FindPropertyRelative("m_Target").objectReferenceValue;
                            var method = call.FindPropertyRelative("m_MethodName").stringValue;
                            listeners.Add(new { index = i, target = target?.name, method });
                        }
#else
                        gCount = UnityEventTools.GetPersistentEventCount(eventProp);
                        for (int i = 0; i < gCount; i++)
                        {
                            var target = UnityEventTools.GetPersistentTarget(eventProp, i);
                            var method = UnityEventTools.GetPersistentMethodName(eventProp, i);
                            listeners.Add(new { index = i, target = target?.name, method });
                        }
#endif
                        return new SuccessResponse($"Found {gCount} listeners",
                            new { count = gCount, listeners });
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

        private static GameObject FindGameObject(string path)
        {
            var go = GameObject.Find(path);
            if (go == null)
                throw new Exception($"GameObject not found at path: {path}");
            return go;
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
