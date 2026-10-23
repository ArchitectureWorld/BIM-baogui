using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Hifc.Reference;
using Hifc.Revit.Management;

internal static class ClassificationUiTests
{
    const string Region="Pset_建筑区域信息属性集",Building="Pset_建筑技术信息属性集";
    sealed class Session:ISelectionSession
    {
        internal EditRequest[] Written;
        public void Read(){}public void Classify(string slot){}public void Close(){}
        public void Write(EditRequest[] requests){Written=requests;}
    }
    static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
    static FieldCheck Field(int id,string set,string name,string value,string profile="")=>new FieldCheck {
        PropertySet=set,Property=name,Source=name,RawType="Label",Binding="实例",OwnerId=id,OwnerUniqueId="classification-"+id,
        Value=value,State="有效",Editable=true,Origin="instance",SourceSignature="classification-"+id+"-"+name,RuleProfileId=profile};
    static ModelElementCheck Row(int id,string parent="学校",string child="中学",string profile="")=>new ModelElementCheck {
        Id=id,UniqueId="classification-"+id,Name="分类载体"+id,Slot="建筑区域",Entity="IfcSlab",Classified=true,
        Fields=new[]{Field(id,Region,"主功能类别",parent,profile),Field(id,Region,"子功能类别",child,profile)}};
    static void WithSelection(ModelElementCheck[] rows,Action<SelectionWindow,Session> test)
    {
        var session=new Session();var window=new SelectionWindow(session);
        try {
            window.Show();window.InitializeContext("报规","单体建筑");
            window.Accept(new SelectionReadResult {SelectedCount=rows.Length,Slots=new[]{"建筑区域"},Snapshot=new ModelSnapshot {
                Scope="所选构件",Complete=true,Stage="报规",ModelKind="单体建筑",DocumentKey=Guid.NewGuid().ToString(),ScanId="class-scan",Elements=rows}});
            window.ShowClassification("建筑区域",new EditPreview {ProposedElements=rows,Changes=new EditChange[0]},new ManualWrite[0]);window.UpdateLayout();test(window,session);
        }finally{window.Close();}
    }
    static ComboBox Edit(SelectionWindow window,string property)
    {
        window.CommitEditors();var row=window.FieldGrid.Items.Cast<SelectionField>().Single(f=>f.Property==property);
        window.FieldGrid.ScrollIntoView(row);window.FieldGrid.CurrentCell=new DataGridCellInfo(row,window.FieldGrid.Columns[1]);
        Assert(window.FieldGrid.BeginEdit(),"Cannot edit manual classification");window.UpdateLayout();
        var combo=ManagerImprovementsTests.Visual<ComboBox>(window.FieldGrid.Columns[1].GetCellContent(row));
        Assert(combo!=null&&combo.IsEditable,"Manual classification still has no editable dropdown");return combo;
    }
    static void Type(ComboBox combo,string value){combo.Text=value;combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();}
    internal static void Run(Action<string,Action> check,string output)
    {
        check("manual_classification_dropdown_works_without_platform_profile_and_writes_custom_text",()=>WithSelection(new[]{Row(71)},(window,session)=>{
            var child=Edit(window,"子功能类别");var options=child.Items.Cast<string>().ToArray();
            Assert(options.Contains("中学")&&options.Contains("办公室")&&options.Length==24,"School child dictionary was not offered in reference mode");
            Assert(window.Draft.Snapshot.Elements[0].Fields.All(f=>f.RuleProfileId==""),"Showing suggestions switched the rule profile");
            Type(child,"自定义教学空间");window.CommitEditors();
            var requests=window.Draft.BuildRequests();
            Assert(requests.Single(r=>r.Kind=="赋值").Value=="自定义教学空间","Custom manual input changed or was lost");
            Assert(window.Draft.Fields.Single(f=>f.Property=="子功能类别").Detail.Contains("平台识别待验证"),"Custom input has no platform-recognition warning");
            Assert(window.Draft.Snapshot.Elements[0].Fields[1].Value=="中学","Editing changed the source snapshot");
            window.Width=1000;window.UpdateLayout();Program.Screenshot(window,Path.Combine(output,"selection-classification-dropdown.png"));
        }));
        check("manual_parent_change_filters_choices_without_erasing_or_staging_old_child",()=>WithSelection(new[]{Row(72,profile:PlatformRules.ProfileId)},(window,session)=>{
            var parent=Edit(window,"主功能类别");Type(parent,"住宅");window.CommitEditors();
            var child=Edit(window,"子功能类别");
            Assert(!child.Items.Cast<string>().Contains("中学")&&child.Text=="中学","Changing parent lost the old child or kept unrelated choices");
            window.CommitEditors();var requests=window.Draft.BuildRequests();
            Assert(requests.Count(r=>r.Kind=="赋值")==1&&requests.Single(r=>r.Kind=="赋值").Source=="主功能类别","Choice refresh staged an unintended child change");
            Assert(window.Draft.Fields.Single(f=>f.Property=="子功能类别").Detail.Contains("住宅"),"Retained incompatible child has no local warning");
        }));
        check("manual_mixed_parents_do_not_guess_options_or_overwrite_unedited_children",()=>WithSelection(new[]{Row(73),Row(74,"住宅","原住宅用途")},(window,session)=>{
            var child=Edit(window,"子功能类别");Assert(child.Items.Cast<string>().All(v=>string.IsNullOrEmpty(v)),"Mixed parents used the first parent's children");
            Assert(child.Text=="多个值","Mixed child display was lost");window.CommitEditors();
            Assert(window.Draft.BuildRequests().All(r=>r.Kind!="赋值"),"Opening a mixed dropdown wrote shared text");
            Assert(window.Draft.Snapshot.Elements[0].Fields[1].Value=="中学"&&window.Draft.Snapshot.Elements[1].Fields[1].Value=="原住宅用途","Unedited originals changed");
        }));
        check("manager_mixed_classification_open_commit_is_noop_and_explicit_edit_unifies",()=>{
            var rows=new[]{Row(76,profile:PlatformRules.ProfileId),Row(77,"住宅","原住宅用途",PlatformRules.ProfileId)};
            var fake=new FakeSession {Snapshot=new ModelSnapshot {Complete=true,DocumentKey=Guid.NewGuid().ToString(),Title="混合分类",Stage="报规",ModelKind="单体建筑",ScanId="class-manager-mixed",Elements=rows}};
            var window=new ManagerWindow(fake,Path.Combine(output,"classification-mixed"));fake.Window=window;
            try {
                window.Show();window.Accept(fake.Snapshot);window.ElementGrid.SelectedItems.Add(rows[0]);window.ElementGrid.SelectedItems.Add(rows[1]);window.UpdateLayout();
                var item=window.FieldGrid.Items.Cast<ManagerWindow.PropertyEditor>().Single(f=>f.Property=="子功能类别");
                Action begin=()=>{window.FieldGrid.CurrentCell=new DataGridCellInfo(item,window.FieldGrid.Columns[1]);Assert(window.FieldGrid.BeginEdit(),"Mixed manager classification cannot edit");window.UpdateLayout();};
                begin();var combo=ManagerImprovementsTests.Visual<ComboBox>(window.FieldGrid.Columns[1].GetCellContent(item));
                Assert(combo!=null&&combo.IsEditable,"Mixed manager classification has no editable dropdown");
                window.FieldGrid.CommitEdit(DataGridEditingUnit.Cell,true);window.FieldGrid.CommitEdit(DataGridEditingUnit.Row,true);
                Assert(window.DraftRequests.Length==0,"Merely opening and committing a mixed classification staged the first object's value for another object");
                Assert(item.Value=="多个值"&&!item.ValueChoices.Contains("中学"),"Mixed classification display or parent filtering used the first object");
                begin();combo=ManagerImprovementsTests.Visual<ComboBox>(window.FieldGrid.Columns[1].GetCellContent(item));Type(combo,"自定义教学空间");
                window.FieldGrid.CommitEdit(DataGridEditingUnit.Cell,true);window.FieldGrid.CommitEdit(DataGridEditingUnit.Row,true);
                Assert(window.DraftRequests.Length==2&&window.DraftRequests.All(r=>r.Value=="自定义教学空间"),"Explicit mixed classification edit did not stage both intended targets");
                Assert(rows[0].Fields[1].Value=="中学"&&rows[1].Fields[1].Value=="原住宅用途","Mixed manager editing changed the source snapshot");
            }finally{window.Close();}
        });
        check("manager_and_common_editors_offer_full_category_dictionary_without_enabling_profile",()=>{
            var row=new ModelElementCheck {Id=75,UniqueId="classification-75",Name="分类项目",Slot="建筑技术",Entity="IfcBuilding",Classified=true,
                Fields=new[]{Field(75,Building,"建筑类别","中小学"),Field(75,Building,"建筑类型名称","中小学等"),Field(75,Building,"建筑用途名称","中学")}};
            var fake=new FakeSession {Snapshot=new ModelSnapshot {Complete=true,DocumentKey=Guid.NewGuid().ToString(),Title="分类字典",Stage="报规",ModelKind="单体建筑",ScanId="class-manager",Elements=new[]{row}}};
            var window=new ManagerWindow(fake,Path.Combine(output,"classification-profiles"));fake.Window=window;
            try {
                window.Show();window.Accept(fake.Snapshot);window.ElementGrid.SelectedItem=row;window.UpdateLayout();
                var item=window.FieldGrid.Items.Cast<ManagerWindow.PropertyEditor>().Single(f=>f.Property=="建筑类别");
                Assert(item.ValueChoices.Contains("门卫房")&&item.ValueChoices.Contains("配电房")&&item.ValueChoices.Contains("大门"),"Manager is still using its shortened category list");
                window.FieldGrid.CurrentCell=new DataGridCellInfo(item,window.FieldGrid.Columns[1]);Assert(window.FieldGrid.BeginEdit(),"Manager classification cannot edit");window.UpdateLayout();
                var combo=ManagerImprovementsTests.Visual<ComboBox>(window.FieldGrid.Columns[1].GetCellContent(item));Assert(combo!=null&&combo.IsEditable,"Manager classification is not editable");
                Type(combo,"自定义学校配套");window.FieldGrid.CommitEdit(DataGridEditingUnit.Cell,true);window.FieldGrid.CommitEdit(DataGridEditingUnit.Row,true);
                Assert(window.DraftRequests.Single().Value=="自定义学校配套"&&item.ValueHint.Contains("平台识别待验证"),"Custom manager value was lost or unmarked");
                var common=new PlanningValueRow {Definition=new ReferenceProperty {PropertySet=Building,Property="建筑类别",RawType="Label"},RuleProfileId="",Value="自定义配套"};
                Assert(common.Choices.Contains("门卫房")&&common.Choices.Contains("大门")&&common.ValueHint.Contains("平台识别待验证"),"Common values did not share the classification policy");
            }finally{window.Close();}
        });
    }
}
