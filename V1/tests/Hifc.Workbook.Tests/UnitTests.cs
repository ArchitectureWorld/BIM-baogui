using System;
using System.IO;
using System.Linq;
using Hifc.Reference;

internal static class UnitTests
{
    static ReportingWorkbookPlan Plan(string property,string type,string unit,string current=null,double? metric=null)
    {
        var field=new FieldCheck {PropertySet="Pset_建筑技术信息属性集",Property=property,Source=property,RawType=type,OwnerUniqueId="b",Binding="实例",State=current==null?"未填写":"有效",Value=current,MetricValue=metric,Editable=true};
        return ReportingWorkbookPlanner.Build(new ReportingWorkbookTable {Id="unit-table",Kind="单体建筑",BuildingName="综合楼",Items=new[]{new ReportingWorkbookItem {Field=property,Value="3",Unit=unit}}},
            new[]{new ModelElementCheck {Id=1,UniqueId="b",Name="综合楼",Entity="IfcBuilding",Fields=new[]{field}}},"单体建筑");
    }
    internal static void Run()
    {
        Program.Check("integer_units_must_match_count_semantics",()=>{
            Program.Assert(!Plan("人数","Integer","层").Changes.Single().CanSelect,"people accepted floor units");
            Program.Assert(Plan("人数","Integer","人").Changes.Single().CanSelect,"people unit rejected");
            Program.Assert(!Plan("地下建筑层数","Integer","户").Changes.Single().CanSelect,"floors accepted household units");
            Program.Assert(Plan("地下建筑层数","Integer","层").Changes.Single().CanSelect,"floor unit rejected");
        });
        Program.Check("workbook_equality_uses_metric_value_not_internal_raw_number",()=>{
            Program.Assert(Plan("建筑高度","Real","m","3",.9144).Changes.Single().CanSelect,"3 feet mistaken for same value as 3 meters");
            Program.Assert(!Plan("建筑高度","Real","m","9.84251968503937",3).Changes.Single().CanSelect,"same metric value considered a conflict");
        });
        Program.Check("workbook_linear_fields_share_canonical_meter_units",()=>{
            foreach(var name in new[]{"长度","进深","室内外高差最大值","窗台高","道路宽度"}){
                var change=Plan(name,"Real","m").Changes.Single();
                Program.Assert(change.CanSelect&&change.ValueUnit=="m",name+" rejected metric length or lost its write unit");
            }
        });
        Program.Check("workbook_template_labels_height_difference_in_meters",()=>{
            var path=Path.Combine(Program.Output,"units-"+Guid.NewGuid().ToString("N")+".xlsx");
            ReportingWorkbookTemplate.Write(path,new[]{"综合楼"},false);
            var field=ReportingWorkbookReader.Read(path).Tables.Single().Items.Single(i=>i.Field=="室内外高差最大值");
            Program.Assert(field.Unit=="m","template omits metric height-difference unit");
        });
    }
}
