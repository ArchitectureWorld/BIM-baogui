using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Autodesk.Revit.UI;
namespace HifcSkillExperiment {
 public sealed class ApiWork : IExternalEventHandler {
  public Converter<UIApplication,object> Work;
  public string ReceiptPath;
  public ExternalEvent Event;
  public void Execute(UIApplication ui) {
   object result;
   try { result=Work(ui); }
   catch(Exception ex) { result=new {success=false,error=ex.ToString()}; }
   try { File.WriteAllText(ReceiptPath,new JavaScriptSerializer {MaxJsonLength=67108864}.Serialize(result),new UTF8Encoding(false)); }
   finally { Event.Dispose(); }
  }
  public string GetName() { return "HIFC skill isolated experiment"; }
 }
}
