using System;
using Autodesk.Revit.UI;
using BIMBaoGui.Stage01.Revit;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class RevitHostRoutingTests
  {
    [Fact]
    public void CurrentEntryFailureFallsBackBeforeBusinessCallbackStarts()
    {
      RhinoInside.Revit.Revit.Reset();
      RhinoInside.Revit.Rhinoceros.Reset(
        RhinoInside.Revit.CurrentInvocationMode.FailBeforeCallback);
      int businessCount = 0;

      bool enqueued = RevitHost.EnqueueAction(
        _ => businessCount++,
        out string error);

      Assert.True(enqueued, error);
      Assert.Equal(1, businessCount);
      Assert.Equal(1, RhinoInside.Revit.Rhinoceros.InvokeCount);
      Assert.Equal(1, RhinoInside.Revit.Revit.LegacyEnqueueCount);
    }

    [Fact]
    public void StartedBusinessCallbackFailurePropagatesWithoutLegacyFallback()
    {
      RhinoInside.Revit.Revit.Reset();
      RhinoInside.Revit.Rhinoceros.Reset(
        RhinoInside.Revit.CurrentInvocationMode.InvokeCallback);
      var expected = new InvalidOperationException(
        "current business callback sentinel");
      int businessCount = 0;
      string error = string.Empty;

      InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
        () => RevitHost.EnqueueAction(
          _ =>
          {
            businessCount++;
            throw expected;
          },
          out error));

      Assert.Same(expected, actual);
      Assert.Equal(1, businessCount);
      Assert.Equal(1, RhinoInside.Revit.Rhinoceros.InvokeCount);
      Assert.Equal(0, RhinoInside.Revit.Revit.LegacyEnqueueCount);
    }

    [Fact]
    public void StartedFailureCallbackExceptionPropagatesWithoutLegacyFallback()
    {
      RhinoInside.Revit.Revit.Reset();
      RhinoInside.Revit.Rhinoceros.Reset(
        RhinoInside.Revit.CurrentInvocationMode.InvokeCallback);
      var businessFailure = new InvalidOperationException(
        "current business failure sentinel");
      var failureCallbackFailure = new InvalidOperationException(
        "failure callback sentinel");
      int businessCount = 0;
      int failureCallbackCount = 0;
      string error = string.Empty;

      InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
        () => RevitHost.EnqueueAction(
          _ =>
          {
            businessCount++;
            throw businessFailure;
          },
          exception =>
          {
            failureCallbackCount++;
            Assert.Same(businessFailure, exception);
            throw failureCallbackFailure;
          },
          out error));

      Assert.Same(failureCallbackFailure, actual);
      Assert.Equal(1, businessCount);
      Assert.Equal(1, failureCallbackCount);
      Assert.Equal(1, RhinoInside.Revit.Rhinoceros.InvokeCount);
      Assert.Equal(0, RhinoInside.Revit.Revit.LegacyEnqueueCount);
    }
  }
}

namespace RhinoInside.Revit
{
  internal enum CurrentInvocationMode
  {
    InvokeCallback,
    FailBeforeCallback
  }

  internal static class Rhinoceros
  {
    internal static CurrentInvocationMode Mode { get; private set; }
    internal static int InvokeCount { get; private set; }

    internal static void Reset(CurrentInvocationMode mode)
    {
      Mode = mode;
      InvokeCount = 0;
    }

    public static void InvokeInHostContext(Action callback)
    {
      InvokeCount++;
      if (Mode == CurrentInvocationMode.FailBeforeCallback)
        throw new InvalidOperationException("current entry sentinel");
      callback();
    }
  }

  internal static class Revit
  {
    public static UIApplication ActiveUIApplication { get; private set; }
    internal static int LegacyEnqueueCount { get; private set; }

    internal static void Reset()
    {
      ActiveUIApplication = new UIApplication();
      LegacyEnqueueCount = 0;
    }

    public static void EnqueueAction(Action<UIApplication> callback)
    {
      LegacyEnqueueCount++;
      callback(ActiveUIApplication);
    }
  }
}

namespace Autodesk.Revit.DB
{
  public class Document
  {
  }
}

namespace Autodesk.Revit.UI
{
  public class UIApplication
  {
    public UIDocument ActiveUIDocument { get; set; }
  }

  public class UIDocument
  {
    public Autodesk.Revit.DB.Document Document { get; set; }
  }
}
