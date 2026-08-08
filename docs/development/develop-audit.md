# `develop` branch audit

Findings from reviewing `develop` against `main` (the v10.0.0 tool-coverage work: 4 new tools,
+22 actions on 8 existing tools), plus everything the live Editor loop has surfaced since.

Status vocabulary: **open** · **fixed** (code changed, not yet run) · **verified** (exercised
against a live Editor; the version(s) are named).

---

## How to reproduce

Two gates. Run both before touching anything else — between them they found 8 of the bugs below,
including three that manual review missed entirely.

```bash
# Gate 1 — Python↔C# parameter contract
cd Server && uv run pytest tests/test_python_csharp_contract.py -q

# Gate 2 — compile across the supported Unity range
tools/check-unity-versions.sh
```

Gate 1 is green today because the contract breaks below are listed in its `KNOWN_IGNORED` /
`KNOWN_UNREACHABLE` allowlists. It therefore blocks *new* drift immediately; delete an entry as you
fix it. Adding an entry to silence a failure means a newly broken tool — don't.

Live loop (see the two local projects in the team notes): edit C# → `refresh_unity` → probe both
Editors via `POST http://127.0.0.1:8090/api/command` with `unity_instance` set. Editing Python
requires a server restart; editing C# does not.

---

## The governing principle

Every version-guard bug found so far was fixed by **deleting the guard**, not by adding another one.
In all four cases the `#if` was written from an assumption about an API rather than from checking it,
and the unguarded call was already correct across the whole 2021.3 → 6.x range.

Before adding any `#if UNITY_*_OR_NEWER`, verify the API actually differs — `execute_code` against a
live Editor answers this in seconds. Then follow CLAUDE.md § *Unity API Compatibility Shims*: the
shim belongs in `Runtime/Helpers/Unity*Compat.cs`, not at the call site.

---

## 1. Contract drift — Python sends a name C# never reads

All confirmed by Gate 1. Each one silently discards the parameter or makes the action always fail.

| Tool | Parameters | Effect | Status |
|---|---|---|---|
| `manage_input_system` | C# reads `path`, Python sends `assetPath` | every action failed | **verified** (6000, 2022) |
| `manage_input_system` | `assetName`, `binding`, `groups`, `interactions`, `processors`, `requiredDevices`, `optionalDevices` | 7 params discarded | open |
| `manage_audio` | C# reads `mixerPath`, Python sends `mixerName`; `groupId` unread | `expose_param`, `set_snapshot` always fail | open |
| `manage_addressables` | `schemaType`, `buildPath`, `loadPath`, `targetPlatform` unread; `labels` sent as list, read as string | `create_group` ignores all config | open |
| `manage_build` | C# `configure_code_generation` requires `platform`; the tool's parameter is `target` | action always fails | open |
| `manage_build` | `buildPath` now unread | dead parameter (left over from the `get_build_report` fix below) | open |
| `find_gameobjects` | `cursor`, `pageSize` unread | **pre-existing on `main`**, not a `develop` regression | open |
| `manage_scene` | `sceneViewTarget` unread | **pre-existing on `main`** | open |

The `manage_input_system` `path` fix currently normalizes the key inside `HandleCommand`. Gate 1 still
flags it, correctly — that is a runtime alias, not a fixed contract. Renaming the C# reads to
`assetPath` is the real fix.

---

## 2. Version guards written from assumption

| Location | Problem | Status |
|---|---|---|
| `ManageOptimization.cs` SpriteAtlas | Three-way guard; **both** new branches false. `SpriteAtlasExtensions.{Add,GetPackingSettings,SetPackingSettings}` exist on every supported version — verified live on 6000. Collapsed to one path, which also removed the `AddObjectToAsset` corruption below. | **verified** (6000, 2022) |
| `ManageBuild.cs` `get_build_report` | `BuildReport.GetReport(path)` is not public API; broke 2022 compile. Also corrected the `files`/`GetFiles()` boundary from `2023_1` to `2022_1`. | **verified** (6000, 2022) |
| `ManageBuild.cs` stripping | `#if UNITY_6000_0_OR_NEWER` unnecessary — `SetManagedStrippingLevel` exists since 2018.3. The `#else` branch used removed enum members and mapped `Medium`→`StripByteCode`. | **verified** (6000, 2022) |
| `ManageOptimization.cs` `iosBuildSubtarget` | Absent on 2022.3 despite sitting in the "old Unity" branch. iOS has no project-wide texture subtarget; per-texture overrides already handled below. | **verified** (6000, 2022) |
| `ManageOptimization.cs` `globalTextureMipmapLimit` | 2022.2+ only, unguarded. Compiles on 2022.3 and 6000; **breaks the declared 2021.3 floor**. Untested — no 2021.3 editor installed. | open |
| `ManageEditor.cs:360` | `report.GetFiles()` unguarded while `ManageBuild.cs` guards the same call. | open |

---

## 3. Guard and precondition logic

| Location | Problem | Status |
|---|---|---|
| `ManageInputSystem.cs:32` | `ResolveComponent("InputActionAsset")` — the type is a `ScriptableObject`. Guard could never pass, so the tool was dead on **every** project. | **verified** (6000 positive, 2022 negative) |
| `ManageOptimization.cs` `batch_resize_textures` | `Mathf.Max(maxWidth ?? 8192, …)` — passing `maxWidth=512` resizes nothing. Should be `Min`. | open |
| `ManageOptimization.cs` `batch_resize_textures` | `filter` is a resize mode in the schema but used as the `FindAssets` search string. | open |
| `ManageOptimization.cs` `set_quality_settings` | `(ShadowResolution)512` — the enum is `Low..VeryHigh` (0–3). Produces garbage. | open |
| `ManageOptimization.cs` `configure_texture_compression` | `GetPlatformTextureSettings("iOS"/"StandaloneWindows64")` — Unity expects `iPhone`/`Standalone`. Only Android works. | open |
| `ManageComponents.cs:158` | `add_param_listener` sets `m_Mode = 1` (Void), so Unity ignores the argument it just wrote. Must match `paramType`. | open |
| `ManageComponents.cs` | `InsertArrayElementAtIndex` copies the previous element; `m_Arguments` not reset, so listener #2 inherits #1's data. | open |
| `ManageGameObject.cs:111` | `set_sibling_index` clamps to 0 when `parent == null`; root objects can never be reordered. | open |
| `ManageBuild.cs` `configure_aab` | Never sets `buildAppBundle`; sets `buildApkPerCpuArchitecture` (APK splitting, wrong for AAB); validates `keystorePath` then discards it. | open |
| `ManageEditor.cs` `create_folder_structure` | `AssetDatabase.CreateFolder` needs an existing parent, so `Settings/Renderer` fails — yet is still reported as created. | open |
| `ManageEditor.cs:232` | `run_health_check` NREs when `checks` is omitted; the `prefab` check `LoadPrefabContents` on every prefab in the project. | open |

---

## 3b. `manage_input_system` persistence model is wrong — BLOCKER

Found while verifying the Phase-1 contract fixes. Bigger than everything else in this file and it
blocks end-to-end verification of the whole tool.

`.inputactions` is a **ScriptedImporter JSON** format — Unity's stock asset begins `{ "name": … }`.
`create_asset` instead calls `ScriptableObject.CreateInstance` + `AssetDatabase.CreateAsset`, which
writes a Unity **YAML** `MonoBehaviour`. `InputActionImporter` rejects it, so the asset imports as
`DefaultAsset` with an `ImportLog`:

```
guid=ffa3bc… typed=NULL anyObj=DefaultAsset allCount=1 [ImportLog] importer=InputActionImporter
```

`create_asset` still returns success and a GUID. Every subsequent action then fails with
`No InputActionAsset found at '…'`, which is why nothing downstream in this tool has ever been
exercised.

Second half of the same defect: every mutating action persists with
`EditorUtility.SetDirty(asset)` + `AssetDatabase.SaveAssets()`. That does **not** write back through
a ScriptedImporter, so even against a valid asset the edits would be discarded on reimport.

Correct model (APIs verified live on 6000.5.3f1): `InputActionAsset.FromJson(string)` static,
`LoadFromJson(string)` instance, `ToJson()` instance. Read the file → `FromJson` → mutate →
`File.WriteAllText(path, asset.ToJson())` → `AssetDatabase.ImportAsset(path)`. Note `ToJson()` on a
bare `CreateInstance` throws `ArgumentNullException` — the instance must be named and initialised first.

This is an I/O-layer rewrite across all 12 actions, not a parameter fix. **Status: open, scope
decision needed.** The Phase-1 parameter fixes below landed and compile clean on both Editors, but
cannot be verified end-to-end until this is done.

## 4. Reporting success on failure

The worst class for an agent: it believes the work is done.

| Location | Problem | Status |
|---|---|---|
| `ManageAudio.cs` `SetSnapshot` | Returns `SuccessResponse("Transitioning to snapshot…")` even when all three reflection fallbacks fail. | open |
| `ManageAudio.cs:371` | `Activator.CreateInstance(AudioMixerController)` — a `ScriptableObject` created without `CreateInstance` has no native object. Asset is broken; correct API is `AudioMixerController.CreateMixerControllerAtPath`. | open |
| `ManageEditor.cs` `create_folder_structure` | Adds to `created` without checking the returned GUID. | open |
| `ManageOptimization.cs` (fixed) | Unity 6 branch used `AssetDatabase.AddObjectToAsset(sprite, atlas)` — embeds the sprite into the atlas file instead of registering a packable. | **verified** (6000, 2022) |

---

## 5. Paging — violates CLAUDE.md's "always page large results"

| Location | Problem | Status |
|---|---|---|
| `FindInFile.cs` `find_references` | Reads every `.cs` in scope in full, no cap; the Python `max_results` is never forwarded. Substring match, so `Player` hits `PlayerController`, comments, string literals. | open |
| `ManageInputSystem.cs` `get_asset` | Returns the whole `.inputactions` JSON inline — ~15k tokens for Unity's stock asset. | open |
| `ManageComponents.cs` `list_all` | Unpaged; also reads `includeInactive` and never uses it. | open |

---

## 6. Architecture and consistency

| Item | Problem | Status |
|---|---|---|
| Target resolution | New code uses `GameObject.Find(path)` in ~10 sites — cannot see inactive objects, and bypasses the existing `target` + `search_method` resolution. `manage_components` now has two different targeting schemes depending on the action. | open |
| Build report | Three implementations: `ManageBuild.get_build_report`, `ManageOptimization.analyze_build_size`, `ManageEditor.generate_report(build_size)`. | open |
| Undo | New mutations skip `Undo.RecordObject`/`Undo.AddComponent`, while `manage_editor` gained `undo`/`redo` actions. | open |
| Preflight | `manage_audio`/`optimization`/`addressables` gate on it; `manage_input_system` and `find_in_file` do not, despite writing assets. | open |
| `manage_addressables` `build_content` | Comment claims a long-running-job dispatch; the code is identical to the default branch and will time out. | open |
| CLI commands | Step 2 of CLAUDE.md § *Adding a New Tool* skipped for all 4 new tools. | open |
| `tool_registry.py:29` | `input_system` group description is in Vietnamese; the other 10 are English, and it is shown to the agent. | open |
| `tools/test_game_creation.py` | `assert_ok` returns a bool that every call site discards — the script always "passes". Docstring points at `Server/tests/manual/`, file lives in `tools/`. Covers none of the 4 new tools. | open |
| Python unit tests | The 11 new test files mock the transport and assert only that Python built the right dict, so no contract drift is observable. Gate 1 exists to cover this. | **fixed** (gate added) |
| CLAUDE.md | `p.GetInt("page_size", "pageSize")` example is stale — the second argument is `int? defaultValue`, not an alternate key. | open |
| `ManageAnimation.cs` `list_model_clips` | `LoadAllAssetsAtPath` called inside the loop — O(n²). | open |
| `ManageEditor.cs` | Default-case error message never listed the 3 new actions. | open |

---

## Gate 1 known limitations

Worth knowing before trusting a green run:

- Keys are compared case- and underscore-insensitively, so a C# `?? p.Get("mapName")` fallback
  correctly clears the snake_case spelling. A `GetRequired` with **no** fallback and only a
  snake_case spelling would slip through if Python happened to send the camelCase variant.
- Python keys are captured by invoking each tool with dummy arguments. An action that rejects the
  dummies early contributes nothing, so coverage per action is best-effort, not total.
- The C# side is parsed, not compiled. Handlers split into subdirectories are folded into the tool
  declared in the nearest attributed ancestor directory; helper files sitting directly in `Tools/`
  are not folded at all.
