using System;
using System.IO;
using BIMBaoGui.RevitAddin.Stage03;
using Xunit;

namespace BIMBaoGui.RevitAddin.Tests
{
  public sealed class NativeStage03OutputDirectoryStoreTests : IDisposable
  {
    private readonly string _root;
    private readonly string _settingsPath;

    public NativeStage03OutputDirectoryStoreTests()
    {
      _root = Path.Combine(
        Path.GetTempPath(),
        "BIMBaoGui.Stage03OutputDirectoryStoreTests",
        Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(_root);
      _settingsPath = Path.Combine(_root, "settings.json");
    }

    [Fact]
    public void Resolve_defaults_to_the_current_revit_model_directory()
    {
      string modelDirectory = Path.Combine(_root, "models", "A");
      string modelPath = Path.Combine(modelDirectory, "A.rvt");
      var store = new NativeStage03OutputDirectoryStore(_settingsPath);

      Assert.Equal(
        Path.GetFullPath(modelDirectory),
        store.Resolve(modelPath));
    }

    [Fact]
    public void Remember_is_keyed_by_model_and_survives_a_new_store_instance()
    {
      string modelA = Path.Combine(_root, "models", "A.rvt");
      string modelB = Path.Combine(_root, "models", "B.rvt");
      string outputA = Path.Combine(_root, "exports", "A");
      string outputB = Path.Combine(_root, "exports", "B");
      var writer = new NativeStage03OutputDirectoryStore(_settingsPath);

      writer.Remember(modelA, outputA);
      writer.Remember(modelB, outputB);

      var reader = new NativeStage03OutputDirectoryStore(_settingsPath);
      Assert.Equal(Path.GetFullPath(outputA), reader.Resolve(modelA));
      Assert.Equal(Path.GetFullPath(outputB), reader.Resolve(modelB));
    }

    [Fact]
    public void Model_identity_is_case_insensitive_and_updates_only_that_model()
    {
      string modelA = Path.Combine(_root, "models", "A.rvt");
      string modelB = Path.Combine(_root, "models", "B.rvt");
      string firstOutput = Path.Combine(_root, "exports", "A-first");
      string secondOutput = Path.Combine(_root, "exports", "A-second");
      string modelBOutput = Path.Combine(_root, "exports", "B");
      var store = new NativeStage03OutputDirectoryStore(_settingsPath);

      store.Remember(modelA.ToUpperInvariant(), firstOutput);
      store.Remember(modelB, modelBOutput);
      store.Remember(modelA.ToLowerInvariant(), secondOutput);

      Assert.Equal(Path.GetFullPath(secondOutput), store.Resolve(modelA));
      Assert.Equal(Path.GetFullPath(modelBOutput), store.Resolve(modelB));
    }

    [Fact]
    public void Corrupt_local_preferences_fall_back_to_the_model_directory()
    {
      string modelDirectory = Path.Combine(_root, "models");
      string modelPath = Path.Combine(modelDirectory, "model.rvt");
      File.WriteAllText(_settingsPath, "{not-json");
      var store = new NativeStage03OutputDirectoryStore(_settingsPath);

      Assert.Equal(
        Path.GetFullPath(modelDirectory),
        store.Resolve(modelPath));
    }

    [Fact]
    public void Remember_rejects_relative_output_directory()
    {
      string modelPath = Path.Combine(_root, "models", "model.rvt");
      var store = new NativeStage03OutputDirectoryStore(_settingsPath);

      Assert.Throws<ArgumentException>(() =>
        store.Remember(modelPath, "relative-output"));
    }

    public void Dispose()
    {
      try
      {
        if (Directory.Exists(_root))
          Directory.Delete(_root, recursive: true);
      }
      catch
      {
        // Test cleanup must not hide the assertion result.
      }
    }
  }
}
