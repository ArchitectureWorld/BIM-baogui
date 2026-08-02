# MVD IFC Normalizer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Stage04 Grasshopper component that preserves an official H-IFC export's geometry while normalizing MVD property names and IFC value types into a new sibling IFC file.

**Architecture:** A small STEP parser indexes only the IFC entities needed for property-set traversal while preserving all unknown statements verbatim. A mapping catalog joins the embedded Stage01 MVD registry to the existing official H-IFC catalog, and a normalizer rewrites only matched property statements, removes recognized `HIFC.` duplicate aliases, writes atomically, and validates by reparsing.

**Tech Stack:** C# / .NET Framework 4.8, xUnit, Grasshopper 8 SDK, Revit 2020 host assembly, `System.Web.Extensions` JSON serialization.

---

### Task 1: STEP parser and entity writer

**Files:**
- Create: `src/BIMBaoGui.Stage01/Mvd/IfcStepEntity.cs`
- Create: `src/BIMBaoGui.Stage01/Mvd/IfcStepDocument.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/IfcStepDocumentTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`

- [ ] **Step 1: Write failing parser tests**

Add tests that parse multiline IFC4 statements, split nested arguments, decode and encode STEP strings, replace one argument, delete an entity, and serialize without changing untouched statements.

```csharp
[Fact]
public void Parse_indexes_multiline_entities_and_nested_arguments()
{
  IfcStepDocument document = IfcStepDocument.Parse(Fixture);
  IfcStepEntity property = document.GetEntity(23);
  Assert.Equal("IFCPROPERTYSINGLEVALUE", property.Type);
  Assert.Equal("'基点坐标Y'", property.Arguments[0]);
  Assert.Equal("IFCREAL(38589642.165)", property.Arguments[2]);
}

[Fact]
public void Serialize_preserves_unknown_statements_and_applies_targeted_changes()
{
  IfcStepDocument document = IfcStepDocument.Parse(Fixture);
  document.GetEntity(23).SetArgument(0, IfcStepSyntax.EncodeString("基点坐标 Y"));
  string output = document.Serialize();
  Assert.Contains("IFCPROPERTYSINGLEVALUE('基点坐标 Y',$,IFCREAL(38589642.165),$)", output);
  Assert.Contains("IFCCARTESIANPOINT((0.,0.,0.))", output);
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj --filter IfcStepDocumentTests
```

Expected: compile failure because `IfcStepDocument` and `IfcStepEntity` do not exist.

- [ ] **Step 3: Implement the minimal parser**

Implement a scanner that separates statements on semicolons outside STEP strings, parses `#<id>=<TYPE>(<args>)`, and splits arguments on commas at nesting depth zero.

```csharp
internal sealed class IfcStepEntity
{
  public int Id { get; }
  public string Type { get; }
  public IList<string> Arguments { get; }
  public bool IsDeleted { get; private set; }
  public void SetArgument(int index, string value) => Arguments[index] = value;
  public void Delete() => IsDeleted = true;
}

internal sealed class IfcStepDocument
{
  public static IfcStepDocument Parse(string text);
  public IfcStepEntity GetEntity(int id);
  public IEnumerable<IfcStepEntity> OfType(string type);
  public string Serialize();
}
```

Reject unterminated strings, unbalanced parentheses, duplicate entity ids, and malformed assignments with `InvalidDataException`.

- [ ] **Step 4: Run parser tests and the full .NET suite**

Expected: parser tests pass and existing tests remain green.

- [ ] **Step 5: Commit parser increment**

```powershell
git add src/BIMBaoGui.Stage01/Mvd tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: add targeted IFC STEP parser"
```

### Task 2: Mapping-driven MVD normalization catalog

**Files:**
- Create: `src/BIMBaoGui.Stage01/Mvd/MvdIfcNormalizationRule.cs`
- Create: `src/BIMBaoGui.Stage01/Mvd/MvdIfcNormalizationCatalog.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/MvdIfcNormalizationCatalogTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`

- [ ] **Step 1: Write failing catalog tests**

```csharp
[Fact]
public void Catalog_joins_MVD_names_to_official_aliases()
{
  MvdIfcNormalizationRule x = MvdIfcNormalizationCatalog.Instance.Rules.Single(
    rule => rule.Entity == "IfcProject" && rule.CanonicalProperty == "基点坐标 X");
  Assert.Equal("申报信息属性集", x.PropertySet);
  Assert.Contains("基点坐标X", x.Aliases);
  Assert.Equal("IfcReal", x.TargetType);
}

[Fact]
public void Catalog_preserves_IfcLabel_requirement()
{
  MvdIfcNormalizationRule projectName = MvdIfcNormalizationCatalog.Instance.Rules.Single(
    rule => rule.Entity == "IfcProject" && rule.CanonicalProperty == "项目名称");
  Assert.Equal("IfcLabel", projectName.TargetType);
}
```

- [ ] **Step 2: Run tests and verify RED**

Expected: compile failure because the catalog does not exist.

- [ ] **Step 3: Implement catalog loading**

Read the embedded Stage01 registry with `JavaScriptSerializer`. For each MVD field, parse `entity|Pset_name|property`, strip `Pset_`, retain the registry property as the canonical MVD name, and use `OfficialHifcMappingCatalog.TryResolveStage01FieldKey` to add the official no-space property alias.

```csharp
internal sealed class MvdIfcNormalizationRule
{
  public string Entity { get; set; }
  public string PropertySet { get; set; }
  public string CanonicalProperty { get; set; }
  public string TargetType { get; set; }
  public string Unit { get; set; }
  public IReadOnlyCollection<string> Aliases { get; set; }
  public IReadOnlyCollection<string> InternalAliases { get; set; }
}
```

Fail loading when two rules create the same entity/property-set/alias key with different canonical targets.

- [ ] **Step 4: Run catalog tests and full suite**

- [ ] **Step 5: Commit catalog increment**

```powershell
git add src/BIMBaoGui.Stage01/Mvd tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: add MVD IFC normalization catalog"
```

### Task 3: Property-set traversal and in-memory normalization

**Files:**
- Create: `src/BIMBaoGui.Stage01/Mvd/MvdIfcNormalizationModels.cs`
- Create: `src/BIMBaoGui.Stage01/Mvd/MvdIfcNormalizer.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/MvdIfcNormalizerTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`

- [ ] **Step 1: Write failing behavior tests**

Cover these independent behaviors:

```csharp
[Fact]
public void Normalize_renames_coordinate_aliases_without_changing_real_values();

[Fact]
public void Normalize_converts_project_name_from_IfcText_to_IfcLabel();

[Fact]
public void Normalize_removes_only_HIFC_duplicates_from_Data_pset();

[Fact]
public void Normalize_keeps_unrelated_Data_properties();

[Fact]
public void Normalize_rejects_non_IFC4_documents();
```

The fixture must include `IfcProject`, `IfcBuilding`, both formal `申报信息属性集` property sets, and a `数据` property set containing one `HIFC.` alias plus one unrelated property.

- [ ] **Step 2: Run tests and verify RED**

Expected: compile failure because `MvdIfcNormalizer` does not exist.

- [ ] **Step 3: Implement relationship traversal**

Build indexes for:

- entity ids by IFC type;
- `IfcRelDefinesByProperties.RelatedObjects -> RelatingPropertyDefinition`;
- `IfcPropertySet.Name -> HasProperties`;
- `IfcPropertySingleValue.Name -> NominalValue`.

Normalize only rules whose owner entity type, property-set name, and property alias all match. Use the target type constructor from the catalog and preserve the inner scalar token after validation.

```csharp
internal sealed class MvdIfcNormalizer
{
  public MvdIfcNormalizationResult Normalize(IfcStepDocument document);
  public MvdIfcValidationResult Validate(IfcStepDocument document);
}
```

For `IfcLabel`, `IfcText`, `IfcIdentifier`, `IfcDate`, and `IfcDateTime`, require a STEP string value. For `IfcReal` and integer types, require a finite numeric token. Do not perform unit conversion.

- [ ] **Step 4: Implement duplicate cleanup**

In a property set named `数据`, remove only `IfcPropertySingleValue` entities whose decoded name exactly matches a rule's internal alias such as `HIFC.申报信息属性集.基点坐标X`. Keep unrelated properties. If the property set becomes empty, delete its defining relation and property-set entity.

- [ ] **Step 5: Run normalizer tests and full suite**

- [ ] **Step 6: Commit normalizer increment**

```powershell
git add src/BIMBaoGui.Stage01/Mvd tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: normalize MVD properties in IFC4"
```

### Task 4: Atomic file service and Stage04 failure report

**Files:**
- Create: `src/BIMBaoGui.Stage01/Mvd/MvdIfcFileService.cs`
- Create: `src/BIMBaoGui.Stage01/Diagnostics/Stage04FailureReportWriter.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/MvdIfcFileServiceTests.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/Stage04FailureReportWriterTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`

- [ ] **Step 1: Write failing file-safety tests**

```csharp
[Fact]
public void Execute_never_changes_source_and_creates_new_MVD_file();

[Fact]
public void Execute_rejects_destination_equal_to_source();

[Fact]
public void Execute_rejects_existing_destination_without_backup_or_overwrite();

[Fact]
public void Execute_removes_temporary_file_when_validation_fails();
```

- [ ] **Step 2: Write failing report tests**

Verify `BIMBaoGui.Stage04.failure-yyyyMMdd-HHmmss-fff.json` is created beside the supplied assembly path, includes source/destination/stage/exception, and leaves no `.tmp` file.

- [ ] **Step 3: Run focused tests and verify RED**

- [ ] **Step 4: Implement atomic file execution**

Read the source with detected UTF-8 BOM/no-BOM encoding, hash the source before processing, write to a unique temporary file in the destination directory, parse and validate the temporary output, verify the source hash is unchanged, then `File.Move(temp, destination)`. Existing destination is an error.

- [ ] **Step 5: Implement the Stage04 report writer**

Reuse the Stage01 report conventions but use diagnostic code `DIAG_STAGE04_MVD_NORMALIZATION_FAILED` and prefix `BIMBaoGui.Stage04.failure-`.

- [ ] **Step 6: Run focused tests and full suite**

- [ ] **Step 7: Commit file service increment**

```powershell
git add src/BIMBaoGui.Stage01/Mvd src/BIMBaoGui.Stage01/Diagnostics tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: add atomic MVD IFC file normalization"
```

### Task 5: Grasshopper Stage04 component

**Files:**
- Create: `src/BIMBaoGui.Stage01/Stage04MvdIfcNormalizeComponent.cs`
- Create: `src/BIMBaoGui.Stage01/Mvd/MvdIfcNormalizationCoordinator.cs`
- Create: `tests/BIMBaoGui.Stage01.Core.Tests/MvdIfcPathPolicyTests.cs`
- Modify: `tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj`

- [ ] **Step 1: Write failing path-policy tests**

Test default output naming, case-insensitive source/destination equality, `.ifc` extension validation, and existing destination rejection.

- [ ] **Step 2: Run path tests and verify RED**

- [ ] **Step 3: Implement coordinator and component**

The component uses `ExplicitExecutionGate`, performs file work on a background task, schedules a Grasshopper solution on completion, and exposes `Success`, `Status`, `Output`, and `Messages`. Exceptions call `Stage04FailureReportWriter` with `typeof(Stage04MvdIfcNormalizeComponent).Assembly.Location`.

Default output naming:

```csharp
string fileName = Path.GetFileNameWithoutExtension(source) + "-MVD.ifc";
return Path.Combine(Path.GetDirectoryName(source), fileName);
```

No Revit transaction or ExternalEvent is required because Stage04 reads and writes IFC files only.

- [ ] **Step 4: Run .NET tests and Revit 2020 Release build**

```powershell
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj
dotnet build src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj -c Release
```

- [ ] **Step 5: Commit component increment**

```powershell
git add src/BIMBaoGui.Stage01 tests/BIMBaoGui.Stage01.Core.Tests
git commit -m "feat: add Stage04 MVD IFC normalizer"
```

### Task 6: Real IFC regression, deployment, and acceptance

**Files:**
- Create: `tests/test_mvd_ifc_normalizer_contract.py`
- Modify: `README.md`
- Modify: `docs/revit2020-v090-acceptance-checklist.md`
- Modify: `specs/hifc-mapping/v1/manifest.sha256.json` only for tracked mapping artifacts changed by implementation, if any

- [ ] **Step 1: Add static release-contract tests**

Assert Stage04's component GUID is unique, the failure-report prefix is correct, the component refuses overwrite, and the release documentation includes the new workflow.

- [ ] **Step 2: Run Python test and verify RED**

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
pytest tests/test_mvd_ifc_normalizer_contract.py -q
```

- [ ] **Step 3: Update documentation minimally**

Document: official export first, Stage04 second, output naming, no overwrite/backups, and failure report location.

- [ ] **Step 4: Run all automated verification**

```powershell
$env:PYTEST_DISABLE_PLUGIN_AUTOLOAD='1'
pytest tests -q
dotnet test tests/BIMBaoGui.Stage01.Core.Tests/BIMBaoGui.Stage01.Core.Tests.csproj
dotnet build src/BIMBaoGui.Stage01/BIMBaoGui.Stage01.csproj -c Release
```

- [ ] **Step 5: Run the real IFC acceptance harness**

Normalize the supplied IFC into a new unique output path and parse it with IFCOpenShell to assert:

- `IfcProject` owns `申报信息属性集`;
- `基点坐标 X` is `IfcReal(3373266.866)`;
- `基点坐标 Y` is `IfcReal(38589642.165)`;
- `基点高程` is `IfcReal(24.0)`;
- `项目编号` and `项目名称` are `IfcLabel`;
- no `HIFC.` property remains in the `IfcProject` `数据` property set;
- the source SHA-256 is unchanged.

- [ ] **Step 6: Commit verification and documentation**

```powershell
git add tests README.md docs specs
git commit -m "test: verify MVD IFC normalization workflow"
```

- [ ] **Step 7: Deploy without plugin backups**

After Revit, Rhino.Inside, and Grasshopper are closed, copy only the Release GHA to:

```text
C:\Users\2899\AppData\Roaming\Grasshopper\Libraries\BIMbaogui\BIMBaoGui.Stage01.gha
```

Verify the deployed SHA-256 matches the Release artifact and the plugin directory contains zero backup GHA/DLL files.
