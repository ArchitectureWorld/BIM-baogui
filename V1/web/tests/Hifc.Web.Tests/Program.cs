using Hifc.Web.Contracts;

int passed = 0, failed = 0;
void Test(string name, Action run) { try { run(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action run) { try { run(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe input was accepted."); }
RequestContext Context(string doc = "doc-1", string scan = "scan-1") => new() { RuleVersion = "rules-1", RequestId = "request-1", DocumentSessionId = doc, ScanId = scan, SnapshotHash = "snapshot-1" };
ElementRecord Element(string id) => new() { Id = 1, UniqueId = id, Name = "道路", Entity = "IfcSlab" };
InventoryPage Page(int index, int count, params ElementRecord[] elements)
{
    var context = Context(); context.SnapshotHash = Canonical.InventoryHash(new[] { Element("a"), Element("b") });
    return new() { Context = context, Stage = "报规", ModelKind = "总图", PageIndex = index, PageCount = count, TotalElements = 2, Elements = elements, PageHash = Canonical.InventoryHash(elements) };
}

Test("SHA256 uses UTF8 without locale-dependent conversion", () => Require(Canonical.Hash("abc") == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA256 mismatch"));
Test("metadata changes invalidate inventory hash", () => { var a = Element("a"); var before = Canonical.InventoryHash(new[] { a }); a.TypeUniqueId = "replacement-type"; Require(before != Canonical.InventoryHash(new[] { a }), "Type replacement was invisible"); });
Test("unset zero false and empty string stay distinct", () => { var p = new ParameterValue { OwnerUniqueId = "a", Source = "flag", Exists = true, HasValue = false, StorageType = "Integer" }; var unset = Canonical.ValuesHash(new[] { p }); p.HasValue = true; p.RawValue = "0"; var zero = Canonical.ValuesHash(new[] { p }); p.StorageType = "String"; p.RawValue = ""; Require(unset != zero && zero != Canonical.ValuesHash(new[] { p }), "Value states collapsed"); });
Test("a delayed response for another document is refused", () => Reject(() => ProtocolGuard.Match(Context(), Context("other-doc"))));
Test("a delayed response for an earlier scan is refused", () => Reject(() => ProtocolGuard.Match(Context(), Context(scan: "old-scan"))));
Test("an incompatible protocol is refused", () => { var actual = Context(); actual.ProtocolMajor = 2; Reject(() => ProtocolGuard.Match(Context(), actual)); });
Test("missing inventory page cannot yield a writable scan", () => Reject(() => ProtocolGuard.InventoryPages(new[] { Page(0, 2, Element("a")) })));
Test("duplicate inventory page is refused", () => Reject(() => ProtocolGuard.InventoryPages(new[] { Page(0, 2, Element("a")), Page(0, 2, Element("b")) })));
Test("valid out-of-order inventory pages are accepted", () => ProtocolGuard.InventoryPages(new[] { Page(1, 2, Element("b")), Page(0, 2, Element("a")) }));
Test("duplicate model UniqueId is refused", () => Reject(() => ProtocolGuard.InventoryPages(new[] { Page(0, 2, Element("a")), Page(1, 2, Element("a")) })));
Test("tampered metadata is refused", () => { var a = Page(0, 1, Element("a"), Element("b")); a.Elements[0].Name = "changed"; Reject(() => ProtocolGuard.InventoryPages(new[] { a })); });
Test("capture must include explicit missing-parameter evidence", () => { var targets = new[] { new CaptureTarget { OwnerUniqueId = "a", OwnerId = 1, Source = "width", Origin = "instance" } }; var page = new CapturePage { Context = Context(), PageIndex = 0, PageCount = 1, TotalValues = 0, PageHash = Canonical.ValuesHash(Array.Empty<ParameterValue>()) }; Reject(() => ProtocolGuard.CapturePages(new[] { page }, targets)); });
Test("manual draft survives rescan with unchanged source", () => { var result = DraftRebaser.Rebase(new[] { new EditDraft { FieldKey = "a", OriginalSignature = "s1", OriginalValue = "4", Value = "5" } }, new[] { new FieldResult { Key = "a", SourceSignature = "s1", Value = "4" } }); Require(result.Reusable.Length == 1 && result.Reusable[0].Value == "5", "Draft was discarded"); });
Test("concurrent model edit produces three-way conflict", () => { var result = DraftRebaser.Rebase(new[] { new EditDraft { FieldKey = "a", OriginalSignature = "s1", OriginalValue = "4", Value = "5" } }, new[] { new FieldResult { Key = "a", SourceSignature = "s2", Value = "6" } }); Require(result.Conflicts.Length == 1 && result.Conflicts[0].CurrentValue == "6" && result.Conflicts[0].Draft.OriginalValue == "4" && result.Conflicts[0].Draft.Value == "5", "Conflict lost one of its values"); });
Test("deleted target retains its draft for review", () => { var result = DraftRebaser.Rebase(new[] { new EditDraft { FieldKey = "deleted", Value = "5" } }, Array.Empty<FieldResult>()); Require(result.InvalidTargets.Length == 1, "Deleted draft was silently lost"); });
RuleCases.Run(Test);
Console.WriteLine($"RESULT {passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
