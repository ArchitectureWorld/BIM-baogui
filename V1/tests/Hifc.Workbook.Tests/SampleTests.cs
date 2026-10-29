using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Hifc.Reference;

internal static class SampleTests
{
    static string Hash(string path) { using(var file=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant(); }
    internal static void Run(string path)
    {
        Program.Check("provided_workbook_readonly_mixed_values_and_metric_warnings",()=>{
            var before=Hash(path);var book=ReportingWorkbookReader.Read(path);
            Program.Assert(before=="de4120238cccbce2db7fba7f393810f1178d8aefdcf369f61d4586d648f07871","supplied workbook differs from approved evidence snapshot");
            Program.Assert(book.Tables.Length==2,"real mixed workbook blocks not separated");
            var building=book.Tables.Single(t=>t.Kind=="单体建筑");var site=book.Tables.Single(t=>t.Kind=="总图");
            Program.Assert(building.Items.Single(i=>i.Provenance.ValueCell=="D7").Value=="综合楼/大门"&&building.Items.Single(i=>i.Provenance.ValueCell=="D21").Value=="6455.60/37.6","real combined values changed");
            Program.Assert(building.Items.Count(i=>i.Field=="耐火等级")==2&&building.Items.Count(i=>i.Field=="楼层面积")==5,"merged floor rows or repeated fields discarded");
            Program.Assert(site.Issues.Count(i=>i.Contains("待核对"))>=3,"real source metric discrepancies hidden");
            var rows=PlannerTests.Model();rows[0].Name=building.BuildingName;rows[0].Building=building.BuildingName;
            var plan=ReportingWorkbookPlanner.Build(building,rows,"单体建筑");
            Program.Assert(plan.Changes.All(c=>!c.Selected)&&plan.Changes.Single(c=>c.Property=="建筑面积").CanSelect==false,"real mixed workbook auto-selected or split ambiguous building values");
            Program.Assert(before==Hash(path),"original workbook changed during read");
            var evidence=new {source=book.Path,sha256=book.Sha256,unchanged=true,tables=book.Tables.Select(t=>new{t.Kind,t.BuildingName,items=t.Items.Length,t.AmbiguousBuilding,t.Issues}).ToArray(),defaultSelected=plan.Changes.Count(c=>c.Selected)};
            File.WriteAllText(Path.Combine(Program.Output,"sample-audit.json"),new JavaScriptSerializer().Serialize(evidence));
            Console.WriteLine("SOURCE "+book.Sha256+"; building items="+building.Items.Length+"; site items="+site.Items.Length+"; source metric warnings="+site.Issues.Length);
        });
    }
}
