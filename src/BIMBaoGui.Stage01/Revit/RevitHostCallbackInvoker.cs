using System;
using System.Threading;

namespace BIMBaoGui.Stage01.Revit
{
  internal sealed class RevitHostReadCapture<T>
  {
    internal RevitHostReadCapture(T result, Exception exception)
    {
      Result = result;
      Exception = exception;
    }

    internal T Result { get; }
    internal Exception Exception { get; }
    internal bool Success => Exception == null;
  }

  internal static class RevitHostReadOperation
  {
    internal static RevitHostReadCapture<T> Capture<T>(Func<T> read)
    {
      if (read == null) throw new ArgumentNullException(nameof(read));
      try
      {
        return new RevitHostReadCapture<T>(read(), null);
      }
      catch (Exception exception)
      {
        return new RevitHostReadCapture<T>(default(T), exception);
      }
    }
  }

  internal sealed class RevitHostCallbackExecutionGate
  {
    private const int Pending = 0;
    private const int CurrentCallbackStarted = 1;
    private const int LegacyFallbackClaimed = 2;
    private int _route = Pending;
    private int _businessCallbackStarted;
    private int _failureCallbackStarted;

    internal bool HasCurrentCallbackStarted =>
      Volatile.Read(ref _route) == CurrentCallbackStarted;

    internal bool IsLegacyFallbackClaimed =>
      Volatile.Read(ref _route) == LegacyFallbackClaimed;

    internal bool TryStartCurrentBusinessCallback()
    {
      if (Interlocked.CompareExchange(
        ref _route,
        CurrentCallbackStarted,
        Pending) == LegacyFallbackClaimed)
      {
        return false;
      }
      return Interlocked.CompareExchange(
        ref _businessCallbackStarted,
        1,
        0) == 0;
    }

    internal bool TryStartCurrentFailureCallback()
    {
      if (Interlocked.CompareExchange(
        ref _route,
        CurrentCallbackStarted,
        Pending) == LegacyFallbackClaimed)
      {
        return false;
      }
      return Interlocked.CompareExchange(
        ref _failureCallbackStarted,
        1,
        0) == 0;
    }

    internal bool TryClaimLegacyFallback()
    {
      return Interlocked.CompareExchange(
        ref _route,
        LegacyFallbackClaimed,
        Pending) == Pending;
    }
  }

  internal static class RevitHostCallbackInvoker
  {
    internal static void Invoke<TContext>(
      TContext context,
      Exception missingContextFailure,
      Action<TContext> action,
      Action<Exception> callbackFailure)
      where TContext : class
    {
      Invoke<TContext>(
        new Func<TContext>(() => context),
        missingContextFailure,
        action,
        callbackFailure);
    }

    internal static void Invoke<TContext>(
      Func<TContext> contextResolver,
      Exception missingContextFailure,
      Action<TContext> action,
      Action<Exception> callbackFailure)
      where TContext : class
    {
      if (contextResolver == null)
        throw new ArgumentNullException(nameof(contextResolver));
      if (missingContextFailure == null)
        throw new ArgumentNullException(nameof(missingContextFailure));
      if (action == null) throw new ArgumentNullException(nameof(action));

      try
      {
        TContext context = contextResolver();
        if (context == null) throw missingContextFailure;
        action(context);
      }
      catch (Exception exception)
      {
        if (callbackFailure == null) throw;
        callbackFailure(exception);
      }
    }
  }
}
