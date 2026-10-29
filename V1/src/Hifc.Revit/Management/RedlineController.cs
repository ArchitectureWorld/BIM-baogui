using System;
using Autodesk.Revit.UI;
using Hifc.Reference;

namespace Hifc.Revit.Management
{
    // Called from an external command or a manager ExternalEvent.Execute callback.
    // ShowDialog keeps callbacks inside that API context; no modeless API access.
    internal static class RedlineController
    {
        internal static void Show(UIApplication ui)
        {
            var document=ui?.ActiveUIDocument?.Document;
            if(document==null){TaskDialog.Show("红线人工资料","请先打开项目模型。");return;}
            try {
                var session=new Session(document);
                var window=new RedlineWindow(session,document.Title+" / 当前共享位置："+(document.ActiveProjectLocation?.Name??"未取得"),RedlineProjectStorage.Load(document));
                new System.Windows.Interop.WindowInteropHelper(window).Owner=ui.MainWindowHandle;
                window.ShowDialog();
            }catch(Exception ex){TaskDialog.Show("红线人工资料",ex.Message);}
        }
        sealed class Session:IRedlineSession
        {
            readonly Autodesk.Revit.DB.Document document;
            internal Session(Autodesk.Revit.DB.Document document){this.document=document;}
            public RedlineReport Compare(RedlineData data,double toleranceM,double minimumGapM)
            {
                try{return RedlineGeometry.Compare(document,data,toleranceM,minimumGapM);}
                catch(Autodesk.Revit.Exceptions.ApplicationException ex){throw new InvalidOperationException("Revit 红线几何读取未完成："+ex.Message,ex);}
            }
            public void Save(RedlineData data)
            {
                try{RedlineProjectStorage.Save(document,data);}
                catch(Autodesk.Revit.Exceptions.ApplicationException ex){throw new InvalidOperationException("Revit 人工红线资料保存未完成："+ex.Message,ex);}
            }
        }
    }
}
