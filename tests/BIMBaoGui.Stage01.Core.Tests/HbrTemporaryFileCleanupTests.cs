using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Revit.Parameters;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class HbrTemporaryFileCleanupTests
  {
    [Fact]
    public void Restore_failure_does_not_skip_delete()
    {
      var restoreFailure = new InvalidOperationException("restore-marker");
      bool deleteCalled = false;

      AggregateException thrown = Assert.Throws<AggregateException>(() =>
        HbrTemporaryFileCleanup.Complete(
          null,
          () => throw restoreFailure,
          () => deleteCalled = true));

      Assert.True(deleteCalled);
      Assert.Same(restoreFailure, Assert.Single(
        thrown.Flatten().InnerExceptions));
    }

    [Fact]
    public void Delete_failure_does_not_hide_successful_restore()
    {
      bool restoreCalled = false;
      var deleteFailure = new IOException("delete-marker");

      AggregateException thrown = Assert.Throws<AggregateException>(() =>
        HbrTemporaryFileCleanup.Complete(
          null,
          () => restoreCalled = true,
          () => throw deleteFailure));

      Assert.True(restoreCalled);
      Assert.Same(deleteFailure, Assert.Single(
        thrown.Flatten().InnerExceptions));
    }

    [Fact]
    public void Primary_restore_and_delete_failures_are_all_preserved()
    {
      var primary = new ArgumentException("primary-marker");
      var restoreFailure = new InvalidOperationException("restore-marker");
      var deleteFailure = new IOException("delete-marker");

      AggregateException thrown = Assert.Throws<AggregateException>(() =>
        HbrTemporaryFileCleanup.Complete(
          primary,
          () => throw restoreFailure,
          () => throw deleteFailure));

      IReadOnlyList<Exception> failures =
        thrown.Flatten().InnerExceptions;
      Assert.Equal(3, failures.Count);
      Assert.Contains(primary, failures);
      Assert.Contains(restoreFailure, failures);
      Assert.Contains(deleteFailure, failures);
    }

    [Fact]
    public void Primary_failure_is_rethrown_when_cleanup_succeeds()
    {
      var primary = new InvalidOperationException("primary-marker");

      Exception thrown = Record.Exception(() =>
        HbrTemporaryFileCleanup.Complete(
          primary,
          () => { },
          () => { }));

      Assert.Same(primary, thrown);
    }

    [Fact]
    public void DeleteTemporaryFile_removes_a_real_temp_file()
    {
      string path = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui_cleanup_test_" + Guid.NewGuid().ToString("N") + ".tmp");
      File.WriteAllText(path, "temporary");
      try
      {
        HbrTemporaryFileCleanup.DeleteTemporaryFile(path);

        Assert.False(File.Exists(path));
      }
      finally
      {
        if (File.Exists(path)) File.Delete(path);
      }
    }
  }
}
