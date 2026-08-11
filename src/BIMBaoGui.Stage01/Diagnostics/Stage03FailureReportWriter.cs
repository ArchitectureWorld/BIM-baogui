using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using BIMBaoGui.Stage01.Stage03;

namespace BIMBaoGui.Stage01.Diagnostics
{
  public sealed class Stage03FailureReportContext
  {
    public string RunId { get; set; } = string.Empty;
    public string PluginVersion { get; set; } = string.Empty;
    public string RevitVersion { get; set; } = string.Empty;
    public string TechnicalCode { get; set; } = string.Empty;
    public string RootCauseStage { get; set; } = string.Empty;
    public IReadOnlyList<string> SafeDiagnosticCodes { get; set; }
      = Array.Empty<string>();
    public Exception Exception { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public DateTimeOffset OccurredLocal { get; set; }
  }

  public sealed class Stage03FailureReportWriteResult
  {
    public bool Success { get; internal set; }
    public string ReportPath { get; internal set; }
    public string ErrorCode { get; internal set; }
    public string OriginalExceptionSummary { get; internal set; }
    public string ReportWriteErrorSummary { get; internal set; }
  }

  internal interface IStage03ActiveGhaResolver
  {
    Stage03ActiveGhaResolution Resolve();
  }

  internal sealed class Stage03ActiveGhaResolution
  {
    internal Stage03ActiveGhaResolution(
      string activeGhaPath,
      string trustedAssemblyName,
      Version trustedAssemblyVersion,
      Guid trustedModuleVersionId)
    {
      ActiveGhaPath = activeGhaPath;
      TrustedAssemblyName = trustedAssemblyName;
      TrustedAssemblyVersion = trustedAssemblyVersion;
      TrustedModuleVersionId = trustedModuleVersionId;
    }

    internal string ActiveGhaPath { get; }
    internal string TrustedAssemblyName { get; }
    internal Version TrustedAssemblyVersion { get; }
    internal Guid TrustedModuleVersionId { get; }
  }

  internal static class Stage03PortableExecutableMetadataReader
  {
    private const uint DosSignature = 0x5A4D;
    private const uint PeSignature = 0x00004550;
    private const uint MetadataSignature = 0x424A5342;
    private const int SectionHeaderSize = 40;
    private const int CliDirectoryIndex = 14;

    internal static Guid ReadModuleVersionId(string path)
    {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("PE 路径不能为空。", nameof(path));
      return ReadModuleVersionId(File.ReadAllBytes(path));
    }

    private static Guid ReadModuleVersionId(byte[] image)
    {
      if (image == null) throw new ArgumentNullException(nameof(image));
      if (ReadUInt16(image, 0) != DosSignature)
        throw InvalidImage();
      int peOffset = ToOffset(ReadUInt32(image, 0x3C));
      if (ReadUInt32(image, peOffset) != PeSignature)
        throw InvalidImage();

      int coffOffset = AddOffset(peOffset, 4);
      int sectionCount = ReadUInt16(image, AddOffset(coffOffset, 2));
      int optionalHeaderSize = ReadUInt16(
        image,
        AddOffset(coffOffset, 16));
      int optionalHeaderOffset = AddOffset(coffOffset, 20);
      RequireRange(image, optionalHeaderOffset, optionalHeaderSize);
      uint optionalMagic = ReadUInt16(image, optionalHeaderOffset);
      int dataDirectoryRelativeOffset;
      int directoryCountRelativeOffset;
      if (optionalMagic == 0x10B)
      {
        dataDirectoryRelativeOffset = 96;
        directoryCountRelativeOffset = 92;
      }
      else if (optionalMagic == 0x20B)
      {
        dataDirectoryRelativeOffset = 112;
        directoryCountRelativeOffset = 108;
      }
      else
      {
        throw InvalidImage();
      }
      int cliDirectoryRelativeOffset = AddOffset(
        dataDirectoryRelativeOffset,
        CliDirectoryIndex * 8);
      if (optionalHeaderSize < AddOffset(cliDirectoryRelativeOffset, 8))
        throw InvalidImage();
      uint directoryCount = ReadUInt32(
        image,
        AddOffset(optionalHeaderOffset, directoryCountRelativeOffset));
      if (directoryCount <= CliDirectoryIndex) throw InvalidImage();
      uint sizeOfHeaders = ReadUInt32(
        image,
        AddOffset(optionalHeaderOffset, 60));
      int cliDirectoryOffset = AddOffset(
        optionalHeaderOffset,
        cliDirectoryRelativeOffset);
      uint cliRva = ReadUInt32(image, cliDirectoryOffset);
      uint cliSize = ReadUInt32(image, AddOffset(cliDirectoryOffset, 4));
      if (cliRva == 0 || cliSize < 16) throw InvalidImage();

      int sectionTableOffset = AddOffset(
        optionalHeaderOffset,
        optionalHeaderSize);
      int cliOffset = MapRva(
        image,
        cliRva,
        sizeOfHeaders,
        sectionTableOffset,
        sectionCount);
      RequireRange(image, cliOffset, 16);
      uint cliHeaderSize = ReadUInt32(image, cliOffset);
      if (cliHeaderSize < 16 || cliHeaderSize > cliSize)
        throw InvalidImage();
      uint metadataRva = ReadUInt32(image, AddOffset(cliOffset, 8));
      uint metadataSizeValue = ReadUInt32(
        image,
        AddOffset(cliOffset, 12));
      int metadataSize = ToOffset(metadataSizeValue);
      if (metadataRva == 0 || metadataSize < 20) throw InvalidImage();
      int metadataOffset = MapRva(
        image,
        metadataRva,
        sizeOfHeaders,
        sectionTableOffset,
        sectionCount);
      RequireRange(image, metadataOffset, metadataSize);
      return ReadMetadataModuleVersionId(
        image,
        metadataOffset,
        metadataSize);
    }

    private static Guid ReadMetadataModuleVersionId(
      byte[] image,
      int metadataOffset,
      int metadataSize)
    {
      if (ReadUInt32(image, metadataOffset) != MetadataSignature)
        throw InvalidImage();
      int cursor = AddOffset(metadataOffset, 12);
      int versionLength = ToOffset(ReadUInt32(image, cursor));
      cursor = AddOffset(cursor, 4);
      RequireMetadataRange(
        image,
        metadataOffset,
        metadataSize,
        cursor,
        versionLength);
      cursor = AlignMetadataOffset(
        metadataOffset,
        AddOffset(cursor, versionLength));
      RequireMetadataRange(
        image,
        metadataOffset,
        metadataSize,
        cursor,
        4);
      int streamCount = ReadUInt16(image, AddOffset(cursor, 2));
      cursor = AddOffset(cursor, 4);

      int tablesOffset = -1;
      int tablesSize = 0;
      int guidHeapOffset = -1;
      int guidHeapSize = 0;
      for (int streamIndex = 0;
        streamIndex < streamCount;
        streamIndex++)
      {
        RequireMetadataRange(
          image,
          metadataOffset,
          metadataSize,
          cursor,
          9);
        int relativeOffset = ToOffset(ReadUInt32(image, cursor));
        int streamSize = ToOffset(ReadUInt32(image, AddOffset(cursor, 4)));
        int nameStart = AddOffset(cursor, 8);
        int nameEnd = nameStart;
        while (nameEnd - nameStart < 32)
        {
          RequireMetadataRange(
            image,
            metadataOffset,
            metadataSize,
            nameEnd,
            1);
          if (image[nameEnd] == 0) break;
          nameEnd++;
        }
        if (nameEnd - nameStart >= 32 || image[nameEnd] != 0)
          throw InvalidImage();
        string streamName = Encoding.ASCII.GetString(
          image,
          nameStart,
          nameEnd - nameStart);
        cursor = AlignMetadataOffset(
          metadataOffset,
          AddOffset(nameEnd, 1));
        if (relativeOffset > metadataSize
          || streamSize > metadataSize - relativeOffset)
        {
          throw InvalidImage();
        }
        int absoluteOffset = AddOffset(metadataOffset, relativeOffset);
        RequireRange(image, absoluteOffset, streamSize);
        if (streamName == "#~" || streamName == "#-")
        {
          if (tablesOffset >= 0) throw InvalidImage();
          tablesOffset = absoluteOffset;
          tablesSize = streamSize;
        }
        else if (streamName == "#GUID")
        {
          if (guidHeapOffset >= 0) throw InvalidImage();
          guidHeapOffset = absoluteOffset;
          guidHeapSize = streamSize;
        }
      }
      if (tablesOffset < 0 || guidHeapOffset < 0)
        throw InvalidImage();
      return ReadModuleTableMvid(
        image,
        tablesOffset,
        tablesSize,
        guidHeapOffset,
        guidHeapSize);
    }

    private static Guid ReadModuleTableMvid(
      byte[] image,
      int tablesOffset,
      int tablesSize,
      int guidHeapOffset,
      int guidHeapSize)
    {
      RequireRange(image, tablesOffset, tablesSize);
      if (tablesSize < 28) throw InvalidImage();
      byte heapSizes = image[AddOffset(tablesOffset, 6)];
      ulong validMask = ReadUInt64(image, AddOffset(tablesOffset, 8));
      if ((validMask & 1UL) == 0) throw InvalidImage();
      int cursor = AddOffset(tablesOffset, 24);
      uint moduleRows = 0;
      for (int tableIndex = 0; tableIndex < 64; tableIndex++)
      {
        if ((validMask & (1UL << tableIndex)) == 0) continue;
        RequireStreamRange(tablesOffset, tablesSize, cursor, 4);
        uint rowCount = ReadUInt32(image, cursor);
        if (tableIndex == 0) moduleRows = rowCount;
        cursor = AddOffset(cursor, 4);
      }
      if ((heapSizes & 0x40) != 0)
      {
        RequireStreamRange(tablesOffset, tablesSize, cursor, 4);
        cursor = AddOffset(cursor, 4);
      }
      if (moduleRows == 0) throw InvalidImage();
      int stringIndexSize = (heapSizes & 0x01) != 0 ? 4 : 2;
      int guidIndexSize = (heapSizes & 0x02) != 0 ? 4 : 2;
      int mvidOffset = AddOffset(
        cursor,
        AddOffset(2, stringIndexSize));
      RequireStreamRange(
        tablesOffset,
        tablesSize,
        mvidOffset,
        guidIndexSize);
      uint mvidIndex = ReadHeapIndex(image, mvidOffset, guidIndexSize);
      if (mvidIndex == 0) throw InvalidImage();
      ulong relativeGuidOffset = ((ulong)mvidIndex - 1UL) * 16UL;
      if (relativeGuidOffset > int.MaxValue
        || relativeGuidOffset + 16UL > (ulong)guidHeapSize)
      {
        throw InvalidImage();
      }
      int mvidGuidOffset = AddOffset(
        guidHeapOffset,
        (int)relativeGuidOffset);
      RequireRange(image, mvidGuidOffset, 16);
      var bytes = new byte[16];
      Buffer.BlockCopy(image, mvidGuidOffset, bytes, 0, bytes.Length);
      Guid value = new Guid(bytes);
      if (value == Guid.Empty) throw InvalidImage();
      return value;
    }

    private static int MapRva(
      byte[] image,
      uint rva,
      uint sizeOfHeaders,
      int sectionTableOffset,
      int sectionCount)
    {
      if (rva < sizeOfHeaders)
      {
        int headerOffset = ToOffset(rva);
        RequireRange(image, headerOffset, 1);
        return headerOffset;
      }
      int sectionBytes;
      try
      {
        sectionBytes = checked(sectionCount * SectionHeaderSize);
      }
      catch (OverflowException)
      {
        throw InvalidImage();
      }
      RequireRange(image, sectionTableOffset, sectionBytes);
      for (int sectionIndex = 0;
        sectionIndex < sectionCount;
        sectionIndex++)
      {
        int sectionOffset = AddOffset(
          sectionTableOffset,
          sectionIndex * SectionHeaderSize);
        uint virtualSize = ReadUInt32(image, AddOffset(sectionOffset, 8));
        uint virtualAddress = ReadUInt32(
          image,
          AddOffset(sectionOffset, 12));
        uint rawSize = ReadUInt32(image, AddOffset(sectionOffset, 16));
        uint rawPointer = ReadUInt32(image, AddOffset(sectionOffset, 20));
        if (rva < virtualAddress) continue;
        ulong delta = (ulong)rva - virtualAddress;
        ulong span = Math.Max((ulong)virtualSize, (ulong)rawSize);
        if (delta >= span) continue;
        if (delta >= rawSize) throw InvalidImage();
        ulong fileOffset = (ulong)rawPointer + delta;
        if (fileOffset > int.MaxValue) throw InvalidImage();
        int result = (int)fileOffset;
        RequireRange(image, result, 1);
        return result;
      }
      throw InvalidImage();
    }

    private static uint ReadHeapIndex(byte[] image, int offset, int size)
    {
      return size == 2
        ? ReadUInt16(image, offset)
        : ReadUInt32(image, offset);
    }

    private static ushort ReadUInt16(byte[] image, int offset)
    {
      RequireRange(image, offset, 2);
      return (ushort)(image[offset] | (image[offset + 1] << 8));
    }

    private static uint ReadUInt32(byte[] image, int offset)
    {
      RequireRange(image, offset, 4);
      return (uint)(image[offset]
        | (image[offset + 1] << 8)
        | (image[offset + 2] << 16)
        | (image[offset + 3] << 24));
    }

    private static ulong ReadUInt64(byte[] image, int offset)
    {
      uint lower = ReadUInt32(image, offset);
      uint upper = ReadUInt32(image, AddOffset(offset, 4));
      return lower | ((ulong)upper << 32);
    }

    private static int ToOffset(uint value)
    {
      if (value > int.MaxValue) throw InvalidImage();
      return (int)value;
    }

    private static int AddOffset(int left, int right)
    {
      try
      {
        return checked(left + right);
      }
      catch (OverflowException)
      {
        throw InvalidImage();
      }
    }

    private static int AlignMetadataOffset(int metadataOffset, int offset)
    {
      int relative = offset - metadataOffset;
      if (relative < 0) throw InvalidImage();
      int aligned = AddOffset(relative, 3) & ~3;
      return AddOffset(metadataOffset, aligned);
    }

    private static void RequireMetadataRange(
      byte[] image,
      int metadataOffset,
      int metadataSize,
      int offset,
      int count)
    {
      if (offset < metadataOffset
        || count < 0
        || offset - metadataOffset > metadataSize - count)
      {
        throw InvalidImage();
      }
      RequireRange(image, offset, count);
    }

    private static void RequireStreamRange(
      int streamOffset,
      int streamSize,
      int offset,
      int count)
    {
      if (offset < streamOffset
        || count < 0
        || offset - streamOffset > streamSize - count)
      {
        throw InvalidImage();
      }
    }

    private static void RequireRange(byte[] image, int offset, int count)
    {
      if (image == null
        || offset < 0
        || count < 0
        || offset > image.Length - count)
      {
        throw InvalidImage();
      }
    }

    private static BadImageFormatException InvalidImage()
    {
      return new BadImageFormatException(
        "PE/CLI 元数据无效或已截断，无法读取 Module MVID。");
    }
  }

  public static class Stage03FailureReportWriter
  {
    private const string ProductionAssemblyName = "BIMBaoGui.Stage01";
    private const string ReportPrefix = "BIMBaoGui.Stage03.failure-";
    private const string ReportFailedCode = "REPORT_FAILED";
    private const int MaxExceptionGraphRecords = 64;
    private const int MaxExceptionGraphNodes = MaxExceptionGraphRecords - 1;
    private const int MaxPendingExceptionItems = MaxExceptionGraphRecords - 2;
    private static readonly Regex SafeCodePattern = new Regex(
      "^[A-Z0-9_.-]{1,128}$",
      RegexOptions.CultureInvariant);
    private static readonly Regex SafeIdentityPattern = new Regex(
      "^[A-Za-z0-9]+(?:[._-][A-Za-z0-9]+)*$",
      RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding Utf8WithoutBom =
      new UTF8Encoding(false, true);

    public static Stage03FailureReportWriteResult TryWrite(
      Stage03FailureReportContext context)
    {
      return TryWrite(context, new CurrentAssemblyGhaResolver());
    }

    internal static Stage03FailureReportWriteResult TryWrite(
      Stage03FailureReportContext context,
      IStage03ActiveGhaResolver activeGhaResolver)
    {
      Exception originalException = context?.Exception;
      string originalSummary = SafeExceptionSummary(originalException);
      try
      {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (activeGhaResolver == null)
          throw new ArgumentNullException(nameof(activeGhaResolver));
        FailureSnapshot snapshot = Snapshot(context, originalException);
        string directory = ResolveGhaDirectory(activeGhaResolver.Resolve());
        byte[] payload = Serialize(snapshot);
        string reportPath = AllocateAndWrite(
          directory,
          snapshot.OccurredLocal,
          payload);
        return new Stage03FailureReportWriteResult
        {
          Success = true,
          ReportPath = reportPath,
          OriginalExceptionSummary = originalSummary
        };
      }
      catch (Exception exception)
      {
        return new Stage03FailureReportWriteResult
        {
          Success = false,
          ErrorCode = ReportFailedCode,
          OriginalExceptionSummary = originalSummary,
          ReportWriteErrorSummary = SafeExceptionSummary(exception)
        };
      }
    }

    private static FailureSnapshot Snapshot(
      Stage03FailureReportContext context,
      Exception originalException)
    {
      string runId = SafeRunId(context.RunId, "REDACTED_RUN_ID");
      string pluginVersion = SafeIdentity(
        context.PluginVersion,
        "REDACTED_PLUGIN_VERSION");
      string revitVersion = SafeIdentity(
        context.RevitVersion,
        "REDACTED_REVIT_VERSION");
      string technicalCode = SafeCode(
        context.TechnicalCode,
        "UNKNOWN_TECHNICAL_FATAL");
      string rootCauseStage = SafeCode(
        context.RootCauseStage,
        "UNKNOWN_STAGE");
      DateTimeOffset occurredUtc = context.OccurredUtc;
      DateTimeOffset occurredLocal = context.OccurredLocal;
      object[] exceptionChain = SafeExceptionChain(originalException);
      string[] diagnosticCodes = SafeCodes(context.SafeDiagnosticCodes);
      return new FailureSnapshot(
        runId,
        pluginVersion,
        revitVersion,
        technicalCode,
        rootCauseStage,
        diagnosticCodes,
        exceptionChain,
        occurredUtc,
        occurredLocal);
    }

    private static string ResolveGhaDirectory(
      Stage03ActiveGhaResolution resolution)
    {
      if (resolution == null)
        throw new ArgumentException("活动 GHA resolver 未返回身份。", nameof(resolution));
      if (!string.Equals(
        resolution.TrustedAssemblyName,
        ProductionAssemblyName,
        StringComparison.Ordinal))
      {
        throw new ArgumentException("受信程序集名称不是 Stage03 生产程序集。");
      }
      if (resolution.TrustedAssemblyVersion == null
        || resolution.TrustedModuleVersionId == Guid.Empty)
      {
        throw new ArgumentException("受信程序集版本或 MVID 无效。");
      }

      string ghaPath = RequireUntrimmedAbsolutePath(
        resolution.ActiveGhaPath,
        nameof(resolution.ActiveGhaPath));
      if (!string.Equals(
        Path.GetFileName(ghaPath),
        ProductionAssemblyName + ".gha",
        StringComparison.OrdinalIgnoreCase))
      {
        throw new ArgumentException("活动插件路径必须使用生产 GHA 规范文件名。");
      }
      if (!File.Exists(ghaPath))
        throw new FileNotFoundException("活动 GHA 不存在。", ghaPath);
      AssemblyName actualName;
      Guid actualModuleVersionId;
      try
      {
        actualName = AssemblyName.GetAssemblyName(ghaPath);
        actualModuleVersionId =
          Stage03PortableExecutableMetadataReader.ReadModuleVersionId(ghaPath);
      }
      catch (Exception exception)
      {
        throw new BadImageFormatException("活动 GHA 不是可验证的托管程序集。", exception);
      }
      if (!string.Equals(
          actualName.Name,
          resolution.TrustedAssemblyName,
          StringComparison.Ordinal)
        || !Equals(
          actualName.Version,
          resolution.TrustedAssemblyVersion)
        || actualModuleVersionId != resolution.TrustedModuleVersionId)
      {
        throw new BadImageFormatException(
          "活动 GHA 的程序集名称、版本或 MVID 与受信生产程序集不一致。");
      }
      return NormalizeDirectoryPath(Path.GetDirectoryName(ghaPath));
    }

    private static byte[] Serialize(FailureSnapshot snapshot)
    {
      var report = new Dictionary<string, object>
      {
        ["schemaVersion"] = "1.0",
        ["reportId"] = Guid.NewGuid().ToString("D"),
        ["occurredUtc"] = snapshot.OccurredUtc.ToString(
          "O", CultureInfo.InvariantCulture),
        ["occurredLocal"] = snapshot.OccurredLocal.ToString(
          "O", CultureInfo.InvariantCulture),
        ["runId"] = snapshot.RunId,
        ["pluginVersion"] = snapshot.PluginVersion,
        ["revitVersion"] = snapshot.RevitVersion,
        ["technicalCode"] = snapshot.TechnicalCode,
        ["rootCauseStage"] = snapshot.RootCauseStage,
        ["safeDiagnosticCodes"] = snapshot.SafeDiagnosticCodes,
        ["exceptionChain"] = snapshot.ExceptionChain
      };
      string json = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 128
      }.Serialize(report);
      return Utf8WithoutBom.GetBytes(json + "\n");
    }

    private static string AllocateAndWrite(
      string directory,
      DateTimeOffset occurredLocal,
      byte[] payload)
    {
      return AllocateAndWrite(
        directory,
        occurredLocal,
        payload,
        Guid.NewGuid);
    }

    internal static string AllocateAndWrite(
      string directory,
      DateTimeOffset occurredLocal,
      byte[] payload,
      Func<Guid> nextGuid)
    {
      if (nextGuid == null) throw new ArgumentNullException(nameof(nextGuid));
      string timestamp = occurredLocal.ToString(
        "yyyyMMdd-HHmmss-fff",
        CultureInfo.InvariantCulture);
      for (int attempt = 0; attempt < 64; attempt++)
      {
        string path = Path.Combine(
          directory,
          ReportPrefix + timestamp + "-"
          + nextGuid().ToString("N") + ".json");
        try
        {
          AtomicJsonReportWriter.WriteTrustedJson(path, payload);
          return path;
        }
        catch (IOException exception) when (
          AtomicJsonReportWriter.IsCreateNewCollision(exception))
        {
        }
      }
      throw new IOException("无法分配唯一的 Stage03 失败报告路径。");
    }

    private static object[] SafeExceptionChain(Exception exception)
    {
      var result = new List<object>();
      var pending = new Queue<ExceptionTraversalItem>();
      var nodeIndexes = new Dictionary<Exception, int>(
        ReferenceExceptionComparer.Instance);
      ExceptionTraversalItem truncationPosition = null;
      string truncationReason = null;
      long omittedCount = 0;
      if (exception != null)
        pending.Enqueue(new ExceptionTraversalItem(exception, -1, 0, 0));
      while (pending.Count > 0)
      {
        ExceptionTraversalItem current = pending.Dequeue();
        int targetNodeIndex;
        if (nodeIndexes.TryGetValue(current.Exception, out targetNodeIndex))
        {
          result.Add(new Dictionary<string, object>
          {
            ["parentIndex"] = current.ParentIndex,
            ["branchIndex"] = current.BranchIndex,
            ["targetNodeIndex"] = targetNodeIndex,
            ["depth"] = current.Depth,
            ["type"] = "REFERENCE",
            ["reference"] = true,
            ["truncated"] = false
          });
          continue;
        }
        if (nodeIndexes.Count >= MaxExceptionGraphNodes)
        {
          if (truncationPosition == null)
          {
            truncationPosition = current;
            truncationReason = "NODE_LIMIT";
          }
          omittedCount += 1L + pending.Count;
          pending.Clear();
          break;
        }
        int nodeIndex = nodeIndexes.Count;
        nodeIndexes.Add(current.Exception, nodeIndex);
        result.Add(new Dictionary<string, object>
        {
          ["nodeIndex"] = nodeIndex,
          ["parentIndex"] = current.ParentIndex,
          ["branchIndex"] = current.BranchIndex,
          ["depth"] = current.Depth,
          ["type"] = current.Exception.GetType().FullName,
          ["hResult"] = current.Exception.HResult,
          ["truncated"] = false
        });
        AggregateException aggregate = current.Exception as AggregateException;
        IReadOnlyList<Exception> children = aggregate != null
          ? aggregate.InnerExceptions
          : current.Exception.InnerException == null
            ? Array.Empty<Exception>()
            : new[] { current.Exception.InnerException };
        if (truncationPosition != null)
        {
          omittedCount += children.Count;
          continue;
        }

        int remainingRecordCapacity = MaxExceptionGraphRecords - 1
          - result.Count
          - pending.Count;
        int remainingQueueCapacity = MaxPendingExceptionItems - pending.Count;
        int childCapacity = Math.Max(
          0,
          Math.Min(remainingRecordCapacity, remainingQueueCapacity));
        int enqueuedChildCount = Math.Min(children.Count, childCapacity);
        for (int branchIndex = 0;
          branchIndex < enqueuedChildCount;
          branchIndex++)
        {
          pending.Enqueue(new ExceptionTraversalItem(
            children[branchIndex],
            nodeIndex,
            branchIndex,
            current.Depth + 1));
        }
        if (enqueuedChildCount < children.Count)
        {
          truncationPosition = new ExceptionTraversalItem(
            children[enqueuedChildCount],
            nodeIndex,
            enqueuedChildCount,
            current.Depth + 1);
          truncationReason = nodeIndexes.Count >= MaxExceptionGraphNodes
            ? "NODE_LIMIT"
            : remainingRecordCapacity <= remainingQueueCapacity
              ? "RECORD_LIMIT"
              : "PENDING_LIMIT";
          omittedCount = children.Count - enqueuedChildCount;
        }
      }
      if (truncationPosition != null)
      {
        result.Add(new Dictionary<string, object>
        {
          ["nodeIndex"] = nodeIndexes.Count,
          ["parentIndex"] = truncationPosition.ParentIndex,
          ["branchIndex"] = truncationPosition.BranchIndex,
          ["depth"] = truncationPosition.Depth,
          ["type"] = "TRUNCATED",
          ["hResult"] = 0,
          ["truncated"] = true,
          ["reason"] = truncationReason,
          ["omittedCount"] = omittedCount
        });
      }
      return result.ToArray();
    }

    private static string[] SafeCodes(IEnumerable<string> values)
    {
      return (values ?? Array.Empty<string>())
        .Select(value => SafeCode(value, "UNSAFE_DIAGNOSTIC_CODE_REDACTED"))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
    }

    private static string SafeCode(string value, string fallback)
    {
      string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
      return SafeCodePattern.IsMatch(normalized)
        && !Stage03SensitiveMetadataPolicy.Contains(normalized)
        ? normalized
        : fallback;
    }

    private static string SafeIdentity(string value, string fallback)
    {
      if (string.IsNullOrEmpty(value)
        || value.Length > 64
        || !SafeIdentityPattern.IsMatch(value)
        || Stage03SensitiveMetadataPolicy.Contains(value))
      {
        return fallback;
      }
      return value;
    }

    private static string SafeRunId(string value, string fallback)
    {
      return Stage03RunIdPolicy.IsValid(value)
        ? value
        : fallback;
    }

    private static string SafeExceptionSummary(Exception exception)
    {
      if (exception == null) return string.Empty;
      try
      {
        return exception.GetType().FullName
          + "; HResult=0x"
          + exception.HResult.ToString("X8", CultureInfo.InvariantCulture);
      }
      catch
      {
        return "System.Exception; HResult=<unavailable>";
      }
    }

    private static string RequireUntrimmedAbsolutePath(
      string value,
      string parameterName)
    {
      string trimmed = value.Trim();
      if (!string.Equals(value, trimmed, StringComparison.Ordinal))
        throw new ArgumentException("路径不能包含首尾空白。", parameterName);
      if (!IsFullyQualifiedPath(value))
        throw new ArgumentException("路径必须是绝对路径。", parameterName);
      string fullPath = Path.GetFullPath(value);
      if (!string.Equals(value, fullPath, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("路径必须是规范绝对路径。", parameterName);
      return fullPath;
    }

    private static bool IsFullyQualifiedPath(string value)
    {
      if (string.IsNullOrEmpty(value) || !Path.IsPathRooted(value))
        return false;
      string root = Path.GetPathRoot(value);
      return !string.IsNullOrEmpty(root)
        && root.Length > 1
        && root[root.Length - 1] != Path.VolumeSeparatorChar;
    }

    private static string NormalizeDirectoryPath(string value)
    {
      string fullPath = Path.GetFullPath(value);
      string root = Path.GetPathRoot(fullPath);
      return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
        ? fullPath
        : fullPath.TrimEnd(
          Path.DirectorySeparatorChar,
          Path.AltDirectorySeparatorChar);
    }

    private sealed class FailureSnapshot
    {
      internal FailureSnapshot(
        string runId,
        string pluginVersion,
        string revitVersion,
        string technicalCode,
        string rootCauseStage,
        string[] safeDiagnosticCodes,
        object[] exceptionChain,
        DateTimeOffset occurredUtc,
        DateTimeOffset occurredLocal)
      {
        RunId = runId;
        PluginVersion = pluginVersion;
        RevitVersion = revitVersion;
        TechnicalCode = technicalCode;
        RootCauseStage = rootCauseStage;
        SafeDiagnosticCodes = safeDiagnosticCodes;
        ExceptionChain = exceptionChain;
        OccurredUtc = occurredUtc;
        OccurredLocal = occurredLocal;
      }

      internal string RunId { get; }
      internal string PluginVersion { get; }
      internal string RevitVersion { get; }
      internal string TechnicalCode { get; }
      internal string RootCauseStage { get; }
      internal string[] SafeDiagnosticCodes { get; }
      internal object[] ExceptionChain { get; }
      internal DateTimeOffset OccurredUtc { get; }
      internal DateTimeOffset OccurredLocal { get; }
    }

    private sealed class ExceptionTraversalItem
    {
      internal ExceptionTraversalItem(
        Exception exception,
        int parentIndex,
        int branchIndex,
        int depth)
      {
        Exception = exception;
        ParentIndex = parentIndex;
        BranchIndex = branchIndex;
        Depth = depth;
      }

      internal Exception Exception { get; }
      internal int ParentIndex { get; }
      internal int BranchIndex { get; }
      internal int Depth { get; }
    }

    private sealed class ReferenceExceptionComparer
      : IEqualityComparer<Exception>
    {
      internal static readonly ReferenceExceptionComparer Instance =
        new ReferenceExceptionComparer();

      public bool Equals(Exception left, Exception right)
      {
        return ReferenceEquals(left, right);
      }

      public int GetHashCode(Exception value)
      {
        return RuntimeHelpers.GetHashCode(value);
      }
    }

    private sealed class CurrentAssemblyGhaResolver : IStage03ActiveGhaResolver
    {
      public Stage03ActiveGhaResolution Resolve()
      {
        Assembly assembly = typeof(Stage03FailureReportWriter).Assembly;
        AssemblyName name = assembly.GetName();
        return new Stage03ActiveGhaResolution(
          assembly.Location,
          name.Name,
          name.Version,
          assembly.ManifestModule.ModuleVersionId);
      }
    }
  }
}
