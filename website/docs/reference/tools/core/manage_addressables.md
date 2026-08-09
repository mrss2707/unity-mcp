---
title: manage_addressables
sidebar_label: manage_addressables
description: "Manage Unity Addressables — create groups, assign assets, build content, inspect dependency chains"
---

# `manage_addressables`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_addressables`

## Description

Manage Unity Addressables — create groups, assign assets, build content, inspect dependency chains

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['create_group', 'assign_asset', 'remove_asset', 'list_groups', 'build_content', 'get_dependency_chain', 'status']` | yes | The operation to perform. |
| `groupName` | `str \| None` | — | Addressable group name. |
| `schemaType` | `str \| None` | — | Schema type for the new group, e.g. BundledAssetGroupSchema (default) or ContentUpdateGroupSchema. |
| `buildPath` | `str \| None` | — | Addressables profile variable naming the group's build path, e.g. Local.BuildPath or Remote.BuildPath — not a filesystem path. |
| `loadPath` | `str \| None` | — | Addressables profile variable naming the group's load path, e.g. Local.LoadPath or Remote.LoadPath — not a filesystem path. |
| `assetPath` | `str \| None` | — | Asset path to assign/remove. |
| `address` | `str \| None` | — | Addressable address for the asset. |
| `labels` | `list[str] \| None` | — | Labels for the addressable asset. |
| `targetPlatform` | `Literal['Android', 'iOS', 'StandaloneWindows64', 'StandaloneOSX'] \| None` | — | Target build platform. |
| `job_id` | `str \| None` | — | Job ID for status polling. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

