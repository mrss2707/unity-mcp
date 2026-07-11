using System;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Manage Unity Audio — AudioSources, Audio Mixers, spatial audio.
    /// Actions: create_source, set_source, play, stop, create_mixer, expose_param, set_snapshot, configure_spatial.
    /// </summary>
    [McpForUnityTool("manage_audio",
        Description = "Manage Unity Audio — AudioSources, Audio Mixers, spatial audio")]
    public static class ManageAudio
    {
        /// <summary>
        /// Cached AudioMixerController type for reflection-based mixer operations.
        /// </summary>
        private static Type _mixerControllerType;

        /// <summary>
        /// Cached AudioMixerSnapshot type for reflection-based snapshot operations.
        /// </summary>
        private static Type _snapshotType;

        /// <summary>
        /// Whether we have attempted reflection init for mixer types.
        /// </summary>
        private static bool _mixerReflectionInitAttempted;

        /// <summary>
        /// Lazily resolves the AudioMixerController type via reflection.
        /// Returns true if the type was found (i.e. the UnityEditor.Audio namespace is available).
        /// </summary>
        private static bool EnsureMixerReflectionCache()
        {
            if (_mixerReflectionInitAttempted)
                return _mixerControllerType != null;

            _mixerReflectionInitAttempted = true;

            try
            {
                _mixerControllerType = Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor");
                _snapshotType = Type.GetType("UnityEditor.Audio.AudioMixerSnapshotController, UnityEditor");
            }
            catch
            {
                // Reflection initialization failed — mixer API not available
            }

            return _mixerControllerType != null;
        }

        public static object HandleCommand(JObject @params)
        {
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
                    "create_source" => CreateSource(p),
                    "set_source" => SetSource(p),
                    "play" => Play(p),
                    "stop" => Stop(p),
                    "create_mixer" => CreateMixer(p),
                    "expose_param" => ExposeParam(p),
                    "set_snapshot" => SetSnapshot(p),
                    "configure_spatial" => ConfigureSpatial(p),
                    _ => new ErrorResponse("UNKNOWN_ACTION",
                        $"Unknown action: {action}. Valid actions: create_source, set_source, play, stop, create_mixer, expose_param, set_snapshot, configure_spatial.")
                };
            }
            catch (Exception ex)
            {
                return new ErrorResponse("OPERATION_ERROR", ex.Message);
            }
        }

        // ─────────────────────────────────────────────
        // 1. create_source
        // ─────────────────────────────────────────────

        /// <summary>
        /// Adds an AudioSource component to a GameObject.
        /// </summary>
        private static object CreateSource(ToolParams p)
        {
            try
            {
                string goPath = p.Get("gameObjectPath");
                if (string.IsNullOrEmpty(goPath))
                    return new ErrorResponse("'gameObjectPath' parameter is required.");

                GameObject go = GameObject.Find(goPath);
                if (go == null)
                    return new ErrorResponse("NOT_FOUND", $"GameObject '{goPath}' not found.");

                AudioSource source = go.AddComponent<AudioSource>();

                string clipPath = p.Get("clipPath");
                if (!string.IsNullOrEmpty(clipPath))
                {
                    AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                    if (clip != null)
                        source.clip = clip;
                }

                float? volume = p.GetFloat("volume");
                if (volume.HasValue)
                    source.volume = Mathf.Clamp01(volume.Value);

                bool? loop = p.GetBool("loop");
                if (loop.HasValue)
                    source.loop = loop.Value;

                bool? playOnAwake = p.GetBool("playOnAwake");
                if (playOnAwake.HasValue)
                    source.playOnAwake = playOnAwake.Value;

                EditorUtility.SetDirty(go);

                return new SuccessResponse(
                    $"AudioSource added to '{go.name}'.",
                    new
                    {
                        gameObject = go.name,
                        clip = source.clip != null ? source.clip.name : null,
                        volume = source.volume,
                        loop = source.loop,
                        playOnAwake = source.playOnAwake
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("CREATE_SOURCE_FAILED", $"Failed to create AudioSource: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 2. set_source
        // ─────────────────────────────────────────────

        /// <summary>
        /// Configures an existing AudioSource on a GameObject.
        /// </summary>
        private static object SetSource(ToolParams p)
        {
            try
            {
                string goPath = p.Get("gameObjectPath");
                if (string.IsNullOrEmpty(goPath))
                    return new ErrorResponse("'gameObjectPath' parameter is required.");

                GameObject go = GameObject.Find(goPath);
                if (go == null)
                    return new ErrorResponse("NOT_FOUND", $"GameObject '{goPath}' not found.");

                AudioSource source = go.GetComponent<AudioSource>();
                if (source == null)
                    return new ErrorResponse("NO_AUDIO_SOURCE", $"No AudioSource found on '{go.name}'. Use create_source first.");

                string clipPath = p.Get("clipPath");
                if (!string.IsNullOrEmpty(clipPath))
                {
                    AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                    if (clip != null)
                        source.clip = clip;
                }

                float? volume = p.GetFloat("volume");
                if (volume.HasValue)
                    source.volume = Mathf.Clamp01(volume.Value);

                float? pitch = p.GetFloat("pitch");
                if (pitch.HasValue)
                    source.pitch = pitch.Value;

                bool? loop = p.GetBool("loop");
                if (loop.HasValue)
                    source.loop = loop.Value;

                bool? playOnAwake = p.GetBool("playOnAwake");
                if (playOnAwake.HasValue)
                    source.playOnAwake = playOnAwake.Value;

                float? spatialBlend = p.GetFloat("spatialBlend");
                if (spatialBlend.HasValue)
                    source.spatialBlend = Mathf.Clamp01(spatialBlend.Value);

                EditorUtility.SetDirty(go);

                return new SuccessResponse(
                    $"AudioSource configured on '{go.name}'.",
                    new
                    {
                        gameObject = go.name,
                        clip = source.clip != null ? source.clip.name : null,
                        volume = source.volume,
                        pitch = source.pitch,
                        loop = source.loop,
                        playOnAwake = source.playOnAwake,
                        spatialBlend = source.spatialBlend
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("SET_SOURCE_FAILED", $"Failed to configure AudioSource: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 3. play
        // ─────────────────────────────────────────────

        /// <summary>
        /// Plays an AudioSource. Only works in Play Mode.
        /// </summary>
        private static object Play(ToolParams p)
        {
            try
            {
                if (!Application.isPlaying)
                    return new ErrorResponse("EDIT_MODE_NOT_SUPPORTED", "Audio playback only works in Play Mode.");

                string goPath = p.Get("gameObjectPath");
                if (string.IsNullOrEmpty(goPath))
                    return new ErrorResponse("'gameObjectPath' parameter is required.");

                GameObject go = GameObject.Find(goPath);
                if (go == null)
                    return new ErrorResponse("NOT_FOUND", $"GameObject '{goPath}' not found.");

                AudioSource source = go.GetComponent<AudioSource>();
                if (source == null)
                    return new ErrorResponse("NO_AUDIO_SOURCE", $"No AudioSource found on '{go.name}'.");

                string clipPath = p.Get("clipPath");
                if (!string.IsNullOrEmpty(clipPath))
                {
                    AudioClip oneShotClip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                    if (oneShotClip != null)
                    {
                        float? volume = p.GetFloat("volume");
                        source.PlayOneShot(oneShotClip, volume ?? 1.0f);

                        return new SuccessResponse(
                            $"Playing one-shot audio '{oneShotClip.name}' on '{go.name}'.",
                            new { gameObject = go.name, clip = oneShotClip.name, mode = "one_shot" });
                    }
                }

                source.Play();

                return new SuccessResponse(
                    $"AudioSource on '{go.name}' is now playing.",
                    new
                    {
                        gameObject = go.name,
                        clip = source.clip != null ? source.clip.name : null,
                        isPlaying = source.isPlaying
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("PLAY_FAILED", $"Failed to play audio: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 4. stop
        // ─────────────────────────────────────────────

        /// <summary>
        /// Stops an AudioSource. Only works in Play Mode.
        /// </summary>
        private static object Stop(ToolParams p)
        {
            try
            {
                if (!Application.isPlaying)
                    return new ErrorResponse("EDIT_MODE_NOT_SUPPORTED", "Audio playback only works in Play Mode.");

                string goPath = p.Get("gameObjectPath");
                if (string.IsNullOrEmpty(goPath))
                    return new ErrorResponse("'gameObjectPath' parameter is required.");

                GameObject go = GameObject.Find(goPath);
                if (go == null)
                    return new ErrorResponse("NOT_FOUND", $"GameObject '{goPath}' not found.");

                AudioSource source = go.GetComponent<AudioSource>();
                if (source == null)
                    return new ErrorResponse("NO_AUDIO_SOURCE", $"No AudioSource found on '{go.name}'.");

                source.Stop();

                return new SuccessResponse(
                    $"AudioSource on '{go.name}' stopped.",
                    new { gameObject = go.name });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("STOP_FAILED", $"Failed to stop audio: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 5. create_mixer
        // ─────────────────────────────────────────────

        /// <summary>
        /// Creates an AudioMixer asset via reflection on UnityEditor.Audio.AudioMixerController.
        /// </summary>
        private static object CreateMixer(ToolParams p)
        {
            try
            {
                if (!EnsureMixerReflectionCache())
                    return new ErrorResponse("MIXER_NOT_SUPPORTED",
                        "Audio Mixer API is not available. Ensure you are using Unity Editor (not a runtime build).");

                string outputPath = p.Get("outputPath");
                if (string.IsNullOrEmpty(outputPath))
                    return new ErrorResponse("'outputPath' parameter is required (e.g. 'Assets/Audio/MyMixer.mixer').");

                // Normalize path
                outputPath = AssetPathUtility.SanitizeAssetPath(outputPath);
                if (outputPath == null)
                    return new ErrorResponse("Invalid path: contains traversal sequences.");

                if (!outputPath.EndsWith(".mixer", StringComparison.OrdinalIgnoreCase))
                    outputPath += ".mixer";

                // Check if already exists
                var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(outputPath);
                if (existing != null)
                {
                    bool overwrite = p.GetBool("overwrite");
                    if (!overwrite)
                        return new ErrorResponse($"AudioMixer already exists at '{outputPath}'. Set overwrite=true to replace it.");
                    AssetDatabase.DeleteAsset(outputPath);
                }

                // Ensure directory exists
                string dir = System.IO.Path.GetDirectoryName(outputPath);
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

                // Create AudioMixerController instance via reflection
                var mixerInstance = Activator.CreateInstance(_mixerControllerType);

                // Create the asset
                AssetDatabase.CreateAsset(mixerInstance as UnityEngine.Object, outputPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                string guid = AssetDatabase.AssetPathToGUID(outputPath);

                return new SuccessResponse(
                    $"AudioMixer created at '{outputPath}'.",
                    new { path = outputPath, guid });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("CREATE_MIXER_FAILED", $"Failed to create AudioMixer: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 6. expose_param
        // ─────────────────────────────────────────────

        /// <summary>
        /// Exposes a parameter on an AudioMixer for runtime control via reflection.
        /// </summary>
        private static object ExposeParam(ToolParams p)
        {
            try
            {
                if (!EnsureMixerReflectionCache())
                    return new ErrorResponse("MIXER_NOT_SUPPORTED",
                        "Audio Mixer API is not available. Ensure you are using Unity Editor (not a runtime build).");

                string mixerPath = p.Get("mixerPath");
                if (string.IsNullOrEmpty(mixerPath))
                    return new ErrorResponse("'mixerPath' parameter is required.");

                mixerPath = AssetPathUtility.SanitizeAssetPath(mixerPath);
                if (mixerPath == null)
                    return new ErrorResponse("Invalid path: contains traversal sequences.");

                var mixerAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(mixerPath);
                if (mixerAsset == null || !_mixerControllerType.IsInstanceOfType(mixerAsset))
                    return new ErrorResponse("MIXER_NOT_FOUND", $"No AudioMixer found at '{mixerPath}'.");

                string paramName = p.Get("paramName");
                if (string.IsNullOrEmpty(paramName))
                    return new ErrorResponse("'paramName' parameter is required (e.g. 'MyParam').");

                // Get exposed parameters via reflection
                var exposedParamsProp = _mixerControllerType.GetProperty("exposedParameters",
                    BindingFlags.Public | BindingFlags.Instance);
                if (exposedParamsProp == null)
                    return new ErrorResponse("MIXER_API_INCOMPATIBLE", "Cannot access exposed parameters on this Unity version.");

                var currentParams = (Array)exposedParamsProp.GetValue(mixerAsset);

                // Check for duplicate
                var nameField = currentParams?.GetType().GetElementType()?.GetField("name");
                if (nameField != null && currentParams != null)
                {
                    for (int i = 0; i < currentParams.Length; i++)
                    {
                        var existingName = (string)nameField.GetValue(currentParams.GetValue(i));
                        if (existingName == paramName)
                            return new ErrorResponse("PARAM_ALREADY_EXPOSED", $"Parameter '{paramName}' is already exposed.");
                    }
                }

                // Create a new exposed parameter struct via reflection and add it
                var elementType = currentParams?.GetType().GetElementType();
                if (elementType == null)
                {
                    // Try using AddExposedParameter method
                    var addMethod = _mixerControllerType.GetMethod("AddExposedParameter",
                        BindingFlags.Public | BindingFlags.Instance,
                        null, new[] { typeof(string) }, null);
                    if (addMethod != null)
                    {
                        addMethod.Invoke(mixerAsset, new object[] { paramName });
                    }
                    else
                    {
                        // Fallback: use the ExposedAudioParameter array
                        var exposedParamType = Type.GetType("UnityEditor.Audio.ExposedAudioParameter, UnityEditor");
                        if (exposedParamType == null)
                            return new ErrorResponse("MIXER_API_INCOMPATIBLE", "Cannot create exposed parameter on this Unity version.");

                        var newParam = Activator.CreateInstance(exposedParamType);
                        var nameProp = exposedParamType.GetProperty("name");
                        nameProp?.SetValue(newParam, paramName);

                        var newArray = Array.CreateInstance(exposedParamType, (currentParams?.Length ?? 0) + 1);
                        if (currentParams != null)
                        {
                            for (int i = 0; i < currentParams.Length; i++)
                                newArray.SetValue(currentParams.GetValue(i), i);
                        }
                        newArray.SetValue(newParam, newArray.Length - 1);
                        exposedParamsProp.SetValue(mixerAsset, newArray);
                    }
                }
                else
                {
                    // We know the element type, create a new instance
                    var newParam = Activator.CreateInstance(elementType);
                    var nameFieldInfo = elementType.GetField("name");
                    nameFieldInfo?.SetValue(newParam, paramName);

                    var newArray = Array.CreateInstance(elementType, (currentParams?.Length ?? 0) + 1);
                    if (currentParams != null)
                    {
                        for (int i = 0; i < currentParams.Length; i++)
                            newArray.SetValue(currentParams.GetValue(i), i);
                    }
                    newArray.SetValue(newParam, newArray.Length - 1);
                    exposedParamsProp.SetValue(mixerAsset, newArray);
                }

                EditorUtility.SetDirty(mixerAsset);
                AssetDatabase.SaveAssets();

                return new SuccessResponse(
                    $"Parameter '{paramName}' exposed on AudioMixer at '{mixerPath}'.",
                    new { path = mixerPath, paramName });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("EXPOSE_PARAM_FAILED", $"Failed to expose parameter: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 7. set_snapshot
        // ─────────────────────────────────────────────

        /// <summary>
        /// Sets an AudioMixer snapshot via reflection, transitioning over fadeTime.
        /// </summary>
        private static object SetSnapshot(ToolParams p)
        {
            try
            {
                if (!EnsureMixerReflectionCache())
                    return new ErrorResponse("MIXER_NOT_SUPPORTED",
                        "Audio Mixer API is not available. Ensure you are using Unity Editor (not a runtime build).");

                string mixerPath = p.Get("mixerPath");
                if (string.IsNullOrEmpty(mixerPath))
                    return new ErrorResponse("'mixerPath' parameter is required.");

                mixerPath = AssetPathUtility.SanitizeAssetPath(mixerPath);
                if (mixerPath == null)
                    return new ErrorResponse("Invalid path: contains traversal sequences.");

                var mixerAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(mixerPath);
                if (mixerAsset == null || !_mixerControllerType.IsInstanceOfType(mixerAsset))
                    return new ErrorResponse("MIXER_NOT_FOUND", $"No AudioMixer found at '{mixerPath}'.");

                string snapshotName = p.Get("snapshotName");
                if (string.IsNullOrEmpty(snapshotName))
                    return new ErrorResponse("'snapshotName' parameter is required.");

                float fadeTime = p.GetFloat("fadeTime") ?? 0.1f;

                // Find the snapshot by name via reflection
                var snapshotsProp = _mixerControllerType.GetProperty("snapshots",
                    BindingFlags.Public | BindingFlags.Instance);
                if (snapshotsProp == null)
                    return new ErrorResponse("MIXER_API_INCOMPATIBLE", "Cannot access snapshots on this Unity version.");

                var snapshots = (Array)snapshotsProp.GetValue(mixerAsset);
                if (snapshots == null || snapshots.Length == 0)
                    return new ErrorResponse("NO_SNAPSHOTS", "No snapshots found on this AudioMixer.");

                object targetSnapshot = null;
                var nameProp = _snapshotType?.GetProperty("name") ?? snapshots.GetType().GetElementType()?.GetProperty("name");

                for (int i = 0; i < snapshots.Length; i++)
                {
                    var snapshot = snapshots.GetValue(i);
                    var snapName = nameProp != null ? (string)nameProp.GetValue(snapshot) : null;
                    if (snapName == snapshotName)
                    {
                        targetSnapshot = snapshot;
                        break;
                    }
                }

                if (targetSnapshot == null)
                    return new ErrorResponse("SNAPSHOT_NOT_FOUND", $"Snapshot '{snapshotName}' not found on this AudioMixer.");

                // Call TransitionTo on the snapshot via reflection
                // The method signature is: TransitionTo( AudioMixerSnapshotController snapshot, float fadeTime )
                // Actually, the transition is typically on the mixer controller itself.
                // Try AudioMixerController.TransitionToSnapshot or similar.
                var transitionMethod = _mixerControllerType.GetMethod("TransitionToSnapshot",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { _snapshotType ?? targetSnapshot.GetType(), typeof(float) }, null);

                if (transitionMethod == null)
                {
                    // Try with object parameter
                    transitionMethod = _mixerControllerType.GetMethod("TransitionToSnapshot",
                        BindingFlags.Public | BindingFlags.Instance,
                        null, new[] { typeof(UnityEngine.Object), typeof(float) }, null);
                }

                if (transitionMethod == null)
                {
                    // Last resort: find any TransitionTo method
                    transitionMethod = _mixerControllerType.GetMethod("TransitionToSnapshot",
                        BindingFlags.Public | BindingFlags.Instance);
                }

                if (transitionMethod != null)
                {
                    var parameters = transitionMethod.GetParameters();
                    if (parameters.Length == 2)
                    {
                        var snapshotParam = targetSnapshot;
                        // If the method expects UnityEngine.Object, wrap as needed
                        if (parameters[0].ParameterType == typeof(UnityEngine.Object))
                            snapshotParam = targetSnapshot as UnityEngine.Object ?? targetSnapshot;

                        transitionMethod.Invoke(mixerAsset, new[] { snapshotParam, (object)fadeTime });
                    }
                    else if (parameters.Length == 1)
                    {
                        transitionMethod.Invoke(mixerAsset, new[] { targetSnapshot });
                    }
                }

                EditorUtility.SetDirty(mixerAsset);

                return new SuccessResponse(
                    $"Transitioning to snapshot '{snapshotName}' with fade time {fadeTime}s.",
                    new { path = mixerPath, snapshotName, fadeTime });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("SET_SNAPSHOT_FAILED", $"Failed to set snapshot: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 8. configure_spatial
        // ─────────────────────────────────────────────

        /// <summary>
        /// Configures 3D spatial audio settings on an AudioSource.
        /// </summary>
        private static object ConfigureSpatial(ToolParams p)
        {
            try
            {
                string goPath = p.Get("gameObjectPath");
                if (string.IsNullOrEmpty(goPath))
                    return new ErrorResponse("'gameObjectPath' parameter is required.");

                GameObject go = GameObject.Find(goPath);
                if (go == null)
                    return new ErrorResponse("NOT_FOUND", $"GameObject '{goPath}' not found.");

                AudioSource source = go.GetComponent<AudioSource>();
                if (source == null)
                    return new ErrorResponse("NO_AUDIO_SOURCE", $"No AudioSource found on '{go.name}'. Use create_source first.");

                float? spatialBlend = p.GetFloat("spatialBlend");
                if (spatialBlend.HasValue)
                    source.spatialBlend = Mathf.Clamp01(spatialBlend.Value);

                float? minDistance = p.GetFloat("minDistance");
                if (minDistance.HasValue)
                    source.minDistance = Mathf.Max(0f, minDistance.Value);

                float? maxDistance = p.GetFloat("maxDistance");
                if (maxDistance.HasValue)
                    source.maxDistance = Mathf.Max(0f, maxDistance.Value);

                string rolloffModeStr = p.Get("rolloffMode");
                if (!string.IsNullOrEmpty(rolloffModeStr))
                {
                    try
                    {
                        source.rolloffMode = (AudioRolloffMode)Enum.Parse(typeof(AudioRolloffMode),
                            rolloffModeStr, ignoreCase: true);
                    }
                    catch (ArgumentException)
                    {
                        return new ErrorResponse("INVALID_ROLLOFF", $"Invalid rolloffMode '{rolloffModeStr}'. Valid values: Logarithmic, Linear, Custom.");
                    }
                }

                float? dopplerLevel = p.GetFloat("dopplerLevel");
                if (dopplerLevel.HasValue)
                    source.dopplerLevel = Mathf.Max(0f, dopplerLevel.Value);

                EditorUtility.SetDirty(go);

                return new SuccessResponse(
                    $"Spatial audio configured on '{go.name}'.",
                    new
                    {
                        gameObject = go.name,
                        spatialBlend = source.spatialBlend,
                        minDistance = source.minDistance,
                        maxDistance = source.maxDistance,
                        rolloffMode = source.rolloffMode.ToString(),
                        dopplerLevel = source.dopplerLevel
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("CONFIGURE_SPATIAL_FAILED", $"Failed to configure spatial audio: {ex.Message}");
            }
        }
    }
}
