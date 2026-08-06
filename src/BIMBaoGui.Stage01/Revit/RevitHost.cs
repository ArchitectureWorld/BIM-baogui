using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
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
      return TryGetContext(
        out uiapp,
        out uidoc,
        out document,
        out error,
        out _);
    }

    public static bool TryGetContext(
      out UIApplication uiapp,
      out UIDocument uidoc,
      out Document document,
      out string error,
      out Exception exception)
    {
      uiapp = null;
      uidoc = null;
      document = null;
      error = string.Empty;
      exception = null;

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
      catch (Exception caught)
      {
        exception = Unwrap(caught);
        error = "读取 Rhino.Inside.Revit 当前文档失败：" + exception.Message;
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
      return RunReadInHostContext(
        read,
        out result,
        out error,
        out _);
    }

    public static bool RunReadInHostContext<T>(
      Func<T> read,
      out T result,
      out string error,
      out Exception exception)
    {
      result = default(T);
      error = string.Empty;
      exception = null;
      if (read == null)
      {
        error = "读取操作不能为空。";
        exception = new ArgumentNullException(nameof(read));
        return false;
      }

      RevitHostReadCapture<T> capture = null;
      Action hostAction = () => capture = RevitHostReadOperation.Capture(read);

      if (!TryInvokeCurrentHostContext(
        hostAction,
        out string invokeError,
        out Exception invokeException))
      {
        if (!TryGetContext(
          out _,
          out _,
          out _,
          out string contextError,
          out Exception contextException))
        {
          error = string.IsNullOrWhiteSpace(invokeError) ? contextError : invokeError + " " + contextError;
          exception = contextException
            ?? invokeException;
          return false;
        }
        hostAction();
      }

      if (capture == null)
      {
        error = "Revit 读取操作失败：宿主回调未执行读取操作。";
        exception = new InvalidOperationException(error);
        return false;
      }

      if (!capture.Success)
      {
        exception = Unwrap(capture.Exception);
        error = "Revit 读取操作失败：" + exception.Message;
        return false;
      }

      result = capture.Result;
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
      return EnqueueAction(uiAction, null, out error);
    }

    public static bool EnqueueAction(
      Action<UIApplication> uiAction,
      Action<Exception> callbackFailure,
      out string error)
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

      var callbackGate = new RevitHostCallbackExecutionGate();
      Action<UIApplication> currentUiAction = uiApplication =>
      {
        if (!callbackGate.TryStartCurrentBusinessCallback()) return;
        uiAction(uiApplication);
      };
      Action<Exception> currentCallbackFailure = exception =>
      {
        if (callbackGate.IsLegacyFallbackClaimed) return;
        if (callbackFailure == null) throw exception;
        if (!callbackGate.TryStartCurrentFailureCallback()) return;
        callbackFailure(exception);
      };
      Action hostAction = () =>
      {
        InvokeUiAction(
          hostType,
          null,
          currentUiAction,
          currentCallbackFailure);
      };

      if (TryInvokeCurrentHostContext(
        hostAction,
        out string currentError,
        out Exception currentException))
      {
        return true;
      }

      if (callbackGate.HasCurrentCallbackStarted)
        RethrowStartedCallbackFailure(currentException, currentError);

      if (!callbackGate.TryClaimLegacyFallback())
      {
        if (callbackGate.HasCurrentCallbackStarted)
          RethrowStartedCallbackFailure(currentException, currentError);
        return true;
      }

      if (TryInvokeLegacyQueue(
        hostType,
        uiAction,
        callbackFailure,
        out string legacyError))
        return true;

      error = "无法进入有效的 Revit API 上下文。";
      if (!string.IsNullOrWhiteSpace(currentError)) error += " 当前接口：" + currentError;
      if (!string.IsNullOrWhiteSpace(legacyError)) error += " 兼容接口：" + legacyError;
      return false;
    }

    private static void RethrowStartedCallbackFailure(
      Exception exception,
      string error)
    {
      if (exception != null)
        ExceptionDispatchInfo.Capture(exception).Throw();
      throw new InvalidOperationException(
        string.IsNullOrWhiteSpace(error)
          ? "Revit 业务回调已开始，但当前接口执行失败。"
          : error);
    }

    private static bool TryInvokeCurrentHostContext(Action action, out string error)
    {
      return TryInvokeCurrentHostContext(action, out error, out _);
    }

    private static bool TryInvokeCurrentHostContext(
      Action action,
      out string error,
      out Exception exception)
    {
      error = string.Empty;
      exception = null;
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
      catch (Exception caught)
      {
        exception = Unwrap(caught);
        error = exception.Message;
        return false;
      }
    }

    private static bool TryInvokeLegacyQueue(
      Type hostType,
      Action<UIApplication> uiAction,
      Action<Exception> callbackFailure,
      out string error)
    {
      error = string.Empty;
      int callbackStarted = 0;
      Action<UIApplication> execute = uiApplication =>
      {
        if (Interlocked.CompareExchange(ref callbackStarted, 1, 0) != 0)
          return;
        InvokeUiAction(
          hostType,
          uiApplication,
          uiAction,
          callbackFailure);
      };
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
          callback = new Action<Document>(_ => execute(null));
        }
        else if (parameterType == typeof(Action<UIApplication>))
        {
          callback = new Action<UIApplication>(execute);
        }
        else if (parameterType == typeof(Action))
        {
          callback = new Action(() => execute(null));
        }

        if (callback == null) continue;

        try
        {
          candidate.Invoke(null, new[] { callback });
          return true;
        }
        catch (Exception caught)
        {
          Exception exception = Unwrap(caught);
          if (Volatile.Read(ref callbackStarted) != 0)
            ExceptionDispatchInfo.Capture(exception).Throw();
          error = exception.Message;
        }
      }

      if (string.IsNullOrWhiteSpace(error)) error = "未找到兼容的 EnqueueAction 委托签名。";
      return false;
    }

    private static void InvokeUiAction(
      Type hostType,
      UIApplication uiApplication,
      Action<UIApplication> uiAction,
      Action<Exception> callbackFailure)
    {
      Action<Exception> forwardedFailure = callbackFailure == null
        ? null
        : new Action<Exception>(exception =>
          callbackFailure(Unwrap(exception)));
      RevitHostCallbackInvoker.Invoke(
        () => uiApplication
          ?? ReadStaticProperty<UIApplication>(
            hostType,
            "ActiveUIApplication"),
        new InvalidOperationException(
          "Rhino.Inside.Revit 当前没有活动 UIApplication。"),
        uiAction,
        forwardedFailure);
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
