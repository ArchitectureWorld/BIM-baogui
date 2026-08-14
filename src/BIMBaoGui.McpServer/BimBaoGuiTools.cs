using System.ComponentModel;
using ModelContextProtocol.Server;

namespace BIMBaoGui.McpServer;

[McpServerToolType]
public static class BimBaoGuiTools
{
  [McpServerTool(Name = "bimbaogui_list_revit_sessions"),
   Description("列出本机正在运行且已加载 BIMBaoGui 的 Revit 2020 会话。")]
  public static Task<string> ListRevitSessions(
    NamedPipeBridgeService bridge,
    CancellationToken cancellationToken)
  {
    return bridge.ListSessionsJsonAsync(cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_get_document_status"),
   Description("读取指定 Revit 2020 会话的当前文档、路径、保存和只读状态。")]
  public static Task<string> GetDocumentStatus(
    NamedPipeBridgeService bridge,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.DocumentStatus,
      new { },
      revit_process_id,
      15000,
      cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_get_rule_package_identity"),
   Description("读取 Revit 插件实际加载的 HBR 规则包身份和 SHA-256。")]
  public static Task<string> GetRulePackageIdentity(
    NamedPipeBridgeService bridge,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.RulePackageIdentity,
      new { },
      revit_process_id,
      15000,
      cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_stage01_get_form_schema"),
   Description("读取原生 Stage01 当前字段、必填状态、枚举、项目条件和模型类型。")]
  public static Task<string> Stage01GetFormSchema(
    NamedPipeBridgeService bridge,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.Stage01FormSchema,
      new { },
      revit_process_id,
      15000,
      cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_stage01_read"),
   Description("通过现有 Revit ExternalEvent 读取当前 RVT 的 Stage01 初始化状态和 canonical payload。")]
  public static Task<string> Stage01Read(
    NamedPipeBridgeService bridge,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.Stage01Read,
      new { },
      revit_process_id,
      30000,
      cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_stage01_validate"),
   Description("只读校验 Stage01 payload，并返回 30 分钟一次性 validation_hash；不会修改 Revit。")]
  public static Task<string> Stage01Validate(
    NamedPipeBridgeService bridge,
    [Description("由 Stage01 schema 构造的完整 payload JSON。")]
    string payload_json,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.Stage01Validate,
      new { payload_json },
      revit_process_id,
      30000,
      cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_stage01_write"),
   Description("使用未过期 validation_hash 调用现有 Stage01 预检、事务、回读和回滚逻辑。")]
  public static Task<string> Stage01Write(
    NamedPipeBridgeService bridge,
    [Description("stage01_validate 返回的一次性哈希。")]
    string validation_hash,
    [Description("必须明确为 true 才允许写入。")]
    bool confirm,
    [Description("已废弃兼容字段；首次初始化不再要求空模型。")]
    bool confirm_blank_project,
    [Description("已初始化文件是否明确允许重新初始化。")]
    bool allow_reinitialize,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.Stage01Write,
      new
      {
        validation_hash,
        confirm,
        confirm_blank_project,
        allow_reinitialize
      },
      revit_process_id,
      120000,
      cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_stage02_preview"),
   Description("调用原生 Stage02 全模型或当前选择只读预览，返回一次性 preview_hash。")]
  public static Task<string> Stage02Preview(
    NamedPipeBridgeService bridge,
    [Description("full_model 或 current_selection。")]
    string scope,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.Stage02Preview,
      new { scope },
      revit_process_id,
      120000,
      cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_stage02_write"),
   Description("使用未过期 preview_hash 调用现有 Stage02 重建预览、事务、回读和部分成功逻辑。")]
  public static Task<string> Stage02Write(
    NamedPipeBridgeService bridge,
    [Description("stage02_preview 返回的一次性哈希。")]
    string preview_hash,
    [Description("必须明确为 true 才允许写入。")]
    bool confirm,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.Stage02Write,
      new { preview_hash, confirm },
      revit_process_id,
      300000,
      cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_stage03_scan"),
   Description("现场重读 Stage01/Stage02 与模型参数，执行 Stage03 严格或强制测试预检，并返回一次性 scan_hash。")]
  public static Task<string> Stage03Scan(
    NamedPipeBridgeService bridge,
    [Description("strict（默认）或 forced_test；两种模式均不需要填写理由。")]
    string mode,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.Stage03Scan,
      new { mode },
      revit_process_id,
      300000,
      cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_stage03_export"),
   Description("消费未过期 scan_hash，导出 Autodesk IFC4 RAW、生成 H-IFC、exact 回读并输出 IFCFlux 人工检查材料。")]
  public static Task<string> Stage03Export(
    NamedPipeBridgeService bridge,
    [Description("stage03_scan 返回的一次性 SHA-256。")]
    string scan_hash,
    [Description("必须明确为 true 才允许导出。")]
    bool confirm,
    [Description("H-IFC 输出根目录的 Windows 绝对路径。")]
    string output_directory,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.Stage03Export,
      new { scan_hash, confirm, output_directory },
      revit_process_id,
      600000,
      cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_stage03_get_last_result"),
   Description("读取当前 Revit 文档最近一次 Stage03 结果、输出路径、内部验证和 IFCFlux 待人工状态。")]
  public static Task<string> Stage03GetLastResult(
    NamedPipeBridgeService bridge,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.Stage03GetLastResult,
      new { },
      revit_process_id,
      30000,
      cancellationToken);
  }

  [McpServerTool(Name = "bimbaogui_stage03_revalidate_file"),
   Description("使用当前文档最近一次 Stage03 字段清单，重新精确读取指定 H-IFC 文件。")]
  public static Task<string> Stage03RevalidateFile(
    NamedPipeBridgeService bridge,
    [Description("待复检 H-IFC 的 Windows 绝对路径。")]
    string ifc_path,
    [Description("Revit 进程 ID；只有一个会话时可传 null。")]
    int? revit_process_id,
    CancellationToken cancellationToken)
  {
    return bridge.CallPayloadAsync(
      BIMBaoGui.McpContracts.BridgeMethodNames.Stage03RevalidateFile,
      new { ifc_path },
      revit_process_id,
      300000,
      cancellationToken);
  }
}
