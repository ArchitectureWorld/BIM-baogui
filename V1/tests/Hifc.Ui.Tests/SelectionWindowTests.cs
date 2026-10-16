using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class SelectionWindowTests
{
    sealed class Session:ISelectionSession
    {
        internal EditRequest[] Written;
        public void Read(){} public void Classify(string slot){} public void Write(EditRequest[] requests){Written=requests;} public void Close(){}
    }
    static void Assert(bool value){if(!value)throw new Exception("selection window assertion failed");}
    static ModelElementCheck Row(int id,string area)=>new ModelElementCheck {Id=id,UniqueId="selected-ui-"+id,Name="同事模型_"+id,Category="楼板",Entity="IfcSlab",Slot="绿地",
        Fields=new[]{new FieldCheck {PropertySet="Pset_绿地信息属性集",Property="投影面积",Source="绿地投影面积",RawType="Real",Binding="实例",OwnerId=id,OwnerUniqueId="selected-ui-"+id,Value=area,State="有效",Editable=true,Origin="instance",SourceSignature="ui-area-"+id},
        new FieldCheck {PropertySet="Pset_绿地信息属性集",Property="绿地类型",Source="绿地类型",RawType="Label",Binding="实例",OwnerId=id,OwnerUniqueId="selected-ui-"+id,Value="公共绿地",State="有效",Editable=true,Origin="instance",SourceSignature="ui-label-"+id}}};
    internal static void Run(Action<string,Action> check,string output)
    {
        var session=new Session();var window=new SelectionWindow(session);window.Show();window.UpdateLayout();
        check("selection_window_has_no_unconfirmed_stage_or_write",()=>Assert(window.Stage==null&&window.Kind==null&&!window.CanWrite));
        window.InitializeContext("报规","总图");var rows=new[]{Row(1,"10"),Row(2,"20")};
        var snapshot=new ModelSnapshot {Scope="所选构件",DocumentKey="selection-ui",Stage="报规",ModelKind="总图",Complete=true,ScanId="selection-ui",Elements=rows};
        window.Accept(new SelectionReadResult {Snapshot=snapshot,Slots=new[]{"绿地"},SelectedCount=2});
        window.ShowClassification("绿地",new EditPreview {ProposedElements=rows,Changes=new EditChange[0]},new ManualWrite[0]);window.UpdateLayout();
        check("selection_window_reuses_context_and_lists_captured_objects",()=>Assert(window.Stage=="报规"&&window.Kind=="总图"&&window.ObjectGrid.Items.Count==2&&window.CanWrite));
        check("selection_filled_value_enters_edit_on_single_mouse_event",()=>{
            var item=window.FieldGrid.Items.Cast<SelectionField>().Single(f=>f.Property=="绿地类型");var content=window.FieldGrid.Columns[1].GetCellContent(item);var cell=(DataGridCell)content.Parent;
            window.FieldGrid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent,Source=content});window.UpdateLayout();
            Assert(cell.IsEditing);var text=ManagerImprovementsTests.Visual<TextBox>(cell);Assert(text!=null&&text.Text=="公共绿地");text.Text="人工调整绿地";text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();window.CommitEditors();Assert(item.Value=="人工调整绿地"&&item.State=="待写入");
        });
        check("selection_write_button_leaves_untouched_multiple_areas_separate",()=>{
            var write=FindButton(window,"写入所选构件");write.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(session.Written.Length==2&&session.Written.Count(r=>r.Kind=="赋值")==1&&session.Written.Single(r=>r.Kind=="赋值").Value=="人工调整绿地");
        });
        check("selection_window_layout_renders_without_clipped_write_action",()=>{
            window.Width=820;window.Height=650;window.UpdateLayout();window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));Assert(FindButton(window,"写入所选构件").ActualWidth>100&&window.FieldGrid.ActualHeight>150&&window.FieldGrid.Columns[1].ActualWidth>=180&&window.ObjectGrid.Columns[0].ActualWidth>=235);
            var root=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var ctx=drawing.RenderOpen()){ctx.DrawRectangle(window.Background,null,new Rect(0,0,root.ActualWidth,root.ActualHeight));ctx.DrawRectangle(new VisualBrush(root),null,new Rect(0,0,root.ActualWidth,root.ActualHeight));}bitmap.Render(drawing);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(output,"selection-window.png")))png.Save(stream);
        });
        check("selection_stale_model_prevents_button_write",()=>{session.Written=null;window.MarkStale("模型已变更");FindButton(window,"写入所选构件").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(!window.CanWrite&&session.Written==null);});
        window.Close();
    }
    static Button FindButton(DependencyObject root,string text)
    {
        if(root is Button button&&Equals(button.Content,text))return button;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=FindButton(VisualTreeHelper.GetChild(root,i),text);if(found!=null)return found;}return null;
    }
}
