using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class ExportReviewTests
{
    static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
    public static void Run(Action<string,Action> check,string output)
    {
        check("export_review_shows_final_versioned_destination",()=>{
            var data=new ExportReviewData{Title="学校_总图"};
            var field=typeof(ExportReviewData).GetProperty("OutputPath");Assert(field!=null,"Final output destination is missing");
            var path=Path.Combine(output,"OUT_学校_报规_总图_20261023_V2.ifc");field.SetValue(data,path);
            var window=new ExportReviewWindow(data);try{window.Show();window.UpdateLayout();
                var text=window.FindName("ExportDestinationText") as TextBlock;
                Assert(text!=null&&text.Text.Contains(path),"Final path/version not visible before export");
            }finally{window.Close();}
        });
        check("empty_property_editor_explains_platform_default_without_setting_a_value",()=>{
            var hint=RuleInputOptions.ValueHint(PlatformRules.ProfileId,"Pset_绿地信息属性集","绿地类型","");
            Assert(hint.Contains("集中绿地")&&hint.Contains("默认"),"Empty field editor hides platform default");
            Assert(RuleInputOptions.ValueHint("","Pset_绿地信息属性集","绿地类型","")=="","Reference-only mode assumed platform defaults");
            Assert(RuleInputOptions.ValueHint(PlatformRules.ProfileId,"Pset_停车位信息属性集","公建车位","0")=="","Explicit false displayed as missing");
        });
        check("export_review_has_locatable_inputs_and_separate_area_values",()=>{
            var data=new ExportReviewData{Title="副本 · 导出核对",ProfileId=PlatformRules.ProfileId,
                Inputs=new[]{new InputReviewItem{ElementId=718110,Name="道路红线",Property="红线宽度",DefaultValue="15",Detail="未填写：平台可能采用默认15m，未写入模型。"}},
                Areas=new[]{new ExportAreaReviewItem{ElementId=311,Name="F1",Property="楼层面积",SourceValue="1784.58",ExportValue="1784.58",Reason="采用人工楼层面积"},
                    new ExportAreaReviewItem{ElementId=1126,Name="F1 主区",Property="投影面积",SourceValue="1658.49",ExportValue="1502.78",Reason="按同层原区域比例分摊人工楼层面积"}}};
            var w=new ExportReviewWindow(data);int[] located=null;w.LocateRequested+=ids=>located=ids;
            try {
                w.Show();w.UpdateLayout();
                Assert(w.InputGrid.IsReadOnly&&w.AreaGrid.IsReadOnly,"Review should not write values");
                w.InputGrid.SelectedIndex=0;w.LocateSelection();Assert(located.SequenceEqual(new[]{718110}),"Input locate lost ID");
                Assert(((InputReviewItem)w.InputGrid.Items[0]).CurrentText=="未填写","Missing source not visible");
                Program.Screenshot(w,Path.Combine(output,"export-input-review.png"));
                var tabs=ManagerImprovementsTests.Visual<TabControl>(w);tabs.SelectedIndex=1;w.UpdateLayout();
                w.AreaGrid.SelectAll();w.LocateSelection();Assert(located.SequenceEqual(new[]{311,1126}),"Area multi-locate lost IDs");
                var area=(ExportAreaReviewItem)w.AreaGrid.Items[1];Assert(area.SourceValue=="1658.49"&&area.ExportValue=="1502.78","Source and intended IFC values not separate");
                Program.Screenshot(w,Path.Combine(output,"export-area-review.png"));
            }finally{w.Close();}
        });
        check("export_review_cancel_does_not_approve_export",()=>{
            var w=new ExportReviewWindow(new ExportReviewData{Title="取消测试"});
            w.Loaded+=(s,e)=>w.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>w.Close()));
            Assert(w.ShowDialog()!=true,"Closing review approved export");
        });
        check("export_review_displays_area_conflict_but_cannot_approve",()=>{
            var data=new ExportReviewData{Title="面积来源冲突",Areas=new[]{new ExportAreaReviewItem{ElementId=77,Property="投影面积",SourceValue="来源冲突",ExportValue="无法确定",Reason="同名参数值不一致"}}};
            var blocker=typeof(ExportReviewData).GetProperty("BlockingReasons");Assert(blocker!=null,"Blocking diagnostics missing");
            blocker.SetValue(data,new[]{"同名参数值不一致"});
            var w=new ExportReviewWindow(data);try{
                w.Show();w.UpdateLayout();
                var proceed=(Button)w.FindName("ContinueExportButton");
                Assert(proceed!=null&&!proceed.IsEnabled&&w.AreaGrid.Items.Count==1,"Area conflict hides review or allows export approval");
            }finally{w.Close();}
        });
        check("manual_drafts_block_export_even_after_window_reopens",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var path=Path.Combine(output,"export-drafts");
            var w=new ManagerWindow(fake,path);fake.Window=w;
            try {
                w.Show();w.StageSelector.SelectedItem="报规";w.KindSelector.SelectedItem="总图";w.StartFullCheck();
                var method=typeof(ManagerWindow).GetMethod("EnsureExportReady",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert(method!=null,"Missing pending-edit guard");method.Invoke(w,null);
                var row=fake.Snapshot.Elements[0];w.ElementGrid.SelectedItem=row;w.StageValue(row.Fields[0],"人工修改");
                bool blocked=false;try{method.Invoke(w,null);}catch(TargetInvocationException e){blocked=e.InnerException.Message.Contains("4 写入 Revit");}
                Assert(blocked&&fake.Written==null&&w.DraftRequests.Length==1,"Draft was silently exported or written");
                w.Close();
                var state=new ManagementStateStore(path,fake.Snapshot.DocumentKey).Load();
                var checkState=typeof(ManagementState).GetMethod("EnsureExportReady",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert(checkState!=null,"Closed-window guard missing");blocked=false;
                try{checkState.Invoke(state,null);}catch(TargetInvocationException e){blocked=e.InnerException.Message.Contains("4 写入 Revit");}
                Assert(blocked,"Persisted drafts bypassed export guard");
            }finally{w.Close();}
        });
        check("save_as_rebinds_pending_drafts_to_new_document_key",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var path=Path.Combine(output,"save-as-drafts");
            var w=new ManagerWindow(fake,path);fake.Window=w;
            try{
                w.Show();w.StageSelector.SelectedItem="报规";w.KindSelector.SelectedItem="总图";w.StartFullCheck();
                var row=fake.Snapshot.Elements[0];w.ElementGrid.SelectedItem=row;w.StageValue(row.Fields[0],"另存前修改");
                var method=typeof(ManagerWindow).GetMethod("RebindDocumentKey",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert(method!=null,"Save As state migration missing");
                method.Invoke(w,new object[]{"new-path-document-key"});
                Assert(!w.Current,"Save As still considered current");w.Close();
                var state=new ManagementStateStore(path,"new-path-document-key").Load();
                Assert(state.Drafts.Single().Value=="另存前修改","New model key lost drafts");
                bool blocked=false;try{state.EnsureExportReady();}catch(InvalidOperationException){blocked=true;}
                Assert(blocked,"New model key bypassed pending edits");
            }finally{w.Close();}
        });
        check("failed_draft_persistence_cannot_close_window_and_discard_edit",()=>{
            var fake=new FakeSession{Snapshot=ManagerImprovementsTests.Sample()};var path=Path.Combine(output,"failed-save-drafts");
            var w=new ManagerWindow(fake,path);fake.Window=w;
            var field=typeof(ManagerWindow).GetField("stateStore",BindingFlags.Instance|BindingFlags.NonPublic);
            try{
                w.Show();w.StageSelector.SelectedItem="报规";w.KindSelector.SelectedItem="总图";w.StartFullCheck();
                var file=Path.Combine(output,"directory-is-a-file.txt");File.WriteAllText(file,"blocks Directory.CreateDirectory");
                field.SetValue(w,new ManagementStateStore(file,fake.Snapshot.DocumentKey));
                var row=fake.Snapshot.Elements[0];w.ElementGrid.SelectedItem=row;w.StageValue(row.Fields[0],"未保存修改");
                w.Close();Assert(w.IsVisible&&w.DraftRequests.Length==1,"Failed persistence silently closed draft window");
            }finally{
                field.SetValue(w,new ManagementStateStore(path,fake.Snapshot.DocumentKey));w.Close();
            }
        });
        check("closed_window_state_can_follow_save_as_without_losing_drafts",()=>{
            var path=Path.Combine(output,"closed-save-as");var prior=new ManagementStateStore(path,"old-saved-key");
            prior.Save(new ManagementState{DocumentKey="old-saved-key",Drafts=new[]{new EditRequest{Kind="赋值",Value="1784.58"}}});
            var state=prior.Load();
            var method=typeof(ManagementState).GetMethod("RebindDocumentKey",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert(method!=null,"Closed state cannot follow Save As");method.Invoke(state,new object[]{"new-saved-key"});
            var next=new ManagementStateStore(path,"new-saved-key");next.Save(state);
            bool blocked=false;try{next.Load().EnsureExportReady();}catch(InvalidOperationException){blocked=true;}
            Assert(blocked&&prior.Load().Drafts.Single().Value=="1784.58","Migrating closed state lost prior evidence or export guard");
        });
    }
}
