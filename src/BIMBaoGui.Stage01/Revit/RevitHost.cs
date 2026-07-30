using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class RevitHost
  {
    private const string HostTypeName = "RhinoInside.Revit.Revit, RhinoInside.Revit";

    public static bool TryGetContext(out UIApplication uiApplication, out UIDocument uiDocument, out Document document, out string error)
    {
      uiApplication = null;
      uiDocument = null;
      document = null;
      error = string.Empty;

      Type hostType = Type.GetType(HostTypeName, false);
      if (hostType == null)
      {
        error = "当前不是 Rhino.Inside.Revit 环境。请先打开 Revit，再从 Revit 的 Rhino.Inside 面板启动 Grasshopper。";
        return false;
      }

      try
      {
        uiApplication = ReadStaticProperty<UIApplication>(hostType, "ActiveUIApplication");
        uiDocument = ReadStaticProperty<UIDocument>(hostType, "ActiveUIDocument");
        document = ReadStaticProperty<Document>(hostType, "ActiveDBDocument");
      }
      catch (Exception exception)
      {
        error = "无法取得 Rhino.Inside.Revit 活动文档：" + exception.Message;
        return false;
      }

      if (uiApplication == null || document == null)
      {
        error = "Revit 当前没有可用的活动项目文档。";
        return false;
      }

      return true;
    }

    public static bool EnqueueAction(Action<UIApplication> action, out string error)
    {
      error = string.Empty;
      if (action == null)
      {
        error = "提交动作为空。";
        return false;
      }

      Type hostType = Type.GetType(HostTypeName, false);
      if (hostType == null)
      {
        error = "RhinoInside.Revit.Revit 未加载。";
        return false;
      }

      MethodInfo enqueueMethod = hostType
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .FirstOrDefault(method => method.Name == "EnqueueAction" && method.GetParameters().Length == 1);
      if (enqueueMethod == null)
      {
        error = "当前 Rhino.Inside.Revit 版本未暴露 EnqueueAction。";
        return false;
      }

      try
      {
        Type delegateType = enqueueMethod.GetParameters()[0].ParameterType;
        object compatibleDelegate = delegateType.IsInstanceOfType(action)
          ? (object) action
          : Delegate.CreateDelegate(delegateType, action.Target, action.Method);
        enqueueMethod.Invoke(null, new[] { compatibleDelegate });
        return true;
      }
      catch (TargetInvocationException exception)
      {
        error = exception.InnerException?.Message ?? exception.Message;
        return false;
      }
      catch (Exception exception)
      {
        error = exception.Message;
        return false;
      }
    }

    private static T ReadStaticProperty<T>(Type hostType, string propertyName) where T : class
    {
      PropertyInfo property = hostType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
      return property?.GetValue(null, null) as T;
    }
  }
}
