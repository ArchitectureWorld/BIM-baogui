using System;
using System.IO;
using BIMBaoGui.Stage01.Stage03;
using Xunit;

namespace BIMBaoGui.Stage01.Core.Tests
{
  public sealed class AutodeskIfcExportPolicyTests
  {
    [Fact]
    public void Validate_ReturnsExactUnusedIfcTarget()
    {
      string directory = NewDirectory();
      try
      {
        string requested = Path.Combine(directory, "model-run-RAW.ifc");

        AutodeskIfcExportTarget target =
          AutodeskIfcExportPathPolicy.Validate(requested);

        Assert.Equal(Path.GetFullPath(requested), target.RawIfcPath);
        Assert.Equal(Path.GetFullPath(directory), target.DirectoryPath);
        Assert.Equal("model-run-RAW", target.FileStem);
        Assert.Equal(
          Path.Combine(Path.GetFullPath(directory), "model-run-RAW.ifc"),
          target.RevitOutputPath);
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Theory]
    [InlineData("relative.ifc")]
    [InlineData("relative.txt")]
    [InlineData(" relative.ifc")]
    [InlineData("relative.ifc ")]
    public void Validate_RejectsNonExactOrNonRootedPath(string value)
    {
      Assert.ThrowsAny<ArgumentException>(() =>
        AutodeskIfcExportPathPolicy.Validate(value));
    }

    [Fact]
    public void Validate_RejectsMissingDirectory()
    {
      string missing = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui-IfcExportPolicy-missing-" + Guid.NewGuid().ToString("N"));
      string requested = Path.Combine(missing, "model.ifc");

      Assert.False(Directory.Exists(missing));
      Assert.Throws<DirectoryNotFoundException>(() =>
        AutodeskIfcExportPathPolicy.Validate(requested));
    }

    [Fact]
    public void Validate_RejectsExistingTargetWithoutChangingIt()
    {
      string directory = NewDirectory();
      string requested = Path.Combine(directory, "model.ifc");
      byte[] original = { 1, 2, 3, 4 };
      try
      {
        File.WriteAllBytes(requested, original);

        Assert.Throws<IOException>(() =>
          AutodeskIfcExportPathPolicy.Validate(requested));
        Assert.Equal(original, File.ReadAllBytes(requested));
      }
      finally
      {
        Directory.Delete(directory, true);
      }
    }

    [Theory]
    [InlineData(false, true, 10, "RolledBack")]
    [InlineData(true, false, 10, "RolledBack")]
    [InlineData(true, true, 0, "RolledBack")]
    [InlineData(true, true, 10, "Started")]
    public void Completion_RejectsFalseMissingEmptyOrUnrolledExport(
      bool exportReturned,
      bool fileExists,
      long fileLength,
      string transactionStatus)
    {
      AutodeskIfcExportCompletionDecision decision =
        AutodeskIfcExportCompletionPolicy.Evaluate(
          exportReturned,
          fileExists,
          fileLength,
          transactionStatus);

      Assert.False(decision.Success);
      Assert.Equal(Stage03TechnicalFatalCodes.ExportFailed, decision.ErrorCode);
    }

    [Fact]
    public void Completion_AcceptsNonEmptyRolledBackExport()
    {
      AutodeskIfcExportCompletionDecision decision =
        AutodeskIfcExportCompletionPolicy.Evaluate(
          true,
          true,
          10,
          "RolledBack");

      Assert.True(decision.Success, decision.Message);
      Assert.Equal(string.Empty, decision.ErrorCode);
    }

    [Fact]
    public void Failure_ExportOnlyPreservesOriginalException()
    {
      var exportFailure = new IOException("export failed");

      Exception combined = AutodeskIfcExportFailurePolicy.Combine(
        exportFailure,
        null);

      Assert.Same(exportFailure, combined);
    }

    [Fact]
    public void Failure_RollbackOnlyWrapsOriginalRollbackException()
    {
      var rollbackFailure = new InvalidOperationException("rollback failed");

      Exception combined = AutodeskIfcExportFailurePolicy.Combine(
        null,
        rollbackFailure);

      var wrapper = Assert.IsType<InvalidOperationException>(combined);
      Assert.Equal(
        "Autodesk IFC4 RAW 导出事务回滚失败。",
        wrapper.Message);
      Assert.Same(rollbackFailure, wrapper.InnerException);
    }

    [Fact]
    public void Failure_ExportAndRollbackAggregatesBothInDeterministicOrder()
    {
      var exportFailure = new IOException("export failed");
      var rollbackFailure = new InvalidOperationException("rollback failed");

      Exception combined = AutodeskIfcExportFailurePolicy.Combine(
        exportFailure,
        rollbackFailure);

      var aggregate = Assert.IsType<AggregateException>(combined);
      Assert.Collection(
        aggregate.InnerExceptions,
        first => Assert.Same(exportFailure, first),
        second =>
        {
          var wrapper = Assert.IsType<InvalidOperationException>(second);
          Assert.Equal(
            "Autodesk IFC4 RAW 导出事务回滚失败。",
            wrapper.Message);
          Assert.Same(rollbackFailure, wrapper.InnerException);
        });
    }

    [Fact]
    public void Failure_ExportAndDisposePreservesBothInDeterministicOrder()
    {
      var exportFailure = new IOException("export failed");
      var disposeFailure = new InvalidOperationException("dispose failed");

      Exception combined = AutodeskIfcExportFailurePolicy.Combine(
        exportFailure,
        null,
        disposeFailure);

      var aggregate = Assert.IsType<AggregateException>(combined);
      Assert.Collection(
        aggregate.InnerExceptions,
        first => Assert.Same(exportFailure, first),
        second => AssertWrappedDisposeFailure(second, disposeFailure));
    }

    [Fact]
    public void Failure_RollbackAndDisposePreservesBothInDeterministicOrder()
    {
      var rollbackFailure = new InvalidOperationException("rollback failed");
      var disposeFailure = new InvalidOperationException("dispose failed");

      Exception combined = AutodeskIfcExportFailurePolicy.Combine(
        null,
        rollbackFailure,
        disposeFailure);

      var aggregate = Assert.IsType<AggregateException>(combined);
      Assert.Collection(
        aggregate.InnerExceptions,
        first =>
        {
          var wrapper = Assert.IsType<InvalidOperationException>(first);
          Assert.Equal(
            "Autodesk IFC4 RAW 导出事务回滚失败。",
            wrapper.Message);
          Assert.Same(rollbackFailure, wrapper.InnerException);
        },
        second => AssertWrappedDisposeFailure(second, disposeFailure));
    }

    [Fact]
    public void Failure_ExportRollbackAndDisposePreservesAllInFixedOrder()
    {
      var exportFailure = new IOException("export failed");
      var rollbackFailure = new InvalidOperationException("rollback failed");
      var disposeFailure = new InvalidOperationException("dispose failed");

      Exception combined = AutodeskIfcExportFailurePolicy.Combine(
        exportFailure,
        rollbackFailure,
        disposeFailure);

      var aggregate = Assert.IsType<AggregateException>(combined);
      Assert.Collection(
        aggregate.InnerExceptions,
        first => Assert.Same(exportFailure, first),
        second =>
        {
          var wrapper = Assert.IsType<InvalidOperationException>(second);
          Assert.Equal(
            "Autodesk IFC4 RAW 导出事务回滚失败。",
            wrapper.Message);
          Assert.Same(rollbackFailure, wrapper.InnerException);
        },
        third => AssertWrappedDisposeFailure(third, disposeFailure));
    }

    private static void AssertWrappedDisposeFailure(
      Exception actual,
      Exception expectedInner)
    {
      var wrapper = Assert.IsType<InvalidOperationException>(actual);
      Assert.Equal(
        "Autodesk IFC4 RAW 导出事务释放失败。",
        wrapper.Message);
      Assert.Same(expectedInner, wrapper.InnerException);
    }

    private static string NewDirectory()
    {
      string directory = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui-IfcExportPolicy-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      return directory;
    }
  }
}
