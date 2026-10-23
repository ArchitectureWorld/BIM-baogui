using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Hifc.Reference;
using Hifc.Revit.Management;
using Microsoft.Win32;

namespace Hifc.Revit
{
    internal static class ExportDestinationPicker
    {
        internal static ModelDetection Detect(Document doc)
            =>CaseWorkflow.Detect(new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .Where(CaseScanJob.IsModelRecord).Select(e=>CaseModelReader.InventoryForDetection(doc,e)).ToArray());
        internal static string DefaultPath(Document doc,string kind,DateTime date)
        {
            var directory=ExportFileNaming.InitialDirectory(doc.PathName,Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            return ExportFileNaming.NextPath(directory,ExportFileNaming.Stem(doc.PathName,doc.Title,PlatformContext.Stage(doc),kind,date));
        }
        internal static string Pick(UIApplication ui)
        {
            var doc=ui.ActiveUIDocument.Document;
            ManagerController.EnsureExportReady(doc);
            var stage=PlatformContext.Stage(doc);
            if(stage!="报规"&&stage!="施工图")throw new InvalidOperationException("请先在模型属性管理中人工确认报规或施工图阶段，再导出 IFC。");
            var detection=Detect(doc);var kind=detection.Kind;
            var filenameKind=ExportFileNaming.FileKind(doc.PathName,doc.Title);
            if(kind!="总图"&&kind!="单体建筑"||filenameKind!=null&&filenameKind!=kind)
            {
                var question=new TaskDialog("H-IFC V1 · 确认导出模型类型") {
                    MainInstruction="模型内容与文件名尚不能一致确定类型，请确认本次导出类型。",
                    MainContent="模型识别："+kind+"\n文件名标识："+(filenameKind??"无明确标识")+"\n"+string.Join("\n",detection.Evidence),
                    CommonButtons=TaskDialogCommonButtons.Cancel,DefaultButton=TaskDialogResult.Cancel};
                question.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,"按单体建筑导出");
                question.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,"按总图导出");
                var answer=question.Show();
                if(answer==TaskDialogResult.CommandLink1)kind="单体建筑";
                else if(answer==TaskDialogResult.CommandLink2)kind="总图";
                else return null;
            }
            var suggestion=DefaultPath(doc,kind,DateTime.Now);
            var dialog=new SaveFileDialog {Title="导出项目 IFC · 选择位置和名称",Filter="IFC (*.ifc)|*.ifc",
                InitialDirectory=Path.GetDirectoryName(suggestion),FileName=Path.GetFileName(suggestion),
                DefaultExt=".ifc",AddExtension=true,CheckPathExists=true,ValidateNames=true,OverwritePrompt=false};
            if(dialog.ShowDialog()!=true)return null;
            // Recompute against the folder the user actually chose. The following
            // review displays this final path before any export files are written.
            return ExportFileNaming.ResolveSelection(dialog.FileName);
        }
    }
}
