using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Hifc.Reference;
using Hifc.Revit;
using Hifc.Revit.Management;

public static class SiteStatisticsProof
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength=128*1024*1024 };
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private static void Configure(Document doc,string kind) { PlatformContext.SetStage(doc,"报规");PlatformContext.SetKind(doc,kind);PlatformContext.Set(doc,new PlanningSettings { RuleProfileId=PlatformRules.ProfileId }); }

    public static string Copy(Document host,string path,string output,bool site)
    {
        var doc=host.Application.OpenDocumentFile(path);
        try
        {
            Configure(doc,site?"总图":"单体建筑");
            var tracked=new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(e=>e is Floor||e is Area||e is Level||e is ProjectInfo||e.Category?.Id.IntegerValue==(int)BuiltInCategory.OST_GenericModel).ToArray();
            var before=Evidence.Hash(Json.Serialize(RevitMappingService.Capture(doc,tracked))); var modified=doc.IsModified;
            object statistics;
            if(site)
            {
                var own=SiteStatisticsReader.Read(doc,"site",false); var all=SiteStatisticsReader.Read(doc,"site",true);
                Require(own.Components.Any(c=>c.Kind=="规划净用地") && own.Components.Any(c=>c.Kind=="绿地"),"Real site area carriers not read");
                Require(own.Components.Where(c=>c.Kind=="停车").All(c=>(doc.GetElement(new ElementId(c.ElementId)) as Area)?.Area>0),"Unplaced historical parking Area entered totals");
                SiteStatisticsWorkbook.Write(Path.Combine(output,"site-only.xlsx"),own); SiteStatisticsWorkbook.Write(Path.Combine(output,"site-with-links.xlsx"),all);
                File.WriteAllText(Path.Combine(output,"site-only.json"),Json.Serialize(own));File.WriteAllText(Path.Combine(output,"site-with-links.json"),Json.Serialize(all));
                statistics=new { ownCount=own.Components.Length, allCount=all.Components.Length, ownMetrics=own.Metrics, allMetrics=all.Metrics, pending=all.Pending.Length };
                Controller(doc,own.Components.First(c=>c.Kind=="绿地").ElementId);
            }
            else
            {
                var report=BuildingAreaStatisticsReader.Read(doc,doc.Title); statistics=new {report.Total,report.Floors};
                var auxiliary=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(l=>l.Name.Contains("容积率0"));
                if(auxiliary!=null)
                {
                    var settings=PlatformContext.Settings(doc);settings.LevelsConfirmed=true;
                    settings.BuildingLevelUniqueIds=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Where(l=>l.Id!=auxiliary.Id).Select(l=>l.UniqueId).ToArray();
                    PlatformContext.Set(doc,settings);
                    var rows=tracked.Select(e=>CaseModelReader.Inventory(doc,e)).ToArray();
                    var rules=CaseWorkflow.ForModel("报规","单体建筑",PlatformRules.ProfileId);
                    for(var i=0;i<tracked.Length;i++)CaseModelReader.ReadFieldsWithPlanning(doc,tracked[i],rows[i],rules,"单体建筑",null,settings);
                    var snapshot=new ModelSnapshot { DocumentKey=ModelReader.Key(doc),Title=doc.Title,Stage="报规",ModelKind="单体建筑",Complete=true,Elements=rows,Planning=settings,ScanId=Guid.NewGuid().ToString("N") };
                    var auxiliaryResult=BlankReportingWorkbookNativeTests.Run(doc,typeof(App).Assembly,snapshot,output,auxiliary.UniqueId);
                    File.WriteAllText(Path.Combine(output,"auxiliary-import.json"),Json.Serialize(auxiliaryResult));
                }
            }
            var structure=ReportingWorkbookModelReader.Read(doc);
            var blank=Path.Combine(output,(site?"site":"building")+"-blank.xlsx");
            ReportingWorkbookTemplate.WriteForModel(blank,structure.Kind,structure.BuildingName,structure.LevelNames);
            Require(ReportingWorkbookReader.Read(blank).Tables.SelectMany(t=>t.Items).All(i=>string.IsNullOrEmpty(i.Value)),"Business values entered blank table");
            Require(before==Evidence.Hash(Json.Serialize(RevitMappingService.Capture(doc,tracked))) && doc.IsModified==modified,"Read-only copy acceptance changed model");
            var result=new {success=true,model=doc.Title,statistics,templateLevels=structure.LevelNames,blank,sourceUnchanged=true};
            File.WriteAllText(Path.Combine(output,"copy-result.json"),Json.Serialize(result));return Json.Serialize(result);
        }
        finally { doc.Close(false); }
    }

    public static string Auxiliary(Document host,string output)
    {
        var doc=host.Application.NewProjectDocument(@"C:\ProgramData\Autodesk\RVT 2020\Templates\China\DefaultCHSCHS.rte");
        try
        {
            Configure(doc,"单体建筑"); Level natural,auxiliary;
            using(var tx=new Transaction(doc,"空白报规表辅助层测试"))
            {
                tx.Start();natural=Levels(doc)[0];natural.Name="1F";auxiliary=Level.Create(doc,natural.Elevation);auxiliary.Name="1F 容积率0";
                RevitMappingService.Prepare(doc,new Element[]{natural,auxiliary},"建筑楼层",output);
                var a=Slab(doc,natural,0,0,10,10);var b=Slab(doc,auxiliary,20,0,10,10);
                var values=new Dictionary<string,string>{{"投影面积","100"},{"计算系数","1"},{"计容系数","0"},{"主功能类别","办公"},{"子功能类别","附属办公"}};
                Fill(doc,a,"建筑区域",output,values);Fill(doc,b,"建筑区域",output,values);tx.Commit();
            }
            var settings=PlatformContext.Settings(doc);settings.LevelsConfirmed=true;settings.BuildingLevelUniqueIds=new[]{natural.UniqueId};PlatformContext.Set(doc,settings);
            var elements=new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(e=>e is Floor||e is Level||e is ProjectInfo).ToArray();
            var rows=elements.Select(e=>CaseModelReader.Inventory(doc,e)).ToArray();var rules=CaseWorkflow.ForModel("报规","单体建筑",PlatformRules.ProfileId);
            for(var i=0;i<elements.Length;i++)CaseModelReader.ReadFieldsWithPlanning(doc,elements[i],rows[i],rules,"单体建筑",null,settings);
            var snapshot=new ModelSnapshot{DocumentKey=ModelReader.Key(doc),Title=doc.Title,Stage="报规",ModelKind="单体建筑",Complete=true,Elements=rows,Planning=settings,ScanId=Guid.NewGuid().ToString("N")};
            var result=BlankReportingWorkbookNativeTests.Run(doc,typeof(App).Assembly,snapshot,output,auxiliary.UniqueId);
            File.WriteAllText(Path.Combine(output,"auxiliary-result.json"),Json.Serialize(result));return Json.Serialize(new{success=true,auxiliary=auxiliary.Name});
        }
        finally{doc.Close(false);}
    }

    public static string Fixture(Document host,string output)
    {
        var app=host.Application; var linked=app.NewProjectDocument(@"C:\ProgramData\Autodesk\RVT 2020\Templates\China\DefaultCHSCHS.rte");
        var linkPath=Path.Combine(output,"linked-green.rvt");
        try
        {
            Configure(linked,"单体建筑");
            using(var tx=new Transaction(linked,"链接绿地测试")) { tx.Start();var level=Levels(linked)[0];var green=Slab(linked,level,0,12,2,10);Fill(linked,green,"绿地",output,new Dictionary<string,string>{{"投影面积","20"},{"折算系数","1"},{"绿地类型","附属绿地"}});tx.Commit(); }
            linked.SaveAs(linkPath,new SaveAsOptions());
        }
        finally { linked.Close(false); }
        var doc=app.NewProjectDocument(@"C:\ProgramData\Autodesk\RVT 2020\Templates\China\DefaultCHSCHS.rte");
        try
        {
            Configure(doc,"总图");
            using(var tx=new Transaction(doc,"总图统计测试"))
            {
                tx.Start();var level=Levels(doc)[0];
                var net=Slab(doc,level,0,0,30,30);Fill(doc,net,"规划净用地",output,new Dictionary<string,string>{{"投影面积","1000"}});
                var a=Slab(doc,level,0,0,10,10);a.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(2);
                Fill(doc,a,"绿地",output,new Dictionary<string,string>{{"投影面积","120"},{"折算系数","0.5"},{"绿地类型","附属绿地"}});
                var b=Slab(doc,level,12,0,10,5);b.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(2);
                Fill(doc,b,"绿地",output,new Dictionary<string,string>{{"投影面积","70"},{"折算系数","1"},{"绿地类型","附属绿地"}});
                var scheme=new FilteredElementCollector(doc).OfClass(typeof(AreaScheme)).Cast<AreaScheme>().First();
                var view=ViewPlan.CreateAreaPlan(doc,scheme.Id,level.Id);var plane=SketchPlane.Create(doc,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,new XYZ(0,0,level.Elevation)));
                foreach(Curve edge in Rectangle(0,25,2,2))doc.Create.NewAreaBoundaryLine(plane,edge,view);
                var park=doc.Create.NewArea(view,new UV(1/.3048,26/.3048));
                Fill(doc,park,"停车位",output,new Dictionary<string,string>{{"停车位类型","标准车位"},{"车位数","10"}});
                var linkType=RevitLinkType.Create(doc,ModelPathUtils.ConvertUserVisiblePathToModelPath(linkPath),new RevitLinkOptions(false));
                RevitLinkInstance.Create(doc,linkType.ElementId); var secondLink=RevitLinkInstance.Create(doc,linkType.ElementId);
                ElementTransformUtils.MoveElement(doc,secondLink.Id,new XYZ(20/.3048,0,0));RevitLinkInstance.Create(doc,linkType.ElementId);
                tx.Commit();
            }
            var own=SiteStatisticsReader.Read(doc,"fixture",false);var all=SiteStatisticsReader.Read(doc,"fixture",true);
            var ownRate=own.Metrics.Single(m=>m.Name=="绿地率");var allRate=all.Metrics.Single(m=>m.Name=="绿地率");
            Require(ownRate.DeclaredValue==13 && Math.Abs(ownRate.GeometricValue.Value-100m/9)<.000001m,"Fixture two green-rate paths differ");
            Require(allRate.DeclaredValue==17 && Math.Abs(allRate.GeometricValue.Value-140m/9)<.000001m,"Link placement or duplicate reference changed rate");
            Require(all.Metrics.Single(m=>m.Name=="机动车位数").DeclaredValue==10,"Native parking Area not counted");
            Require(all.Notes.Any(n=>n.Contains("重复来源")),"Duplicate link reference not disclosed");
            Require(all.Components.Count(c=>c.Kind=="绿地")==4,"Distinct link placements not retained exactly once");
            var named=Path.Combine(output,"3D_总图_测试学校_报规_20261101_V12.rvt");doc.SaveAs(named,new SaveAsOptions());
            var day=DateTime.Now;var stamp=day.ToString("yyyyMMdd",CultureInfo.InvariantCulture);
            File.WriteAllText(Path.Combine(output,"OUT_测试学校_报规_总图_"+stamp+"_V3.ifc.log"),"legacy version reservation");
            var first=ExportDestinationPicker.DefaultPath(doc,"总图",day);
            Require(Path.GetFileName(first)=="OUT_总图_测试学校_报规_"+stamp+"_V4.ifc","Native naming entry retained old date/version or missed legacy order");
            var floors=new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Element>().ToArray();
            NativeExportService.Export(doc,floors,first);var firstHash=Evidence.FileHash(first);
            var second=ExportDestinationPicker.DefaultPath(doc,"总图",day);NativeExportService.Export(doc,floors,second);
            Require(Path.GetFileName(second)=="OUT_总图_测试学校_报规_"+stamp+"_V5.ifc" && Evidence.FileHash(first)==firstHash,"Repeat native export did not preserve/increment version");
            File.WriteAllText(Path.Combine(output,"fixture-report.json"),Json.Serialize(all));
            SiteStatisticsWorkbook.Write(Path.Combine(output,"fixture-statistics.xlsx"),all);
            var result=new {success=true,declaredOwn=ownRate.DeclaredValue,geometricOwn=ownRate.GeometricValue,declaredWithLinks=allRate.DeclaredValue,geometricWithLinks=allRate.GeometricValue,parking=10,duplicateLinkExcluded=true,distinctPlacementsCounted=true,nativeIfcFirst=first,nativeIfcSecond=second,previousIfcPreserved=true};
            File.WriteAllText(Path.Combine(output,"fixture-result.json"),Json.Serialize(result));return Json.Serialize(result);
        }
        finally { doc.Close(false); }
    }

    private static void Controller(Document doc,int greenId)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(SiteStatisticsController);var ui=new UIApplication(doc.Application);
        var controller=(SiteStatisticsController)Activator.CreateInstance(type,flags,null,new object[]{ui,doc},CultureInfo.InvariantCulture);
        var window=(SiteStatisticsWindow)type.GetField("window",flags).GetValue(controller);
        try
        {
            Refresh(controller,window,ui,type,flags);
            var before=window.CurrentReport.Components.Single(c=>c.ElementId==greenId).DeclaredArea;
            using(var group=new TransactionGroup(doc,"统计失效测试回滚"))
            {
                group.Start();using(var tx=new Transaction(doc,"测试面积变更")) { tx.Start();doc.GetElement(new ElementId(greenId)).GetParameters("绿地投影面积").Single().Set((before+1).Value.ToString(CultureInfo.InvariantCulture));tx.Commit(); }
                Require(!window.ExportButton.IsEnabled,"Changed model left stale export active");
                Refresh(controller,window,ui,type,flags);
                Require(window.CurrentReport.Components.Single(c=>c.ElementId==greenId).DeclaredArea==before+1,"Native controller refresh kept stale value");
                group.RollBack();
            }
        }
        finally { window.Close();controller.Execute(ui); }
        Require((bool)type.GetField("disposed",flags).GetValue(controller),"Site controller not disposed");
    }
    private static void Refresh(SiteStatisticsController c,SiteStatisticsWindow w,UIApplication ui,Type type,BindingFlags flags)
    {
        w.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));c.Execute(ui);
        var reader=(SiteStatisticsReader)type.GetField("reader",flags).GetValue(c);Require(reader!=null,"Site reader missing");
        while(!reader.Complete)reader.ReadNext();type.GetMethod("Idle",flags).Invoke(c,new object[]{null,null});
        Require(w.CurrentReport!=null && w.ExportButton.IsEnabled,"Site report was not published");
    }
    private static Level[] Levels(Document doc)=>new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l=>l.ProjectElevation).ToArray();
    private static CurveArray Rectangle(double x,double y,double w,double h)
    {
        var p=new[]{new XYZ(x/.3048,y/.3048,0),new XYZ((x+w)/.3048,y/.3048,0),new XYZ((x+w)/.3048,(y+h)/.3048,0),new XYZ(x/.3048,(y+h)/.3048,0)};
        var result=new CurveArray();for(var i=0;i<4;i++)result.Append(Line.CreateBound(p[i],p[(i+1)%4]));return result;
    }
    private static Floor Slab(Document doc,Level level,double x,double y,double w,double h)
        =>doc.Create.NewFloor(Rectangle(x,y,w,h),new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First(t=>!t.IsFoundationSlab),level,false);
    private static void Fill(Document doc,Element e,string slot,string output,Dictionary<string,string> values)
    {
        RevitMappingService.Prepare(doc,new[]{e},slot,output);
        foreach(var p in CaseExportPolicy.PropertiesForSlot(slot,PlatformRules.ProfileId))if(values.TryGetValue(p.Property,out var value))
        {
            var parameter=e.GetParameters(p.SourceParameter).Single();
            if(parameter.StorageType==StorageType.String)parameter.Set(value);else if(parameter.StorageType==StorageType.Integer)parameter.Set(int.Parse(value,CultureInfo.InvariantCulture));
            else if(parameter.StorageType==StorageType.Double)parameter.Set(double.Parse(value,CultureInfo.InvariantCulture));
        }
    }
}
