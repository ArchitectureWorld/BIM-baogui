try {
var flags = System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
var asm=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Hifc.V1.Revit");
var json=asm.GetType("Hifc.Revit.Evidence").GetMethod("Json",flags);
var targetPath=(string)parameters[0];var outputDirectory=(string)parameters[1];
var target=document.Application.Documents.Cast<Autodesk.Revit.DB.Document>().Single(d=>string.Equals(System.IO.Path.GetFullPath(d.PathName),System.IO.Path.GetFullPath(targetPath),StringComparison.OrdinalIgnoreCase));
System.IO.Directory.CreateDirectory(outputDirectory);
System.Converter<Autodesk.Revit.DB.XYZ,double[]> point = p=>new[]{p.X*0.3048,p.Y*0.3048,p.Z*0.3048};
System.Converter<Autodesk.Revit.DB.GeometryElement,System.Collections.Generic.List<Autodesk.Revit.DB.Solid>> collectSolids=null;
collectSolids=geometry=>{var solids=new System.Collections.Generic.List<Autodesk.Revit.DB.Solid>();if(geometry!=null)foreach(var item in geometry){var solid=item as Autodesk.Revit.DB.Solid;if(solid!=null&&solid.Volume>1e-10)solids.Add(Autodesk.Revit.DB.SolidUtils.Clone(solid));var instance=item as Autodesk.Revit.DB.GeometryInstance;if(instance!=null)solids.AddRange(collectSolids(instance.GetInstanceGeometry()));}return solids;};
System.Converter<Autodesk.Revit.DB.Element,object[]> pars = e=>e==null?new object[0]:e.Parameters.Cast<Autodesk.Revit.DB.Parameter>().Where(p=>p.HasValue && !p.Definition.Name.Contains("材质")).Select(p=>(object)new{ name=p.Definition.Name,storage=p.StorageType.ToString(),definitionType=p.Definition.ParameterType.ToString(),shared=p.IsShared,guid=p.IsShared?p.GUID.ToString():null,readOnly=p.IsReadOnly,value=p.StorageType==Autodesk.Revit.DB.StorageType.String?(object)p.AsString():p.StorageType==Autodesk.Revit.DB.StorageType.Double?(object)p.AsDouble():p.StorageType==Autodesk.Revit.DB.StorageType.Integer?(object)p.AsInteger():(object)p.AsElementId().IntegerValue,display=p.AsValueString() }).ToArray();
foreach(var d in new[]{target}) {
 var list=new System.Collections.Generic.List<object>();
 foreach(var e in new Autodesk.Revit.DB.FilteredElementCollector(d).WhereElementIsNotElementType().Where(e=>e is Autodesk.Revit.DB.Floor || e is Autodesk.Revit.DB.Area || e is Autodesk.Revit.DB.Level || e is Autodesk.Revit.DB.ProjectInfo || e is Autodesk.Revit.DB.RevitLinkInstance || e is Autodesk.Revit.DB.Architecture.Room || e.Category!=null && e.Category.Id.IntegerValue==(int)Autodesk.Revit.DB.BuiltInCategory.OST_Parking)) {
  var loops=new System.Collections.Generic.List<object>(); string geometryError=null; int? connectedComponents=null; string connectivityError=null;
  if(e is Autodesk.Revit.DB.Floor)try{using(var options=new Autodesk.Revit.DB.Options{DetailLevel=Autodesk.Revit.DB.ViewDetailLevel.Fine}){var solids=collectSolids(e.get_Geometry(options));Autodesk.Revit.DB.Solid body=null;foreach(var solid in solids)body=body==null?solid:Autodesk.Revit.DB.BooleanOperationsUtils.ExecuteBooleanOperation(body,solid,Autodesk.Revit.DB.BooleanOperationsType.Union);if(body==null)throw new Exception("No solid geometry");connectedComponents=Autodesk.Revit.DB.SolidUtils.SplitVolumes(body).Count(s=>s.Volume>1e-10);}}catch(Exception ex){connectivityError=ex.Message;}
  try {
   if(e is Autodesk.Revit.DB.Area) {
    using(var options=new Autodesk.Revit.DB.SpatialElementBoundaryOptions()) {
     var bounds=((Autodesk.Revit.DB.Area)e).GetBoundarySegments(options);
     if(bounds!=null)foreach(var loop in bounds)loops.Add(loop.Select(s=>new{kind=s.GetCurve().GetType().Name,points=s.GetCurve().Tessellate().Select(p=>point(p)).ToArray()}).ToArray());
    }
   } else if(e is Autodesk.Revit.DB.Floor) {
    foreach(var reference in Autodesk.Revit.DB.HostObjectUtils.GetTopFaces((Autodesk.Revit.DB.HostObject)e)) {
     var face=e.GetGeometryObjectFromReference(reference) as Autodesk.Revit.DB.Face;
     if(face!=null)foreach(var loop in face.GetEdgesAsCurveLoops()) loops.Add(loop.Select(c=>new{kind=c.GetType().Name,points=c.Tessellate().Select(p=>point(p)).ToArray()}).ToArray());
    }
   }
  } catch(Exception ex){geometryError=ex.Message;}
  var bb=e.get_BoundingBox(null);var lp=e.Location as Autodesk.Revit.DB.LocationPoint; var type=d.GetElement(e.GetTypeId());
  object proj=null; if(e is Autodesk.Revit.DB.Floor) { try { proj=asm.GetType("Hifc.Revit.Management.ModelGeometry").GetMethod("ProjectedAreaM2",flags).Invoke(null,new object[]{e}); } catch(Exception ex) { geometryError="projection: "+ex.ToString(); } }
  list.Add(new{id=e.Id.IntegerValue,uid=e.UniqueId,name=e.Name,category=e.Category==null?null:e.Category.Name,apiType=e.GetType().FullName,typeId=e.GetTypeId().IntegerValue,typeName=type==null?null:type.Name,levelId=e.LevelId.IntegerValue,levelName=d.GetElement(e.LevelId)==null?null:d.GetElement(e.LevelId).Name,areaScheme=e is Autodesk.Revit.DB.Area?((Autodesk.Revit.DB.Area)e).AreaScheme.Name:null,areaM2=e is Autodesk.Revit.DB.Area?(double?)(((Autodesk.Revit.DB.Area)e).Area*0.09290304):null,projectedAreaM2=proj,connectedComponents,connectivityError,point=lp==null?null:point(lp.Point),rotation=lp==null || !(e is Autodesk.Revit.DB.FamilyInstance)?(double?)null:lp.Rotation,bbox=bb==null?null:new{min=point(bb.Transform.OfPoint(bb.Min)),max=point(bb.Transform.OfPoint(bb.Max))},loops,geometryError,parameters=pars(e),typeParameters=pars(type)});
 }
 var name="inventory.json";
 System.IO.File.WriteAllText(System.IO.Path.Combine(outputDirectory,name),(string)json.Invoke(null,new object[]{new{path=d.PathName,title=d.Title,modified=d.IsModified,units="coordinates m; areas m2; raw native parameter doubles remain internal Revit units",elements=list}}),new System.Text.UTF8Encoding(false));
}
var jobType=asm.GetType("Hifc.Revit.Management.CaseScanJob",true);
var job=Activator.CreateInstance(jobType,flags,null,new object[]{target,(string)parameters[2],(string)parameters[3]},null);
jobType.GetMethod("ReadNext",flags).Invoke(job,new object[]{target,100000});
var snapshot=jobType.GetMethod("Snapshot",flags).Invoke(job,new object[]{target});
System.IO.File.WriteAllText(System.IO.Path.Combine(outputDirectory,"snapshot.json"),(string)json.Invoke(null,new object[]{snapshot}),new System.Text.UTF8Encoding(false));
return new{captured=true,path=target.PathName,count=((Array)snapshot.GetType().GetProperty("Elements").GetValue(snapshot,null)).Length};
} catch(Exception ex) { return new{error=ex.ToString()}; }
