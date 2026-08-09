---
title: manage_build
sidebar_label: manage_build
description: "Manage Unity player builds — trigger builds, switch platforms, configure settings, manage build scenes and profiles, run batch builds across platforms."
---

# `manage_build`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_build`

## Description

Manage Unity player builds — trigger builds, switch platforms, configure settings, manage build scenes and profiles, run batch builds across platforms. Actions: build, status, platform, settings, scenes, profiles, batch, cancel, configure_code_generation, configure_aab, get_build_report.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `str` | yes | Action: build, status, platform, settings, scenes, profiles, batch, cancel |
| `target` | `str \| None` | — | Build target: windows64, osx, linux64, android, ios, webgl, uwp, tvos, visionos |
| `output_path` | `str \| None` | — | Output path for the build |
| `scenes` | `str \| None` | — | JSON array of scene paths, or comma-separated paths |
| `development` | `str \| None` | — | Development build (true/false) |
| `options` | `str \| None` | — | JSON array of BuildOptions: clean_build, auto_run, deep_profiling, compress_lz4, strict_mode, detailed_report |
| `subtarget` | `str \| None` | — | Build subtarget: player or server |
| `scripting_backend` | `str \| None` | — | Scripting backend: mono or il2cpp (persistent change) |
| `profile` | `str \| None` | — | Build Profile asset path (Unity 6+ only) |
| `property` | `str \| None` | — | Settings property: product_name, company_name, version, bundle_id, scripting_backend, defines, architecture |
| `value` | `str \| None` | — | Value to set for the property (omit to read) |
| `activate` | `str \| None` | — | Activate a build profile (true/false) |
| `targets` | `str \| None` | — | JSON array of targets for batch build |
| `profiles` | `str \| None` | — | JSON array of profile paths for batch build (Unity 6+) |
| `output_dir` | `str \| None` | — | Base output directory for batch builds |
| `job_id` | `str \| None` | — | Job ID for status/cancel |
| `scriptingBackend` | `Literal['Mono', 'IL2CPP'] \| None` | — | Scripting backend for configure_code_generation. |
| `strippingLevel` | `Literal['Disabled', 'Low', 'Medium', 'High'] \| None` | — | Managed stripping level for configure_code_generation. |
| `compilerConfig` | `Literal['Debug', 'Release', 'Master'] \| None` | — | IL2CPP compiler configuration. |
| `preserveAssemblies` | `list[str] \| None` | — | Assemblies to preserve from stripping (generates link.xml). |
| `bundleVersionCode` | `int \| None` | — | Android bundle version code for configure_aab. |
| `keystorePath` | `str \| None` | — | Path to keystore for configure_aab. Passwords are NOT parameters — set UNITY_ANDROID_KEYSTORE_PASS and UNITY_ANDROID_KEYALIAS_PASS in the Unity Editor's environment. |
| `keyAlias` | `str \| None` | — | Key alias for configure_aab. Requires keystorePath. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

