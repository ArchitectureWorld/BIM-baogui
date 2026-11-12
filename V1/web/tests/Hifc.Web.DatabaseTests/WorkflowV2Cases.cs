using System.Reflection;
using Hifc.Web.Contracts;
using Hifc.Web.RuleEngine;
using Hifc.Web.Server;
using Npgsql;

internal static class WorkflowV2Cases
{
    internal static async Task Run(NpgsqlDataSource source, Func<string, Func<Task>, Task> test)
    {
        using var signer=new PlanSigner(Path.Combine(Path.GetTempPath(),"HifcWorkflowV2",Guid.NewGuid().ToString("N"),"signing.pem"));
        var workflow=new WorkflowStore(source,new RuleAdapter(),signer);await workflow.InitializeAsync();
        async Task<object> Invoke(string name,params object[] args){
            var method=typeof(WorkflowStore).GetMethod(name)??throw new InvalidOperationException("Workflow protocol-2 operation missing: "+name);
            Task task;try{task=(Task)method.Invoke(workflow,args);}catch(TargetInvocationException e){throw e.InnerException;}
            await task;return task.GetType().GetProperty("Result")?.GetValue(task);
        }
        async Task<(Guid Actor,CheckResult Result)> Scene(){
            var account=await new AccountStore(source).TryCreateOrdinaryAsync("139"+Random.Shared.NextInt64(10000000,100000000),"新版流程验收");
            var actor=Guid.Parse(account.Id);var elements=new[]{new ElementRecord{Id=1,UniqueId="land",Name="规划净用地",Entity="IfcSlab",SourceEntity="IfcSlab",DeclaredSlot="规划净用地",CategoryId=-2000032,ProjectedAreaM2=100}};
            var context=new RequestContext{RuleVersion=new RuleAdapter().RuleVersion,RequestId=Guid.NewGuid().ToString("N"),DocumentSessionId="doc",ScanId=Guid.NewGuid().ToString("N"),SnapshotHash=Canonical.InventoryHash(elements)};
            var capture=await workflow.StartAsync(actor,new InventoryPage{Context=context,Stage="报规",ModelKind="总图",PageCount=1,TotalElements=1,Elements=elements,PageHash=Canonical.InventoryHash(elements)});
            var values=capture.Targets.DistinctBy(t=>t.OwnerUniqueId+"|"+t.Source+"|"+t.Origin).Select(t=>new ParameterValue{OwnerId=t.OwnerId,OwnerUniqueId=t.OwnerUniqueId,Source=t.Source,Origin=t.Origin}).ToArray();
            await workflow.CaptureAsync(actor,new CapturePage{Context=context,PageCount=1,TotalValues=values.Length,Values=values,PageHash=Canonical.ValuesHash(values)});
            return(actor,await workflow.CompleteAsync(actor,context));
        }
        static void Require(bool value,string why){if(!value)throw new Exception(why);}
        static async Task Reject(Func<Task> body){try{await body();}catch(InvalidOperationException e)when(!e.Message.StartsWith("Workflow protocol-2 operation missing:")){return;}throw new Exception("Unsafe workflow operation was accepted.");}
        await test("protocol2 material preview retains the exact model context and ownership",async()=>{
            var scene=await Scene();
            var request=new MaterialPreviewRequest{Context=scene.Result.Context,Kind="redline",FileName="manual.json",DataJson="{\"Version\":1,\"Accepted\":true,\"Unit\":1,\"Frame\":1,\"Axes\":1,\"CoordinateSystemName\":\"内部\",\"Points\":[{\"Sequence\":1,\"X\":0,\"Y\":0},{\"Sequence\":2,\"X\":10,\"Y\":0},{\"Sequence\":3,\"X\":0,\"Y\":10}]}"};
            var preview=(MaterialPreview)await Invoke("MaterialPreviewAsync",scene.Actor,request,CancellationToken.None);
            Require(preview.Context.ConfigurationHash==scene.Result.Context.ConfigurationHash&&!string.IsNullOrEmpty(preview.Id),"Material preview lost its context.");
            await Reject(()=>Invoke("MaterialSelectionsAsync",Guid.NewGuid(),new MaterialSelectionRequest{Context=request.Context,Decisions=new[]{new MaterialDecision{PreviewId=preview.Id,Accepted=true}}},CancellationToken.None));
        });
        await test("protocol2 rejects material selections that were never previewed",async()=>{
            var scene=await Scene();
            await Reject(()=>Invoke("MaterialSelectionsAsync",scene.Actor,new MaterialSelectionRequest{Context=scene.Result.Context,Decisions=new[]{new MaterialDecision{PreviewId="not-previewed",Accepted=true}}},CancellationToken.None));
        });
        await test("protocol2 exported receipts are bound to plan actor and restored source",async()=>{
            var scene=await Scene();
            var plan=(ExportPlan)await Invoke("PreviewExportAsync",scene.Actor,new ExportRequest{Context=scene.Result.Context,ProjectName="测试",BuildingName="总图",FileName="OUT_总图_测试_报规_20261029_V1.ifc"},CancellationToken.None);
            Require(!string.IsNullOrEmpty(plan.Signature)&&plan.Hash==Canonical.ExportHash(plan),"Export plan was not signed.");
            var receipt=new ExportReceipt{InputValidation=plan.InputValidation,PlanId=plan.Id,DocumentSessionId="doc",IfcPath="C:/isolated/result.ifc",IfcHash=new string('a',64),Exported=true,SourceRestored=false,SnapshotBefore="before",SnapshotAfter="after",CompletedUtc=DateTime.UtcNow};
            await Reject(()=>Invoke("ExportReceiptAsync",scene.Actor,receipt,CancellationToken.None));
            receipt.SourceRestored=true;receipt.SnapshotAfter="before";
            await Invoke("ExportReceiptAsync",scene.Actor,receipt,CancellationToken.None);
            var stored=await new WorkflowStore(source,new RuleAdapter(),signer).GetExportRecordAsync(scene.Actor,plan.Id);
            Require(stored.Receipt.IfcHash==receipt.IfcHash&&stored.Plan.Hash==plan.Hash,"Export evidence did not survive store recreation.");
            await Reject(()=>workflow.GetExportRecordAsync(Guid.NewGuid(),plan.Id));
        });
    }
}
