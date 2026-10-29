using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;

// Optional, hash-pinned project inputs for the isolated copy acceptance host.
// This is not part of the installed add-in and never edits an IFC file.
public sealed partial class CustomerProjectAcceptanceHost
{
    public sealed class WorkbookInputFile
    {
        public string Schema { get; set; }
        public string WorkbookPath { get; set; }
        public string WorkbookSha256 { get; set; }
        public WorkbookModelInput[] Models { get; set; }
    }
    public sealed class WorkbookModelInput
    {
        public string Name { get; set; }
        public string SourceSha256 { get; set; }
        public string Kind { get; set; }
        public object Planning { get; set; }
        public object Underground { get; set; }
        public WorkbookField[] Values { get; set; }
        public WorkbookReference[] References { get; set; }
        public string[] UnclassifiedGeometryUniqueIds { get; set; }
    }
    public sealed class WorkbookField
    {
        public string UniqueId { get; set; }
        public string PropertySet { get; set; }
        public string Property { get; set; }
        public string Value { get; set; }
        public string Basis { get; set; }
    }
    public sealed class WorkbookReference
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Basis { get; set; }
    }
    WorkbookInputFile workbookInputs;
    void LoadWorkbookInputs(Dictionary<string,object> launch)
    {
        if(!launch.ContainsKey("workbook_input_manifest"))return;
        var path=Convert.ToString(launch["workbook_input_manifest"]);
        if(Hash(path)!=Convert.ToString(launch["workbook_input_sha256"]))throw new InvalidOperationException("Workbook input manifest hash changed.");
        workbookInputs=new JavaScriptSerializer().Deserialize<WorkbookInputFile>(File.ReadAllText(path));
        if(workbookInputs.Schema!="hifc.project-workbook-inputs/1" || Hash(workbookInputs.WorkbookPath)!=workbookInputs.WorkbookSha256)
            throw new InvalidOperationException("Workbook input schema or original workbook hash changed.");
        var inputs=Items(launch["inputs"]).Cast<Dictionary<string,object>>().ToArray();
        if(workbookInputs.Models.Length!=inputs.Length || workbookInputs.Models.Select(m=>m.Name).Distinct().Count()!=inputs.Length)
            throw new InvalidOperationException("Workbook model scope must match exactly.");
        foreach(var input in inputs)WorkbookModel(input);
    }
    WorkbookModelInput WorkbookModel(Dictionary<string,object> input)
    {
        if(workbookInputs==null)return null;
        var model=workbookInputs.Models.Single(m=>m.Name==(string)input["name"]);
        if(model.SourceSha256!=(string)input["sha256"])throw new InvalidOperationException("Workbook targets a different model version.");
        return model;
    }
    void ConfirmWorkbookScope(Document doc,object scan,string kind,WorkbookModelInput model)
    {
        if(kind!=model.Kind)throw new InvalidOperationException("Model kind does not match approved workbook scope.");
        var serializer=new JavaScriptSerializer();
        planningSettings=serializer.Deserialize(Json(model.Planning),core.GetType("Hifc.Reference.PlanningSettings",true));
        undergroundSettings=serializer.Deserialize(Json(model.Underground),core.GetType("Hifc.Reference.UndergroundSettings",true));
        if((string)Get(planningSettings,"RuleProfileId")!=ruleProfileId)throw new InvalidOperationException("Workbook rule profile changed.");
        var rows=Items(Get(scan,"Elements"));
        foreach(var role in Items(Get(planningSettings,"Roles"))) {
            var row=rows.Single(r=>(string)Get(r,"UniqueId")== (string)Get(role,"UniqueId"));
            var identity=core.GetType("Hifc.Reference.PlanningInputs").GetMethod("Identity").Invoke(null,new[]{row});
            if((string)identity!=(string)Get(role,"Identity"))throw new InvalidOperationException("Workbook carrier identity/geometry changed.");
        }
        foreach(var uid in (string[])Get(planningSettings,"BuildingLevelUniqueIds"))
            if(!(doc.GetElement(uid) is Level))throw new InvalidOperationException("Confirmed building level no longer exists.");
        var upper=(string)Get(planningSettings,"TopBoundaryLevelUniqueId");
        if(!string.IsNullOrEmpty(upper) && (!(doc.GetElement(upper) is Level) || ((string[])Get(planningSettings,"BuildingLevelUniqueIds")).Contains(upper)))
            throw new InvalidOperationException("Roof upper boundary must be separate from occupied building levels.");
        foreach(var uid in model.UnclassifiedGeometryUniqueIds??new string[0]) {
            var element=doc.GetElement(uid);
            if(element==null || !string.IsNullOrEmpty((string)Static("Hifc.Revit.RevitMappingService","Slot",doc,element)))
                throw new InvalidOperationException("Excluded roof geometry has an existing business classification; review instead of silently clearing it.");
        }
        var store=Store(doc);var state=Call(store,"Load");
        Set(state,"Stage","报规");Set(state,"Kind",kind);Set(state,"Planning",planningSettings);Set(state,"Underground",undergroundSettings);
        Call(store,"Save",state);
        var restored=Call(store,"Load");
        if(Json(Get(restored,"Planning"))!=Json(planningSettings)||Json(Get(restored,"Underground"))!=Json(undergroundSettings))
            throw new InvalidOperationException("Workbook context did not persist.");
        Static("Hifc.Revit.Management.PlatformContext","Set",doc,planningSettings);
        Static("Hifc.Revit.Management.PlatformContext","SetStage",doc,"报规");
        Write(doc.Title+"-workbook-confirmation.json",new{model,workbookInputs.WorkbookPath,workbookInputs.WorkbookSha256,context=ContextEvidence(doc)});
    }
    Array WorkbookRequests(Document doc,object scan,WorkbookModelInput model)
    {
        var requestType=plugin.GetType("Hifc.Revit.Management.EditRequest",true);
        if(model==null)return Array.CreateInstance(requestType,0);
        var rows=Items(Get(scan,"Elements"));
        var requests=Array.CreateInstance(requestType,model.Values.Length);
        for(int index=0;index<model.Values.Length;index++) {
            var item=model.Values[index];
            if(string.IsNullOrWhiteSpace(item.Value)||string.IsNullOrWhiteSpace(item.Basis))throw new InvalidOperationException("Workbook writes require a nonempty value and provenance.");
            var row=rows.Single(r=>(string)Get(r,"UniqueId")==item.UniqueId);
            var field=Items(Get(row,"Fields")).Single(f=>(string)Get(f,"PropertySet")==item.PropertySet && (string)Get(f,"Property")==item.Property && (string)Get(f,"Binding")=="实例");
            var request=New("Hifc.Revit.Management.EditRequest");
            Set(request,"Kind","赋值");Set(request,"Ids",new[]{(int)Get(row,"Id")});
            Set(request,"PropertySet",item.PropertySet);Set(request,"Source",Get(field,"Source"));Set(request,"Binding","实例");Set(request,"Value",item.Value);
            var baseline=New("Hifc.Revit.Management.EditBaseline");
            Set(baseline,"ElementId",Get(row,"Id"));Set(baseline,"ElementUniqueId",item.UniqueId);
            foreach(var key in new[]{"OwnerUniqueId","Value","State","SourceSignature"})Set(baseline,key,Get(field,key));
            var baselines=Array.CreateInstance(baseline.GetType(),1);baselines.SetValue(baseline,0);Set(request,"Baselines",baselines);
            requests.SetValue(request,index);
        }
        return requests;
    }
    void WriteWorkbookReferences(Document doc,WorkbookModelInput model)
    {
        // Keep every populated source cell readable in Project Information. These
        // prefixed names are reference data, not official Pset calculation inputs.
        var refs=model.References;
        if(refs.Select(r=>r.Name).Distinct().Count()!=refs.Length || refs.Any(r=>!r.Name.StartsWith("报规表参考_",StringComparison.Ordinal)||string.IsNullOrWhiteSpace(r.Value)))
            throw new InvalidOperationException("Invalid workbook reference fields.");
        var definitions=Array.CreateInstance(plugin.GetType("Hifc.Revit.ParameterDefinition",true),refs.Length);
        for(int i=0;i<refs.Length;i++) {
            var definition=New("Hifc.Revit.ParameterDefinition");definition.GetType().GetField("Name",F).SetValue(definition,refs[i].Name);
            definition.GetType().GetField("Guid",F).SetValue(definition,core.GetType("Hifc.Reference.ReferencePolicy").GetMethod("SourceGuid").Invoke(null,new object[]{refs[i].Name}));
            definitions.SetValue(definition,i);
        }
        using(var tx=new Transaction(doc,"记录报规属性表原值及来源")) {
            tx.Start();
            Static("Hifc.Revit.RevitMappingService","EnsureDefinitions",doc,new Element[]{doc.ProjectInformation},definitions,run,false);
            doc.Regenerate();
            foreach(var item in refs) {
                var p=doc.ProjectInformation.GetParameters(item.Name).Single();
                if(p.IsReadOnly||p.StorageType!=StorageType.String||!p.Set(item.Value)||p.AsString()!=item.Value)
                    throw new InvalidOperationException("Workbook reference write/readback failed: "+item.Name);
            }
            if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Workbook reference transaction did not commit.");
        }
        Write(doc.Title+"-workbook-reference-written.json",refs);
    }
    void VerifyWorkbookInputs(Document doc,object scan,WorkbookModelInput model)
    {
        var rows=Items(Get(scan,"Elements"));var evidence=new List<object>();
        foreach(var item in model.Values) {
            var row=rows.Single(r=>(string)Get(r,"UniqueId")==item.UniqueId);
            var field=Items(Get(row,"Fields")).Single(f=>(string)Get(f,"PropertySet")==item.PropertySet&&(string)Get(f,"Property")==item.Property);
            var actual=(string)Get(field,"Value");
            if(actual!=item.Value)throw new InvalidOperationException("Workbook actual readback mismatch: "+item.Property+" = "+actual+" expected "+item.Value);
            evidence.Add(new{item.UniqueId,item.PropertySet,item.Property,item.Value,item.Basis,actual,owner_id=Get(field,"OwnerId"),source=Get(field,"Source")});
        }
        foreach(var item in model.References) {
            var actual=doc.ProjectInformation.GetParameters(item.Name).Single().AsString();
            if(actual!=item.Value)throw new InvalidOperationException("Reopened workbook reference mismatch: "+item.Name);
            evidence.Add(new{item.Name,item.Value,item.Basis,actual,reference_only=true});
        }
        foreach(var uid in model.UnclassifiedGeometryUniqueIds??new string[0])
            if(!string.IsNullOrEmpty((string)Static("Hifc.Revit.RevitMappingService","Slot",doc,doc.GetElement(uid))))
                throw new InvalidOperationException("Roof geometry was incorrectly assigned to a building region.");
        Write(doc.Title+"-workbook-reopened-verification.json",evidence);
    }
}
