using Hifc.Web.Contracts;
using Hifc.Web.Server;
using Npgsql;

internal static class PlatformLaunchStoreCases
{
    internal static async Task RunAsync(Func<string, Func<Task>, Task> test, NpgsqlDataSource source, AccountStore accounts)
    {
        if (new NpgsqlConnectionStringBuilder(source.ConnectionString).Database != "hifc_web_test") throw new InvalidOperationException("Use the isolated hifc_web_test database.");
        var directory = Path.Combine(Path.GetTempPath(), "hifc-launch-database-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try {
            using var signer = new PlanSigner(Path.Combine(directory, "key.pem")); var store = new PlatformLaunchStore(source, signer, accounts); await store.InitializeAsync();
            async Task<(AccountInfo Account, PlatformLaunchRequest Request, ExportPlan Plan, ExportReceipt Receipt)> Fixture()
            {
                var account = await accounts.TryCreateOrdinaryAsync("139" + Random.Shared.NextInt64(10000000, 100000000), "平台许可测试");
                var validation = new Hifc.Reference.RequiredInputValidation {
                    rule_profile_id = Hifc.Reference.PlatformRules.ProfileId, model_kind = "总图", complete = true, checked_objects = 1, checked_fields = 1
                };
                var proof = System.Text.Json.JsonSerializer.Deserialize<RequiredInputProof>(System.Text.Json.JsonSerializer.Serialize(validation));
                var plan = new ExportPlan { InputValidation = proof, Id = Guid.NewGuid().ToString("N"), Context = new RequestContext {
                    RuleVersion = new Hifc.Web.RuleEngine.RuleAdapter().RuleVersion, DocumentSessionId = Guid.NewGuid().ToString("N") } }; plan.Hash = Canonical.ExportHash(plan);
                var receipt = new ExportReceipt { InputValidation = proof, PlanId = plan.Id, DocumentSessionId = plan.Context.DocumentSessionId, IfcHash = new string('a', 64), SnapshotBefore = "snapshot", SnapshotAfter = "snapshot", Exported = true, SourceRestored = true };
                return (account, new PlatformLaunchRequest { ExportPlanId = plan.Id, DocumentSessionId = receipt.DocumentSessionId, ProjectId = "project", TargetModelId = "model", TargetGeneration = "generation", IfcSha256 = receipt.IfcHash, RunId = Guid.NewGuid().ToString(), MainReleaseId = "0.5.0-dev-20261029T000000Z", MainSourceCommit = new string('b', 40), ConfirmedReplacement = true }, plan, receipt);
            }
            async Task Reject(Func<Task> action) { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Replayed launch was accepted."); }
            await test("platform launch consumption is atomic across independent service instances", async () => {
                var f = await Fixture(); var permit = await store.IssueAsync(f.Request, f.Account, f.Plan, f.Receipt);
                async Task<bool> Consume(PlatformLaunchStore instance, char nonce) { try { var receipt = await instance.ConsumeAsync(new PlatformConsumeRequest { Permit = permit, ConsumerNonce = new string(nonce, 64) }); PlatformLaunchTrust.Verify(PlatformLaunchTrust.ConsumptionPayload(receipt), receipt.Signature, signer.PublicKey); return true; } catch (InvalidOperationException) { return false; } }
                var results = await Task.WhenAll(Consume(store, 'a'), Consume(new PlatformLaunchStore(source, signer, accounts), 'b'));
                if (results.Count(x => x) != 1) throw new Exception("Consume was not single-use.");
                await Reject(async () => await new PlatformLaunchStore(source, signer, accounts).IssueAsync(f.Request, f.Account, f.Plan, f.Receipt));
            });
            await test("platform consumption rechecks account expiry using database time", async () => {
                var f = await Fixture(); var permit = await store.IssueAsync(f.Request, f.Account, f.Plan, f.Receipt);
                await using var expire = source.CreateCommand("UPDATE hifc_web.accounts SET expires_at=clock_timestamp()-interval '1 second' WHERE id=@id"); expire.Parameters.AddWithValue("id", Guid.Parse(f.Account.Id)); await expire.ExecuteNonQueryAsync();
                await Reject(async () => await store.ConsumeAsync(new PlatformConsumeRequest { Permit = permit, ConsumerNonce = new string('c', 64) }));
            });
            await test("deleted Web account cannot consume a previously signed platform launch", async () => {
                var f = await Fixture(); var permit = await store.IssueAsync(f.Request, f.Account, f.Plan, f.Receipt);
                await using var remove = source.CreateCommand("DELETE FROM hifc_web.accounts WHERE id=@id"); remove.Parameters.AddWithValue("id", Guid.Parse(f.Account.Id)); await remove.ExecuteNonQueryAsync();
                await Reject(async () => await store.ConsumeAsync(new PlatformConsumeRequest { Permit = permit, ConsumerNonce = new string('d', 64) }));
            });
        } finally { Directory.Delete(directory, true); }
    }
}
