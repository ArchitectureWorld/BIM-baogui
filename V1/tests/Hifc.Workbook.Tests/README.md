# Reporting workbook verification

This standalone .NET Framework 4.8/WPF suite needs no Revit host or Office.
It links only the new workbook window and the existing theme, and references the pure core library.

```powershell
dotnet build V1\tests\Hifc.Workbook.Tests\Hifc.Workbook.Tests.csproj -c Release --nologo -v minimal
& V1\tests\Hifc.Workbook.Tests\bin\Release\net48\Hifc.Workbook.Tests.exe "$env:TEMP\hifc-workbook-tests"
```

Pass `--sample <xlsx-path>` after the output directory to run the explicit supplied-workbook check. That optional check verifies the approved source hash, merged floor rows, mixed building values, and metric warnings, then verifies the original hash again. Its `sample-audit.json` and the rendered `workbook-preview.png` stay in the chosen output directory.

The fixture XML describes hand-checkable Excel worksheets. The tests create small XLSX packages from it at runtime. The original customer workbook is never copied into source control or changed.

## Integration contract

```csharp
var source = ReportingWorkbookReader.Read(path);
var plan = ReportingWorkbookPlanner.Build(source.Tables[0], snapshot.Elements,
    snapshot.ModelKind, targetBuildingUniqueId);
var decision = ReportingWorkbookPlanner.Decide(plan);
// Or: new ReportingWorkbookWindow(snapshot.Elements, snapshot.ModelKind).ShowDialog()
```

`targetBuildingUniqueId == null` requests an exact, unique name match. An empty string means the target selection was explicitly cleared. A nonempty ID must identify exactly one `IfcBuilding`. Duplicate building or floor names do not fall back to array order.

`Changes` includes current values, stable element/owner IDs, binding, and source signatures for the existing edit baseline. `CanSelect` blocks blank, invalid, readonly, ambiguous-target, and uncached-formula rows. `Selected` defaults to false for conflicts, duplicate source fields, and tables containing mixed building information. The decision rejects duplicate selected sources for one target property. Parent/child rule values are checked again when confirming the decision.

`References` retains every accepted table item, including unmapped fields, its original value, unit, notes, and file/hash/sheet/cell/formula-cache provenance. It does not create new IFC property mappings. Current-model scope mismatches produce references only.

The dialog returns only the currently selected table. It accepts multiple source files, preserves per-table checkbox choices while switching tables, and requires an explicit table selection when several are loaded. The caller stages the returned changes in the existing property grid and persists accepted references only with the existing model-write transaction. The dialog itself does not mutate snapshots or model data.

Imported floor totals target a unique `IfcBuildingStorey` / `Pset_建筑楼层信息属性集` / `楼层面积`. The existing floor-area pipeline owns any allocation to regions.

`ReportingWorkbookTemplate.Write(path, buildingNames, includeSite: true)` creates a new reusable workbook with one project site sheet and one building per sheet. It never overwrites an existing file and supplies no numeric or boolean business defaults.
