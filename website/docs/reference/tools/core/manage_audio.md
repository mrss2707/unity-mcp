---
title: manage_audio
sidebar_label: manage_audio
description: "Manage Unity Audio — create and configure AudioSources, Audio Mixers, mixer snapshots, and 3D spatial audio settings"
---

# `manage_audio`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_audio`

## Description

Manage Unity Audio — create and configure AudioSources, Audio Mixers, mixer snapshots, and 3D spatial audio settings

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['create_source', 'set_source', 'play', 'stop', 'create_mixer', 'expose_param', 'set_snapshot', 'configure_spatial']` | yes | The operation to perform. |
| `gameObjectPath` | `str \| None` | — | Path to the target GameObject. |
| `clipPath` | `str \| None` | — | Path to the AudioClip asset. |
| `playOnAwake` | `bool \| None` | — | Whether audio plays on awake. |
| `loop` | `bool \| None` | — | Whether audio loops. |
| `volume` | `float \| None` | — | Audio volume (0.0 to 1.0). |
| `pitch` | `float \| None` | — | Audio pitch (default: 1.0). |
| `spatialBlend` | `float \| None` | — | Spatial blend (0.0 = 2D, 1.0 = 3D). |
| `minDistance` | `float \| None` | — | 3D sound min distance. |
| `maxDistance` | `float \| None` | — | 3D sound max distance. |
| `rolloffMode` | `Literal['Logarithmic', 'Linear', 'Custom'] \| None` | — | Audio rolloff mode. |
| `dopplerLevel` | `float \| None` | — | Doppler effect level (0.0 to 5.0). |
| `mixerPath` | `str \| None` | — | Path to the AudioMixer asset (e.g. 'Assets/Audio/Main.mixer'). |
| `outputPath` | `str \| None` | — | Output path for new AudioMixer asset. |
| `paramName` | `str \| None` | — | Exposed parameter name. |
| `mixerGroup` | `str \| None` | — | Name of a group inside mixerPath to route the AudioSource through (create_source/set_source). |
| `snapshotName` | `str \| None` | — | Snapshot name. |
| `fadeTime` | `float \| None` | — | Fade transition time in seconds. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

