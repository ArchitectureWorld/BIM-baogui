using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using ComboBox=System.Windows.Controls.ComboBox;

// Registered temporarily for a fresh test host; operates exclusively on run/models.
public sealed class SelectionAssignmentHost:IExternalApplication,IExternalEventHandler
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    UIControlledApplication app;
    ExternalEvent api;
    Action<UIApplication> action;
    string run,phase="startup",beforeUnselected,beforeTypes;
    int ticks;
    DateTime deadline;
    Document doc;
    Assembly plugin;
    object controller,window,manager;
    string lastFullScan;
    readonly Dictionary<string,bool> checks=new Dictionary<string,bool>();
    readonly int[] targets={392908,393030};
    static object Get(object o,string name)=>o.GetType().GetProperty(name,Flags).GetValue(o);
    static object Field(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
    static object[] Items(object o)=>((IEnumerable)o).Cast<object>().ToArray();
    static Exception Unwrap(Exception e)=>e is TargetInvocationException&&e.InnerException!=null?e.InnerException:e;
    string Json(object value)=>new JavaScriptSerializer {MaxJsonLength=67108864}.Serialize(value);
    void Write(string file,object value)=>File.WriteAllText(Path.Combine(run,file),Json(value));
    void Log(string value)=>File.AppendAllText(Path.Combine(run,"selection-host.log"),DateTime.Now.ToString("O")+" "+value+"\n");
    public Result OnStartup(UIControlledApplication application)
    {
        run=Environment.GetEnvironmentVariable("HIFC_V1_SELECTION_TEST_RUN");if(string.IsNullOrEmpty(run))return Result.Succeeded;
        if(!Directory.Exists(Path.Combine(run,"models")))return Result.Failed;
        app=application;app.Idling+=Tick;return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication application){if(app!=null)app.Idling-=Tick;api?.Dispose();return Result.Succeeded;}
    public string GetName()=>"HIFC selection assignment acceptance";
    public void Execute(UIApplication ui){var next=action;action=null;try{next?.Invoke(ui);}catch(Exception ex){Fail(ex);}}
    void Schedule(Action<UIApplication> next){action=next;if(api.Raise()!=ExternalEventRequest.Accepted)throw new InvalidOperationException("Acceptance API request was rejected.");}
    void Fail(Exception ex){app.Idling-=Tick;Write("selection-host-result.json",new{all_passed=false,phase,error=Unwrap(ex).ToString(),checks});Log("FAILED "+Unwrap(ex));}
    object Static(string type,string method,params object[] args)=>plugin.GetType(type,true).GetMethod(method,Flags).Invoke(null,args);
    void Check(string name,bool value){checks[name]=value;if(!value)throw new InvalidOperationException("Acceptance failed: "+name);}
    string Raw(Document d,int id,string name)
    {
        var values=d.GetElement(new ElementId(id)).GetParameters(name);if(values.Count!=1)return null;var p=values[0];
        return !p.HasValue?null:p.StorageType==StorageType.String?p.AsString():p.StorageType==StorageType.Integer?p.AsInteger().ToString():p.AsDouble().ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    }
    string Capture(params int[] ids)=>Json(Static("Hifc.Revit.RevitMappingService","Capture",doc,ids.Select(id=>doc.GetElement(new ElementId(id))).ToArray()));
    object Draft=>Get(window,"Draft");
    bool HasClass(string slot)=>Draft!=null&&(string)Get(Draft,"Slot")==slot;
    void SetValue(string property,string value)
    {
        var f=Items(Get(Draft,"Fields")).Single(x=>(string)Get(x,"Property")==property);f.GetType().GetProperty("Value").SetValue(f,value);
    }
    Button FindButton(DependencyObject root,string text)
    {
        if(root is Button b&&Equals(b.Content,text))return b;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var b2=FindButton(VisualTreeHelper.GetChild(root,i),text);if(b2!=null)return b2;}return null;
    }
    void WriteButton(){FindButton((Window)window,"写入所选构件").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    void Select(UIApplication ui,params int[] ids){ui.ActiveUIDocument.Selection.SetElementIds(ids.Select(id=>new ElementId(id)).ToArray());Call(controller,"Read");}
    bool FullCheckFinished()
    {
        var windows=(IDictionary)plugin.GetType("Hifc.Revit.Management.ManagerController").GetField("Windows",Flags).GetValue(null);
        manager=windows.Values.Cast<object>().FirstOrDefault();if(manager==null)return false;
        var snapshot=Field(manager,"snapshot");if(snapshot==null||!(bool)Get(snapshot,"Complete"))return false;
        var scan=(string)Get(snapshot,"ScanId");if(scan==lastFullScan||!(bool)Get(Get(manager,"Window"),"Current"))return false;
        Check("post_write_scan_is_whole_project",(string)Get(snapshot,"Scope")=="全项目"&&Items(Get(snapshot,"Elements")).Length>targets.Length);
        lastFullScan=scan;return true;
    }
    void Screenshot(string file)
    {
        var w=(Window)window;w.UpdateLayout();var root=(FrameworkElement)w.Content;
        var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);var visual=new DrawingVisual();
        using(var drawing=visual.RenderOpen()){drawing.DrawRectangle(w.Background,null,new Rect(0,0,root.ActualWidth,root.ActualHeight));drawing.DrawRectangle(new VisualBrush(root),null,new Rect(0,0,root.ActualWidth,root.ActualHeight));}bitmap.Render(visual);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(run,file)))png.Save(stream);
    }
    void Tick(object sender,IdlingEventArgs args)
    {
        var ui=sender as UIApplication;if(ui==null)return;
        try
        {
            if(phase=="startup")
            {
                if(ticks++==0){deadline=DateTime.UtcNow.AddMinutes(8);api=ExternalEvent.Create(this);}
                if(ticks<3){args.SetRaiseWithoutDelay();return;}
                Check("fresh_host_has_no_user_documents",!ui.Application.Documents.Cast<Document>().Any());
                plugin=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Hifc.V1.Revit");
                phase="opening";Schedule(u=>{
                    var path=Path.Combine(run,"models","selection-test.rvt");doc=u.OpenAndActivateDocument(path).Document;
                    if(!Path.GetFullPath(doc.PathName).StartsWith(Path.Combine(Path.GetFullPath(run),"models")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Unsafe acceptance path.");
                    Check("fixture_element_identity_verified",doc.GetElement(new ElementId(392908)).UniqueId=="864cc805-b566-4fed-aa52-11eca4a54b3d-0005fecc"&&doc.GetElement(new ElementId(393030)).UniqueId=="5423279a-0405-441a-8300-88a852cb69c6-0005ff46"&&doc.GetElement(new ElementId(393385)).UniqueId=="2bc6a3e0-7f56-49a3-b5ca-4365b7379361-000600a9"&&doc.GetElement(new ElementId(396396)).UniqueId=="fb21f857-8b52-48a7-bcb2-0d276d3b1fc7-00060c6c");
                    beforeUnselected=Capture(393385,396396);beforeTypes=Capture(targets.Select(id=>doc.GetElement(new ElementId(id)).GetTypeId().IntegerValue).Distinct().ToArray());
                    using(var tx=new Transaction(doc,"Acceptance: nonstandard business classification")){tx.Start();doc.GetElement(new ElementId(targets[0])).LookupParameter("类目").Set("同事任意填写的类目");doc.GetElement(new ElementId(targets[1])).LookupParameter("类目").Set("");tx.Commit();}
                    u.ActiveUIDocument.Selection.SetElementIds(targets.Select(id=>new ElementId(id)).ToArray());
                    Static("Hifc.Revit.Management.SelectionController","Open",u);
                    var windows=(IDictionary)plugin.GetType("Hifc.Revit.Management.SelectionController").GetField("Windows",Flags).GetValue(null);controller=windows.Values.Cast<object>().Single();window=Get(controller,"Window");
                    Call(window,"InitializeContext","报规","总图");Call(controller,"Read");phase="reading";
                });return;
            }
            if(phase=="reading"&&Draft!=null&&Items(Get(Get(Draft,"Snapshot"),"Elements")).Length==2)
            {phase="classifying";((ComboBox)Get(window,"SlotSelector")).SelectedItem="绿地";return;}
            if(phase=="classifying"&&HasClass("绿地"))
            {
                Check("nonstandard_names_and_classes_allow_manual_classification",true);
                var field=Items(Get(Draft,"Fields")).Single(f=>(string)Get(f,"Property")=="投影面积");Check("separate_automatic_values_not_forced_to_one_value",(string)Get(field,"Value")=="多个值");
                SetValue("绿地类型","人工确认绿地");Screenshot("selection-pending.png");phase="writing";WriteButton();return;
            }
            if(phase=="writing"&&Raw(doc,targets[0],"类目")=="绿地"&&Raw(doc,targets[1],"类目")=="绿地"&&FullCheckFinished())
            {
                Check("both_selected_instances_written",Raw(doc,targets[0],"绿地类型")=="人工确认绿地"&&Raw(doc,targets[1],"绿地类型")=="人工确认绿地");
                Check("each_instance_received_its_own_model_area",Raw(doc,targets[0],"绿地投影面积")!=Raw(doc,targets[1],"绿地投影面积")&&double.Parse(Raw(doc,targets[0],"绿地投影面积"),System.Globalization.CultureInfo.InvariantCulture)>0);
                Check("unselected_instances_not_overwritten",Capture(393385,396396)==beforeUnselected);
                Check("shared_type_values_not_changed",Capture(targets.Select(id=>doc.GetElement(new ElementId(id)).GetTypeId().IntegerValue).Distinct().ToArray())==beforeTypes);
                Check("manual_write_is_orange_after_readback",Items(Get(Draft,"Fields")).Any(f=>(string)Get(f,"Property")=="绿地类型"&&(string)Get(f,"State")=="已手工写入"));Screenshot("selection-written.png");
                phase="one-reading";Schedule(u=>Select(u,targets[1]));return;
            }
            if(phase=="one-reading"&&Draft!=null&&Items(Get(Get(Draft,"Snapshot"),"Elements")).Length==1&&HasClass("绿地"))
            {phase="road-classifying";((ComboBox)Get(window,"SlotSelector")).SelectedItem="道路边界线";return;}
            if(phase=="road-classifying"&&HasClass("道路边界线")){SetValue("道路宽度","8.5");phase="road-writing";WriteButton();return;}
            if(phase=="road-writing"&&Raw(doc,targets[1],"类目")=="道路边界线"&&FullCheckFinished())
            {
                Check("classification_switch_clears_old_exclusive_values",string.IsNullOrEmpty(Raw(doc,targets[1],"绿地投影面积"))&&string.IsNullOrEmpty(Raw(doc,targets[1],"绿地类型")));
                Check("manual_road_width_written",Raw(doc,targets[1],"道路边界线道路宽度")=="8.5");
                SetValue("道路宽度","9.5");phase="second-writing";WriteButton();return;
            }
            if(phase=="second-writing"&&Raw(doc,targets[1],"道路边界线道路宽度")=="9.5"&&FullCheckFinished())
            {
                Check("filled_value_can_be_edited_again",true);
                phase="parking-reading";Schedule(u=>{var p=new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Parking).WhereElementIsNotElementType().First();Select(u,p.Id.IntegerValue);});return;
            }
            if(phase=="parking-reading"&&Draft!=null&&Items(Get(Get(Draft,"Snapshot"),"Elements")).Length==0)
            {
                Check("parking_family_cannot_be_forced_into_area_carrier",!((ComboBox)Get(window,"SlotSelector")).Items.Cast<object>().Any()&&!(bool)Get(window,"CanWrite"));
                phase="finish";Schedule(u=>{
                    Check("unselected_instances_still_preserved",Capture(393385,396396)==beforeUnselected);
                    ((Window)window).Close();if(manager!=null)((Window)Get(manager,"Window")).Close();
                    doc.Save();var copyPath=doc.PathName;
                    u.OpenAndActivateDocument(Path.Combine(run,"models","idle-carrier.rvt"));if(!doc.Close(false))throw new Exception("Could not close saved acceptance copy.");
                    var reopened=u.Application.OpenDocumentFile(copyPath);
                    Check("saved_and_reopened_values_persist",Raw(reopened,targets[0],"类目")=="绿地"&&Raw(reopened,targets[0],"绿地类型")=="人工确认绿地"&&Raw(reopened,targets[1],"道路边界线道路宽度")=="9.5");
                    var key=Static("Hifc.Revit.Management.ModelReader","Key",reopened);var storeType=plugin.GetType("Hifc.Revit.Management.ManagementStateStore");
                    var output=(string)Static("Hifc.Revit.RuntimePaths","OutputDirectory");var store=Activator.CreateInstance(storeType,Flags,null,new object[]{Path.Combine(output,"management-profiles"),key},null);var remembered=Call(store,"Load");
                    Check("stage_and_model_kind_are_remembered",(string)Get(remembered,"Stage")=="报规"&&(string)Get(remembered,"Kind")=="总图");reopened.Close(false);
                    Write("selection-host-result.json",new{all_passed=checks.All(c=>c.Value),checks,assembly=plugin.Location,version=plugin.GetName().Version.ToString(),model=copyPath,scope="Real Revit model copy; modeless UI events; write, readback, full rescan, save and reopen"});Log("PASSED");app.Idling-=Tick;
                    u.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit));
                });return;
            }
            args.SetRaiseWithoutDelay();
        }
        catch(Exception ex){Fail(ex);}
    }
}
