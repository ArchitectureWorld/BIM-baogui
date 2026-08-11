using System;
using System.Reflection;
using BIMBaoGui.Stage01.Revit;
using BIMBaoGui.Stage01.Stage02;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class RevitHostCallbackInvokerTests
  {
    [Fact]
    public void ReadOperationCapturePreservesOriginalExceptionIdentity()
    {
      Type operationType = typeof(RevitHostCallbackInvoker).Assembly.GetType(
        "BIMBaoGui.Stage01.Revit.RevitHostReadOperation",
        false);
      Assert.True(operationType != null, "Expected typed Revit read capture seam.");
      MethodInfo capture = operationType.GetMethod(
        "Capture",
        BindingFlags.Static | BindingFlags.NonPublic);
      Assert.NotNull(capture);
      var expected = new InvalidOperationException("revit read failed");
      MethodInfo genericCapture = capture.MakeGenericMethod(typeof(object));

      object result = genericCapture.Invoke(
        null,
        new object[] { new Func<object>(() => throw expected) });
      PropertyInfo exception = result.GetType().GetProperty(
        "Exception",
        BindingFlags.Instance | BindingFlags.NonPublic);

      Assert.NotNull(exception);
      Assert.Same(expected, exception.GetValue(result, null));
    }

    [Fact]
    public void AvailableContextAlwaysInvokesBusinessAction()
    {
      var context = new object();
      bool actionCalled = false;
      string reportedFailure = string.Empty;

      RevitHostCallbackInvoker.Invoke(
        context,
        new InvalidOperationException("context missing"),
        actualContext =>
        {
          Assert.Same(context, actualContext);
          actionCalled = true;
          reportedFailure = "当前没有活动 Revit 项目文档。";
        },
        null);

      Assert.True(actionCalled);
      Assert.Equal("当前没有活动 Revit 项目文档。", reportedFailure);
    }

    [Fact]
    public void ActionFailureIsForwardedExactlyOnce()
    {
      var expected = new InvalidOperationException("action failed");
      Exception forwarded = null;
      int forwardCount = 0;

      RevitHostCallbackInvoker.Invoke(
        new object(),
        new InvalidOperationException("context missing"),
        _ => throw expected,
        exception =>
        {
          forwardCount++;
          forwarded = exception;
        });

      Assert.Equal(1, forwardCount);
      Assert.Same(expected, forwarded);
    }

    [Theory]
    [InlineData("missing-context")]
    [InlineData("resolver-throw")]
    [InlineData("action-throw")]
    public void CallbackFailureCompletesWriteAttemptExactlyOnce(
      string failureMode)
    {
      var state = new Stage02PreparationWriteAttemptState();
      Guid attemptToken = state.BeginAttempt();
      var expected = new InvalidOperationException(failureMode);
      Exception completedException = null;
      int completionCount = 0;
      var completionGate =
        new Stage02PreparationCompletionGate<Exception>(exception =>
        {
          completionCount++;
          completedException = exception;
          Assert.Equal(
            Stage02PreparationWriteCompletionDisposition.Publish,
            state.CompleteAttempt(attemptToken, "host-callback-failure.json"));
        });

      Func<object> resolver = () => new object();
      Action<object> action = _ => { };
      if (string.Equals(
        failureMode,
        "missing-context",
        StringComparison.Ordinal))
      {
        resolver = () => null;
      }
      else if (string.Equals(
        failureMode,
        "resolver-throw",
        StringComparison.Ordinal))
      {
        resolver = () => throw expected;
      }
      else
      {
        action = _ => throw expected;
      }

      RevitHostCallbackInvoker.Invoke(
        resolver,
        expected,
        action,
        exception => completionGate.TryComplete(exception));

      Assert.Equal(1, completionCount);
      Assert.Same(expected, completedException);
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Idle, state.Phase);
      Assert.False(
        completionGate.TryComplete(
          new InvalidOperationException("duplicate callback")));
      Assert.Equal(1, completionCount);
      Assert.Equal(
        "host-callback-failure.json",
        state.LastFailureReportPath);
    }

    [Fact]
    public void CompletionConsumerFailureDoesNotDeliverASecondCompletion()
    {
      var state = new Stage02PreparationWriteAttemptState();
      Guid attemptToken = state.BeginAttempt();
      var expected = new InvalidOperationException("completion consumer failed");
      Exception forwarded = null;
      int completionCount = 0;
      int callbackFailureCount = 0;
      var completionGate =
        new Stage02PreparationCompletionGate<object>(_ =>
        {
          completionCount++;
          Assert.Equal(
            Stage02PreparationWriteCompletionDisposition.Publish,
            state.CompleteAttempt(attemptToken, string.Empty));
          throw expected;
        });

      RevitHostCallbackInvoker.Invoke(
        new object(),
        new InvalidOperationException("context missing"),
        _ => completionGate.TryComplete(new object()),
        exception =>
        {
          callbackFailureCount++;
          forwarded = exception;
          Assert.False(completionGate.TryComplete(new object()));
        });

      Assert.Equal(1, completionCount);
      Assert.Equal(1, callbackFailureCount);
      Assert.Same(expected, forwarded);
      Assert.Equal(Stage02PreparationWriteAttemptPhase.Idle, state.Phase);
    }

    [Fact]
    public void NullFailureCallbackPreservesOriginalThrow()
    {
      var expected = new InvalidOperationException("action failed");

      InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
        () => RevitHostCallbackInvoker.Invoke(
          new object(),
          new InvalidOperationException("context missing"),
          _ => throw expected,
          null));

      Assert.Same(expected, actual);
    }

    [Fact]
    public void MissingContextIsForwardedWithoutInvokingBusinessAction()
    {
      var expected = new InvalidOperationException("context missing");
      Exception forwarded = null;
      bool actionCalled = false;

      RevitHostCallbackInvoker.Invoke<object>(
        (object)null,
        expected,
        _ => actionCalled = true,
        exception => forwarded = exception);

      Assert.False(actionCalled);
      Assert.Same(expected, forwarded);
    }

    [Fact]
    public void MissingContextWithNullFailureCallbackPreservesThrow()
    {
      var expected = new InvalidOperationException("context missing");

      InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
        () => RevitHostCallbackInvoker.Invoke<object>(
          (object)null,
          expected,
          _ => { },
          null));

      Assert.Same(expected, actual);
    }

    [Fact]
    public void ResolverFailureIsForwardedExactlyOnceWithoutInvokingAction()
    {
      var expected = new InvalidOperationException("resolver failed");
      Func<object> resolver = () => throw expected;
      Exception forwarded = null;
      int forwardCount = 0;
      bool actionCalled = false;

      RevitHostCallbackInvoker.Invoke<object>(
        resolver,
        new InvalidOperationException("context missing"),
        _ => actionCalled = true,
        exception =>
        {
          forwardCount++;
          forwarded = exception;
        });

      Assert.False(actionCalled);
      Assert.Equal(1, forwardCount);
      Assert.Same(expected, forwarded);
    }

    [Fact]
    public void ResolverFailureWithNullFailureCallbackPreservesThrow()
    {
      var expected = new InvalidOperationException("resolver failed");
      Func<object> resolver = () => throw expected;

      InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
        () => RevitHostCallbackInvoker.Invoke<object>(
          resolver,
          new InvalidOperationException("context missing"),
          _ => { },
          null));

      Assert.Same(expected, actual);
    }

    [Fact]
    public void ResolverReturningNullUsesMissingContextFailure()
    {
      var expected = new InvalidOperationException("context missing");
      Func<object> resolver = () => null;
      Exception forwarded = null;
      bool actionCalled = false;

      RevitHostCallbackInvoker.Invoke<object>(
        resolver,
        expected,
        _ => actionCalled = true,
        exception => forwarded = exception);

      Assert.False(actionCalled);
      Assert.Same(expected, forwarded);
    }

    [Fact]
    public void SuccessfulResolverInvokesActionWithResolvedContext()
    {
      var expected = new object();
      int resolverCount = 0;
      int actionCount = 0;

      RevitHostCallbackInvoker.Invoke<object>(
        () =>
        {
          resolverCount++;
          return expected;
        },
        new InvalidOperationException("context missing"),
        actual =>
        {
          actionCount++;
          Assert.Same(expected, actual);
        },
        null);

      Assert.Equal(1, resolverCount);
      Assert.Equal(1, actionCount);
    }
  }
}
