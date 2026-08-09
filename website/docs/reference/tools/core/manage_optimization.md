---
title: manage_optimization
sidebar_label: manage_optimization
description: "Manage Cross-platform Optimization — quality settings, texture compression, sprite atlases, lightmaps, occlusion culling, build size analysis"
---

# `manage_optimization`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_optimization`

## Description

Manage Cross-platform Optimization — quality settings, texture compression, sprite atlases, lightmaps, occlusion culling, build size analysis

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['set_quality_settings', 'configure_texture_compression', 'batch_resize_textures', 'set_sprite_atlas', 'configure_lightmap', 'analyze_build_size', 'configure_occlusion']` | yes | The operation to perform. |
| `preset` | `Literal['low', 'medium', 'high', 'ultra'] \| None` | — | Quality preset level for set_quality_settings. |
| `platform` | `Literal['Android', 'iOS', 'StandaloneWindows64', 'StandaloneOSX'] \| None` | — | Target platform. |
| `format` | `Literal['ASTC', 'ETC2', 'PVRTC', 'DXT5'] \| None` | — | Texture compression format. |
| `maxWidth` | `int \| None` | — | Maximum texture width for batch resize. |
| `maxHeight` | `int \| None` | — | Maximum texture height for batch resize. |
| `filter` | `Literal['Point', 'Bilinear', 'Trilinear'] \| None` | — | Resize filter mode. |
| `path` | `str \| None` | — | Target asset path for batch operations. |
| `atlasName` | `str \| None` | — | Sprite atlas asset name. |
| `includePaths` | `list[str] \| None` | — | Paths to include in the sprite atlas. |
| `packingSettings` | `dict \| None` | — | Sprite atlas packing settings. |
| `lightmapSize` | `int \| None` | — | Lightmap resolution size. |
| `compression` | `Literal['None', 'Low', 'Normal', 'High'] \| None` | — | Lightmap compression level. |
| `realtimeGI` | `bool \| None` | — | Enable real-time global illumination. |
| `bakeSettings` | `dict \| None` | — | Lightmap bake settings. |
| `cullingMask` | `int \| None` | — | Occlusion culling layer mask. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

