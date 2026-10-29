using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Expression = System.Linq.Expressions.Expression;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Xml.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Hifc.Reference;

// A disposable generated fixture only; never opens a customer original.
public sealed class PropertyDataAcceptanceHost:IExternalApplication,IExternalEventHandler
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    string run; ExternalEvent external; UIControlledApplication controlled; Assembly plugin;
    Document document;object manager,selection;string reportPath;int ticks;bool started,waitingManual;
    int aliasCarrierId;
    UIApplication continuationUi;bool continuationPending;
    DateTime manualDeadline;
    readonly Dictionary<string,bool> checks=new Dictionary<string,bool>();
    readonly List<object> classificationTargets=new List<object>();
    JavaScriptSerializer Json()=>new JavaScriptSerializer{MaxJsonLength=64*1024*1024};
    void Write(string file,object value)=>File.WriteAllText(Path.Combine(run,file),Json().Serialize(value));
    void Log(string message)=>File.AppendAllText(Path.Combine(run,"progress.log"),DateTime.Now.ToString("O")+" "+message+Environment.NewLine);
    void Check(string key,bool ok){checks[key]=ok;Write("checks.json",checks);if(!ok)throw new InvalidOperationException(key);}
    Type T(string name)=>plugin.GetType("Hifc.Revit."+name,true);
    static object Get(object o,string name)=>o.GetType().GetProperty(name,Flags)?.GetValue(o)??o.GetType().GetField(name,Flags)?.GetValue(o);
    static void Set(object o,string name,object value){var p=o.GetType().GetProperty(name,Flags);if(p!=null)p.SetValue(o,value);else o.GetType().GetField(name,Flags).SetValue(o,value);}
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
    object Static(string type,string name,params object[] args)=>T(type).GetMethod(name,Flags).Invoke(null,args);
    static object[] Items(object value)=>((IEnumerable)value).Cast<object>().ToArray();
    object New(string type,params object[] args)=>Activator.CreateInstance(T(type),Flags,null,args,CultureInfo.InvariantCulture);
    static Array Typed(Type type,IEnumerable<object> values){var a=values.ToArray();var result=Array.CreateInstance(type,a.Length);for(int i=0;i<a.Length;i++)result.SetValue(a[i],i);return result;}
    static Exception Root(Exception error){while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;return error;}
    public Result OnStartup(UIControlledApplication app)
    {
        run=Environment.GetEnvironmentVariable("HIFC_PROPERTY_DATA_RUN");
        if(string.IsNullOrEmpty(run))return Result.Succeeded;
        if(!Directory.Exists(run))return Result.Failed;
        // The host is copied away from the candidate DLLs. Resolve the exact
        // dependency before JIT compiles callbacks using its DTOs.
        try {
            var input=Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(run,"input.json")));
            Assembly.LoadFrom(Path.Combine((string)input["package"],"Hifc.Reference.Core.dll"));
            controlled=app;app.Idling+=Tick;return Result.Succeeded;
        }catch(Exception error){Write("startup-error.json",new{error=error.ToString()});return Result.Failed;}
    }
    public Result OnShutdown(UIControlledApplication app){app.Idling-=Tick;external?.Dispose();return Result.Succeeded;}
    public string GetName()=>"HIFC property data acceptance";
    void StopTick(){if(controlled!=null)controlled.Idling-=Tick;if(continuationUi!=null)continuationUi.Idling-=Tick;}
    void Fail(Exception error){error=Root(error);Log("FAIL "+error);Write("error.json",new{error=error.ToString(),checks});StopTick();}
    void Tick(object sender,IdlingEventArgs args)
    {
        var ui=sender as UIApplication;if(ui==null)return;
        try{
            if(!started){if(++ticks<3){args.SetRaiseWithoutDelay();return;}started=true;external=ExternalEvent.Create(this);external.Raise();return;}
            if(!waitingManual)return;
            if(DateTime.UtcNow>manualDeadline)throw new TimeoutException("Manual controller did not complete its full silent scan within two minutes.");
            if(manager==null){var managers=(IDictionary)T("Management.ManagerController").GetField("Windows",Flags).GetValue(null);if(!managers.Contains(document)){args.SetRaiseWithoutDelay();return;}manager=managers[document];}
            if(Get(manager,"scan")!=null){args.SetRaiseWithoutDelay();return;}
            var window=(Window)Get(manager,"Window");
            Check("manual_write_did_not_open_management_window",!window.IsVisible);
            Check("silent_check_is_full_project",(bool)Get(Get(window,"Snapshot"),"Complete")&&(string)Get(Get(window,"Snapshot"),"Scope")=="全项目");
            Check("silent_check_visited_unselected_carriers",((ModelElementCheck[])Get(Get(window,"Snapshot"),"Elements")).Count(r=>r.Entity=="IfcSlab"||r.Entity=="IfcBuildingElementProxy")>=3);
            Check("manual_tool_received_full_report",File.Exists((string)Get(selection,"fullReportPath")));
            reportPath=(string)Get(selection,"fullReportPath");StopTick();external.Raise();
        }catch(Exception error){Fail(error);}
    }
    public void Execute(UIApplication ui)
    {
        try{if(continuationPending){continuationPending=false;ContinueSavedFixture(ui);}else if(document==null)Run(ui);else Finish(ui);}catch(Exception error){Fail(error);}
    }
    // A failed test harness can finish in the same generated-model process.
    // The candidate plugin is never replaced. Queue work after the MCP caller's
    // outer transaction ends, and record the exact supplemental test assembly.
    public static string ResumeGeneratedFixture(Document active,string expectedHostSha256)
    {
        var host=new PropertyDataAcceptanceHost();host.run=Environment.GetEnvironmentVariable("HIFC_PROPERTY_DATA_RUN");
        if(string.IsNullOrEmpty(host.run)||!Path.GetFullPath(active.PathName).StartsWith(Path.GetFullPath(Path.Combine(host.run,"models"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Continuation is restricted to this run's generated fixture.");
        var assembly=typeof(PropertyDataAcceptanceHost).Assembly.Location;
        if(Hash(assembly)!=expectedHostSha256)throw new InvalidOperationException("Continuation assembly hash differs.");
        foreach(var check in host.Json().Deserialize<Dictionary<string,bool>>(File.ReadAllText(Path.Combine(host.run,"checks.json"))))host.checks.Add(check.Key,check.Value);
        if(host.checks.Values.Any(v=>!v)||!host.checks.ContainsKey("workbook_meters_correct_with_millimeter_project_display"))throw new InvalidOperationException("Earlier acceptance did not reach the saved-workbook boundary.");
        host.plugin=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Hifc.V1.Revit");host.document=active;
        var previousError=Path.Combine(host.run,"error.json");if(File.Exists(previousError))File.Move(previousError,Path.Combine(host.run,"error-before-continuation-"+DateTime.Now.ToString("HHmmss")+".json"));
        host.Write("continuation.json",new{assembly,sha256=Hash(assembly),expected_sha256=expectedHostSha256,plugin_sha256=Hash(host.plugin.Location),core_sha256=Hash(typeof(BooleanPolicy).Assembly.Location),document=active.PathName,previous_checks=host.checks.Count});
        host.started=true;host.continuationPending=true;host.continuationUi=new UIApplication(active.Application);
        host.external=ExternalEvent.Create(host);host.continuationUi.Idling+=host.Tick;host.external.Raise();return "Same-process fixture continuation queued.";
    }
    void ContinueSavedFixture(UIApplication ui)
    {
        Log("same-process saved-workbook continuation");
        Check("continuation_has_no_customer_documents",ui.Application.Documents.Cast<Document>().All(d=>!d.IsFamilyDocument&&Path.GetFullPath(d.PathName).StartsWith(Path.GetFullPath(Path.Combine(run,"models"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)));
        var first=new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().Single(l=>l.Name=="一层");
        var floors=new FilteredElementCollector(document).OfClass(typeof(Floor)).Cast<Floor>().ToArray();
        var region=floors.Single(f=>f.GetParameters("类目").SingleOrDefault()?.AsString()=="建筑区域");
        aliasCarrierId=floors.Single(f=>f.GetParameters("类目").SingleOrDefault()?.AsString()=="总平构件").Id.IntegerValue;
        var settings=(PlanningSettings)Static("Management.PlatformContext","Settings",document);
        var heightSource=CaseExportPolicy.SourceProperties(PlatformRules.ProfileId).Single(p=>!p.NativeGroup&&p.Entity=="IfcBuilding"&&p.Property=="规划高度").SourceParameter;
        var input=Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(run,"input.json")));
        if(input.TryGetValue("classification_inputs",out var classificationFlag)&&Convert.ToBoolean(classificationFlag))ClassificationAcceptance(first);
        FinishDataChecks(ui,first,region,settings,heightSource,document.PathName);
    }
    object Scan(string kind)
    {
        var settings=Static("Management.PlatformContext","Settings",document);
        var job=New("Management.CaseScanJob",document,"报规",kind,new UndergroundSettings(),settings);
        while(!(bool)Get(job,"Complete"))Call(job,"ReadNext",document,500);
        return Call(job,"Snapshot",document);
    }
    Parameter Legacy(Document doc,string source,BuiltInCategory category,ParameterType kind=ParameterType.Text)
    {
        var path=Path.Combine(run,"legacy-"+Guid.NewGuid().ToString("N")+".txt");
        File.WriteAllText(path,"# Revit shared parameter fixture\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\nGROUP\t1\tFixture\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\r\n",System.Text.Encoding.Unicode);
        var old=doc.Application.SharedParametersFilename;try{
            doc.Application.SharedParametersFilename=path;var file=doc.Application.OpenSharedParameterFile();var group=file.Groups.get_Item("Fixture")??file.Groups.Create("Fixture");
            var definition=group.Definitions.Create(new ExternalDefinitionCreationOptions(source,kind){GUID=ReferencePolicy.SourceGuid(source),Visible=true,UserModifiable=true});
            var categories=doc.Application.Create.NewCategorySet();categories.Insert(Category.GetCategory(doc,category));
            if(!doc.ParameterBindings.Insert(definition,doc.Application.Create.NewInstanceBinding(categories),BuiltInParameterGroup.PG_DATA))throw new InvalidOperationException("fixture binding failed");doc.Regenerate();
            return new FilteredElementCollector(doc).OfCategory(category).WhereElementIsNotElementType().First().GetParameters(source).Single();
        }finally{doc.Application.SharedParametersFilename=old;}
    }
    static Floor Rectangle(Document doc,FloorType type,Level level,double dx=0)
    {
        var z=level.Elevation;
        var points=new[]{new XYZ(dx,0,z),new XYZ(dx+10/.3048,0,z),new XYZ(dx+10/.3048,10/.3048,z),new XYZ(dx,10/.3048,z)};
        var curves=new CurveArray();for(int i=0;i<4;i++)curves.Append(Line.CreateBound(points[i],points[(i+1)%4]));
        var floor=doc.Create.NewFloor(curves,type,level,false);
        floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(0);
        return floor;
    }
    void Run(UIApplication ui)
    {
        Log("fixture start");Check("fresh_host_without_customer_documents",!ui.Application.Documents.Cast<Document>().Any());
        plugin=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Hifc.V1.Revit");
        var input=Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(run,"input.json")));
        Check("candidate_assembly_isolated",Path.GetFullPath(plugin.Location).StartsWith(Path.GetFullPath((string)input["package"])+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
        var core=typeof(BooleanPolicy).Assembly;
        Check("candidate_core_assembly_isolated",Path.GetFullPath(core.Location).StartsWith(Path.GetFullPath((string)input["package"])+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
        Write("loaded.json",new{plugin=plugin.Location,version=plugin.GetName().Version.ToString(),sha256=Hash(plugin.Location),core=core.Location,core_sha256=Hash(core.Location),host_sha256=Hash(GetType().Assembly.Location)});
        document=ui.Application.NewProjectDocument((string)input["template"]);
        Level first,second,third,lower;Floor land,control,region;string regionBoolean;
        var heightSource=CaseExportPolicy.SourceProperties(PlatformRules.ProfileId).Single(p=>!p.NativeGroup&&p.Entity=="IfcBuilding"&&p.Property=="规划高度").SourceParameter;
        Log("resolved native fixture contract");
        using(var tx=new Transaction(document,"Generate isolated fixture")){tx.Start();
            var levels=new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().OrderBy(l=>l.Elevation).ToArray();
            foreach(var l in levels)l.Name="模板标高_"+l.Id.IntegerValue;
            first=levels.First();first.Name="一层";first.Elevation=0;
            second=Level.Create(document,4/.3048);second.Name="二层";third=Level.Create(document,8/.3048);third.Name="三层";lower=Level.Create(document,-1/.3048);lower.Name="控制红线载体";
            document.ProjectInformation.BuildingName="验收单体";
            var floorType=new FilteredElementCollector(document).OfClass(typeof(FloorType)).Cast<FloorType>().First(t=>!t.IsFoundationSlab);
            land=Rectangle(document,floorType,first);control=Rectangle(document,floorType,lower);region=Rectangle(document,floorType,first,20/.3048);
            Legacy(document,"建筑基底",BuiltInCategory.OST_Levels);first.GetParameters("建筑基底").Single().Set("0");second.GetParameters("建筑基底").Single().Set("");third.GetParameters("建筑基底").Single().Set("是");
            regionBoolean=CaseExportPolicy.SourceProperties(PlatformRules.ProfileId).First(p=>!p.NativeGroup&&p.ExportType=="Boolean"&&p.SourceParameter!="建筑基底"&&BooleanPolicy.IsSource(p.SourceParameter,PlatformRules.ProfileId)).SourceParameter;
            Legacy(document,regionBoolean,BuiltInCategory.OST_Floors);land.GetParameters(regionBoolean).Single().Set("是");control.GetParameters(regionBoolean).Single().Set("否");
            tx.Commit();}
        var firstId=first.UniqueId;var secondId=second.UniqueId;var thirdId=third.UniqueId;var landId=land.UniqueId;var controlId=control.UniqueId;var regionId=region.UniqueId;
        var path=Path.Combine(run,"models","3D_验收项目_综合楼.rvt");document.SaveAs(path,new SaveAsOptions{OverwriteExistingFile=false});document.Close(false);document=ui.OpenAndActivateDocument(path).Document;
        first=(Level)document.GetElement(firstId);second=(Level)document.GetElement(secondId);third=(Level)document.GetElement(thirdId);
        land=(Floor)document.GetElement(landId);control=(Floor)document.GetElement(controlId);region=(Floor)document.GetElement(regionId);
        // Only the modeled floor is a business storey. The other levels remain
        // unselected owners proving that migration preserves the whole binding.
        var settings=(PlanningSettings)Static("Management.PlatformContext","Settings",document);settings.RuleProfileId=PlatformRules.ProfileId;settings.LevelsConfirmed=true;settings.BuildingLevelUniqueIds=new[]{first.UniqueId};
        Static("Management.PlatformContext","Set",document,settings);Static("Management.PlatformContext","SetStage",document,"报规");
        using(var tx=new Transaction(document,"Invalid bool preflight fixture")){tx.Start();second.GetParameters("建筑基底").Single().Set("未核定");tx.Commit();}
        bool rejected=false;try{Static("BooleanParameterService","Inspect",document,"建筑基底");}catch(Exception error){rejected=Root(error) is InvalidOperationException;}
        Check("invalid_legacy_boolean_blocks_without_changes",rejected&&first.GetParameters("建筑基底").Single().StorageType==StorageType.String);
        using(var tx=new Transaction(document,"Restore empty legacy bool")){tx.Start();second.GetParameters("建筑基底").Single().Set("");tx.Commit();}
        var unsafeChange=New("Management.EditChange");Set(unsafeChange,"Id",first.Id.IntegerValue);Set(unsafeChange,"OwnerUniqueId",first.UniqueId);Set(unsafeChange,"Source","建筑基底");Set(unsafeChange,"Action","赋值");Set(unsafeChange,"ExpectedRaw","invalid");
        var changes=Typed(T("Management.EditChange"),new[]{unsafeChange});var preview=New("Management.EditPreview");Set(preview,"DocumentKey",Static("Management.ModelReader","Key",document));Set(preview,"Changes",changes);Set(preview,"SnapshotHash",Static("Management.CaseEdits","Fingerprint",document,changes));
        rejected=false;try{Static("Management.CaseEdits","Apply",document,preview);}catch(Exception error){rejected=Root(error) is FormatException;}
        Check("migration_and_write_rollback_are_atomic",rejected&&first.GetParameters("建筑基底").Single().StorageType==StorageType.String&&first.GetParameters("建筑基底").Single().AsString()=="0"&&third.GetParameters("建筑基底").Single().AsString()=="是");
        using(var tx=new Transaction(document,"First-floor empty default probe")){tx.Start();first.GetParameters("建筑基底").Single().Set("");Static("BooleanParameterService","Migrate",document,"建筑基底",run);
            Check("empty_first_floor_footprint_becomes_native_true",first.GetParameters("建筑基底").Single().Definition.ParameterType==ParameterType.YesNo&&first.GetParameters("建筑基底").Single().AsInteger()==1);tx.RollBack();}
        ViewSchedule schedule;ScheduleFieldId scheduleField;
        using(var tx=new Transaction(document,"Migration reference fixture")){tx.Start();schedule=ViewSchedule.CreateSchedule(document,new ElementId(BuiltInCategory.OST_Floors));
            var id=land.GetParameters(regionBoolean).Single().Id;var field=schedule.Definition.GetSchedulableFields().Single(f=>f.ParameterId==id);scheduleField=schedule.Definition.AddField(field).FieldId;tx.Commit();}
        rejected=false;try{Static("BooleanParameterService","Inspect",document,regionBoolean);}catch(Exception error){rejected=Root(error).Message.Contains("明细表");}
        Check("schedule_reference_blocks_definition_replacement",rejected&&land.GetParameters(regionBoolean).Single().StorageType==StorageType.String);
        using(var tx=new Transaction(document,"Migrate captured legacy values")){tx.Start();schedule.Definition.RemoveField(scheduleField);
            Static("BooleanParameterService","Migrate",document,"建筑基底",run);Static("BooleanParameterService","Migrate",document,regionBoolean,run);tx.Commit();}
        Check("native_yesno_preserves_explicit_first_floor_false",first.GetParameters("建筑基底").Single().Definition.ParameterType==ParameterType.YesNo&&first.GetParameters("建筑基底").Single().AsInteger()==0);
        Check("empty_boolean_is_false_and_legacy_true_preserved",second.GetParameters("建筑基底").Single().AsInteger()==0&&third.GetParameters("建筑基底").Single().AsInteger()==1);
        Check("unselected_category_values_preserved",land.GetParameters(regionBoolean).Single().AsInteger()==1&&control.GetParameters(regionBoolean).Single().AsInteger()==0);
        AliasAcceptance(first,region);
        var foreignSource=CaseExportPolicy.SourceProperties(PlatformRules.ProfileId).Where(p=>p.ExportType=="Boolean"&&!p.NativeGroup&&p.SourceParameter!="建筑基底"&&p.SourceParameter!=regionBoolean&&BooleanPolicy.IsSource(p.SourceParameter,PlatformRules.ProfileId))
            .Select(p=>p.SourceParameter).First(name=>!new FilteredElementCollector(document).OfClass(typeof(ParameterElement)).Any(p=>p.Name==name));
        var familyDocument=ui.Application.NewFamilyDocument("C:\\ProgramData\\Autodesk\\RVT 2020\\Family Templates\\Chinese\\公制常规模型.rft");
        using(var tx=new Transaction(familyDocument,"Foreign text source fixture")){tx.Start();familyDocument.FamilyManager.NewType("非共享文字型");var p=familyDocument.FamilyManager.AddParameter(foreignSource,BuiltInParameterGroup.PG_DATA,ParameterType.Text,true);familyDocument.FamilyManager.Set(p,"是");tx.Commit();}
        var legacyDensityFile=Directory.GetFiles(run,"legacy-*.txt").Single(file=>File.ReadAllText(file).Contains("\t建筑密度\t"));
        var previousSharedFile=ui.Application.SharedParametersFilename;
        try {
            ui.Application.SharedParametersFilename=legacyDensityFile;
            var legacyDefinition=ui.Application.OpenSharedParameterFile().Groups.get_Item("Fixture").Definitions.get_Item("建筑密度") as ExternalDefinition;
            using(var tx=new Transaction(familyDocument,"Shared family source spoof fixture")){tx.Start();var p=familyDocument.FamilyManager.AddParameter(legacyDefinition,BuiltInParameterGroup.PG_DATA,true);familyDocument.FamilyManager.Set(p,"1");tx.Commit();}
        }finally{ui.Application.SharedParametersFilename=previousSharedFile;}
        familyDocument.SaveAs(Path.Combine(run,"foreign-source.rfa"));var family=familyDocument.LoadFamily(document);familyDocument.Close(false);FamilyInstance foreign;
        using(var tx=new Transaction(document,"Place foreign source fixture")){tx.Start();var symbol=(FamilySymbol)document.GetElement(family.GetFamilySymbolIds().First());symbol.Activate();document.Regenerate();foreign=document.Create.NewFamilyInstance(new XYZ(60/.3048,0,0),symbol,Autodesk.Revit.DB.Structure.StructuralType.NonStructural);tx.Commit();}
        rejected=false;try{Static("BooleanParameterService","InspectOwner",document,foreign,foreignSource);}catch(Exception error){rejected=Root(error) is InvalidOperationException;}
        Check("foreign_unshared_family_text_source_is_blocked",rejected&&foreign.GetParameters(foreignSource).Single().StorageType==StorageType.String&&foreign.GetParameters(foreignSource).Single().AsString()=="是");
        rejected=false;try{Static("BooleanAliasService","Inspect",document,"总平构件建筑密度",new Element[]{foreign});}catch(Exception error){rejected=Root(error) is InvalidOperationException;}
        Check("legacy_guid_in_family_cannot_spoof_project_binding",rejected&&foreign.GetParameters("建筑密度").Single().AsString()=="1"&&foreign.GetParameters("总平构件建筑密度").Count==0);
        using(var tx=new Transaction(document,"Mixed owned and foreign boolean fixture")){tx.Start();Legacy(document,foreignSource,BuiltInCategory.OST_Floors);tx.Commit();}
        var migrationChanges=Activator.CreateInstance(typeof(List<>).MakeGenericType(T("Management.EditChange")));
        var mixedField=new FieldCheck {RawType="Boolean",State="有效",Source=foreignSource,Value="0"};
        Static("Management.CaseEdits","AddBooleanMigration",document,land,mixedField,migrationChanges);rejected=false;
        try{Static("Management.CaseEdits","AddBooleanMigration",document,foreign,mixedField,migrationChanges);}catch(Exception error){rejected=Root(error) is InvalidOperationException;}
        Check("migration_dedup_does_not_hide_foreign_owner",Items(migrationChanges).Length==1&&rejected&&foreign.GetParameters(foreignSource).Single().AsString()=="是");
        using(var tx=new Transaction(document,"Classify real fixture carriers")){tx.Start();
            var units=document.GetUnits();units.SetFormatOptions(UnitType.UT_Length,new FormatOptions(DisplayUnitType.DUT_MILLIMETERS));units.SetFormatOptions(UnitType.UT_Area,new FormatOptions(DisplayUnitType.DUT_SQUARE_FEET));document.SetUnits(units);
            Legacy(document,"楼层面积",BuiltInCategory.OST_Levels,ParameterType.Area);first.GetParameters("楼层面积").Single().Set(100/.3048/.3048);
            Legacy(document,heightSource,BuiltInCategory.OST_ProjectInformation,ParameterType.Length);document.ProjectInformation.GetParameters(heightSource).Single().Set(100/.3048);
            Static("RevitMappingService","Prepare",document,new Element[]{land},"规划净用地",run);Static("RevitMappingService","Prepare",document,new Element[]{control},"规划控制线",run);Static("RevitMappingService","Prepare",document,new Element[]{region},"建筑区域",run);
            control.GetParameters("规划控制线名称").Single().Set("净用地控制红线");control.GetParameters("红线类型").Single().Set("净用地控制红线");
            using(var position=new ProjectPosition(1000/.3048,2000/.3048,0,Math.PI/6))document.ActiveProjectLocation.SetProjectPosition(XYZ.Zero,position);tx.Commit();}
        var manual=new RedlineData {Accepted=true,Unit=RedlineUnit.Meters,Frame=RedlineFrame.Internal,Axes=RedlineAxes.XEastYNorth,Points=new[]{new RedlinePoint{Sequence=1,X=0,Y=0},new RedlinePoint{Sequence=2,X=10,Y=0},new RedlinePoint{Sequence=3,X=10,Y=10},new RedlinePoint{Sequence=4,X=0,Y=10}}};
        var pair=(RedlineReport)Static("Management.RedlineGeometry","Compare",document,manual,.001,.001);Write("redline-pair.json",pair);
        Check("same_horizontal_contour_separate_solids_pass",pair.Pair.Passed&&pair.Manual.Passed);
        var probe=new XYZ(7/.3048,3/.3048,0);var converted=(RedlinePoint)Static("Management.RedlineGeometry","ConvertInternal",document,probe,RedlineFrame.Shared,RedlineAxes.XEastYNorth,1);
        // Expected coordinates are an independent point transform, not a roundtrip
        // through the conversion under test.
        var transform=document.ActiveProjectLocation.GetTotalTransform();var shared=transform.Inverse.OfPoint(probe);
        Check("shared_translation_rotation_nonorigin_control_point",Math.Abs(converted.X-shared.X*.3048)<1e-7&&Math.Abs(converted.Y-shared.Y*.3048)<1e-7);
        using(var tx=new Transaction(document,"Collision fixture - rollback only")){
            tx.Start();ElementTransformUtils.MoveElement(document,control.Id,new XYZ(0,0,1/.3048));document.Regenerate();
            var collision=(RedlineReport)Static("Management.RedlineGeometry","Compare",document,manual,.001,.001);Check("actual_solid_collision_rejected",!collision.Pair.Passed);tx.RollBack();
        }
        Static("Management.RedlineProjectStorage","Save",document,manual);
        var workbook=Path.Combine(run,"单体报规资料.xlsx");ReportingWorkbookTemplate.Write(workbook,new[]{"验收单体"},false);FillWorkbook(workbook,"一层","123.45");
        var snapshot=Scan("单体建筑");var rows=(ModelElementCheck[])Get(snapshot,"Elements");var source=ReportingWorkbookReader.Read(workbook);var plan=ReportingWorkbookPlanner.Build(source.Tables.Single(),rows,"单体建筑",document.ProjectInformation.UniqueId);
        foreach(var change in plan.Changes)change.Selected=change.CanSelect;var decision=ReportingWorkbookPlanner.Decide(plan);Write("workbook-plan.json",plan);
        Check("real_xlsx_floor_and_usage_resolve_to_live_targets",decision.Changes.Any(c=>c.Property=="楼层面积"&&c.Value=="123.45")&&decision.Changes.Any(c=>c.Property=="建筑用途名称"&&c.Value=="中学"));
        var requests=decision.Changes.Select(c=>{var r=New("Management.EditRequest");Set(r,"Kind","赋值");Set(r,"Ids",new[]{c.ElementId});Set(r,"PropertySet",c.PropertySet);Set(r,"Source",c.Source);Set(r,"Binding",c.Binding);Set(r,"Value",c.Value);
            Set(r,"ValueUnit",c.ValueUnit);var b=New("Management.EditBaseline");Set(b,"ElementId",c.ElementId);Set(b,"ElementUniqueId",c.ElementUniqueId);Set(b,"OwnerUniqueId",c.OwnerUniqueId);Set(b,"Value",c.CurrentValue);Set(b,"State",c.CurrentState);Set(b,"SourceSignature",c.SourceSignature);Set(r,"Baselines",Typed(T("Management.EditBaseline"),new[]{b}));return r;}).ToArray();
        var combined=Static("Management.CaseEdits","PreviewBatch",document,snapshot,Typed(T("Management.EditRequest"),requests));Write("workbook-write-preview.json",combined);
        Static("Management.CaseEdits","ApplyWithWorkbook",document,combined,new[]{decision});
        Check("workbook_official_values_and_references_written_together",((Array)Static("Management.ReportingWorkbookProjectStorage","Load",document)).Length==1&&Math.Abs(first.GetParameters("楼层面积").Single().AsDouble()*.3048*.3048-123.45)<1e-6);
        Check("workbook_meters_correct_with_millimeter_project_display",Math.Abs(document.ProjectInformation.GetParameters(heightSource).Single().AsDouble()*.3048-3)<1e-7);
        if(input.TryGetValue("classification_inputs",out var classificationFlag)&&Convert.ToBoolean(classificationFlag))ClassificationAcceptance(first);
        FinishDataChecks(ui,first,region,settings,heightSource,path);
    }
    void ClassificationAcceptance(Level first)
    {
        var snapshot=Scan("单体建筑");var rows=(ModelElementCheck[])Get(snapshot,"Elements");
        var civil=document.ProjectInformation.GetParameters("民用建筑").Single().AsInteger();var area=first.GetParameters("楼层面积").Single().AsDouble();
        var inputs=new[]{new[]{"建筑技术","建筑类型名称","自定义校园建筑"},new[]{"建筑技术","建筑类别","自定义教学配套"},new[]{"建筑技术","建筑用途名称","自定义教学用途"},
            new[]{"建筑区域","主功能类别","自定义主功能"},new[]{"建筑区域","子功能类别","自定义子功能"}};
        var requests=inputs.Select(input=>{
            var row=rows.Single(r=>r.Slots.Contains(input[0]));var field=row.Fields.Single(f=>f.Property==input[1]&&f.Binding=="实例");
            var request=New("Management.EditRequest");Set(request,"Kind","赋值");Set(request,"Ids",new[]{row.Id});Set(request,"PropertySet",field.PropertySet);Set(request,"Source",field.Source);Set(request,"Binding",field.Binding);Set(request,"Value",input[2]);
            var baseline=New("Management.EditBaseline");Set(baseline,"ElementId",row.Id);Set(baseline,"ElementUniqueId",row.UniqueId);Set(baseline,"OwnerUniqueId",field.OwnerUniqueId);Set(baseline,"Value",field.Value);Set(baseline,"State",field.State);Set(baseline,"SourceSignature",field.SourceSignature);
            Set(request,"Baselines",Typed(T("Management.EditBaseline"),new[]{baseline}));
            classificationTargets.Add(new{element_id=row.Id,owner_id=field.OwnerId,owner_unique_id=field.OwnerUniqueId,property_set=field.PropertySet,property=field.Property,source=field.Source,value=input[2],ifc_type="IfcLabel"});return request;
        }).ToArray();
        var preview=Static("Management.CaseEdits","PreviewBatch",document,snapshot,Typed(T("Management.EditRequest"),requests));
        Static("Management.CaseEdits","Apply",document,preview);
        Check("custom_classification_values_written_by_production_pipeline",classificationTargets.All(t=>document.GetElement(new ElementId((int)Get(t,"owner_id"))).GetParameters((string)Get(t,"source")).Single().AsString()==(string)Get(t,"value")));
        Check("custom_classification_keeps_civil_participation_and_manual_area",document.ProjectInformation.GetParameters("民用建筑").Single().AsInteger()==civil&&first.GetParameters("楼层面积").Single().AsDouble()==area);
        Write("classification-targets.json",classificationTargets);
    }
    void FinishDataChecks(UIApplication ui,Level first,Floor region,PlanningSettings settings,string heightSource,string path)
    {
        document.Save();var reopenedPath=Path.Combine(run,"models","保存重开核验.rvt");File.Copy(path,reopenedPath,true);
        Check("reopen_copy_matches_saved_fixture",Hash(path)==Hash(reopenedPath));
        // Revit cannot close the active UI document through Document.Close.
        // Reopen the exact saved bytes independently, retaining the active
        // fixture for the following production modeless-controller checks.
        var reopened=ui.Application.OpenDocumentFile(reopenedPath);
        try {
            var reopenedFirst=(Level)reopened.GetElement(first.UniqueId);
            Check("native_boolean_and_manual_floor_area_survive_reopen",reopenedFirst.GetParameters("建筑基底").Single().Definition.ParameterType==ParameterType.YesNo&&reopenedFirst.GetParameters("建筑基底").Single().AsInteger()==0&&Math.Abs(reopenedFirst.GetParameters("楼层面积").Single().AsDouble()*.3048*.3048-123.45)<1e-6);
            Check("workbook_and_manual_redline_metadata_survive_reopen",((Array)Static("Management.ReportingWorkbookProjectStorage","Load",reopened)).Length==1&&((RedlineData)Static("Management.RedlineProjectStorage","Load",reopened)).Points.Length==4);
            if(classificationTargets.Count>0)Check("custom_classification_values_survive_reopen",classificationTargets.All(t=>reopened.GetElement((string)Get(t,"owner_unique_id")).GetParameters((string)Get(t,"source")).Single().AsString()==(string)Get(t,"value")));
        }finally{reopened.Close(false);}
        Static("Management.PlatformContext","Set",document,settings);Static("Management.PlatformContext","SetStage",document,"报规");
        var ifc=Path.Combine(run,"OUT_验收单体.ifc");var exportedScope=Static("NativeExportService","ExportProject",document,ifc);var receipt=(string)Get(exportedScope,"ReceiptPath");Check("full_project_ifc_export_completed",File.Exists(ifc)&&File.Exists(receipt));
        Write("export-targets.json",new{ifc,receipt,first_level_id=first.Id.IntegerValue,first_level_unique_id=first.UniqueId,region_id=region.Id.IntegerValue,region_unique_id=region.UniqueId,expected_floor_area=123.45,expected_region_area=123.45,expected_building_height=3,alias_carrier_id=aliasCarrierId,classification_targets=classificationTargets});
        // Exercise the actual manual-controller write, not only its UI abstraction.
        ui.ActiveUIDocument.Selection.SetElementIds(new[]{region.Id});Static("Management.SelectionController","Open",ui);
        var windows=(IDictionary)T("Management.SelectionController").GetField("Windows",Flags).GetValue(null);var controller=windows[document];selection=Get(controller,"Window");
        Call(selection,"InitializeContext","报规","单体建筑");
        // Queue read/classification/write using production methods; the manual
        // controller's ExternalEvent will run after this test event returns.
        var result=Static("Management.SelectionService","Read",document,new[]{region.Id.IntegerValue},"报规","单体建筑");var selectedSnapshot=Get(result,"Snapshot");
        var classify=Static("Management.SelectionService","Classify",document,selectedSnapshot,"建筑区域");Call(selection,"Accept",result);Call(selection,"ShowClassification","建筑区域",classify,Array.CreateInstance(T("Management.ManualWrite"),0));
        Set(controller,"snapshot",selectedSnapshot);Set(controller,"readEpoch",Get(controller,"epoch"));
        var draft=Get(selection,"Draft");Call(controller,"Write",Call(draft,"BuildRequests"));
        waitingManual=true;manualDeadline=DateTime.UtcNow.AddMinutes(2);Log("waiting for production manual write and full silent scan");
    }
    void AliasAcceptance(Level level,Floor unrelated)
    {
        const string source="总平构件建筑密度",legacy="建筑密度";
        Log("mixed logical source copy acceptance");
        using(var tx=new Transaction(document,"Generate mixed logical source fixture")) {
            tx.Start();var type=new FilteredElementCollector(document).OfClass(typeof(FloorType)).Cast<FloorType>().First(t=>!t.IsFoundationSlab);
            var general=Rectangle(document,type,level,40/.3048);aliasCarrierId=general.Id.IntegerValue;
            Legacy(document,legacy,BuiltInCategory.OST_Floors);
            var old=new FilteredElementCollector(document).OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>().Single(p=>p.Name==legacy);
            var categories=document.Application.Create.NewCategorySet();categories.Insert(Category.GetCategory(document,BuiltInCategory.OST_Floors));categories.Insert(document.ProjectInformation.Category);
            if(!document.ParameterBindings.ReInsert(old.GetDefinition(),document.Application.Create.NewInstanceBinding(categories),BuiltInParameterGroup.PG_DATA))throw new Exception("mixed fixture binding failed");
            document.Regenerate();general.GetParameters(legacy).Single().Set("1");document.ProjectInformation.GetParameters(legacy).Single().Set("0.22");unrelated.GetParameters(legacy).Single().Set("0");
            bool blocked=false;try{Static("BooleanAliasService","Inspect",document,source,new Element[]{general});}catch(Exception error){blocked=Root(error).Message.Contains("业务含义未确认");}
            Check("unknown_unselected_legacy_boolean_owner_blocks_copy",blocked&&general.GetParameters(source).Count==0);
            var selected=Static("Management.SelectionService","Read",document,new[]{general.Id.IntegerValue,unrelated.Id.IntegerValue},"报规","总图");
            var selectedRows=(ModelElementCheck[])Get(Get(selected,"Snapshot"),"Elements");
            var requests=new[]{general,unrelated}.Select(owner=>{
                var row=selectedRows.Single(r=>r.Id==owner.Id.IntegerValue&&r.UniqueId==owner.UniqueId);
                var request=New("Management.EditRequest");Set(request,"Kind","分类");Set(request,"Ids",new[]{row.Id});Set(request,"Slot","总平构件");
                var baseline=New("Management.EditBaseline");Set(baseline,"ElementId",row.Id);Set(baseline,"ElementUniqueId",row.UniqueId);Set(baseline,"OwnerUniqueId",row.UniqueId);Set(baseline,"Value",row.Slot);
                Set(request,"Baselines",Typed(T("Management.EditBaseline"),new[]{baseline}));return request;
            }).ToArray();
            var batch=Static("Management.CaseEdits","PreviewSelection",document,Get(selected,"Snapshot"),Typed(T("Management.EditRequest"),requests));
            Check("explicit_batch_classification_keeps_whole_alias_context",((string[])Get(batch,"Conflicts")).Length==0&&Items(Get(batch,"Changes")).Any(c=>(string)Get(c,"Source")==source)&&general.GetParameters(source).Count==0&&unrelated.GetParameters(source).Count==0);
            unrelated.GetParameters(legacy).Single().Set("");general.GetParameters(legacy).Single().Set("0.22");
            blocked=false;try{Static("BooleanAliasService","Inspect",document,source,new Element[]{general});}catch(Exception error){blocked=Root(error) is InvalidOperationException;}
            Check("real_density_value_cannot_be_guessed_as_boolean",blocked&&general.GetParameters(source).Count==0);
            general.GetParameters(legacy).Single().Set("1");
            var schedule=ViewSchedule.CreateSchedule(document,new ElementId(BuiltInCategory.OST_Floors));
            var matchingFields=schedule.Definition.GetSchedulableFields().Where(f=>f.ParameterId==old.Id).ToArray();
            Write("legacy-schedule-fields.json",matchingFields.Select(f=>new{parameter_id=f.ParameterId.IntegerValue,field_type=f.FieldType.ToString()}));
            // The legacy definition is also bound to ProjectInformation, so
            // Revit can offer both Instance and ProjectInfo schedule fields.
            // Either is a real reference; this fixture needs one, not uniqueness.
            var field=matchingFields.First(f=>f.FieldType==ScheduleFieldType.Instance);var fieldId=schedule.Definition.AddField(field).FieldId;
            blocked=false;try{Static("BooleanAliasService","Inspect",document,source,new Element[]{general});}catch(Exception error){blocked=Root(error).Message.Contains("明细表");}
            Check("legacy_boolean_reference_blocks_alias_switch",blocked&&general.GetParameters(source).Count==0);schedule.Definition.RemoveField(fieldId);
            Static("RevitMappingService","Prepare",document,new Element[]{general},"总平构件",run);general.GetParameters("构件类型").Single().Set("风雨连廊");
            Check("mixed_boolean_copies_true_to_separate_native_source",general.GetParameters(source).Single().Definition.ParameterType==ParameterType.YesNo&&general.GetParameters(source).Single().AsInteger()==1);
            Check("mixed_copy_preserves_original_real_definition_and_value",document.ProjectInformation.GetParameters(legacy).Single().StorageType==StorageType.String&&document.ProjectInformation.GetParameters(legacy).Single().AsString()=="0.22"&&general.GetParameters(legacy).Single().AsString()=="1");
            general.GetParameters(source).Single().Set(0);Static("RevitMappingService","Prepare",document,new Element[]{general},"总平构件",run);
            Check("canonical_manual_false_wins_over_legacy_true",general.GetParameters(source).Single().AsInteger()==0&&general.GetParameters(legacy).Single().AsString()=="1");tx.Commit();
        }
    }
    void Finish(UIApplication ui)
    {
        callbackWindow=selection;callbackCalled=false;Call(selection,"SetBusy",true);
        var field=T("Management.ManagerController").GetField("selectionCheckCompleted",Flags);var signature=field.FieldType.GetMethod("Invoke");
        var parameters=signature.GetParameters().Select(p=>Expression.Parameter(p.ParameterType,p.Name)).ToArray();
        var callback=Expression.Lambda(field.FieldType,Expression.Call(typeof(PropertyDataAcceptanceHost).GetMethod("LifecycleCallback",Flags),Expression.Convert(parameters[0],typeof(object)),parameters[1],parameters[2]),parameters).Compile();
        field.SetValue(manager,callback);Call(manager,"Close");Call(manager,"DisposeInApi",ui);
        Check("disposing_manager_drains_manual_check_callback",callbackCalled&&!(bool)Get(selection,"busy"));
        Static("Management.SelectionController","Shutdown");Static("Management.ManagerController","Shutdown");
        document.Save();Write("result.json",new{all_passed=checks.Values.All(v=>v),checks,report=reportPath,scope="Generated Revit fixture; no customer originals or platform writes."});Log("PASS complete");
        try{ui.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit));}catch(Exception error){Log("normal exit unavailable: "+error.Message);}
    }
    static object callbackWindow;static bool callbackCalled;
    static void LifecycleCallback(object snapshot,string path,string error){callbackCalled=true;Call(callbackWindow,"SetBusy",false);}
    static string Hash(string path){using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    static void FillWorkbook(string path,string level,string area)
    {
        XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Update)){
            var entry=zip.GetEntry("xl/worksheets/sheet1.xml");XDocument sheet;using(var stream=entry.Open())sheet=XDocument.Load(stream);entry.Delete();
            Action<XElement,string> text=(cell,value)=>{cell.SetAttributeValue("t","inlineStr");cell.RemoveNodes();cell.Add(new XElement(ns+"is",new XElement(ns+"t",value)));};
            foreach(var row in sheet.Descendants(ns+"row").Where(r=>(int)r.Attribute("r")>=6)){
                var cells=row.Elements(ns+"c").ToArray();var label=cells[1].Value;
                if(label=="建筑用途名称")text(cells[2],"中学");
                if(label=="建筑类别")text(cells[2],"中小学");
                if(label=="规划高度")text(cells[2],"3");
                if(label=="投影面积")text(cells[2],"100");
                if(label=="楼层面积"){text(cells[0],level);text(cells[2],area);}
            }
            using(var stream=zip.CreateEntry("xl/worksheets/sheet1.xml").Open())sheet.Save(stream);
        }
    }
}
