# H-IFC Revit 2020 Runtime Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recover the vendor H-IFC application in Revit 2020 with a reversible, evidence-backed repair and a fresh `API_SUCCESS` journal.

**Architecture:** Treat the vendor plugin as an external black box. Narrow its startup boundary with file timestamps and a temporary diagnostic add-in, preserve live configuration with SHA-256 manifests, repair only files that differ from verified vendor baselines, and validate using a fresh Revit process and journal.

**Tech Stack:** PowerShell 7/Windows PowerShell 5.1, .NET Framework 4.8, Autodesk Revit 2020 API, Revit journal files.

---

### Task 1: Capture immutable runtime evidence

**Files:**
- Create: `docs/reviews/2026-08-01-hifc-runtime-recovery-log.md`

- [ ] **Step 1: Record process, journal, manifest, binaries, and configuration hashes**

Run:

```powershell
Get-Process Revit | Select-Object Id,StartTime,MainWindowTitle
Get-FileHash 'C:\Program Files\HIFCTool\REVIT2020\net48\Hust.XAR.Shell.dll' -Algorithm SHA256
Get-FileHash 'C:\Program Files\HIFCTool\REVIT2020\net48\Hust.IFC.RVT2HIFC.dll' -Algorithm SHA256
Get-FileHash 'C:\Users\2899\Documents\HIFCTool\cnf\BaseFile\HIFCToolRvtToIfcConfig.txt' -Algorithm SHA256
Get-FileHash 'C:\Users\2899\Documents\HIFCTool\cnf\BaseFile\HIFCToolRvtToIfcConfig_Bak.txt' -Algorithm SHA256
```

Expected: hashes and timestamps are captured without changing the live environment.

- [ ] **Step 2: Record the narrowed startup boundary**

Write the evidence that failed starts do not delete or refresh `HIFCToolExeclAttributeImport.json`, while an isolated reflection probe of `MyRvtToIfcConfigContr.ReadAttrTxtFile` returns 166 records. Therefore the current failure is before the configuration delete/rebuild statement in `Hust.XAR.Shell.App.OnStartup`.

- [ ] **Step 3: Commit the evidence note**

```powershell
git add docs/reviews/2026-08-01-hifc-runtime-recovery-log.md
git commit -m "docs: capture H-IFC runtime recovery evidence"
```

### Task 2: Build a temporary startup exception probe

**Files:**
- Create: `tools/HifcStartupProbe/HifcStartupProbe.csproj`
- Create: `tools/HifcStartupProbe/ProbeApplication.cs`
- Create: `tools/HifcStartupProbe/HifcStartupProbe.addin.template`

- [ ] **Step 1: Implement a user-level Revit application probe**

The probe implements `IExternalApplication`. In `OnStartup`, it loads `Hust.XAR.Shell.dll`, creates `Hust.XAR.Shell.App`, invokes its `OnStartup(UIControlledApplication)`, and writes the result or the full inner-exception chain to `%LOCALAPPDATA%\BIMBaoGui\Diagnostics\HifcStartupProbe-<timestamp>.log`. It must never swallow a logging failure into Revit and must return `Result.Succeeded` after recording the vendor result.

Core invocation:

```csharp
try
{
  Assembly assembly = Assembly.LoadFrom(VendorAssembly);
  Type type = assembly.GetType("Hust.XAR.Shell.App", true);
  object instance = Activator.CreateInstance(type);
  MethodInfo method = type.GetMethod("OnStartup", new[] { typeof(UIControlledApplication) });
  object result = method.Invoke(instance, new object[] { application });
  WriteLog("Vendor OnStartup result: " + result);
}
catch (Exception exception)
{
  WriteLog(FormatExceptionChain(exception));
}
return Result.Succeeded;
```

- [ ] **Step 2: Build the probe**

Run:

```powershell
dotnet build tools\HifcStartupProbe\HifcStartupProbe.csproj -c Release
```

Expected: build succeeds with zero warnings and errors.

- [ ] **Step 3: Install after the vendor manifest**

Copy the probe DLL and a manifest named `ZZ.BIMBaoGui.HifcStartupProbe.addin` to `%APPDATA%\Autodesk\Revit\Addins\2020\HifcStartupProbe`. Back up any existing file at that exact destination first.

- [ ] **Step 4: Commit the reusable diagnostic source**

```powershell
git add tools/HifcStartupProbe
git commit -m "tool: add Revit H-IFC startup exception probe"
```

### Task 3: Repair the live object mapping independently

**Files:**
- Modify: `C:\Users\2899\Documents\HIFCTool\cnf\BaseFile\HIFCToolRvtToIfcConfig.txt`
- Create: timestamped backup and manifest in the same directory

- [ ] **Step 1: Verify both 738-byte baselines are identical**

Compare the live `_Bak` file and the installed vendor file byte-for-byte and by SHA-256. Stop if they differ.

- [ ] **Step 2: Back up the 237-byte live file**

Copy it to `HIFCToolRvtToIfcConfig.pre-bimbaogui-<timestamp>.txt` and write a JSON manifest containing source path, backup path, length, hash, and UTC timestamp.

- [ ] **Step 3: Replace using the verified baseline**

Copy to a temporary file in the same directory, verify its hash, then use `Move-Item` to replace the live target. Verify the final target is 738 bytes and contains four non-comment mapping rows.

### Task 4: Restart Revit safely and identify the startup root cause

- [ ] **Step 1: Confirm the open RVT has no unsaved changes**

Use a normal Revit close flow. If Revit asks to save, choose Save for the supplied RVT and wait for completion; never terminate the process while a save or dialog is active.

- [ ] **Step 2: Start a fresh Revit 2020 process**

Open `20260731test02.rvt`, wait for startup dialogs, then collect the newest journal and probe log.

- [ ] **Step 3: Classify the result**

- If the vendor manifest reports `API_SUCCESS`, remove the temporary probe and record that the configuration repair plus clean restart recovered startup; do not claim a narrower cause than the evidence supports.
- If it remains `API_ERROR`, use the probe exception chain to repair only the identified missing/corrupt dependency or invalid state, then repeat once.
- If the probe itself cannot run because the vendor application partially initialized before failing, temporarily disable only `HIFCTool.Addin`, run the probe as the sole vendor invoker, and restore the manifest afterward.

- [ ] **Step 4: Remove temporary deployed probe files**

Keep the probe source in the repository, but remove the user-level `.addin` and deployed DLL after diagnosis.

### Task 5: Verify official export readiness

- [ ] **Step 1: Require fresh journal evidence**

Expected journal lines:

```text
API_SUCCESS { Starting External Application: 华中科技大学.REVIT.Plug ... }
API_SUCCESS { Starting External Application: Rhino.Inside ... }
```

- [ ] **Step 2: Open the official H-IFC UI**

Verify the ribbon/dockable panel is present and the object-mapping page contains ProjectInfo→IfcProject, ProjectInfo→IfcBuilding, Level→IfcBuildingStorey, and Room→IfcSpace.

- [ ] **Step 3: Update and commit the recovery log**

Record the new journal path, exact result, repaired files, backup paths, and hashes.

```powershell
git add docs/reviews/2026-08-01-hifc-runtime-recovery-log.md
git commit -m "docs: verify H-IFC Revit 2020 runtime recovery"
```
