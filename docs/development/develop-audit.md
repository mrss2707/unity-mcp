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

Almost every version-guard bug found so far was fixed by **deleting the guard**, not by adding
another one. The `#if` was written from an assumption about an API rather than from checking it, and
the unguarded call was already correct across the whole 2021.3 → 6.x range. Seven of nine guards
went this way, including the three `UNITY_2022_2_OR_NEWER` brackets in `ManageComponents` whose
`#else` branches called `UnityEventTools` methods that exist in no Unity version.

Two guards survived, both genuine: `globalTextureMipmapLimit` (a real rename, shimmed) and
`BuildReport.GetFiles()` (a real 2022.1 boundary, routed through one helper).

A corollary the campaign kept re-learning: **verify the reflection shape on a live Editor before
writing the call, not after.** Constructor arity, nested types, property names, accepted string
vocabularies — each was cheap to check with `execute_code` and each had been guessed wrong.

So before adding any `#if UNITY_*_OR_NEWER`, verify the API actually differs. Then follow
CLAUDE.md § *Unity API Compatibility Shims*: the shim belongs in `Runtime/Helpers/Unity*Compat.cs`
(or `Editor/Helpers/` for editor-only types), not at the call site.

---

## 1. Contract drift — Python sends a name C# never reads

All confirmed by Gate 1. Each one silently discards the parameter or makes the action always fail.

| Tool | Parameters | Effect | Status |
|---|---|---|---|
| `manage_input_system` | C# reads `path`, Python sends `assetPath` | every action failed | **verified** (6000, 2022) |
| `manage_input_system` | `assetName`, `binding`, `groups`, `interactions`, `processors`, `requiredDevices`, `optionalDevices` | 7 params discarded | fixed (blocked from end-to-end proof by §3b) |
| `manage_audio` | C# reads `mixerPath`, Python sends `mixerName`; `groupId` unread | `expose_param`, `set_snapshot` always fail | **verified** (6000, 2022) |
| `manage_addressables` | `schemaType`, `buildPath`, `loadPath`, `targetPlatform` unread; `labels` sent as list, read as string | **the whole tool was dead** — every reflected type name was wrong, see below | **verified** (2022 positive, 6000 negative) |
| `manage_build` | C# `configure_code_generation` requires `platform`; the tool's parameter is `target` | action always fails | **verified** (6000, 2022) |
| `manage_build` | `buildPath` now unread | dead parameter (left over from the `get_build_report` fix below) | **verified** (6000, 2022) |
| `find_gameobjects` | `cursor`, `pageSize` unread | **pre-existing on `main`**, not a `develop` regression | open |
| `manage_scene` | `sceneViewTarget` unread | **pre-existing on `main`** | open |

The runtime `path` alias in `HandleCommand` has been deleted; the 14 C# reads are now `assetPath`.
`KNOWN_UNREACHABLE` is empty — no parameter C# requires is unreachable from Python. The only
remaining `KNOWN_IGNORED` entries are `manage_addressables` (no verification host, below) and the
two rows pre-existing on `main`.

---

### `manage_addressables` — verification host, and what it found

`com.unity.addressables` 2.3.16 — the version Unity 6000.5.3f1 resolves — **does not compile there**:
`AsyncOperationBase.cs:282` and `VirtualAssetBundle.cs:37` still call `Object.GetInstanceID()`, which
6000.5 raises as CS0619. This is the same deprecation `Runtime/Helpers/UnityObjectIdCompat.cs` shims
for our own code, but it is inside the package and cannot be patched from here. Installing it broke
the probe project and dropped the bridge; it was removed and the project recovered.

Addressables **1.21.21 on the 2022.3 project** works, so that is now the verification host — and the
6000 project stays the permanent negative case (`PACKAGE_MISSING` must keep being returned there).
Note `refresh_unity` does not re-resolve the manifest; `PackageManager.Client.Resolve()` does.

With a host, the four ignored parameters turned out to be the least of it. **Every reflected type
name in the tool was wrong** — the assembly is `Unity.Addressables.Editor`, not
`Unity.AddressableAssets.Editor`, and settings/group/entry live under `…AddressableAssets.Settings`,
not `…AddressableAssets`. `PackageAvailable` therefore returned false on every project ever, so the
tool answered `PACKAGE_MISSING` unconditionally and none of its seven actions had ever run. Behind
that:

| Action | What was actually broken |
|---|---|
| `create_group` | Looked for a 5-parameter `CreateGroup`; the real one takes 6 (`params Type[]`). Always `API_INCOMPATIBLE`. Groups were also created with no schema, so they could not be built. |
| `assign_asset` | Called `AddressableAssetGroup.CreateEntry`, which does not exist at any accessibility. The real API is `AddressableAssetSettings.CreateOrMoveEntry`. |
| `remove_asset` | Called `AddressableAssetGroup.RemoveEntry`; the method is `RemoveAssetEntry`. |
| `list_groups`, and the duplicate check | `group.entries` is `ICollection<T>`, cast to `IList` — always null. Every group reported `entryCount: 0`. |
| `labels` | Read with `p.Get` while Python sends an array, and written by mutating `entry.labels`, which does not register the label project-wide. Now `settings.AddLabel` + `entry.SetLabel`. |
| `buildPath` / `loadPath` | These name Addressables *profile variables* (`Local.BuildPath`), not filesystem paths, and the properties are read-only `ProfileValueReference`s written via `SetVariableByName`. |
| `targetPlatform` | Deleted from the Python schema — groups have no per-group platform. |

Verified end to end on 2022.3.62f2: `create_group` (BundledAssetGroupSchema, Remote.BuildPath /
Remote.LoadPath) → `assign_asset` (address `probe/tex`, labels `ui`+`preload` registered
project-wide) → `list_groups` reporting `entryCount: 1` → `remove_asset` → entry gone, `entryCount: 0`.
`get_dependency_chain` also returns. `build_content` remains unexercised — it is the polling work in §6.

## 2. Version guards written from assumption

| Location | Problem | Status |
|---|---|---|
| `ManageOptimization.cs` SpriteAtlas | Three-way guard; **both** new branches false. `SpriteAtlasExtensions.{Add,GetPackingSettings,SetPackingSettings}` exist on every supported version — verified live on 6000. Collapsed to one path, which also removed the `AddObjectToAsset` corruption below. | **verified** (6000, 2022) |
| `ManageBuild.cs` `get_build_report` | `BuildReport.GetReport(path)` is not public API; broke 2022 compile. Also corrected the `files`/`GetFiles()` boundary from `2023_1` to `2022_1`. | **verified** (6000, 2022) |
| `ManageBuild.cs` stripping | `#if UNITY_6000_0_OR_NEWER` unnecessary — `SetManagedStrippingLevel` exists since 2018.3. The `#else` branch used removed enum members and mapped `Medium`→`StripByteCode`. | **verified** (6000, 2022) |
| `ManageOptimization.cs` `iosBuildSubtarget` | Absent on 2022.3 despite sitting in the "old Unity" branch. iOS has no project-wide texture subtarget; per-texture overrides already handled below. | **verified** (6000, 2022) |
| `ManageOptimization.cs` `globalTextureMipmapLimit` | 2022.2+ only, unguarded — **breaks the declared 2021.3 floor**. Shimmed in `Runtime/Helpers/UnityQualityCompat.cs` by reflection, because the two spellings overlap on 2022.2–2023.x. The one case where the guard was not simply deleted. | fixed (2021.3 rung not reached) |
| `ManageEditor.cs:360` | `report.GetFiles()` unguarded while `ManageBuild.cs` guards the same call. Both now route through `Editor/Helpers/BuildReportCompat.cs`. | fixed (2021.3 rung not reached) |

---

## 3. Guard and precondition logic

| Location | Problem | Status |
|---|---|---|
| `ManageInputSystem.cs:32` | `ResolveComponent("InputActionAsset")` — the type is a `ScriptableObject`. Guard could never pass, so the tool was dead on **every** project. | **verified** (6000 positive, 2022 negative) |
| `ManageOptimization.cs` `batch_resize_textures` | `Mathf.Max(maxWidth ?? 8192, …)` — passing `maxWidth=512` resizes nothing. Now `Min`. | **verified** (6000, 2022) |
| `ManageOptimization.cs` `batch_resize_textures` | `filter` is a `FilterMode` in the schema but was passed to `FindAssets` as the search string, matching nothing. Now applied to `importer.filterMode`. | **verified** (6000, 2022) |
| `ManageOptimization.cs` `set_quality_settings` | `(ShadowResolution)512` — the enum is `Low..VeryHigh`. Also applied overrides *after* `SetQualityLevel` (which reloads them from the tier) and hard-coded level indices 0/2/4/5 that assume six tiers; the URP template ships two. Rewritten: name-match the tier, fall back to proportional position, apply overrides last, report actual state. | **verified** (6000, 2022) |
| `ManageOptimization.cs` `configure_texture_compression` | **This row was wrong.** `iOS` and `StandaloneWindows64` are accepted on both versions — Unity aliases them. The real defect: no `SaveAndReimport`, so nothing reached the `.meta` file on *any* platform while `GetPlatformTextureSettings` still read the change back. Unrecognised platform names are also stored as dead overrides rather than rejected, and an unparseable `format` fell back to `Automatic` silently. All three fixed. | **verified** (6000, 2022) |
| `ManageComponents.cs` listeners | Five defects, all fatal: `m_CallState` never set (defaults to `Off` — **every listener this tool ever added was disabled**), `m_Mode` hard-coded to Void, stale argument copy, `m_Target` pointed at the GameObject instead of the component, and `paramType: Object` passed the listener's own target. Also deleted three `#if UNITY_2022_2_OR_NEWER` brackets whose `#else` called `UnityEventTools` methods that do not exist. | **verified** (6000, 2022) |
| `ManageComponents.cs` | Listener and inspection actions were unreachable: C# required `target` for every action, the Python schema sends it only for add/remove/set_property. | **verified** (6000, 2022) |
| `ManageGameObject.cs:111` | `set_sibling_index` clamps to 0 when `parent == null`; root objects can never be reordered. Now uses `scene.rootCount`. | **verified** (6000) |
| `ManageBuild.cs` `configure_aab` | Never set `buildAppBundle`; set `buildApkPerCpuArchitecture` (APK splitting, wrong for AAB); validated `keystorePath` then discarded it; defaulted `bundleVersionCode` to 0, resetting real projects. Passwords now come from `UNITY_ANDROID_KEYSTORE_PASS`/`UNITY_ANDROID_KEYALIAS_PASS`, never as parameters. | **verified** (6000, 2022) |
| `ManageEditor.cs` `create_folder_structure` | `AssetDatabase.CreateFolder` needs an existing parent, so `Settings/Renderer` failed — yet was reported as created. Creates each level in turn and checks the returned GUID. | **verified** (6000, 2022) |
| `ManageEditor.cs:232` | `run_health_check` NREs when `checks` is omitted. Defaults to the three cheap checks; `prefab` stays opt-in and the response names what ran. | **verified** (6000, 2022) |

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
| `ManageAudio.cs` `SetSnapshot` | Worse than reported: all three fallbacks searched for `TransitionToSnapshot`, which exists on **no** Unity type — enumerated live. The action had never done anything. Rewritten on public `FindSnapshot`/`TransitionTo` (play mode) and `TargetSnapshot` (edit mode), with read-back. | **verified** (6000, 2022) |
| `ManageAudio.cs:371` | `Activator.CreateInstance(AudioMixerController)` — a `ScriptableObject` created without `CreateInstance` has no native object. Asset is broken; correct API is `AudioMixerController.CreateMixerControllerAtPath`. | **verified** (6000, 2022) |
| `ManageEditor.cs` `create_folder_structure` | Adds to `created` without checking the returned GUID. | **verified** (6000, 2022) |
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
