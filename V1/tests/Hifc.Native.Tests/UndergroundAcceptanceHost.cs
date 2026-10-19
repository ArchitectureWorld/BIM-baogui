using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

// Creates a controlled underground fixture from an architectural template.
// All writes stay under the current acceptance run, never in a user model.
public sealed class UndergroundAcceptanceHost:IExternalApplication,IExternalEventHandler
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    UIControlledApplication application;ExternalEvent api;string run;Assembly plugin,core;
    readonly Dictionary<string,bool> checks=new Dictionary<string,bool>();
    static object Get(object o,string p)=>o.GetType().GetProperty(p,F).GetValue(o);
    static void Set(object o,string p,object v)=>o.GetType().GetProperty(p,F).SetValue(o,v);
    static object[] Items(object value)=>((IEnumerable)value).Cast<object>().ToArray();
    object Call(object o,string m,params object[] args)=>o.GetType().GetMethod(m,F).Invoke(o,args);
    object Static(string type,string method,params object[] args)=>plugin.GetType(type,true).GetMethod(method,F).Invoke(null,args);
    object New(string type,params object[] args)=>Activator.CreateInstance(plugin.GetType(type,true),F,null,args,null);
    object CoreJson(string type,object value)=>new JavaScriptSerializer().Deserialize(new JavaScriptSerializer().Serialize(value),core.GetType(type,true));
    void Write(string name,object value)=>File.WriteAllText(Path.Combine(run,name),new JavaScriptSerializer{MaxJsonLength=67108864}.Serialize(value));
    void Check(string name,bool value){checks[name]=value;File.AppendAllText(Path.Combine(run,"host.log"),DateTime.Now.ToString("O")+" "+name+" "+value+"\n");if(!value)throw new Exception(name);}
    static string Hash(string path){using(var h=SHA256.Create())using(var s=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
    static double Ft(double m)=>m/0.3048;
    static string Raw(Element e,string name)
    {
        var p=e.GetParameters(name).SingleOrDefault();if(p==null||!p.HasValue)return null;
        if(p.StorageType==StorageType.Double)return UnitUtils.ConvertFromInternalUnits(p.AsDouble(),p.Definition.UnitType==UnitType.UT_Area?DisplayUnitType.DUT_SQUARE_METERS:DisplayUnitType.DUT_METERS).ToString("0.######",CultureInfo.InvariantCulture);
        return p.AsString();
    }
    void BindTyped(Document doc,string name,ParameterType type,Category category)
    {
        var path=Path.Combine(run,"typed-source-"+Guid.NewGuid().ToString("N")+".txt");
        File.WriteAllText(path,"# Revit shared parameters\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\nGROUP\t1\tAcceptance\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\r\n",System.Text.Encoding.Unicode);
        var previous=doc.Application.SharedParametersFilename;
        try {doc.Application.SharedParametersFilename=path;var group=doc.Application.OpenSharedParameterFile().Groups.get_Item("Acceptance");
            var definition=group.Definitions.Create(new ExternalDefinitionCreationOptions(name,type){GUID=Guid.NewGuid(),Visible=true,UserModifiable=true,HideWhenNoValue=true});
            var categories=doc.Application.Create.NewCategorySet();categories.Insert(category);if(!doc.ParameterBindings.Insert(definition,doc.Application.Create.NewInstanceBinding(categories),BuiltInParameterGroup.PG_DATA))throw new Exception("Typed fixture source binding failed");
        }finally{doc.Application.SharedParametersFilename=previous;}
    }
    public Result OnStartup(UIControlledApplication app){run=Environment.GetEnvironmentVariable("HIFC_V1_UNDERGROUND_TEST_RUN");if(string.IsNullOrEmpty(run))return Result.Succeeded;application=app;app.Idling+=Start;return Result.Succeeded;}
    public Result OnShutdown(UIControlledApplication app){if(application!=null){app.Idling-=Start;app.Idling-=Exit;}api?.Dispose();return Result.Succeeded;}
    public string GetName()=>"HIFC underground acceptance";
    void Start(object sender,IdlingEventArgs e){application.Idling-=Start;api=ExternalEvent.Create(this);api.Raise();}
    void Exit(object sender,IdlingEventArgs args){var ui=(UIApplication)sender;var id=RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);if(ui.CanPostCommand(id)){application.Idling-=Exit;ui.PostCommand(id);}}
    Area CreateArea(Document doc,Level level,AreaScheme scheme,double x,double width,double height,string name)
    {
        var view=ViewPlan.CreateAreaPlan(doc,scheme.Id,level.Id);view.Name="地下验收-"+name;
        var z=level.Elevation;var sketch=SketchPlane.Create(doc,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,new XYZ(0,0,z)));
        var points=new[]{new XYZ(Ft(x),0,z),new XYZ(Ft(x+width),0,z),new XYZ(Ft(x+width),Ft(height),z),new XYZ(Ft(x),Ft(height),z)};
        for(var i=0;i<4;i++)doc.Create.NewAreaBoundaryLine(sketch,Line.CreateBound(points[i],points[(i+1)%4]),view);
        doc.Regenerate();var area=doc.Create.NewArea(view,new UV(Ft(x+width/2),Ft(height/2)));area.Name=name;return area;
    }
    object Snapshot(Document doc,object settings)
    {
        var scan=New("Hifc.Revit.Management.CaseScanJob",doc,"报规","单体建筑",settings);
        while(!(bool)Get(scan,"Complete"))Call(scan,"ReadNext",doc,500);
        var result=Call(scan,"Snapshot",doc);Check("full_scan_"+Guid.NewGuid().ToString("N").Substring(0,6),(bool)Get(result,"Complete")&&(string)Get(result,"Scope")=="全项目");return result;
    }
    void Assign(Document doc,Element[] areas,string slot)
    {
        var read=Static("Hifc.Revit.Management.SelectionService","Read",doc,areas.Select(a=>a.Id.IntegerValue).ToArray(),"报规","单体建筑");
        Check(slot+"_offered_for_area",Items(Get(read,"Slots")).Cast<string>().Contains(slot));
        var snapshot=Get(read,"Snapshot");var preview=Static("Hifc.Revit.Management.SelectionService","Classify",doc,snapshot,slot);
        var draft=New("Hifc.Revit.Management.SelectionDraft",snapshot);Call(draft,"SetClassification",slot,Get(preview,"ProposedElements"));
        foreach(var field in Items(Get(draft,"Fields")))
        {
            if(!(bool)Get(field,"Editable"))continue;
            var property=(string)Get(field,"Property");var type=(string)Get(field,"RawType");var value=(string)Get(field,"Value");
            if(property=="投影面积"||property=="名称")continue;
            if(string.IsNullOrEmpty(value)||value=="未填写")Set(field,"Value",type=="Boolean"?"否":type=="Integer"?(property=="车位数"?"19":"1"):type=="Real"?"1":"受控验收-"+slot);
        }
        var requests=Call(draft,"BuildRequests");Static("Hifc.Revit.Management.SelectionService","Write",doc,snapshot,requests);
        foreach(var area in areas)Check(slot+"_class_written_"+area.Id.IntegerValue,Raw(area,"类目")==slot);
        if(slot!="停车位")foreach(var area in areas.OfType<Area>())
        {
            var expected=UnitUtils.ConvertFromInternalUnits(area.Area,DisplayUnitType.DUT_SQUARE_METERS);
            Check(slot+"_area_m2_"+area.Id.IntegerValue,Math.Abs(double.Parse(Raw(area,slot+"投影面积"),CultureInfo.InvariantCulture)-expected)<1e-5);
        }
    }
    public void Execute(UIApplication ui)
    {
        Document doc=null;string source=null;string sourceHash=null;
        try
        {
            var installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","Revit","Addins","2020","Hifc.V1");
            plugin=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Hifc.V1.Revit");core=Assembly.LoadFrom(Path.Combine(installed,"Hifc.Reference.Core.dll"));
            Check("installed_044_loaded",plugin.GetName().Version.ToString()=="0.4.4.0");
            var template=Environment.GetEnvironmentVariable("HIFC_V1_UNDERGROUND_TEMPLATE");
            doc=ui.Application.NewProjectDocument(template);
            var areas=new List<Area>();Level b1,b2;int regionId;
            using(var tx=new Transaction(doc,"Controlled underground fixture"))
            {
                tx.Start();b1=Level.Create(doc,Ft(97.4));b1.Name="B1_地下验收";b2=Level.Create(doc,Ft(93.8));b2.Name="B2_地下验收";
                var structure=Level.Create(doc,Ft(93.7));structure.Name="结构底板_不计入地下层数";
                var ground=Level.Create(doc,Ft(101));ground.Name="地坪_地下验收";
                var scheme=new FilteredElementCollector(doc).OfClass(typeof(AreaScheme)).Cast<AreaScheme>().First();
                areas.Add(CreateArea(doc,b1,scheme,0,10,10,"人防区域A"));areas.Add(CreateArea(doc,b2,scheme,30,11,10,"人防区域B"));
                areas.Add(CreateArea(doc,b1,scheme,60,12,10,"消防分区A"));areas.Add(CreateArea(doc,b1,scheme,90,20,10,"停车场A"));areas.Add(CreateArea(doc,b1,scheme,120,4,10,"停车位A"));
                BindTyped(doc,"人防区域投影面积",ParameterType.Area,areas[0].Category);
                BindTyped(doc,"地下建筑高度",ParameterType.Length,doc.ProjectInformation.Category);
                var profile=new CurveArray();var points=new[]{new XYZ(Ft(150),0,b1.Elevation),new XYZ(Ft(170),0,b1.Elevation),new XYZ(Ft(170),Ft(15),b1.Elevation),new XYZ(Ft(150),Ft(15),b1.Elevation)};
                for(var i=0;i<4;i++)profile.Append(Line.CreateBound(points[i],points[(i+1)%4]));
                var floorType=new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First(t=>!t.IsFoundationSlab);
                regionId=doc.Create.NewFloor(profile,floorType,b1,false).Id.IntegerValue;
                tx.Commit();
            }
            var allAreas=areas.Select(a=>a.Id.IntegerValue).ToArray();
            source=Path.Combine(run,"models","underground-source.rvt");doc.SaveAs(source,new SaveAsOptions{OverwriteExistingFile=false});doc.Close(false);doc=null;sourceHash=Hash(source);
            var copy=Path.Combine(run,"models","underground-test.rvt");File.Copy(source,copy);doc=ui.Application.OpenDocumentFile(copy);
            areas=allAreas.Select(id=>(Area)doc.GetElement(new ElementId(id))).ToList();
            // The source document wrappers are invalid after Close; resolve by preserved ids.
            b1=(Level)doc.GetElement(areas[0].LevelId);b2=(Level)doc.GetElement(areas[1].LevelId);
            Assign(doc,areas.Take(2).ToArray(),"人防区域");Assign(doc,new[]{areas[2]},"消防分区");Assign(doc,new[]{areas[3]},"停车场");Assign(doc,new[]{areas[4]},"停车位");
            var region=doc.GetElement(new ElementId(regionId));Assign(doc,new[]{region},"建筑区域");
            var regionEntity=(string)Static("Hifc.Revit.RevitMappingService","Entity",doc,region);var regionArea=double.Parse(Raw(region,"建筑区域投影面积"),CultureInfo.InvariantCulture);
            Write("region-probe.json",new{entity=regionEntity,projected_area_m2=regionArea,nominal_rectangle_m2=300.0});
            Check("underground_building_region_keeps_slab_carrier",regionEntity=="IfcSlab"&&Math.Abs(regionArea-300.0)<0.001);
            var settings=CoreJson("Hifc.Reference.UndergroundSettings",new{Confirmed=true,GroundElevationM=101.0,LevelUniqueIds=new[]{b1.UniqueId,b2.UniqueId}});
            var snapshot=Snapshot(doc,settings);var preview=Static("Hifc.Revit.Management.CaseEdits","PreviewBatch",doc,snapshot,Array.CreateInstance(plugin.GetType("Hifc.Revit.Management.EditRequest"),0));
            Check("whole_project_preparation_has_no_conflicts",Items(Get(preview,"Conflicts")).Length==0);Static("Hifc.Revit.Management.CaseEdits","Apply",doc,preview);
            Check("confirmed_count_ignores_structure_and_positive_project_offset",Raw(doc.ProjectInformation,"地下建筑层数")=="2");
            Check("height_uses_explicit_ground_datum",Math.Abs(double.Parse(Raw(doc.ProjectInformation,"地下建筑高度"),CultureInfo.InvariantCulture)-7.2)<1e-6);
            Check("ground_datum_written_in_metres",Raw(doc.ProjectInformation,"地坪标高")=="101");
            var rerun=Snapshot(doc,settings);var projectRow=Items(Get(rerun,"Elements")).Single(r=>(string)Get(r,"Entity")=="IfcBuilding");
            Check("write_is_visible_in_new_full_snapshot",Items(Get(projectRow,"Fields")).Any(f=>(string)Get(f,"Property")=="地下建筑层数"&&(string)Get(f,"Value")=="2"));
            // Fill the remaining building metadata with labeled synthetic test values,
            // solely so the strict export audit can verify every emitted receipt row.
            using(var tx=new Transaction(doc,"Controlled metadata values for IFC audit"))
            {
                tx.Start();foreach(var field in Items(Get(projectRow,"Fields")))
                {
                    var parameter=doc.ProjectInformation.GetParameters((string)Get(field,"Source")).Single();if(parameter.HasValue&&(parameter.StorageType!=StorageType.String||!string.IsNullOrEmpty(parameter.AsString())))continue;
                    var type=(string)Get(field,"RawType");parameter.Set(type=="Boolean"?"0":type=="Integer"||type=="Real"?"1":"受控地下验收");
                }
                tx.Commit();
            }
            // Independent override must be preserved and reported as a difference.
            using(var tx=new Transaction(doc,"Controlled manual override")){tx.Start();doc.ProjectInformation.LookupParameter("地下建筑高度").Set(Ft(7.5));tx.Commit();}
            var changed=Snapshot(doc,settings);var height=Items(Get(Items(Get(changed,"Elements")).Single(r=>(string)Get(r,"Entity")=="IfcBuilding"),"Fields")).Single(f=>(string)Get(f,"Property")=="地下建筑高度");
            Check("manual_height_preserved_and_difference_reported",Raw(doc.ProjectInformation,"地下建筑高度")=="7.5"&&((string)Get(height,"SuggestionReason")).Contains("不同")&&string.IsNullOrEmpty((string)Get(height,"SuggestedValue")));
            var exportElements=areas.Cast<Element>().Concat(new[]{region,doc.ProjectInformation}).ToArray();
            var allBefore=new JavaScriptSerializer().Serialize(Static("Hifc.Revit.RevitMappingService","Capture",doc,exportElements));
            var receipt=Static("Hifc.Revit.NativeExportService","Export",doc,exportElements,Path.Combine(run,"underground.ifc"));
            Write("native-receipt-copy.json",receipt);Check("native_ifc_exists",File.Exists(Path.Combine(run,"underground.ifc")));
            Static("Hifc.Revit.NativeExportService","Export",doc,new Element[]{areas[4]},Path.Combine(run,"parking-only.ifc"));
            Static("Hifc.Revit.NativeExportService","Export",doc,new Element[]{areas[3]},Path.Combine(run,"carpark-only.ifc"));
            Check("all_selected_and_unselected_source_values_restored_after_three_exports",allBefore==new JavaScriptSerializer().Serialize(Static("Hifc.Revit.RevitMappingService","Capture",doc,exportElements)));
            var modelKey=(string)Static("Hifc.Revit.Management.ModelReader","Key",doc);
            var store=New("Hifc.Revit.Management.ManagementStateStore",Path.Combine((string)Static("Hifc.Revit.RuntimePaths","OutputDirectory"),"management-profiles"),modelKey);
            var state=Call(store,"Load");Set(state,"Stage","报规");Set(state,"Kind","单体建筑");Set(state,"Underground",settings);Call(store,"Save",state);
            doc.Save();doc.Close(false);doc=ui.Application.OpenDocumentFile(copy);
            Check("saved_reopened_areas_and_values_persist",allAreas.All(id=>!string.IsNullOrEmpty(Raw(doc.GetElement(new ElementId(id)),"类目")))&&Raw(doc.ProjectInformation,"地下建筑层数")=="2"&&Raw(doc.ProjectInformation,"地下建筑高度")=="7.5");
            Check("underground_confirmation_persists",(bool)Get(Get(Call(store,"Load"),"Underground"),"Confirmed"));
            Check("controlled_source_copy_unchanged",Hash(source)==sourceHash);
            Write("underground-host-result.json",new{all_passed=true,checks,version=plugin.GetName().Version.ToString(),assembly=plugin.Location,source_sha256=sourceHash,source,model=copy,areas=allAreas,
                scope="Controlled Revit 2020 underground model copy; native services, full scans, writes, native IFC and save/reopen; not a customer underground model or platform acceptance"});
        }
        catch(Exception ex)
        {
            while(ex is TargetInvocationException&&ex.InnerException!=null)ex=ex.InnerException;
            Write("underground-host-result.json",new{all_passed=false,error=ex.ToString(),checks});
        }
        finally{if(doc?.IsValidObject==true)doc.Close(false);application.Idling+=Exit;}
    }
}
