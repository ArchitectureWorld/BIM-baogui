using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Hifc.Reference;

internal static class ExportArtifactsTests
{
    static void Assert(bool value,string reason){if(!value)throw new Exception(reason);}
    static object Create(string destination,string records)
    {
        var type=typeof(ReferenceCatalog).Assembly.GetType("Hifc.Reference.ExportArtifacts");
        Assert(type!=null,"Export does not isolate internal artifacts");
        return type.GetMethod("Create").Invoke(null,new object[]{destination,records,Guid.NewGuid().ToString("D")});
    }
    static string Value(object item,string name)=>(string)item.GetType().GetProperty(name).GetValue(item);
    static void Publish(object item,string hash)
    {try{item.GetType().GetMethod("Publish").Invoke(item,new object[]{hash});}catch(TargetInvocationException e){throw e.InnerException;}}
    static void WithFolders(Action<string,string> test)
    {
        var root=Path.Combine(Path.GetTempPath(),"hifc-delivery-"+Guid.NewGuid().ToString("N"));
        var delivery=Path.Combine(root,"delivery");var records=Path.Combine(root,"records");Directory.CreateDirectory(delivery);
        try{test(delivery,records);}finally{Directory.Delete(root,true);}
    }
    internal static void Run(Action<string,Action> check)
    {
        check("delivery_contains_only_ifc_and_retains_internal_records",()=>WithFolders((delivery,records)=>{
            var output=Path.Combine(delivery,"OUT_学校_报规_综合楼_20261023_V1.ifc");var item=Create(output,records);
            File.WriteAllText(Value(item,"StagedIfcPath"),"native IFC bytes");File.WriteAllText(Value(item,"ConfigurationPath"),"mapping");
            File.WriteAllText(Value(item,"ReceiptPath"),"test receipt");File.WriteAllText(Path.Combine(Value(item,"DirectoryPath"),"binding-test.txt"),"binding");
            File.WriteAllText(Value(item,"StagedIfcPath")+".log","native log");
            Assert(!Directory.EnumerateFileSystemEntries(delivery).Any(),"Work files leaked before success");
            Publish(item,PlatformSyncHandoff.Sha256(Value(item,"StagedIfcPath")));
            Assert(Directory.GetFiles(delivery).SequenceEqual(new[]{output})&&Directory.GetDirectories(delivery).Length==0,"Sidecars leaked into delivery");
            Assert(File.ReadAllText(output)=="native IFC bytes"&&File.Exists(Value(item,"ReceiptPath"))&&File.Exists(Value(item,"ConfigurationPath")),"Delivery or evidence lost");
        }));
        check("incomplete_or_changed_export_never_publishes_ifc",()=>WithFolders((delivery,records)=>{
            var output=Path.Combine(delivery,"failed.ifc");var item=Create(output,records);var staged=Value(item,"StagedIfcPath");File.WriteAllText(staged,"before");
            var hash=PlatformSyncHandoff.Sha256(staged);bool blocked=false;
            try{Publish(item,hash);}catch(InvalidOperationException){blocked=true;}
            Assert(blocked&&!File.Exists(output),"Missing receipt published IFC");
            File.WriteAllText(Value(item,"ReceiptPath"),"test receipt");File.WriteAllText(staged,"changed");blocked=false;
            try{Publish(item,hash);}catch(InvalidOperationException){blocked=true;}
            Assert(blocked&&!File.Exists(output),"Changed IFC published");
        }));
        check("publication_collision_preserves_existing_ifc",()=>WithFolders((delivery,records)=>{
            var output=Path.Combine(delivery,"existing.ifc");var item=Create(output,records);
            File.WriteAllText(Value(item,"StagedIfcPath"),"new IFC");File.WriteAllText(Value(item,"ReceiptPath"),"test receipt");
            File.WriteAllText(output,"keep old IFC");bool blocked=false;
            try{Publish(item,PlatformSyncHandoff.Sha256(Value(item,"StagedIfcPath")));}catch(IOException){blocked=true;}
            Assert(blocked&&File.ReadAllText(output)=="keep old IFC","Existing output overwritten or removed");
        }));
    }
}
