using System;
using Microsoft.Diagnostics.Runtime;
internal static class Program
{
    static void Main(string[] args)
    {
        using (var target = DataTarget.LoadDump(args[0]))
        foreach (var version in target.ClrVersions)
        {
            using (var runtime = version.CreateRuntime())
            foreach (var thread in runtime.Threads)
            {
                var ex = thread.CurrentException;
                if (ex == null) continue;
                Console.WriteLine("Thread " + thread.OSThreadId + " " + ex.Type?.Name + " " + ex.Message);
                foreach (var frame in ex.StackTrace) Console.WriteLine(frame.ToString());
                Console.WriteLine("Current stack:");
                foreach (var frame in thread.EnumerateStackTrace()) Console.WriteLine(frame.ToString());
            }
        }
    }
}
