using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hifc.Web.Contracts;
using Hifc.Web.Revit;

internal static class NativeParityCases
{
    internal static void Run(Action<string, Action> test)
    {
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        test("capture pagination sends complete definitions once and preserves all value pages", () => {
            var values = Enumerable.Range(0, 2050).Select(i => new ParameterValue { OwnerId = i, OwnerUniqueId = "owner-" + i, Source = "value", Origin = "instance" }).ToArray();
            var definitions = new[] { new NativeDefinitionSnapshot { Source = "definition-a", Values = values }, new NativeDefinitionSnapshot { Source = "definition-b" } };
            foreach (var definition in definitions) definition.Hash = Canonical.DefinitionHash(definition);
            var pages = NativeCapturePages.Create(new RequestContext(), values, definitions);
            Require(pages.Length == 5 && pages.All(p => p.Definitions.Length == 0 && p.DefinitionChunks.Length <= 8), "Full definitions were duplicated or chunk pages exceeded the cap.");
            Require(pages.Take(3).All(p => p.DefinitionChunks.Length == 0) && pages.Skip(3).All(p => p.Values.Length == 0), "Definition chunks did not use independent empty-values pages.");
            Require(pages.All(p => p.PageCount == 5 && p.TotalValues == 2050 && p.PageHash == Canonical.ValuesHash(p.Values)), "Capture page counts or hashes are wrong.");
            Require(pages.SelectMany(p => p.Values).Select(v => v.OwnerId).SequenceEqual(Enumerable.Range(0, 2050)), "Pagination lost, duplicated or reordered values.");
            var chunks = pages.SelectMany(p => p.DefinitionChunks).Where(c => c.Header.Source == "definition-a").ToArray();
            Require(chunks.Length == 9 && chunks.All(c => c.Values.Length <= 256 && c.Header.Values.Length == 0 && c.Header.Hash == definitions[0].Hash)
                && chunks.SelectMany(c => c.Values).Select(v => v.OwnerId).SequenceEqual(Enumerable.Range(0, 2050)), "Definition chunks lost values or repeated a full header payload.");
            var reconstructed = DefinitionChunks.Assemble(pages);
            Require(reconstructed.Length == 2 && reconstructed.Single(d => d.Source == "definition-a").Hash == definitions[0].Hash
                && reconstructed.Single(d => d.Source == "definition-a").Values.Length == 2050, "The actual protocol assembler could not reconstruct the native chunks.");
            var empty = NativeCapturePages.Create(new RequestContext(), new ParameterValue[0], definitions);
            Require(empty.Length == 2 && empty.All(p => p.TotalValues == 0) && empty.SelectMany(p => p.DefinitionChunks).Count() == 10, "An empty values capture lost definition evidence.");
        });
        test("canonical metric input and display never inherit project feet for bare numbers", () => {
            int parsed = 0; Func<string, string, double?> parse = (text, unit) => { parsed++; return unit == "m²" ? 100 : 3.25; };
            Require(NativeUnitInput.Normalize("100", "m²", parse) == "100" && NativeUnitInput.Normalize("3.25", "m", parse) == "3.25" && parsed == 0, "A bare canonical value was interpreted in project units.");
            Require(NativeUnitInput.Normalize("100 m²", "m²", parse) == "100" && NativeUnitInput.Normalize("3250 mm", "m", parse) == "3.25" && parsed == 2, "Explicit units did not use the canonical parser result.");
            Require(NativeUnitInput.Normalize("0.8", null, parse) == "0.8", "Dimensionless value was scaled.");
            var area = new FieldResult { RawType = "Real", Value = "1076.3910416709722", DisplayValue = "1076.39 SF", MetricValue = 100, InputUnit = "m²" };
            Require(NativeUnitInput.EditorValue(area) == "100" && NativeUnitInput.DisplayValue(area) == "100 m²", "Displayed/editable area disagrees with the transmitted unit.");
        });
        test("native export filename rejects traversal, alternate streams and reserved device names", () => {
            foreach (var name in new[] { "../escape.ifc", "sub\\escape.ifc", "x.ifc:stream", "NUL.ifc", "COM1.ifc", ".hidden.ifc", "bad.txt" }) {
                bool rejected = false; try { NativeExportExecutor.OutputPath(Path.GetTempPath(), name); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Unsafe export name was accepted: " + name);
            }
        });
        test("native export protects an existing IFC and admits a fresh version", () => {
            var directory = Path.Combine(Path.GetTempPath(), "hifc-native-export-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var existing = Path.Combine(directory, "项目_报规_20261029_V1.ifc"); File.WriteAllText(existing, "existing IFC");
            try {
                bool rejected = false; try { NativeExportExecutor.OutputPath(directory, Path.GetFileName(existing)); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected && File.ReadAllText(existing) == "existing IFC", "Existing IFC could be replaced.");
                Require(NativeExportExecutor.OutputPath(directory, "项目_报规_20261029_V2.ifc") == Path.Combine(directory, "项目_报规_20261029_V2.ifc"), "Fresh version rejected.");
            } finally { File.Delete(existing); Directory.Delete(directory); }
        });
        test("native mixed values remain untouched until an explicit edit and preserve false separately from blank", () => {
            var fields = new[] { new FieldResult { Key = "blank", RawType = "Boolean", Value = "", Editable = true }, new FieldResult { Key = "false", RawType = "Boolean", Value = "0", Editable = true } };
            var written = new List<string>(); var edit = new NativeEditBuffer(fields, new EditDraft[0], (field, value) => written.Add(field.Key + "=" + value));
            Require(edit.Mixed && edit.Input == null && written.Count == 0, "Loading mixed values produced a write.");
            edit.Set("0"); Require(!edit.Mixed && written.SequenceEqual(new[] { "blank=0", "false=0" }), "Explicit false was lost or applied outside the group.");
            edit.Set("0"); Require(written.Count == 2, "Unchanged edit created repeated writes.");
        });
        test("native editing refuses a partially readonly selection without partial writes", () => {
            var written = new List<string>(); var edit = new NativeEditBuffer(new[] { new FieldResult { Key = "writable", Editable = true }, new FieldResult { Key = "readonly", Editable = false } }, new EditDraft[0], (field, value) => written.Add(field.Key));
            bool rejected = false; try { edit.Set("replacement"); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && written.Count == 0, "Readonly mixed selection partially wrote values.");
        });
        test("native client assembly has no private rule engine reference", () => {
            Require(typeof(ProfileStore).Assembly.GetReferencedAssemblies().All(a => !a.Name.StartsWith("Hifc.Reference", StringComparison.Ordinal) && a.Name != "Hifc.Web.RuleEngine"), "Private rules leaked into the client.");
        });
    }
}
