using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools.Build;
using MCPForUnity.Runtime.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.U2D;
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

                // Map preset to quality parameters. shadowResolution is an enum with four members
                // (Low..VeryHigh) — the pixel counts it corresponds to are not the values.
                UnityEngine.ShadowResolution shadowResolution;
                int mipmapLimit;
                float lodBias;
                int antiAliasing;
                int shadowCascades;

                switch (preset.ToLowerInvariant())
                {
                    case "low":
                        shadowResolution = UnityEngine.ShadowResolution.Low;
                        mipmapLimit = 1; // half res
                        lodBias = 0.5f;
                        antiAliasing = 0;
                        shadowCascades = 1; // 0 is not a legal value — Unity clamps it up to 1
                        break;

                    case "medium":
                        shadowResolution = UnityEngine.ShadowResolution.Medium;
                        mipmapLimit = 0; // full res
                        lodBias = 1.0f;
                        antiAliasing = 2;
                        shadowCascades = 2;
                        break;

                    case "high":
                        shadowResolution = UnityEngine.ShadowResolution.High;
                        mipmapLimit = 0;
                        lodBias = 2.0f;
                        antiAliasing = 4;
                        shadowCascades = 4;
                        break;

                    case "ultra":
                        shadowResolution = UnityEngine.ShadowResolution.VeryHigh;
                        mipmapLimit = 0;
                        lodBias = 4.0f;
                        antiAliasing = 8;
                        shadowCascades = 4;
                        break;

                    default:
                        return new ErrorResponse("INVALID_PRESET",
                            $"Unknown preset '{preset}'. Valid values: low, medium, high, ultra.");
                }

                string platform = p.Get("platform");
                string resolvedLevelName = null;
                if (!string.IsNullOrEmpty(platform))
                {
                    object levelError = SelectQualityLevel(platform, preset, out resolvedLevelName);
                    if (levelError != null) return levelError;
                }

                // Overrides are applied AFTER SetQualityLevel: switching level reloads every value
                // from the project's quality tier, so applying them first would silently discard them.
                QualitySettings.shadowResolution = shadowResolution;
                QualitySettings.lodBias = lodBias;
                QualitySettings.antiAliasing = antiAliasing;
                QualitySettings.shadowCascades = shadowCascades;
                bool mipmapApplied = UnityQualityCompat.TrySetGlobalTextureMipmapLimit(mipmapLimit);

                // Report what QualitySettings actually holds now, not what was requested.
                return new SuccessResponse(
                    $"Quality settings applied: preset '{preset}'.",
                    new
                    {
                        preset,
                        platform = string.IsNullOrEmpty(platform) ? null : platform,
                        qualityLevel = QualitySettings.names[QualitySettings.GetQualityLevel()],
                        resolvedLevelName,
                        shadowResolution = QualitySettings.shadowResolution.ToString(),
                        globalTextureMipmapLimit = mipmapApplied
                            ? UnityQualityCompat.GetGlobalTextureMipmapLimit()
                            : null,
                        lodBias = QualitySettings.lodBias,
                        antiAliasing = QualitySettings.antiAliasing,
                        shadowCascades = QualitySettings.shadowCascades
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("SET_QUALITY_FAILED", $"Failed to set quality settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Switches to the quality level that best matches <paramref name="preset"/> among the
        /// levels the project actually enables for <paramref name="platform"/>. Returns an
        /// ErrorResponse on failure, or null on success with the chosen level in
        /// <paramref name="levelName"/>.
        /// </summary>
        /// <remarks>
        /// Level indices cannot be hard-coded: the default project has six tiers, but a URP
        /// template ships two ("Mobile", "PC"), so index 4 for "high" lands wherever it lands.
        /// Match the tier by name first, and fall back to the preset's proportional position in
        /// whatever list this project defines.
        /// </remarks>
        private static object SelectQualityLevel(string platform, string preset, out string levelName)
        {
            levelName = null;

            if (!BuildTargetMapping.TryResolveBuildTarget(platform, out BuildTarget buildTarget))
                return new ErrorResponse("INVALID_PLATFORM", BuildTargetMapping.GetUnknownBuildTargetMessage(platform));

            string platformName = BuildTargetMapping.GetPlatformSettingsName(buildTarget);
            if (platformName == null)
                return new ErrorResponse("PLATFORM_UNSUPPORTED",
                    $"Build target '{buildTarget}' has no per-platform quality level list.");

            int[] levels = QualitySettings.GetActiveQualityLevelsForPlatform(platformName);
            if (levels == null || levels.Length == 0)
                return new ErrorResponse("NO_QUALITY_LEVELS",
                    $"No quality levels are enabled for '{platformName}' in Project Settings > Quality.");

            string[] names = QualitySettings.names;
            string[] synonyms = preset.ToLowerInvariant() switch
            {
                "low" => new[] { "low", "mobile", "fastest", "fast", "performant" },
                "medium" => new[] { "medium", "simple", "good", "balanced" },
                "high" => new[] { "high", "beautiful", "pc", "desktop" },
                _ => new[] { "ultra", "veryhigh", "fantastic", "max" },
            };

            int chosen = -1;
            foreach (int level in levels)
            {
                if (level < 0 || level >= names.Length) continue;
                string normalized = names[level].Replace(" ", string.Empty).ToLowerInvariant();
                if (Array.IndexOf(synonyms, normalized) >= 0)
                {
                    chosen = level;
                    break;
                }
            }

            if (chosen < 0)
            {
                float position = preset.ToLowerInvariant() switch
                {
                    "low" => 0f,
                    "medium" => 1f / 3f,
                    "high" => 2f / 3f,
                    _ => 1f,
                };
                chosen = levels[Mathf.Clamp(Mathf.RoundToInt(position * (levels.Length - 1)), 0, levels.Length - 1)];
            }

            QualitySettings.SetQualityLevel(chosen, applyExpensiveChanges: true);
            levelName = names[chosen];
            return null;
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

                if (!BuildTargetMapping.TryResolveBuildTarget(platform, out BuildTarget buildTarget))
                    return new ErrorResponse("INVALID_PLATFORM", BuildTargetMapping.GetUnknownBuildTargetMessage(platform));

                string importerPlatform = BuildTargetMapping.GetPlatformSettingsName(buildTarget);
                if (importerPlatform == null)
                    return new ErrorResponse("PLATFORM_UNSUPPORTED",
                        $"Build target '{buildTarget}' has no texture importer override page.");

                // Set platform-wide compression targets. Only the mobile families map onto a
                // subtarget; anything else (DXT5, an exact ASTC_4x4 member) leaves the
                // project-wide switch alone rather than resetting it to Generic.
                if (buildTarget == BuildTarget.Android && !string.IsNullOrEmpty(format))
                {
                    switch (format.ToLowerInvariant())
                    {
                        case "etc":
                        case "etc1":
                            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ETC;
                            break;
                        case "astc":
                            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
                            break;
                        case "etc2":
                            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ETC2;
                            break;
                    }
                }
                // iOS/tvOS have no project-wide texture subtarget switch (unlike Android);
                // compression is set per texture via the TextureImporter overrides applied below.

                // Apply platform overrides to individual textures under path
                int scanned = 0;
                int updatedCount = 0;
                var failedPaths = new List<string>();
                if (!string.IsNullOrEmpty(path))
                {
                    string safePath = AssetPathUtility.SanitizeAssetPath(path);
                    if (safePath == null)
                        return new ErrorResponse("INVALID_PATH", $"Invalid path '{path}'.");

                    TextureImporterFormat? targetFormat = null;
                    if (!string.IsNullOrEmpty(format))
                    {
                        targetFormat = ResolveTextureFormat(format);
                        if (!targetFormat.HasValue)
                            return new ErrorResponse("INVALID_FORMAT",
                                $"Unknown texture format '{format}'. Use a family name (ASTC, ETC2, PVRTC, DXT5) "
                                + "or an exact TextureImporterFormat member such as ASTC_4x4 or ETC2_RGBA8.");
                    }

                    string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { safePath });
                    scanned = textureGuids.Length;
                    foreach (string guid in textureGuids)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                        if (importer == null) continue;

                        var platformSettings = importer.GetPlatformTextureSettings(importerPlatform);
                        platformSettings.overridden = true;
                        if (targetFormat.HasValue)
                            platformSettings.format = targetFormat.Value;

                        importer.SetPlatformTextureSettings(platformSettings);
                        // SetPlatformTextureSettings only mutates the in-memory importer. Without
                        // SaveAndReimport the change never reaches the .meta file, so it is lost on
                        // the next domain reload and never reaches version control — while
                        // GetPlatformTextureSettings still reads it back, which is why this looked
                        // like it worked.
                        importer.SaveAndReimport();

                        var applied = ((TextureImporter)AssetImporter.GetAtPath(assetPath))
                            .GetPlatformTextureSettings(importerPlatform);
                        if (applied.overridden && (!targetFormat.HasValue || applied.format == targetFormat.Value))
                            updatedCount++;
                        else
                            failedPaths.Add(assetPath);
                    }
                }

                if (failedPaths.Count > 0)
                    return new ErrorResponse("COMPRESSION_NOT_APPLIED",
                        $"{failedPaths.Count} of {scanned} texture(s) did not accept the '{importerPlatform}' override.",
                        new { platform, importerPlatform, texturesUpdated = updatedCount, failedPaths = failedPaths.Take(20).ToList() });

                return new SuccessResponse(
                    $"Texture compression configured for '{platform}'.",
                    new
                    {
                        platform,
                        importerPlatform,
                        format = format ?? "default",
                        texturesScanned = scanned,
                        texturesUpdated = updatedCount
                    });
            }
            catch (Exception ex)
            {
                return new ErrorResponse("CONFIGURE_COMPRESSION_FAILED", $"Failed to configure texture compression: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves a texture format name to a concrete <see cref="TextureImporterFormat"/>.
        /// Accepts exact enum members, plus the four family names the tool schema offers —
        /// ASTC, ETC2 and PVRTC name compression families, not members, so each maps to the
        /// block size that is the sensible mobile default. Returns null when unrecognised;
        /// falling back to Automatic would silently ignore the caller's request.
        /// </summary>
        private static TextureImporterFormat? ResolveTextureFormat(string format)
        {
            switch (format.ToLowerInvariant())
            {
                case "astc": return TextureImporterFormat.ASTC_6x6;
                case "etc2": return TextureImporterFormat.ETC2_RGBA8;
                case "pvrtc": return TextureImporterFormat.PVRTC_RGBA4;
            }

            return Enum.TryParse(format, ignoreCase: true, out TextureImporterFormat parsed)
                ? parsed
                : (TextureImporterFormat?)null;
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

                // maxTextureSize is a single cap on the longest edge, so the requested width and
                // height must both be satisfied — that is the smaller of the two, not the larger.
                int targetMaxSize = Mathf.Min(maxWidth ?? int.MaxValue, maxHeight ?? int.MaxValue);

                FilterMode? filterMode = null;
                if (!string.IsNullOrEmpty(filter))
                {
                    if (!Enum.TryParse(filter, ignoreCase: true, out FilterMode parsedFilter))
                        return new ErrorResponse("INVALID_FILTER",
                            $"Unknown filter '{filter}'. Valid values: Point, Bilinear, Trilinear.");
                    filterMode = parsedFilter;
                }

                string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { path });
                var modifiedPaths = new List<string>();

                foreach (string guid in textureGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer == null) continue;

                    bool changed = false;
                    // Only shrink; never upscale a texture that is already within budget.
                    if (importer.maxTextureSize > targetMaxSize)
                    {
                        importer.maxTextureSize = targetMaxSize;
                        changed = true;
                    }
                    if (filterMode.HasValue && importer.filterMode != filterMode.Value)
                    {
                        importer.filterMode = filterMode.Value;
                        changed = true;
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                        modifiedPaths.Add(assetPath);
                    }
                }

                // Single refresh at end
                if (modifiedPaths.Count > 0)
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                return new SuccessResponse(
                    $"Updated {modifiedPaths.Count} of {textureGuids.Length} texture(s) under '{path}'.",
                    new
                    {
                        maxSize = targetMaxSize,
                        filterMode = filterMode?.ToString(),
                        texturesScanned = textureGuids.Length,
                        texturesResized = modifiedPaths.Count,
                        modifiedPaths = modifiedPaths.Take(50).ToList(),
                        modifiedPathsTruncated = modifiedPaths.Count > 50,
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

                // Apply packing settings. UnityEditor.U2D.SpriteAtlasExtensions supplies
                // GetPackingSettings/SetPackingSettings/Add on every supported version (2021.3 → 6.x);
                // SpriteAtlas itself never exposed these as direct properties.
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

                var files = BuildReportCompat.GetFiles(latestReport)?
                    .Select(f => new { path = f.Path, size = f.Size, role = f.Role })
                    .Cast<object>()
                    .ToList() ?? new List<object>();

                // Collect packed asset info separately from build output files.
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
                        files,
                        packedAssets
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

                if (smallestOccluder.HasValue)
                {
                    StaticOcclusionCulling.smallestOccluder = smallestOccluder.Value;
                }

                if (smallestHole.HasValue)
                {
                    StaticOcclusionCulling.smallestHole = smallestHole.Value;
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
