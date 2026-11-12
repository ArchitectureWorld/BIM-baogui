using System;using System.Linq;using System.Collections.Generic;using System.Reflection;using Autodesk.Revit.DB;using Autodesk.Revit.UI;using Hifc.Reference;
namespace Hifc.Acceptance {
 public static class SlopedProjectionAcceptance {
  static CurveArray Box(double x0,double y0,double x1,double y1){var pts=new[]{new XYZ(x0,y0,0),new XYZ(x1,y0,0),new XYZ(x1,y1,0),new XYZ(x0,y1,0)};var a=new CurveArray();for(int i=0;i<4;i++)a.Append(Line.CreateBound(pts[i],pts[(i+1)%4]));return a;}
  static CurveArray Circle(double x,double y,double r){var a=new CurveArray();for(int i=0;i<4;i++)a.Append(Arc.Create(new XYZ(x,y,0),r,i*Math.PI/2,(i+1)*Math.PI/2,XYZ.BasisX,XYZ.BasisY));return a;}
  static Floor Flat(Document d,FloorType type,Level level,CurveArray curves,double offset){var f=d.Create.NewFloor(curves,type,level,false);f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(offset);d.Regenerate();return f;}
  static void Grade(Document d,Floor f,double origin,bool multi){var ed=f.SlabShapeEditor;ed.Enable();d.Regenerate();foreach(SlabShapeVertex v in ed.SlabShapeVertices){var q=v.Position;var z=.01*(q.X-origin)+.02*q.Y;if(multi&&q.X-origin>5&&q.Y>5)z+=.13;ed.ModifySubElement(v,z);}d.Regenerate();}
  static object Check(Document d,Type geometry,Floor a,Floor b,string name,string expectation){var rows=new[]{a,b}.Select(f=>new ModelElementCheck{Id=f.Id.IntegerValue,UniqueId=f.UniqueId,Name=f.Name,Slot="建筑区域",Entity="IfcSlab",Category="楼板",LevelUniqueId=d.GetElement(f.LevelId).UniqueId}).ToArray();geometry.GetMethod("CheckAggregation",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{d,rows});var issues=rows.Select(r=>r.AreaAggregationIssue).Where(s=>!string.IsNullOrWhiteSpace(s)).ToArray();bool passed=expectation=="clear"?issues.Length==0:expectation=="overlap"?issues.Length==2&&issues.All(s=>s.StartsWith("建筑区域投影重叠")):issues.Length==2&&issues.All(s=>s.StartsWith("区域投影交叠未能可靠核对"));object diagnostic=null;if(!passed){string error=null;try{foreach(var f in new[]{a,b}){var solids=(Solid[])geometry.GetMethod("ProjectionSolids",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{f});if(solids!=null)foreach(var solid in solids)solid.Dispose();}}catch(Exception ex){error=ex.GetBaseException().ToString();}diagnostic=new{error,faces=HostObjectUtils.GetTopFaces(a).Select(r=>(Face)a.GetGeometryObjectFromReference(r)).Select(f=>new{type=f.GetType().Name,curves=f.GetEdgesAsCurveLoops().SelectMany(l=>l).Select(c=>c.GetType().Name).Distinct().ToArray()}).ToArray()};}return new{name,expectation,passed,issues,diagnostic};}
  public static object Run(UIApplication ui,string candidateAssembly){
   var assembly=string.IsNullOrWhiteSpace(candidateAssembly)?AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Hifc.V1.Revit"):Assembly.LoadFrom(candidateAssembly);
   var geometry=assembly.GetType("Hifc.Revit.Management.ModelGeometry");var active=ui.ActiveUIDocument.Document.PathName;var doc=ui.Application.NewProjectDocument(UnitSystem.Metric);var results=new List<object>();int before=ui.Application.Documents.Size;
   try{
    var level=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().First();var type=new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First();
    using(var tx=new Transaction(doc,"isolated projection regression fixtures")){
     tx.Start();double x=0;
     var a=Flat(doc,type,level,Box(x,0,x+10,10),0);var b=Flat(doc,type,level,Box(x+10,0,x+20,10),5);results.Add(Check(doc,geometry,a,b,"flat shared edge","clear"));x+=40;
     a=Flat(doc,type,level,Box(x,0,x+10,10),0);b=Flat(doc,type,level,Box(x+5,5,x+15,15),5);results.Add(Check(doc,geometry,a,b,"flat projection overlap despite height separation","overlap"));x+=40;
     a=Flat(doc,type,level,Box(x,0,x+10,10),0);doc.Create.NewOpening(a,Box(x+3,3,x+7,7),true);doc.Regenerate();Grade(doc,a,x,false);b=Flat(doc,type,level,Box(x+3,3,x+7,7),5);results.Add(Check(doc,geometry,a,b,"sloping slab hole contains another floor","clear"));
     var c=Flat(doc,type,level,Box(x+2,2,x+4,4),10);results.Add(Check(doc,geometry,a,c,"sloping slab actual projected overlap","overlap"));x+=40;
     a=Flat(doc,type,level,Box(x,0,x+10,10),0);doc.Create.NewOpening(a,Box(x+3,3,x+7,7),true);doc.Regenerate();Grade(doc,a,x,true);b=Flat(doc,type,level,Box(x+3,3,x+7,7),5);results.Add(Check(doc,geometry,a,b,"graded RuledFace keeps hole","clear"));c=Flat(doc,type,level,Box(x+2,2,x+4,4),10);results.Add(Check(doc,geometry,a,c,"graded RuledFace actual projected overlap","overlap"));x+=40;
     a=Flat(doc,type,level,Circle(x+10,10,8),0);doc.Create.NewOpening(a,Circle(x+10,10,3),true);doc.Regenerate();b=Flat(doc,type,level,Box(x+9,9,x+11,11),5);results.Add(Check(doc,geometry,a,b,"horizontal curved slab preserves analytic hole","clear"));x+=40;
     a=doc.Create.NewSlab(Circle(x+10,10,8),level,Line.CreateBound(new XYZ(x+8,10,0),new XYZ(x+12,10,0)),.03,false);a.ChangeTypeId(type.Id);doc.Regenerate();b=Flat(doc,type,level,Box(x+9,9,x+11,11),5);results.Add(Check(doc,geometry,a,b,"unsupported tilted curved boundary stays blocked","unknown"));
     tx.RollBack();
    }
   }finally{doc.Close(false);}
   return new{results,assembly=assembly.FullName,originalActiveUnchanged=ui.ActiveUIDocument.Document.PathName==active,temporaryDocumentClosed=ui.Application.Documents.Size==before-1};
  }
 }
}
