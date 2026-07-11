using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Runtime.Helpers;

namespace MCPForUnity.Editor.Tools.Cameras
{
    internal static class CameraCreate
    {
        private static readonly Dictionary<string, (string body, string aim)> Presets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["follow"]        = ("CinemachineFollow",              "CinemachineRotationComposer"),
            ["third_person"]  = ("CinemachineThirdPersonFollow",   "CinemachineRotationComposer"),
            ["freelook"]      = ("CinemachineOrbitalFollow",       "CinemachineRotationComposer"),
            ["dolly"]         = ("CinemachineSplineDolly",         "CinemachineRotationComposer"),
            ["static"]        = (null,                              "CinemachineHardLookAt"),
            ["top_down"]      = ("CinemachineFollow",              null),
            ["side_scroller"] = ("CinemachinePositionComposer",    null),
        };

        internal static object CreateBasicCamera(JObject @params)
        {
            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            string name = ParamCoercion.CoerceString(props["name"], null) ?? "Camera";
            float fov = ParamCoercion.CoerceFloat(props["fieldOfView"], 60f);
            float near = ParamCoercion.CoerceFloat(props["nearClipPlane"], 0.3f);
            float far = ParamCoercion.CoerceFloat(props["farClipPlane"], 1000f);

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create Camera '{name}'");
            var cam = go.AddComponent<UnityEngine.Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = near;
            cam.farClipPlane = far;

            // Position near follow target if provided
            string follow = ParamCoercion.CoerceString(props["follow"], null);
            if (follow != null)
            {
                var target = CameraHelpers.ResolveGameObjectRef(follow);
                if (target != null)
                    go.transform.position = target.transform.position + new Vector3(0, 5, -10);
            }

            // Look at target if provided
            string lookAt = ParamCoercion.CoerceString(props["lookAt"] ?? props["look_at"], null);
            if (lookAt != null)
            {
                var target = CameraHelpers.ResolveGameObjectRef(lookAt);
                if (target != null)
                    go.transform.LookAt(target.transform);
            }

            CameraHelpers.MarkDirty(go);

            return new
            {
                success = true,
                message = $"Created basic Camera '{name}' (Cinemachine not installed — using Unity Camera).",
                data = new
                {
                    instanceID = go.GetInstanceIDCompat(),
                    cinemachine = false,
                    hint = "Install com.unity.cinemachine for presets, blending, and virtual camera features."
                }
            };
        }

        internal static object CreateCinemachineCamera(JObject @params)
        {
            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();
            string name = ParamCoercion.CoerceString(props["name"], null) ?? "CM Camera";
            string preset = ParamCoercion.CoerceString(props["preset"], null) ?? "follow";
            int priority = ParamCoercion.CoerceInt(props["priority"], 10);

            if (!Presets.TryGetValue(preset, out var presetDef))
            {
                return new ErrorResponse(
                    $"Unknown preset '{preset}'. Valid presets: {string.Join(", ", Presets.Keys)}.");
            }

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create CinemachineCamera '{name}'");

            // Add CinemachineCamera component
            var cmType = CameraHelpers.CinemachineCameraType;
            var cmCamera = go.AddComponent(cmType);

            // PrioritySettings is a struct with Enabled + m_Value — use SerializedProperty
            using (var so = new SerializedObject(cmCamera))
            {
                var priorityProp = so.FindProperty("Priority");
                if (priorityProp != null)
                {
                    var enabledProp = priorityProp.FindPropertyRelative("Enabled");
                    var valueProp = priorityProp.FindPropertyRelative("m_Value");
                    if (enabledProp != null) enabledProp.boolValue = true;
                    if (valueProp != null) valueProp.intValue = priority;
                    so.ApplyModifiedProperties();
                }
                else
                {
                    CameraHelpers.SetReflectionProperty(cmCamera, "Priority", priority);
                }
            }

            // Add Body component
            string bodyName = null;
            if (presetDef.body != null)
            {
                var bodyType = CameraHelpers.ResolveComponentType(presetDef.body);
                if (bodyType != null)
                {
                    go.AddComponent(bodyType);
                    bodyName = presetDef.body;
                }
            }

            // Add Aim component
            string aimName = null;
            if (presetDef.aim != null)
            {
                var aimType = CameraHelpers.ResolveComponentType(presetDef.aim);
                if (aimType != null)
                {
                    go.AddComponent(aimType);
                    aimName = presetDef.aim;
                }
            }

            // Set Follow target
            var followToken = props["follow"];
            if (followToken != null && followToken.Type != JTokenType.Null)
                CameraHelpers.SetTransformTarget(cmCamera, "Follow", followToken);

            // Set LookAt target
            var lookAtToken = props["lookAt"] ?? props["look_at"];
            if (lookAtToken != null && lookAtToken.Type != JTokenType.Null)
                CameraHelpers.SetTransformTarget(cmCamera, "LookAt", lookAtToken);

            CameraHelpers.MarkDirty(go);

            return new
            {
                success = true,
                message = $"Created CinemachineCamera '{name}' with preset '{preset}'.",
                data = new
                {
                    instanceID = go.GetInstanceIDCompat(),
                    cinemachine = true,
                    preset,
                    priority,
                    body = bodyName,
                    aim = aimName
                }
            };
        }

        internal static object EnsureBrain(JObject @params)
        {
            var props = CameraHelpers.ExtractProperties(@params) ?? new JObject();

            // Check if Brain already exists
            var existingBrain = CameraHelpers.FindBrain();
            if (existingBrain != null)
            {
                return new
                {
                    success = true,
                    message = $"CinemachineBrain already exists on '{existingBrain.gameObject.name}'.",
                    data = new
                    {
                        instanceID = existingBrain.gameObject.GetInstanceIDCompat(),
                        alreadyExisted = true
                    }
                };
            }

            // Find target camera
            string cameraRef = ParamCoercion.CoerceString(props["camera"], null);
            UnityEngine.Camera cam;
            if (cameraRef != null)
            {
                var camGo = CameraHelpers.ResolveGameObjectRef(cameraRef);
                cam = camGo != null ? camGo.GetComponent<UnityEngine.Camera>() : null;
            }
            else
            {
                cam = CameraHelpers.FindMainCamera();
            }

            if (cam == null)
                return new ErrorResponse("No Camera found to add CinemachineBrain to.");

            var brainType = CameraHelpers.CinemachineBrainType;
            Undo.RecordObject(cam.gameObject, "Add CinemachineBrain");
            var brain = cam.gameObject.AddComponent(brainType);

            // Configure default blend if provided
            string blendStyle = ParamCoercion.CoerceString(props["defaultBlendStyle"] ?? props["default_blend_style"], null);
            float blendDuration = ParamCoercion.CoerceFloat(props["defaultBlendDuration"] ?? props["default_blend_duration"], -1f);

            if (blendStyle != null || blendDuration >= 0)
            {
                // Set via SerializedProperty for the DefaultBlend struct
                using var so = new SerializedObject(brain);
                var defaultBlendProp = so.FindProperty("DefaultBlend") ?? so.FindProperty("m_DefaultBlend");
                if (defaultBlendProp != null)
                {
                    if (blendStyle != null)
                    {
                        var styleProp = defaultBlendProp.FindPropertyRelative("Style")
                                     ?? defaultBlendProp.FindPropertyRelative("m_Style");
                        if (styleProp != null)
                        {
                            int idx = Array.FindIndex(styleProp.enumNames,
                                n => n.Equals(blendStyle, StringComparison.OrdinalIgnoreCase));
                            if (idx >= 0)
                                styleProp.enumValueIndex = idx;
                        }
                    }
                    if (blendDuration >= 0)
                    {
                        var timeProp = defaultBlendProp.FindPropertyRelative("Time")
                                    ?? defaultBlendProp.FindPropertyRelative("m_Time");
                        if (timeProp != null)
                            timeProp.floatValue = blendDuration;
                    }
                    so.ApplyModifiedProperties();
                }
            }

            CameraHelpers.MarkDirty(cam.gameObject);

            return new
            {
                success = true,
                message = $"CinemachineBrain added to '{cam.gameObject.name}'.",
                data = new
                {
                    instanceID = cam.gameObject.GetInstanceIDCompat(),
                    alreadyExisted = false
                }
            };
        }

        internal static object CreateDolly(ToolParams p)
        {
            if (!CameraHelpers.HasCinemachine)
                return new ErrorResponse(
                    "Cinemachine package is not installed. " +
                    "Install com.unity.cinemachine via Package Manager.");

            string[] trackPoints = p.GetStringArray("trackPoints") ?? Array.Empty<string>();
            string cartName = p.Get("cartName", "DollyCart");
            string vcamPath = p.Get("vcamPath");

            // Resolve Cinemachine types via reflection
            var smoothPathType = CameraHelpers.ResolveComponentType("CinemachineSmoothPath");
            var dollyCartType = CameraHelpers.ResolveComponentType("CinemachineDollyCart");
            if (smoothPathType == null || dollyCartType == null)
                return new ErrorResponse("Cinemachine dolly types not found.");

            // Create CinemachineSmoothPath
            var pathGO = new GameObject("DollyTrack");
            var smoothPath = pathGO.AddComponent(smoothPathType);

            // Set waypoints via reflection
            var waypointsProp = (MemberInfo)smoothPathType.GetProperty("m_Waypoints")
                             ?? smoothPathType.GetField("m_Waypoints");
            if (waypointsProp != null)
            {
                var waypoints = GetWaypointsArray(waypointsProp, smoothPath);
                if (waypoints != null)
                {
                    int count = Mathf.Min(trackPoints.Length, waypoints.Length);
                    var wpType = waypoints.GetType().GetElementType();
                    var positionField = wpType?.GetField("position");
                    var rollField = wpType?.GetField("roll");

                    for (int i = 0; i < count; i++)
                    {
                        string[] parts = trackPoints[i].Split(',');
                        if (parts.Length >= 3 &&
                            float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                            float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                            float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float z))
                        {
                            var pos = new Vector3(x, y, z);
                            var wp = waypoints.GetValue(i);
                            positionField?.SetValue(wp, pos);

                            if (parts.Length >= 4 && rollField != null &&
                                float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float roll))
                            {
                                rollField.SetValue(wp, roll);
                            }

                            waypoints.SetValue(wp, i);
                        }
                    }
                }
            }

            // Create dolly cart
            var cartGO = new GameObject(cartName);
            var cart = cartGO.AddComponent(dollyCartType);

            // Set path and speed via reflection
            var pathMember = (MemberInfo)dollyCartType.GetProperty("m_Path")
                          ?? dollyCartType.GetField("m_Path");
            if (pathMember is PropertyInfo pPathPi) pPathPi.SetValue(cart, smoothPath);
            else if (pathMember is FieldInfo pPathFi) pPathFi.SetValue(cart, smoothPath);

            var speedMember = (MemberInfo)dollyCartType.GetProperty("m_Speed")
                           ?? dollyCartType.GetField("m_Speed");
            if (speedMember is PropertyInfo sPi) sPi.SetValue(cart, 1f);
            else if (speedMember is FieldInfo sFi) sFi.SetValue(cart, 1f);

            // Create virtual camera on cart
            var vcamGO = new GameObject("DollyVCam");
            var vcamType = CameraHelpers.CinemachineCameraType;
            var vcam = vcamGO.AddComponent(vcamType);
            vcamGO.transform.SetParent(cartGO.transform);

            if (!string.IsNullOrEmpty(vcamPath))
            {
                var followTarget = CameraHelpers.ResolveGameObjectRef(vcamPath);
                if (followTarget != null)
                    CameraHelpers.SetReflectionProperty(vcam, "Follow", followTarget.transform);
            }

            CameraHelpers.MarkDirty(pathGO);
            CameraHelpers.MarkDirty(cartGO);

            return new SuccessResponse(
                $"Created dolly track with {trackPoints.Length} waypoints",
                new { trackName = "DollyTrack", cartName, waypointsCount = trackPoints.Length });
        }

        internal static object CreateStateDriven(ToolParams p)
        {
            if (!CameraHelpers.HasCinemachine)
                return new ErrorResponse(
                    "Cinemachine package is not installed. " +
                    "Install com.unity.cinemachine via Package Manager.");

            string parentName = p.Get("parentName", "StateDrivenCamera");
            string animatorPath = p.Get("animatorPath");
            if (string.IsNullOrEmpty(animatorPath))
                return new ErrorResponse("'animatorPath' parameter is required.");
            string defaultCam = p.Get("defaultCam");

            var sdcType = CameraHelpers.ResolveComponentType("CinemachineStateDrivenCamera");
            if (sdcType == null)
                return new ErrorResponse("CinemachineStateDrivenCamera type not found.");

            var go = new GameObject(parentName);
            var sdc = go.AddComponent(sdcType);

            var animator = GameObject.Find(animatorPath)?.GetComponent<Animator>();
            if (animator == null)
                return new ErrorResponse("ANIMATOR_NOT_FOUND",
                    $"No Animator found at: {animatorPath}");

            // Set m_AnimatedTarget via reflection
            var animTargetMember = (MemberInfo)sdcType.GetProperty("m_AnimatedTarget")
                                ?? sdcType.GetField("m_AnimatedTarget");
            if (animTargetMember is PropertyInfo aPi) aPi.SetValue(sdc, animator);
            else if (animTargetMember is FieldInfo aFi) aFi.SetValue(sdc, animator);

            if (!string.IsNullOrEmpty(defaultCam))
            {
                var defaultCamGO = CameraHelpers.ResolveGameObjectRef(defaultCam);
                var defaultVCam = defaultCamGO?.GetComponent(CameraHelpers.CinemachineCameraType);
                if (defaultVCam != null)
                {
                    // Set m_ChildCameras via reflection
                    var childCamerasField = sdcType.GetField("m_ChildCameras");
                    if (childCamerasField != null)
                    {
                        var elementType = childCamerasField.FieldType.GetElementType();
                        if (elementType != null)
                        {
                            var childArray = Array.CreateInstance(elementType, 1);
                            childArray.SetValue(defaultVCam, 0);
                            childCamerasField.SetValue(sdc, childArray);
                        }
                    }
                }
            }

            CameraHelpers.MarkDirty(go);

            return new SuccessResponse(
                $"Created StateDrivenCamera '{parentName}'");
        }

        internal static object CreateClearshot(ToolParams p)
        {
            if (!CameraHelpers.HasCinemachine)
                return new ErrorResponse(
                    "Cinemachine package is not installed. " +
                    "Install com.unity.cinemachine via Package Manager.");

            string parentName = p.Get("parentName", "ClearShotCamera");
            string[] childVcams = p.GetStringArray("childVcams");

            var csType = CameraHelpers.ResolveComponentType("CinemachineClearShot");
            if (csType == null)
                return new ErrorResponse("CinemachineClearShot type not found.");

            var go = new GameObject(parentName);
            var cs = go.AddComponent(csType);

            if (childVcams != null && childVcams.Length > 0)
            {
                // Resolve child vcams by name/path
                var resolved = new List<object>();
                foreach (var name in childVcams)
                {
                    var camGO = CameraHelpers.ResolveGameObjectRef(name);
                    if (camGO != null)
                    {
                        var vcam = camGO.GetComponent(CameraHelpers.CinemachineCameraType);
                        if (vcam != null)
                            resolved.Add(vcam);
                    }
                }

                // Set m_ChildCameras via reflection
                var childCamerasField = csType.GetField("m_ChildCameras");
                if (childCamerasField != null && resolved.Count > 0)
                {
                    var elementType = childCamerasField.FieldType.GetElementType();
                    if (elementType != null)
                    {
                        var childArray = Array.CreateInstance(elementType, resolved.Count);
                        for (int i = 0; i < resolved.Count; i++)
                            childArray.SetValue(resolved[i], i);
                        childCamerasField.SetValue(cs, childArray);
                    }
                }
            }

            CameraHelpers.MarkDirty(go);

            return new SuccessResponse(
                $"Created ClearShot camera '{parentName}' with {childVcams?.Length ?? 0} children");
        }

        internal static object SetCinemachineVolume(ToolParams p)
        {
            if (!CameraHelpers.HasCinemachine)
                return new ErrorResponse(
                    "Cinemachine package is not installed. " +
                    "Install com.unity.cinemachine via Package Manager.");

            var vcamPathResult = p.GetRequired("vcamPath");
            if (!vcamPathResult.IsSuccess)
                return new ErrorResponse(vcamPathResult.ErrorMessage);
            string vcamPath = vcamPathResult.Value;

            string volumeProfilePath = p.Get("volumeProfilePath");
            int priority = p.GetInt("priority") ?? 0;

            var vcamGO = CameraHelpers.ResolveGameObjectRef(vcamPath);
            if (vcamGO == null)
                return new ErrorResponse("VCAM_NOT_FOUND",
                    $"GameObject not found: {vcamPath}");

            var vcam = vcamGO.GetComponent(CameraHelpers.CinemachineCameraType);
            if (vcam == null)
                return new ErrorResponse("VCAM_NOT_FOUND",
                    $"CinemachineCamera not found on: {vcamPath}");

            // Resolve CinemachineVolumeSettings type
            var volumeSettingsType = CameraHelpers.ResolveComponentType("CinemachineVolumeSettings");
            if (volumeSettingsType == null)
                return new ErrorResponse(
                    "CinemachineVolumeSettings type not found. Ensure Cinemachine package is up to date.");

            // Add or get CinemachineVolumeSettings extension
            var volumeSettings = vcamGO.GetComponent(volumeSettingsType);
            if (volumeSettings == null)
                volumeSettings = vcamGO.AddComponent(volumeSettingsType);

            if (!string.IsNullOrEmpty(volumeProfilePath))
            {
                var volumeProfileType = UnityTypeResolver.ResolveAny("VolumeProfile");
                if (volumeProfileType != null)
                {
                    var loadMethod = typeof(AssetDatabase).GetMethod(
                        "LoadAssetAtPath", new[] { typeof(string), typeof(Type) });
                    if (loadMethod != null)
                    {
                        var profile = loadMethod.Invoke(null,
                            new object[] { volumeProfilePath, volumeProfileType });
                        if (profile != null)
                        {
                            var profileMember = (MemberInfo)volumeSettingsType.GetField("m_Profile")
                                             ?? volumeSettingsType.GetProperty("m_Profile");
                            if (profileMember is PropertyInfo prPi) prPi.SetValue(volumeSettings, profile);
                            else if (profileMember is FieldInfo prFi) prFi.SetValue(volumeSettings, profile);
                        }
                    }
                }
            }

            CameraHelpers.MarkDirty(vcamGO);

            return new SuccessResponse(
                $"Set volume on '{vcamPath}' (priority={priority})");
        }

        private static Array GetWaypointsArray(MemberInfo propOrField, object target)
        {
            if (propOrField is PropertyInfo pi)
                return pi.GetValue(target) as Array;
            if (propOrField is FieldInfo fi)
                return fi.GetValue(target) as Array;
            return null;
        }
    }
}
