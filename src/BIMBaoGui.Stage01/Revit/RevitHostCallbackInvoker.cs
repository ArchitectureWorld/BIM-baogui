using System;

namespace BIMBaoGui.Stage01.Revit
{
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
