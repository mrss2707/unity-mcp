using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.U2D;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Manage Cross-platform Optimization — quality levels, texture compression, lightmaps, occlusion, and build analysis.
    /// Actions: set_quality_settings, configure_texture_compression, batch_resize_textures, set_sprite_atlas,
    ///          configure_lightmap, analyze_build_size, configure_occlusion.
    /// </summary>
    [McpForUnityTool("manage_optimization",
        Description = "Manage Cross-platform Optimization")]
    public static class ManageOptimization
    {
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
                    "set_quality_settings" => SetQualitySettings(p),
                    "configure_texture_compression" => ConfigureTextureCompression(p),
                    "batch_resize_textures" => BatchResizeTextures(p),
                    "set_sprite_atlas" => SetSpriteAtlas(p),
                    "configure_lightmap" => ConfigureLightmap(p),
                    "analyze_build_size" => AnalyzeBuildSize(p),
                    "configure_occlusion" => ConfigureOcclusion(p),
                    _ => new ErrorResponse("UNKNOWN_ACTION",
                        $"Unknown action: {action}. Valid actions: set_quality_settings, configure_texture_compression, batch_resize_textures, set_sprite_atlas, configure_lightmap, analyze_build_size, configure_occlusion.")
                };
            }
            catch (Exception ex)
            {
                return new ErrorResponse("OPERATION_ERROR", ex.Message);
            }
        }

        // ─────────────────────────────────────────────
        // 1. set_quality_settings
        // ─────────────────────────────────────────────

        /// <summary>
        /// Configures Unity quality settings for a given platform and preset level.
        /// Maps preset names (low, medium, high, ultra) to specific quality overrides
        /// including shadow resolution, texture quality, LOD bias, and anti-aliasing.
        /// </summary>
        private static object SetQualitySettings(ToolParams p)
        {
            try
            {
                string preset = p.Get("preset");
                if (string.IsNullOrEmpty(preset))
                    return new ErrorResponse("'preset' parameter is required (low, medium, high, ultra).");

                string platform = p.Get("platform");

                // Resolve BuildTargetGroup for platform-specific quality
                BuildTargetGroup targetGroup = BuildTargetGroup.Unknown;
                if (!string.IsNullOrEmpty(platform))
                {
                    try
                    {
                        // Try common platform name mappings
                        string platformLower = platform.ToLowerInvariant();
                        targetGroup = platformLower switch
                        {
                            "standalone" or "windows" or "mac" or "linux" => BuildTargetGroup.Standalone,
                            "android" => BuildTargetGroup.Android,
                            "ios" or "iphone" => BuildTargetGroup.iOS,
                            "webgl" => BuildTargetGroup.WebGL,
                            "ps4" or "playstation4" => BuildTargetGroup.PS4,
                            "ps5" or "playstation5" => BuildTargetGroup.PS5,
                            "switch" or "nintendo" => BuildTargetGroup.Switch,
                            "xboxone" => BuildTargetGroup.XboxOne,
                            "tvOS" or "tvos" or "apple tv" => BuildTargetGroup.tvOS,
                            _ => (BuildTargetGroup)Enum.Parse(typeof(BuildTargetGroup), platform, ignoreCase: true)
                        };
                    }
                    catch
                    {
                        return new ErrorResponse("INVALID_PLATFORM", $"Unknown platform '{platform}'.");
                    }
                }

                // Map preset to quality parameters
                int shadowResolution;
                int textureQuality;
                float lodBias;
                int antiAliasing;

                switch (preset.ToLowerInvariant())
                {
                    case "low":
                        shadowResolution = 512;
                        textureQuality = QualitySettings.globalTextureMipmapLimit; // half res
                        QualitySettings.globalTextureMipmapLimit = 1;
                        lodBias = 0.5f;
                        antiAliasing = 0;
                        break;

                    case "medium":
                        shadowResolution = 1024;
                        QualitySettings.globalTextureMipmapLimit = 0; // full res
                        lodBias = 1.0f;
                        antiAliasing = 2;
                        break;

                    case "high":
                        shadowResolution = 2048;
                        QualitySettings.globalTextureMipmapLimit = 0;
                        lodBias = 2.0f;
                        antiAliasing = 4;
                        break;

                    case "ultra":
                        shadowResolution = 4096;
                        QualitySettings.globalTextureMipmapLimit = 0;
                        lodBias = 4.0f;
                        antiAliasing = 8;
                        break;

                    default:
                        return new ErrorResponse("INVALID_PRESET",
                            $"Unknown preset '{preset}'. Valid values: low, medium, high, ultra.");
                }

                // Apply individual quality settings
                QualitySettings.shadowResolution = (UnityEngine.ShadowResolution)shadowResolution;
                QualitySettings.lodBias = lodBias;
                QualitySettings.antiAliasing = antiAliasing;
                QualitySettings.shadowCascades = preset.ToLowerInvariant() switch
                {
                    "low" => 0,
                    "medium" => 2,
                    "high" or "ultra" => 4,
                    _ => QualitySettings.shadowCascades
                };

                // Select named quality level for the target group
                if (targetGroup != BuildTargetGroup.Unknown)
                {
                    int levelIndex = preset.ToLowerInvariant() switch
                    {
                        "low" => 0,
                        "medium" => 2,
                        "high" => 4,
                        "ultra" => 5,
                        _ => -1
                    };

                    if (levelIndex >= 0)
                    {
#if !UNITY_2022_2_OR_NEWER
                        int[] levels = QualitySettings.GetQualityLevelsForPlatform(targetGroup);
                        if (levels != null && levels.Length > 0)
                        {
#endif
                            int clampedIndex = Mathf.Min(levelIndex, QualitySettings.names.Length - 1);
                            QualitySettings.SetQualityLevel(clampedIndex, applyExpensiveChanges: true);
#if !UNITY_2022_2_OR_NEWER
                        }
#endif
                    }
                }

                return new SuccessResponse(
                    $"Quality settings applied: preset '{preset}'.",
                    new
                    {
                        preset,
                        platform = string.IsNullOrEmpty(platform) ? null : platform,
                        shadowResolution,
                        masterTextureLimit = QualitySettings.globalTextureMipmapLimit,
                        lodBias,
                        antiAliasing,
                        shadowCascades = QualitySettings.shadowCascades
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("SET_QUALITY_FAILED", $"Failed to set quality settings: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 2. configure_texture_compression
        // ─────────────────────────────────────────────

        /// <summary>
        /// Configures texture compression for a target platform. Optionally iterates
        /// textures under a given path to apply platform-specific overrides.
        /// </summary>
        private static object ConfigureTextureCompression(ToolParams p)
        {
            try
            {
                string platform = p.Get("platform");
                if (string.IsNullOrEmpty(platform))
                    return new ErrorResponse("'platform' parameter is required.");

                string format = p.Get("format");
                string path = p.Get("path");

                string platformLower = platform.ToLowerInvariant();

                // Set platform-wide compression targets
                if (platformLower == "android")
                {
                    if (!string.IsNullOrEmpty(format))
                    {
                        string formatLower = format.ToLowerInvariant();
                        EditorUserBuildSettings.androidBuildSubtarget = formatLower switch
                        {
                            "etc" or "etc1" => MobileTextureSubtarget.ETC,
                            "astc" => MobileTextureSubtarget.ASTC,
#if !UNITY_2022_1_OR_NEWER
                            "pvrtc" => MobileTextureSubtarget.PVRTC,
#endif
                            "etc2" => MobileTextureSubtarget.ETC2,
                            _ => MobileTextureSubtarget.Generic
                        };
                    }
                }
                else if (platformLower == "ios" || platformLower == "iphone" || platformLower == "tvos")
                {
                    if (!string.IsNullOrEmpty(format))
                    {
                        string formatLower = format.ToLowerInvariant();
#if !UNITY_2023_1_OR_NEWER
                        EditorUserBuildSettings.iosBuildSubtarget = formatLower switch
                        {
                            "pvrtc" => MobileTextureSubtarget.PVRTC,
                            "astc" => MobileTextureSubtarget.ASTC,
                            "etc" or "etc2" => MobileTextureSubtarget.ETC2,
                            _ => MobileTextureSubtarget.Generic
                        };
#else
                        // iosBuildSubtarget removed in Unity 2023.1+
                        // Per-texture platform overrides are applied below via TextureImporter
#endif
                    }
                }

                // Apply platform overrides to individual textures under path
                int updatedCount = 0;
                if (!string.IsNullOrEmpty(path))
                {
                    string safePath = AssetPathUtility.SanitizeAssetPath(path);
                    if (safePath == null)
                        return new ErrorResponse("INVALID_PATH", $"Invalid path '{path}'.");

                    string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { safePath });
                    foreach (string guid in textureGuids)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                        if (importer == null) continue;

                        var platformSettings = importer.GetPlatformTextureSettings(platform);
                        if (!string.IsNullOrEmpty(format))
                        {
                            platformSettings.overridden = true;
                            platformSettings.format = TryParseTextureFormat(format, platformLower);
                        }
                        else
                        {
                            platformSettings.overridden = true;
                        }

                        importer.SetPlatformTextureSettings(platformSettings);
                        updatedCount++;
                    }

                    if (updatedCount > 0)
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }

                return new SuccessResponse(
                    $"Texture compression configured for '{platform}'.",
                    new
                    {
                        platform,
                        format = format ?? "default",
                        texturesUpdated = updatedCount
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("CONFIGURE_COMPRESSION_FAILED", $"Failed to configure texture compression: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to parse a texture format string into a TextureImporterFormat.
        /// Falls back to Automatic (platform default) on failure.
        /// </summary>
        private static TextureImporterFormat TryParseTextureFormat(string format, string platform)
        {
            if (string.IsNullOrEmpty(format))
                return TextureImporterFormat.Automatic;

            try
            {
                return (TextureImporterFormat)Enum.Parse(typeof(TextureImporterFormat), format, ignoreCase: true);
            }
            catch
            {
                // Fallback to platform-specific defaults
                return platform switch
                {
                    "android" => TextureImporterFormat.Automatic,
                    "ios" or "iphone" => TextureImporterFormat.Automatic,
                    _ => TextureImporterFormat.Automatic
                };
            }
        }

        // ─────────────────────────────────────────────
        // 3. batch_resize_textures
        // ─────────────────────────────────────────────

        /// <summary>
        /// Batch resizes all textures under a given path to the specified maximum dimensions.
        /// Validates the path is within Assets/ and performs a single AssetDatabase.Refresh
        /// at the end of the operation.
        /// </summary>
        private static object BatchResizeTextures(ToolParams p)
        {
            try
            {
                string path = p.Get("path");
                if (string.IsNullOrEmpty(path))
                    return new ErrorResponse("'path' parameter is required.");

                // Validate path is within Assets/
                if (!AssetPathUtility.IsValidAssetPath(path))
                    return new ErrorResponse("INVALID_PATH", $"Path '{path}' is not valid. Must be within the Assets/ folder.");

                int? maxWidth = p.GetInt("maxWidth");
                int? maxHeight = p.GetInt("maxHeight");
                string filter = p.Get("filter");

                if (!maxWidth.HasValue && !maxHeight.HasValue)
                    return new ErrorResponse("Either 'maxWidth' or 'maxHeight' parameter is required.");

                int targetMaxSize = Mathf.Max(maxWidth ?? 8192, maxHeight ?? 8192);
                string searchFilter = "t:Texture2D";
                if (!string.IsNullOrEmpty(filter))
                    searchFilter = filter;

                string[] textureGuids = AssetDatabase.FindAssets(searchFilter, new[] { path });
                var modifiedPaths = new List<string>();

                foreach (string guid in textureGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer == null) continue;

                    // Only modify if current size is larger than target
                    if (importer.maxTextureSize > targetMaxSize)
                    {
                        importer.maxTextureSize = targetMaxSize;
                        importer.SaveAndReimport();
                        modifiedPaths.Add(assetPath);
                    }
                }

                // Single refresh at end
                if (modifiedPaths.Count > 0)
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                return new SuccessResponse(
                    $"Resized {modifiedPaths.Count} texture(s) to max dimension {targetMaxSize}.",
                    new
                    {
                        maxSize = targetMaxSize,
                        texturesResized = modifiedPaths.Count,
                        path
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("BATCH_RESIZE_FAILED", $"Failed to batch resize textures: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 4. set_sprite_atlas
        // ─────────────────────────────────────────────

        /// <summary>
        /// Creates or configures a SpriteAtlas asset. Supports setting packing settings
        /// (allowRotation, tightPacking, padding) and including sprite/texture paths.
        /// </summary>
        private static object SetSpriteAtlas(ToolParams p)
        {
            try
            {
                string atlasName = p.Get("atlasName");
                if (string.IsNullOrEmpty(atlasName))
                    return new ErrorResponse("'atlasName' parameter is required.");

                string outputPath = p.Get("outputPath") ?? $"Assets/{atlasName}.spriteatlas";
                outputPath = AssetPathUtility.SanitizeAssetPath(outputPath);
                if (outputPath == null)
                    return new ErrorResponse("INVALID_PATH", "Invalid output path.");

                if (!outputPath.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase))
                    outputPath += ".spriteatlas";

                // Load existing or create new atlas
                SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(outputPath);
                bool isNew = atlas == null;

                if (isNew)
                {
                    atlas = new SpriteAtlas();

                    // Ensure directory exists
                    string dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                    {
                        string normalizedDir = dir.Replace('\\', '/');
                        string[] parts = normalizedDir.Split('/');
                        string current = parts[0];
                        for (int i = 1; i < parts.Length; i++)
                        {
                            string next = current + "/" + parts[i];
                            if (!AssetDatabase.IsValidFolder(next))
                                AssetDatabase.CreateFolder(current, parts[i]);
                            current = next;
                        }
                    }

                    AssetDatabase.CreateAsset(atlas, outputPath);
                }

                // Apply packing settings
#if UNITY_2022_2_OR_NEWER
                var packingToken = p.GetRaw("packingSettings") as JObject;
                if (packingToken != null)
                {
                    if (packingToken["allowRotation"] != null)
                        atlas.enableRotation = packingToken["allowRotation"].Value<bool>();
                    if (packingToken["tightPacking"] != null)
                        atlas.enableTightPacking = packingToken["tightPacking"].Value<bool>();
                    if (packingToken["padding"] != null)
                        atlas.padding = packingToken["padding"].Value<int>();
                }
#else
                var packingParams = atlas.GetPackingSettings();
                var packingToken = p.GetRaw("packingSettings") as JObject;
                if (packingToken != null)
                {
                    if (packingToken["allowRotation"] != null)
                        packingParams.enableRotation = packingToken["allowRotation"].Value<bool>();
                    if (packingToken["tightPacking"] != null)
                        packingParams.enableTightPacking = packingToken["tightPacking"].Value<bool>();
                    if (packingToken["padding"] != null)
                        packingParams.padding = packingToken["padding"].Value<int>();
                    atlas.SetPackingSettings(packingParams);
                }
#endif

                // Add sprites/textures from include paths
                string[] includePaths = p.GetStringArray("includePaths");
                if (includePaths != null && includePaths.Length > 0)
                {
                    foreach (string includePath in includePaths)
                    {
                        string safePath = AssetPathUtility.SanitizeAssetPath(includePath);
                        if (safePath == null) continue;

                        // Add all sprites under this path
                        string[] spriteGuids = AssetDatabase.FindAssets("t:Sprite", new[] { safePath });
                        foreach (string guid in spriteGuids)
                        {
                            string spritePath = AssetDatabase.GUIDToAssetPath(guid);
                            var spriteObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(spritePath);
                            if (spriteObj != null)
                                atlas.Add(new[] { spriteObj });
                        }
                    }
                }

                EditorUtility.SetDirty(atlas);
                AssetDatabase.SaveAssets();

#if UNITY_2022_2_OR_NEWER
                return new SuccessResponse(
                    isNew ? $"SpriteAtlas '{atlasName}' created at '{outputPath}'." :
                            $"SpriteAtlas '{atlasName}' configured at '{outputPath}'.",
                    new
                    {
                        path = outputPath,
                        isNew,
                        packingSettings = new
                        {
                            allowRotation = atlas.enableRotation,
                            tightPacking = atlas.enableTightPacking,
                            padding = atlas.padding
                        }
                    });
#else
                var appliedPacking = atlas.GetPackingSettings();

                return new SuccessResponse(
                    isNew ? $"SpriteAtlas '{atlasName}' created at '{outputPath}'." :
                            $"SpriteAtlas '{atlasName}' configured at '{outputPath}'.",
                    new
                    {
                        path = outputPath,
                        isNew,
                        packingSettings = new
                        {
                            allowRotation = appliedPacking.enableRotation,
                            tightPacking = appliedPacking.enableTightPacking,
                            padding = appliedPacking.padding
                        }
                    });
#endif
            }
            catch (Exception ex)
            {
                return new ErrorResponse("SET_SPRITE_ATLAS_FAILED", $"Failed to set SpriteAtlas: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 5. configure_lightmap
        // ─────────────────────────────────────────────

        /// <summary>
        /// Configures lightmap baking settings including lightmap size, compression,
        /// realtime GI toggle, and bake-specific parameters.
        /// </summary>
        private static object ConfigureLightmap(ToolParams p)
        {
            try
            {
                int? lightmapSize = p.GetInt("lightmapSize");
                string compression = p.Get("compression");
                bool? realtimeGI = p.GetBool("realtimeGI");
                var bakeSettingsToken = p.GetRaw("bakeSettings") as JObject;

                // Access LightingSettings via Lightmapping API
                var lightingSettings = Lightmapping.lightingSettings;
                bool createdNewSettings = false;

                if (lightingSettings == null)
                {
                    lightingSettings = new LightingSettings();
                    Lightmapping.lightingSettings = lightingSettings;
                    createdNewSettings = true;
                }

                var changed = new List<string>();

                if (lightmapSize.HasValue)
                {
                    lightingSettings.lightmapMaxSize = lightmapSize.Value;
                    lightingSettings.lightmapResolution = Mathf.Max(1, lightmapSize.Value / 32);
                    changed.Add("lightmapMaxSize");
                    changed.Add("lightmapResolution");
                }

                if (!string.IsNullOrEmpty(compression))
                {
                    string compLower = compression.ToLowerInvariant();
                    if (compLower == "none" || compLower == "false" || compLower == "off")
                        lightingSettings.lightmapCompression = LightmapCompression.None;
                    else if (compLower == "low" || compLower == "normalquality")
                        lightingSettings.lightmapCompression = LightmapCompression.NormalQuality;
                    else if (compLower == "high" || compLower == "highquality")
                        lightingSettings.lightmapCompression = LightmapCompression.HighQuality;
                    else
                        lightingSettings.lightmapCompression = LightmapCompression.NormalQuality;
                    changed.Add("lightmapCompression");
                }

                if (realtimeGI.HasValue)
                {
                    lightingSettings.realtimeGI = realtimeGI.Value;
                    changed.Add("realtimeGI");

                    // Also toggle global realtimeGI state
                    Lightmapping.realtimeGI = realtimeGI.Value;
                }

                if (bakeSettingsToken != null)
                {
                    foreach (var prop in bakeSettingsToken.Properties())
                    {
                        string name = prop.Name.ToLowerInvariant();
                        JToken value = prop.Value;

                        switch (name)
                        {
                            case "bakedgi":
                            case "baked_gi":
                                lightingSettings.bakedGI = value.Value<bool>();
                                changed.Add(name);
                                break;
                            case "lightmapper":
                                if (Enum.TryParse<LightingSettings.Lightmapper>(value.ToString(), true, out var lm))
                                {
                                    lightingSettings.lightmapper = lm;
                                    changed.Add(name);
                                }
                                break;
                            case "directsamples":
                            case "direct_sample_count":
                                lightingSettings.directSampleCount = value.Value<int>();
                                changed.Add(name);
                                break;
                            case "indirectsamples":
                            case "indirect_sample_count":
                                lightingSettings.indirectSampleCount = value.Value<int>();
                                changed.Add(name);
                                break;
                            case "environmentalsamples":
                            case "environmental_sample_count":
                                lightingSettings.environmentSampleCount = value.Value<int>();
                                changed.Add(name);
                                break;
                        }
                    }
                }

                EditorUtility.SetDirty(lightingSettings);

                return new SuccessResponse(
                    "Lightmap settings configured.",
                    new
                    {
                        createdNewSettings,
                        changedSettings = changed,
                        lightmapMaxSize = lightingSettings.lightmapMaxSize,
                        lightmapResolution = lightingSettings.lightmapResolution,
                        lightmapCompression = lightingSettings.lightmapCompression.ToString(),
                        realtimeGI = lightingSettings.realtimeGI,
                        bakedGI = lightingSettings.bakedGI
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("CONFIGURE_LIGHTMAP_FAILED", $"Failed to configure lightmap: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 6. analyze_build_size
        // ─────────────────────────────────────────────

        /// <summary>
        /// Analyzes the latest build report to determine build size, per-file breakdown,
        /// and build result status.
        /// </summary>
        private static object AnalyzeBuildSize(ToolParams p)
        {
            try
            {
                var latestReport = BuildReport.GetLatestReport();
                if (latestReport == null)
                    return new ErrorResponse("NO_BUILD_REPORT", "No build report available. Perform a build first.");

                var summary = latestReport.summary;

                // Build per-file breakdown from all report steps
                var files = new List<object>();
                foreach (var step in latestReport.steps)
                {
                    foreach (var message in step.messages)
                    {
                        if (message.type == LogType.Error || message.type == LogType.Warning)
                            continue;

                        if (!string.IsNullOrEmpty(message.content))
                        {
                            files.Add(new
                            {
                                step = step.name,
                                message = message.content
                            });
                        }
                    }
                }

                // Collect file size info from packed assets
                var packedAssets = new List<object>();
                foreach (var packedAsset in latestReport.packedAssets)
                {
                    foreach (var assetInfo in packedAsset.contents)
                    {
                        packedAssets.Add(new
                        {
                            path = assetInfo.sourceAssetPath,
                            size = assetInfo.packedSize
                        });
                    }
                }

                return new SuccessResponse(
                    $"Build analysis complete. Total size: {summary.totalSize} bytes.",
                    new
                    {
                        result = summary.result.ToString(),
                        platform = summary.platform.ToString(),
                        outputPath = summary.outputPath,
                        totalSizeBytes = summary.totalSize,
                        totalSizeMB = Math.Round(summary.totalSize / (1024.0 * 1024.0), 2),
                        totalTimeSeconds = summary.totalTime.TotalSeconds,
                        totalErrors = summary.totalErrors,
                        totalWarnings = summary.totalWarnings,
                        startedAt = summary.buildStartedAt,
                        endedAt = summary.buildEndedAt,
                        files = packedAssets
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("ANALYZE_BUILD_FAILED", $"Failed to analyze build size: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        // 7. configure_occlusion
        // ─────────────────────────────────────────────

        /// <summary>
        /// Configures occlusion culling settings including culling mask, smallest occluder,
        /// and smallest hole parameters.
        /// </summary>
        private static object ConfigureOcclusion(ToolParams p)
        {
            try
            {
                string cullingMask = p.Get("cullingMask");
                float? smallestOccluder = p.GetFloat("smallestOccluder");
                float? smallestHole = p.GetFloat("smallestHole");

                // Apply culling mask to the current scene camera if specified
                if (!string.IsNullOrEmpty(cullingMask))
                {
                    string maskLower = cullingMask.ToLowerInvariant();
                    if (maskLower == "everything" || maskLower == "all")
                    {
                        // Set all cameras to cull everything
                        foreach (var cam in Camera.allCameras)
                        {
                            cam.cullingMask = -1;
                        }
                    }
                    else if (int.TryParse(cullingMask, out int maskValue))
                    {
                        foreach (var cam in Camera.allCameras)
                        {
                            cam.cullingMask = maskValue;
                        }
                    }
                    else
                    {
                        // Try parsing as layer names
                        int computedMask = 0;
                        string[] layerNames = cullingMask.Split(',');
                        foreach (string layerName in layerNames)
                        {
                            string trimmed = layerName.Trim();
                            int layer = LayerMask.NameToLayer(trimmed);
                            if (layer >= 0)
                                computedMask |= 1 << layer;
                        }

                        if (computedMask != 0)
                        {
                            foreach (var cam in Camera.allCameras)
                            {
                                cam.cullingMask = computedMask;
                            }
                        }
                    }
                }

                // Configure occlusion culling settings
                bool occlusionSettingsChanged = false;

                if (smallestOccluder.HasValue)
                {
                    StaticOcclusionCulling.smallestOccluder = smallestOccluder.Value;
                    occlusionSettingsChanged = true;
                }

                if (smallestHole.HasValue)
                {
                    StaticOcclusionCulling.smallestHole = smallestHole.Value;
                    occlusionSettingsChanged = true;
                }

                return new SuccessResponse(
                    "Occlusion culling settings configured.",
                    new
                    {
                        smallestOccluder = StaticOcclusionCulling.smallestOccluder,
                        smallestHole = StaticOcclusionCulling.smallestHole,
                        isOcclusionCullingEnabled = StaticOcclusionCulling.isRunning
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("CONFIGURE_OCCLUSION_FAILED", $"Failed to configure occlusion: {ex.Message}");
            }
        }
    }
}
