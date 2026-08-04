using System;
using BIMBaoGui.Stage01.Revit;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class RevitHostCallbackInvokerTests
  {
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
