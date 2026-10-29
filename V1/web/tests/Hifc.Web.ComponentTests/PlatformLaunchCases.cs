using Hifc.Web.Contracts;
using Hifc.Web.Server;
using Hifc.Web.Client;

static class PlatformLaunchCases
{
    public static void ExportInterop(string output)
    {
        var temp=Path.Combine(Path.GetTempPath(),"hifc-launch-interop-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try{using var signer=new PlanSigner(Path.Combine(temp,"key.pem"));var now=DateTime.UtcNow;var account=new AccountInfo{Id=Guid.NewGuid().ToString(),ExpiresAtUtc=now.AddHours(1)};
            var plan=new ExportPlan{Id=Guid.NewGuid().ToString("N"),Context=new RequestContext{DocumentSessionId="interop-document"}};plan.Hash=Canonical.ExportHash(plan);
            var receipt=new ExportReceipt{PlanId=plan.Id,DocumentSessionId=plan.Context.DocumentSessionId,IfcHash=new string('a',64),SnapshotBefore="snapshot",SnapshotAfter="snapshot",Exported=true,SourceRestored=true};
            var request=new PlatformLaunchRequest{ExportPlanId=plan.Id,DocumentSessionId=receipt.DocumentSessionId,ProjectId="project",TargetModelId="target",TargetGeneration="generation",IfcSha256=receipt.IfcHash,RunId=Guid.NewGuid().ToString(),MainReleaseId="0.5.0-dev-20261029T000000Z",MainSourceCommit=new string('b',40),ConfirmedReplacement=true};
            var permit=signer.IssuePlatformLaunch(request,account,now,plan,receipt);File.WriteAllText(output,JsonWire.Serialize(new{Key=signer.PublicKey,Permit=permit,Receipt=receipt,ReceiptIdentity=PlatformLaunchTrust.ReceiptIdentity(receipt)}));
        }finally{Directory.Delete(temp,true);}
    }
    public static void Run(Action<string, Action> test)
    {
        test("platform launch is bound to accepted native export and account lifetime", () => {
            var temp=Path.Combine(Path.GetTempPath(),"hifc-launch-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            try {
                using var signer=new PlanSigner(Path.Combine(temp,"key.pem"));var now=DateTime.UtcNow;
                var account=new AccountInfo {Id=Guid.NewGuid().ToString(),ExpiresAtUtc=now.AddSeconds(120)};
                var plan=new ExportPlan {Id=Guid.NewGuid().ToString("N"),Context=new RequestContext {DocumentSessionId="doc-1"}};plan.Hash=Canonical.ExportHash(plan);
                var receipt=new ExportReceipt {PlanId=plan.Id,DocumentSessionId="doc-1",IfcHash=new string('a',64),SnapshotBefore="snapshot",SnapshotAfter="snapshot",SourceRestored=true,Exported=true};
                var request=new PlatformLaunchRequest {ExportPlanId=plan.Id,DocumentSessionId="doc-1",ProjectId="project",TargetModelId="model",TargetGeneration="generation",IfcSha256=receipt.IfcHash,RunId=Guid.NewGuid().ToString(),MainReleaseId="0.5.0-dev-20261029T000000Z",MainSourceCommit=new string('b',40),ConfirmedReplacement=true};
                var permit=signer.IssuePlatformLaunch(request,account,now,plan,receipt);
                PlatformLaunchTrust.Validate(permit,request,account.Id,signer.PublicKey,now);
                if(permit.ExpiresAtUnixSeconds>new DateTimeOffset(account.ExpiresAtUtc).ToUnixTimeSeconds())throw new Exception("permit exceeds account lifetime");
                void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("unsafe platform launch accepted");}
                permit.TargetModelId="other";Reject(()=>PlatformLaunchTrust.Validate(permit,request,account.Id,signer.PublicKey,now));
                receipt.SourceRestored=false;Reject(()=>signer.IssuePlatformLaunch(request,account,now,plan,receipt));receipt.SourceRestored=true;
                receipt.IfcHash=new string('c',64);Reject(()=>signer.IssuePlatformLaunch(request,account,now,plan,receipt));receipt.IfcHash=request.IfcSha256;
                account.ExpiresAtUtc=now;Reject(()=>signer.IssuePlatformLaunch(request,account,now,plan,receipt));
            }finally{Directory.Delete(temp,true);}
        });
    }
}
