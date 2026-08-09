---
title: find_in_file
sidebar_label: find_in_file
description: "Searches a file with a regex pattern and returns line numbers and excerpts."
---

# `find_in_file`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.find_in_file`

## Description

Searches a file with a regex pattern and returns line numbers and excerpts.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `uri` | `str` | yes | The resource URI to search under Assets/ or file path form supported by read_resource |
| `pattern` | `str` | yes | The regex pattern to search for |
| `action` | `Literal['search', 'find_references'] \| None` | — | Action: search (regex search in file) or find_references (find symbol references across project). |
| `symbolName` | `str \| None` | — | Symbol/class name to find references for (find_references action). |
| `scope` | `str \| None` | — | Search scope path (default: Assets). |
| `project_root` | `str \| None` | — | Optional project root path |
| `max_results` | `int` | — | Cap results to avoid huge payloads |
| `ignore_case` | `bool \| str \| None` | — | Case insensitive search |
| `page_size` | `int \| str \| None` | — | Number of results to return per page. |
| `cursor` | `int \| str \| None` | — | Zero-based result cursor for paging. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

