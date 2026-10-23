using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hifc.Revit.Management;

internal static class ManagementStateRetryTests
{
    static void Assert(bool value,string message) { if(!value)throw new Exception(message); }
    static IOException ReplaceFailure() => new IOException("simulated ERROR_UNABLE_TO_REMOVE_REPLACED",unchecked((int)0x80070497));
    static void WithFiles(Action<string,string> test)
    {
        var directory=Path.Combine(Path.GetTempPath(),"hifc-state-retry-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target=Path.Combine(directory,"profile.json");var source=Path.Combine(directory,"profile.tmp");
        File.WriteAllText(target,"old state",new UTF8Encoding(false));
        File.WriteAllText(source,"new state",new UTF8Encoding(false));
        test(source,target);
        File.Delete(source);File.Delete(target);Directory.Delete(directory);
    }
    static void AssertRecoverable(string source,string target)
    {
        Assert(File.Exists(target)&&File.ReadAllText(target)=="old state","old state was removed or overwritten before atomic replacement");
        Assert(File.Exists(source)&&File.ReadAllText(source)=="new state","temporary new state was lost");
    }
    static Exception Capture(Action action)
    {
        try {action();return null;}catch(Exception error){return error;}
    }
    public static void Run(Action<string,Action> check)
    {
        check("state_replace_transient_1175_recovers_with_same_atomic_pair",()=>WithFiles((source,target)=>{
            int attempts=0;var delays=new List<int>();
            ManagementStateStore.ReplaceWithRetry(source,target,(from,to)=>{
                Assert(from==source&&to==target,"retry changed source or destination");AssertRecoverable(source,target);
                attempts++;if(attempts<=3)throw ReplaceFailure();
                File.Replace(from,to,null);
            },delay=>{AssertRecoverable(source,target);delays.Add(delay);});
            Assert(attempts==4&&delays.SequenceEqual(new[]{100,200,400}),"transient replacement did not honor the bounded retry schedule");
            Assert(File.ReadAllText(target)=="new state"&&!File.Exists(source),"successful replacement did not publish exactly the new state");
        }));
        check("state_replace_exhausted_1175_preserves_recovery_files",()=>WithFiles((source,target)=>{
            int attempts=0;var delays=new List<int>();var expected=ReplaceFailure();
            var actual=Capture(()=>ManagementStateStore.ReplaceWithRetry(source,target,(from,to)=>{
                Assert(from==source&&to==target,"retry changed the replacement pair");AssertRecoverable(source,target);attempts++;throw expected;
            },delays.Add));
            Assert(ReferenceEquals(actual,expected),"retry exhaustion swallowed or replaced the original failure");
            Assert(attempts==4&&delays.SequenceEqual(new[]{100,200,400}),"persistent replacement failure exceeded or skipped the retry budget");
            AssertRecoverable(source,target);
        }));
        check("state_replace_access_denied_fails_immediately",()=>{
            foreach(var expected in new Exception[]{new UnauthorizedAccessException("denied"),new IOException("denied",unchecked((int)0x80070005))})
                WithFiles((source,target)=>{
                    int attempts=0,waits=0;
                    var actual=Capture(()=>ManagementStateStore.ReplaceWithRetry(source,target,(from,to)=>{attempts++;throw expected;},delay=>waits++));
                    Assert(ReferenceEquals(actual,expected)&&attempts==1&&waits==0,"access denied was retried or hidden");AssertRecoverable(source,target);
                });
        });
        check("state_replace_sharing_violation_fails_immediately",()=>WithFiles((source,target)=>{
            int attempts=0,waits=0;var expected=new IOException("sharing violation",unchecked((int)0x80070020));
            var actual=Capture(()=>ManagementStateStore.ReplaceWithRetry(source,target,(from,to)=>{attempts++;throw expected;},delay=>waits++));
            Assert(ReferenceEquals(actual,expected)&&attempts==1&&waits==0,"sharing violation was retried or hidden");AssertRecoverable(source,target);
        }));
        check("state_replace_1175_does_not_retry_when_either_file_disappears",()=>{
            foreach(bool loseSource in new[]{true,false})WithFiles((source,target)=>{
                int attempts=0,waits=0;var expected=ReplaceFailure();
                var actual=Capture(()=>ManagementStateStore.ReplaceWithRetry(source,target,(from,to)=>{
                    attempts++;File.Delete(loseSource?source:target);throw expected;
                },delay=>waits++));
                Assert(ReferenceEquals(actual,expected)&&attempts==1&&waits==0,"missing file was treated as a recoverable replacement failure");
                Assert(loseSource?File.Exists(target)&&File.ReadAllText(target)=="old state":File.Exists(source)&&File.ReadAllText(source)=="new state","remaining recovery file changed");
            });
        });
    }
}
