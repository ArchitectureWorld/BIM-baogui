using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BIMBaoGui.Stage01.Revit
{
  internal static class RevitHost
  {
    private const string RevitTypeName = "RhinoInside.Revit.Revit";
    private const string RhinocerosTypeName = "RhinoInside.Revit.Rhinoceros";
    private const string RhinoInsideAssemblyName = "RhinoInside.Revit";

    public static bool TryGetContext(out UIApplication uiapp, out UIDocument uidoc, out Document document, out string error)
    {
      uiapp = null;
      uidoc = null;
      document = null;
      error = string.Empty;

      Type hostType = ResolveRhinoInsideType(RevitTypeName);
      if (hostType == null)
      {
        error = "当前不在 Rhino.Inside.Revit 环境中。请先打开 Revit 2020，再从 Revit 中启动 Rhino.Inside 和 Grasshopper。";
        return false;
      }

      try
      {
        uiapp = ReadStaticProperty<UIApplication>(hostType, "ActiveUIApplication");
        uidoc = ReadStaticProperty<UIDocument>(hostType, "ActiveUIDocument");
        document = ReadStaticProperty<Document>(hostType, "ActiveDBDocument") ?? uidoc?.Document;
        if (uiapp == null || document == null)
        {
          error = "Rhino.Inside.Revit 已加载，但当前没有活动的 Revit 项目文档。";
          return false;
        }

        return true;
      }
      catch (Exception exception)
      {
        error = "读取 Rhino.Inside.Revit 当前文档失败：" + Unwrap(exception).Message;
        return false;
      }
    }

    /// <summary>
    /// Runs Revit read operations inside Rhino.Inside.Revit's current host API context.
    /// Current Rhino 8 builds expose Rhinoceros.InvokeInHostContext(Action).
    /// Legacy builds that do not expose it fall back to direct reads only after a
    /// valid Rhino.Inside.Revit context has been confirmed.
    /// </summary>
    public static bool RunReadInHostContext<T>(Func<T> read, out T result, out string error)
    {
      result = default(T);
      error = string.Empty;
      if (read == null)
      {
        error = "读取操作不能为空。";
        return false;
      }

      T capturedResult = default(T);
      Exception capturedException = null;
      Action hostAction = () =>
      {
        try { capturedResult = read(); }
        catch (Exception exception) { capturedException = exception; }
      };

      if (!TryInvokeCurrentHostContext(hostAction, out string invokeError))
      {
        if (!TryGetContext(out _, out _, out _, out string contextError))
        {
          error = string.IsNullOrWhiteSpace(invokeError) ? contextError : invokeError + " " + contextError;
          return false;
        }
        hostAction();
      }

      if (capturedException != null)
      {
        error = "Revit 读取操作失败：" + Unwrap(capturedException).Message;
        return false;
      }

      result = capturedResult;
      return true;
    }

    /// <summary>
    /// Schedules a Revit write operation in a valid host API context.
    /// Rhino 8 / current Rhino.Inside.Revit uses InvokeInHostContext(Action).
    /// Older Rhino.Inside.Revit builds are supported through the historical
    /// Revit.EnqueueAction(Action&lt;Document&gt;) contract.
    /// </summary>
    public static bool EnqueueAction(Action<UIApplication> uiAction, out string error)
    {
      error = string.Empty;
      if (uiAction == null)
      {
        error = "提交操作不能为空。";
        return false;
      }

      Type hostType = ResolveRhinoInsideType(RevitTypeName);
      if (hostType == null)
      {
        error = "当前未从 Revit 启动 Rhino.Inside.Revit，不能写入 Revit。";
        return false;
      }

      Action hostAction = () =>
      {
        UIApplication current = ReadStaticProperty<UIApplication>(hostType, "ActiveUIApplication");
        if (current == null)
          throw new InvalidOperationException("Rhino.Inside.Revit 当前没有活动 UIApplication。");
        uiAction(current);
      };

      if (TryInvokeCurrentHostContext(hostAction, out string currentError))
        return true;

      if (TryInvokeLegacyQueue(hostType, uiAction, out string legacyError))
        return true;

      error = "无法进入有效的 Revit API 上下文。";
      if (!string.IsNullOrWhiteSpace(currentError)) error += " 当前接口：" + currentError;
      if (!string.IsNullOrWhiteSpace(legacyError)) error += " 兼容接口：" + legacyError;
      return false;
    }

    private static bool TryInvokeCurrentHostContext(Action action, out string error)
    {
      error = string.Empty;
      Type rhinocerosType = ResolveRhinoInsideType(RhinocerosTypeName);
      if (rhinocerosType == null)
      {
        error = "未找到 RhinoInside.Revit.Rhinoceros。";
        return false;
      }

      MethodInfo invoke = rhinocerosType
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .FirstOrDefault(method =>
        {
          if (!string.Equals(method.Name, "InvokeInHostContext", StringComparison.Ordinal)) return false;
          if (method.IsGenericMethodDefinition) return false;
          ParameterInfo[] parameters = method.GetParameters();
          return parameters.Length == 1 && parameters[0].ParameterType == typeof(Action);
        });

      if (invoke == null)
      {
        error = "当前 Rhino.Inside.Revit 未公开 InvokeInHostContext(Action)。";
        return false;
      }

      try
      {
        invoke.Invoke(null, new object[] { action });
        return true;
      }
      catch (Exception exception)
      {
        error = Unwrap(exception).Message;
        return false;
      }
    }

    private static bool TryInvokeLegacyQueue(Type hostType, Action<UIApplication> uiAction, out string error)
    {
      error = string.Empty;
      MethodInfo[] candidates = hostType
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .Where(method => string.Equals(method.Name, "EnqueueAction", StringComparison.Ordinal))
        .Where(method => method.GetParameters().Length == 1)
        .ToArray();

      foreach (MethodInfo candidate in candidates)
      {
        Type parameterType = candidate.GetParameters()[0].ParameterType;
        object callback = null;

        if (parameterType == typeof(Action<Document>))
        {
          callback = new Action<Document>(document =>
          {
            UIApplication current = ReadStaticProperty<UIApplication>(hostType, "ActiveUIApplication");
            if (current == null)
              throw new InvalidOperationException("Rhino.Inside.Revit 当前没有活动 UIApplication。");
            if (document == null)
              throw new InvalidOperationException("Rhino.Inside.Revit 当前没有活动 Document。");
            uiAction(current);
          });
        }
        else if (parameterType == typeof(Action<UIApplication>))
        {
          callback = uiAction;
        }
        else if (parameterType == typeof(Action))
        {
          callback = new Action(() =>
          {
            UIApplication current = ReadStaticProperty<UIApplication>(hostType, "ActiveUIApplication");
            if (current == null)
              throw new InvalidOperationException("Rhino.Inside.Revit 当前没有活动 UIApplication。");
            uiAction(current);
          });
        }

        if (callback == null) continue;

        try
        {
          candidate.Invoke(null, new[] { callback });
          return true;
        }
        catch (Exception exception)
        {
          error = Unwrap(exception).Message;
        }
      }

      if (string.IsNullOrWhiteSpace(error)) error = "未找到兼容的 EnqueueAction 委托签名。";
      return false;
    }

    private static Type ResolveRhinoInsideType(string fullName)
    {
      Type resolved = Type.GetType(fullName + ", " + RhinoInsideAssemblyName, false);
      if (resolved != null) return resolved;

      foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
      {
        try
        {
          Type candidate = assembly.GetType(fullName, false);
          if (candidate != null) return candidate;
        }
        catch { }
      }

      return null;
    }

    private static T ReadStaticProperty<T>(Type type, string name) where T : class
    {
      PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
      return property?.GetValue(null, null) as T;
    }

    private static Exception Unwrap(Exception exception)
    {
      Exception current = exception;
      while (current is TargetInvocationException && current.InnerException != null)
        current = current.InnerException;
      return current;
    }
  }
}
