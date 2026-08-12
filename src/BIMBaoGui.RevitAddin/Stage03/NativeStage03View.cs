using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal sealed class NativeStage03View : UserControl
  {
    private readonly TextBox _outputDirectory;
    private readonly RadioButton _strictMode;
    private readonly RadioButton _forcedMode;
    private readonly TextBox _forceReason;
    private readonly Button _scanButton;
    private readonly Button _exportButton;
    private readonly Button _revalidateButton;
    private readonly Button _openDirectoryButton;
    private readonly CheckBox _problemsOnly;
    private readonly TextBlock _summaryText;
    private readonly TextBlock _statusText;
    private readonly ListBox _fieldList;
    private readonly StackPanel _detailPanel;
    private NativeStage03ScanResult _scan;
    private NativeStage03ExecutionResult _lastResult;
    private bool _busy;

    internal NativeStage03View()
    {
      Background = Brushes.White;
      var root = new Grid();
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition
      {
        Height = new GridLength(1, GridUnitType.Star)
      });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(96) });

      var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
      heading.Children.Add(new TextBlock
      {
        Text = "03 检测与 H-IFC",
        FontSize = 20,
        FontWeight = FontWeights.SemiBold
      });
      _summaryText = new TextBlock
      {
        Text = "先执行现场扫描与预检，再导出 Autodesk IFC4 RAW、转译 H-IFC，并等待 IFCFlux 人工检查。",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 5, 0, 0)
      };
      heading.Children.Add(_summaryText);
      Grid.SetRow(heading, 0);
      root.Children.Add(heading);

      var settings = new Grid { Margin = new Thickness(0, 0, 0, 10) };
      settings.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      settings.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(1, GridUnitType.Star)
      });
      settings.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

      var outputLabel = Label("输出目录");
      Grid.SetRow(outputLabel, 0);
      Grid.SetColumn(outputLabel, 0);
      settings.Children.Add(outputLabel);
      _outputDirectory = new TextBox
      {
        MinWidth = 360,
        Margin = new Thickness(8, 3, 8, 3),
        Padding = new Thickness(7, 5, 7, 5)
      };
      Grid.SetRow(_outputDirectory, 0);
      Grid.SetColumn(_outputDirectory, 1);
      settings.Children.Add(_outputDirectory);
      var browse = ActionButton("选择目录", 90);
      browse.Click += (_, __) => BrowseOutputDirectory();
      Grid.SetRow(browse, 0);
      Grid.SetColumn(browse, 2);
      settings.Children.Add(browse);

      var modeLabel = Label("导出模式");
      Grid.SetRow(modeLabel, 1);
      Grid.SetColumn(modeLabel, 0);
      settings.Children.Add(modeLabel);
      var modes = new WrapPanel { Margin = new Thickness(8, 6, 0, 4) };
      _strictMode = new RadioButton
      {
        Content = "严格模式（默认）",
        IsChecked = true,
        Margin = new Thickness(0, 0, 18, 0)
      };
      _forcedMode = new RadioButton
      {
        Content = "强制测试模式",
        Margin = new Thickness(0, 0, 18, 0)
      };
      _strictMode.Checked += (_, __) => ApplyModeState();
      _forcedMode.Checked += (_, __) => ApplyModeState();
      modes.Children.Add(_strictMode);
      modes.Children.Add(_forcedMode);
      Grid.SetRow(modes, 1);
      Grid.SetColumn(modes, 1);
      Grid.SetColumnSpan(modes, 2);
      settings.Children.Add(modes);

      var reasonLabel = Label("强制原因");
      Grid.SetRow(reasonLabel, 2);
      Grid.SetColumn(reasonLabel, 0);
      settings.Children.Add(reasonLabel);
      _forceReason = new TextBox
      {
        IsEnabled = false,
        Margin = new Thickness(8, 3, 0, 3),
        Padding = new Thickness(7, 5, 7, 5),
        ToolTip = "强制测试模式必须填写原因；该原因进入文件名与 validation.json。"
      };
      Grid.SetRow(_forceReason, 2);
      Grid.SetColumn(_forceReason, 1);
      Grid.SetColumnSpan(_forceReason, 2);
      settings.Children.Add(_forceReason);
      Grid.SetRow(settings, 1);
      root.Children.Add(settings);

      var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
      _scanButton = ActionButton("扫描与预检", 115);
      _exportButton = ActionButton("导出并转译", 115);
      _revalidateButton = ActionButton("重新校验结果", 125);
      _openDirectoryButton = ActionButton("打开输出目录", 125);
      _problemsOnly = new CheckBox
      {
        Content = "仅显示问题",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 8, 0, 0)
      };
      _exportButton.IsEnabled = false;
      _revalidateButton.IsEnabled = false;
      _openDirectoryButton.IsEnabled = false;
      _scanButton.Click += (_, __) => RequestScan();
      _exportButton.Click += (_, __) => RequestExport();
      _revalidateButton.Click += (_, __) => RequestRevalidate();
      _openDirectoryButton.Click += (_, __) => OpenOutputDirectory();
      _problemsOnly.Checked += (_, __) => RenderFields();
      _problemsOnly.Unchecked += (_, __) => RenderFields();
      actions.Children.Add(_scanButton);
      actions.Children.Add(_exportButton);
      actions.Children.Add(_revalidateButton);
      actions.Children.Add(_openDirectoryButton);
      actions.Children.Add(_problemsOnly);
      Grid.SetRow(actions, 2);
      root.Children.Add(actions);

      var workspace = new Grid();
      workspace.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(320)
      });
      workspace.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(1, GridUnitType.Star)
      });
      _fieldList = new ListBox
      {
        Margin = new Thickness(0, 0, 14, 0),
        HorizontalContentAlignment = HorizontalAlignment.Stretch
      };
      _fieldList.SelectionChanged += (_, __) => RenderSelectedField();
      Grid.SetColumn(_fieldList, 0);
      workspace.Children.Add(_fieldList);
      _detailPanel = new StackPanel { Margin = new Thickness(4, 0, 14, 16) };
      var detailScroll = new ScrollViewer
      {
        Content = _detailPanel,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
      };
      Grid.SetColumn(detailScroll, 1);
      workspace.Children.Add(detailScroll);
      Grid.SetRow(workspace, 3);
      root.Children.Add(workspace);

      _statusText = new TextBlock
      {
        Text = "状态：等待扫描",
        TextWrapping = TextWrapping.Wrap,
        Padding = new Thickness(10),
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 250))
      };
      var statusScroll = new ScrollViewer
      {
        Content = _statusText,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Margin = new Thickness(0, 8, 0, 0)
      };
      Grid.SetRow(statusScroll, 4);
      root.Children.Add(statusScroll);
      Content = root;
      RenderFields();
    }

    internal event Action<string> StatusChanged;

    private void RequestScan()
    {
      if (_busy) return;
      NativeStage03Mode mode = _forcedMode.IsChecked == true
        ? NativeStage03Mode.ForcedTest
        : NativeStage03Mode.Strict;
      if (mode == NativeStage03Mode.ForcedTest
        && string.IsNullOrWhiteSpace(_forceReason.Text))
      {
        SetStatus("强制测试模式必须填写强制原因。" );
        return;
      }
      _scan = null;
      _lastResult = null;
      SetBusy(true, "正在通过 Revit ExternalEvent 扫描当前模型和固定 GUID 参数……" );
      try
      {
        RevitExternalEventDispatcher.RequestStage03Scan(
          new NativeStage03ScanRequest
          {
            Mode = mode,
            ForceReason = _forceReason.Text ?? string.Empty
          },
          ApplyScanResult,
          ApplyFailure);
      }
      catch (Exception exception)
      {
        ApplyFailure(exception);
      }
    }

    private void RequestExport()
    {
      if (_busy || _scan == null || !_scan.AllowExport) return;
      string output = (_outputDirectory.Text ?? string.Empty).Trim();
      if (output.Length == 0)
      {
        SetStatus("请先选择 Stage03 输出目录。" );
        return;
      }
      SetBusy(true, "正在导出 IFC4 RAW、转译 H-IFC 并执行 exact 回读……" );
      try
      {
        RevitExternalEventDispatcher.RequestStage03Export(
          new NativeStage03ExportRequest
          {
            ConfirmedScan = _scan,
            OutputDirectory = output
          },
          ApplyExecutionResult,
          ApplyFailure);
      }
      catch (Exception exception)
      {
        ApplyFailure(exception);
      }
    }

    private void RequestRevalidate()
    {
      if (_busy || _lastResult?.Paths == null
        || string.IsNullOrWhiteSpace(_lastResult.Paths.FinalIfcPath)) return;
      SetBusy(true, "正在重新读取 H-IFC 并核对 Entity / Pset / Property / 类型 / 值……" );
      try
      {
        RevitExternalEventDispatcher.RequestStage03Revalidate(
          _lastResult.Paths.FinalIfcPath,
          ApplyExecutionResult,
          ApplyFailure);
      }
      catch (Exception exception)
      {
        ApplyFailure(exception);
      }
    }

    private void ApplyScanResult(NativeStage03ScanResult result)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(
          new Action<NativeStage03ScanResult>(ApplyScanResult),
          result);
        return;
      }
      SetBusy(false, string.Empty);
      _scan = result;
      _exportButton.IsEnabled = result != null && result.AllowExport;
      if (string.IsNullOrWhiteSpace(_outputDirectory.Text)
        && !string.IsNullOrWhiteSpace(result?.DocumentPath))
      {
        string directory = Path.GetDirectoryName(result.DocumentPath);
        if (!string.IsNullOrWhiteSpace(directory))
          _outputDirectory.Text = Path.Combine(directory, "BIMBaoGui_HIFC_Output");
      }
      RenderFields();
      SetStatus(result == null
        ? "预检失败：未返回结果。"
        : result.Status + "｜Scan SHA-256=" + result.ScanHash + "｜"
          + string.Join(" ", result.Messages));
    }

    private void ApplyExecutionResult(NativeStage03ExecutionResult result)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(
          new Action<NativeStage03ExecutionResult>(ApplyExecutionResult),
          result);
        return;
      }
      SetBusy(false, string.Empty);
      _lastResult = result;
      _revalidateButton.IsEnabled = result?.Paths != null
        && !string.IsNullOrWhiteSpace(result.Paths.FinalIfcPath)
        && File.Exists(result.Paths.FinalIfcPath);
      _openDirectoryButton.IsEnabled = result?.Paths != null
        && Directory.Exists(result.Paths.RunDirectory);
      if (result?.Fields != null && _scan != null)
        _scan.Fields = result.Fields;
      RenderFields();
      SetStatus(result == null
        ? "Stage03 未返回执行结果。"
        : result.Status + "｜" + result.InternalValidationStatus + "｜"
          + result.IfcFluxStatus + "｜" + result.Message + "｜"
          + string.Join(" ", result.Messages));
    }

    private void ApplyFailure(Exception exception)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(new Action<Exception>(ApplyFailure), exception);
        return;
      }
      SetBusy(false, string.Empty);
      SetStatus("Stage03 Revit 请求失败："
        + (exception == null ? "未知错误" : exception.Message));
    }

    private void RenderFields()
    {
      NativeStage03FieldEvidence selected =
        (_fieldList.SelectedItem as FieldListItem)?.Field;
      _fieldList.Items.Clear();
      IEnumerable<NativeStage03FieldEvidence> fields = _scan?.Fields
        ?? Array.Empty<NativeStage03FieldEvidence>();
      if (_problemsOnly.IsChecked == true)
      {
        fields = fields.Where(value => value.Active
          && value.Status != "STRICT_READY"
          && value.Status != "INTERNAL_PASS");
      }
      foreach (NativeStage03FieldEvidence field in fields)
        _fieldList.Items.Add(new FieldListItem(field));
      if (selected != null)
      {
        FieldListItem match = _fieldList.Items.Cast<FieldListItem>()
          .FirstOrDefault(value => value.Field.PropertyId == selected.PropertyId
            && value.Field.OwnerUniqueId == selected.OwnerUniqueId);
        if (match != null) _fieldList.SelectedItem = match;
      }
      if (_fieldList.SelectedItem == null && _fieldList.Items.Count > 0)
        _fieldList.SelectedIndex = 0;

      if (_scan == null)
        _summaryText.Text = "尚未执行 Stage03 扫描与预检。";
      else
        _summaryText.Text = "字段=" + _scan.Fields.Count
          + "｜导出字段=" + _scan.ExportFields.Count
          + "｜技术阻断=" + _scan.TechnicalFatalCodes.Count
          + "｜业务阻断=" + _scan.BusinessBlockers.Count
          + "｜模式=" + _scan.Mode
          + "｜允许导出=" + (_scan.AllowExport ? "是" : "否")
          + "｜Scan SHA-256=" + _scan.ScanHash;
      RenderSelectedField();
    }

    private void RenderSelectedField()
    {
      _detailPanel.Children.Clear();
      FieldListItem selected = _fieldList.SelectedItem as FieldListItem;
      if (selected == null)
      {
        _detailPanel.Children.Add(new TextBlock
        {
          Text = _scan == null
            ? "先执行“扫描与预检”。"
            : "当前筛选没有可显示字段。",
          Margin = new Thickness(8)
        });
        return;
      }
      NativeStage03FieldEvidence field = selected.Field;
      _detailPanel.Children.Add(new TextBlock
      {
        Text = field.IfcProperty + " · " + field.Status,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8)
      });
      AddDetail("路径", field.Entity + " / " + field.PropertySet + " / " + field.IfcProperty);
      AddDetail("字段 ID", field.PropertyId);
      AddDetail("载体", field.RoleId + "｜ElementId=" + field.ElementId + "｜" + field.OwnerUniqueId);
      AddDetail("Owner", field.OwnerStrategy + "｜GlobalId=" + Empty(field.OwnerGlobalId));
      AddDetail("类型与单位", field.DeclaredIfcType + "｜" + Empty(field.CanonicalUnit));
      AddDetail("值", Empty(field.CanonicalValue));
      AddDetail("要求/运行状态", field.Requirement + "｜" + field.RuntimeStatus);
      AddDetail("严格/强制", "strict=" + field.StrictExportReady + "｜forced=" + field.ExportableInForcedMode);
      AddDetail("说明", Empty(field.Message));
    }

    private void AddDetail(string label, string value)
    {
      var panel = new StackPanel();
      panel.Children.Add(new TextBlock
      {
        Text = label,
        FontWeight = FontWeights.SemiBold
      });
      panel.Children.Add(new TextBlock
      {
        Text = value ?? string.Empty,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 0)
      });
      _detailPanel.Children.Add(new Border
      {
        Child = panel,
        BorderBrush = new SolidColorBrush(Color.FromRgb(224, 227, 232)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10),
        Margin = new Thickness(0, 0, 0, 8),
        Background = Brushes.White
      });
    }

    private void BrowseOutputDirectory()
    {
      using (var dialog = new System.Windows.Forms.FolderBrowserDialog
      {
        Description = "选择 Stage03 H-IFC 输出根目录",
        ShowNewFolderButton = true,
        SelectedPath = Directory.Exists(_outputDirectory.Text)
          ? _outputDirectory.Text
          : string.Empty
      })
      {
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
          _outputDirectory.Text = dialog.SelectedPath;
      }
    }

    private void OpenOutputDirectory()
    {
      string directory = _lastResult?.Paths?.RunDirectory;
      if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
      {
        SetStatus("Stage03 输出目录不存在。" );
        return;
      }
      Process.Start(new ProcessStartInfo
      {
        FileName = "explorer.exe",
        Arguments = "\"" + directory + "\"",
        UseShellExecute = true
      });
    }

    private void ApplyModeState()
    {
      bool forced = _forcedMode.IsChecked == true;
      _forceReason.IsEnabled = forced && !_busy;
      _scan = null;
      _lastResult = null;
      _exportButton.IsEnabled = false;
      _revalidateButton.IsEnabled = false;
      _openDirectoryButton.IsEnabled = false;
      RenderFields();
    }

    private void SetBusy(bool busy, string status)
    {
      _busy = busy;
      _scanButton.IsEnabled = !busy;
      _exportButton.IsEnabled = !busy && _scan != null && _scan.AllowExport;
      _revalidateButton.IsEnabled = !busy && _lastResult?.Paths != null
        && File.Exists(_lastResult.Paths.FinalIfcPath);
      _openDirectoryButton.IsEnabled = !busy && _lastResult?.Paths != null
        && Directory.Exists(_lastResult.Paths.RunDirectory);
      _strictMode.IsEnabled = !busy;
      _forcedMode.IsEnabled = !busy;
      _forceReason.IsEnabled = !busy && _forcedMode.IsChecked == true;
      _outputDirectory.IsEnabled = !busy;
      _problemsOnly.IsEnabled = !busy;
      if (!string.IsNullOrWhiteSpace(status)) SetStatus(status);
    }

    private void SetStatus(string status)
    {
      _statusText.Text = "状态：" + (status ?? string.Empty);
      StatusChanged?.Invoke(status ?? string.Empty);
    }

    private static TextBlock Label(string text)
    {
      return new TextBlock
      {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 4, 0)
      };
    }

    private static Button ActionButton(string text, double minWidth)
    {
      return new Button
      {
        Content = text,
        Padding = new Thickness(14, 7, 14, 7),
        Margin = new Thickness(0, 0, 8, 0),
        MinWidth = minWidth
      };
    }

    private static string Empty(string value)
    {
      return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private sealed class FieldListItem
    {
      internal FieldListItem(NativeStage03FieldEvidence field)
      {
        Field = field;
      }

      internal NativeStage03FieldEvidence Field { get; }

      public override string ToString()
      {
        return Field.IfcProperty + " · " + Field.Status
          + " · Id=" + Field.ElementId;
      }
    }
  }
}
