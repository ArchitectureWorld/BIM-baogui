using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BIMBaoGui.RevitAddin.Issues;

namespace BIMBaoGui.RevitAddin.Stage03
{
  internal sealed class NativeStage03View : UserControl
  {
    private readonly TextBox _outputDirectory;
    private readonly NativeStage03OutputDirectoryStore _outputDirectoryStore;
    private readonly NativeIssueHub _issueHub;
    private readonly RadioButton _strictMode;
    private readonly RadioButton _forcedMode;
    private readonly Button _scanButton;
    private readonly Button _exportButton;
    private readonly Button _revalidateButton;
    private readonly Button _openDirectoryButton;
    private readonly CheckBox _problemsOnly;
    private readonly TextBlock _summaryText;
    private readonly TextBlock _statusText;
    private readonly ListView _checklist;
    private readonly StackPanel _detailPanel;
    private NativeStage03ScanResult _scan;
    private NativeStage03ExecutionResult _lastResult;
    private bool _busy;
    private string _activeDocumentPath = string.Empty;
    private string _focusCheckId = string.Empty;

    internal NativeStage03View()
      : this(new NativeStage03OutputDirectoryStore(), new NativeIssueHub())
    {
    }

    internal NativeStage03View(
      NativeStage03OutputDirectoryStore store)
      : this(store, new NativeIssueHub())
    {
    }

    internal NativeStage03View(NativeIssueHub hub)
      : this(new NativeStage03OutputDirectoryStore(), hub)
    {
    }

    internal NativeStage03View(
      NativeStage03OutputDirectoryStore store, NativeIssueHub hub)
    {
      _outputDirectoryStore = store
        ?? throw new ArgumentNullException(nameof(store));
      _issueHub = hub ?? throw new ArgumentNullException(nameof(hub));
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
      _outputDirectory.TextChanged += (_, __) => UpdateOutputDirectoryButtonState();
      _outputDirectory.LostFocus += (_, __) => RememberOutputDirectory();
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

      Grid.SetRow(settings, 1);
      root.Children.Add(settings);

      var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
      _scanButton = ActionButton("扫描与预检", 115);
      _exportButton = ActionButton("导出并转译", 115);
      _revalidateButton = ActionButton("重新校验结果", 125);
      _openDirectoryButton = ActionButton("打开导出位置文件夹", 155);
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
      _problemsOnly.Checked += (_, __) => RenderChecklist();
      _problemsOnly.Unchecked += (_, __) => RenderChecklist();
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
      _checklist = new ListView
      {
        Margin = new Thickness(0, 0, 14, 0),
        HorizontalContentAlignment = HorizontalAlignment.Stretch
      };
      _checklist.View = ChecklistColumns();
      _checklist.SelectionChanged += (_, __) => RenderSelectedCheck();
      Grid.SetColumn(_checklist, 0);
      workspace.Children.Add(_checklist);
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
      RenderChecklist();
    }

    internal event Action<string> StatusChanged;
    internal event Action<NativeIssueRecord> NavigationRequested;

    internal void ApplyDocumentPath(string documentPath)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(
          new Action<string>(ApplyDocumentPath),
          documentPath);
        return;
      }
      SetActiveDocumentPath(documentPath);
    }

    internal void NavigateToCheck(string checkId)
    {
      _focusCheckId = checkId ?? string.Empty;
      SelectFocusedCheck();
    }

    private void RequestScan()
    {
      RequestScan(string.Empty);
    }

    private void RequestScan(string focusCheckId)
    {
      if (_busy) return;
      NativeStage03Mode mode = _forcedMode.IsChecked == true
        ? NativeStage03Mode.ForcedTest
        : NativeStage03Mode.Strict;
      _focusCheckId = focusCheckId ?? string.Empty;
      _scan = null;
      _lastResult = null;
      SetBusy(true, "正在通过 Revit ExternalEvent 扫描当前模型和固定 GUID 参数……" );
      try
      {
        RevitExternalEventDispatcher.RequestStage03Scan(
          new NativeStage03ScanRequest
          {
            Mode = mode,
            ForceReason = string.Empty,
            OutputDirectory = _outputDirectory.Text ?? string.Empty,
            FocusCheckId = focusCheckId
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
      if (!NativeStage03OutputDirectoryStore.TryNormalizeOutputDirectory(
        _outputDirectory.Text,
        out string output))
      {
        SetStatus("Stage03 输出目录必须是绝对路径。" );
        return;
      }
      _outputDirectory.Text = output;
      if (!RememberOutputDirectory()) return;
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
      if (!string.IsNullOrWhiteSpace(result?.DocumentPath))
        ApplyDocumentPath(result.DocumentPath);
      PublishChecklistIssues(result);
      RenderChecklist();
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
      UpdateOutputDirectoryButtonState();
      if (result?.Fields != null && _scan != null)
        _scan.Fields = result.Fields;
      RenderChecklist();
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

    private void RenderChecklist()
    {
      string selectedCheckId =
        (_checklist.SelectedItem as ListViewItem)?.Tag is ChecklistListItem item
          ? item.Check.CheckId : string.Empty;
      _checklist.Items.Clear();
      IEnumerable<NativeStage03ChecklistItem> checks = _scan?.Checklist
        ?? Array.Empty<NativeStage03ChecklistItem>();
      if (_problemsOnly.IsChecked == true)
      {
        checks = checks.Where(value => value.Status
          == NativeStage03ChecklistStatus.Failed
          || value.Status == NativeStage03ChecklistStatus.Warning);
      }
      foreach (NativeStage03ChecklistItem check in checks)
      {
        var listItem = new ListViewItem
        {
          Tag = new ChecklistListItem(check),
          Content = new ChecklistListItem(check),
          Background = BrushFromArgbHex(
            NativeStage03ChecklistPresentation.Background(check.Status))
        };
        _checklist.Items.Add(listItem);
      }
      if (!string.IsNullOrWhiteSpace(selectedCheckId))
        _focusCheckId = selectedCheckId;
      SelectFocusedCheck();
      if (_checklist.SelectedItem == null && _checklist.Items.Count > 0)
        _checklist.SelectedIndex = 0;

      if (_scan == null)
        _summaryText.Text = "尚未执行 Stage03 扫描与预检。";
      else
        _summaryText.Text = "检查项=" + _scan.Checklist.Count
          + "｜通过=" + _scan.PassedCount
          + "｜失败=" + _scan.FailedCount
          + "｜警告=" + _scan.WarningCount
          + "｜技术阻断=" + _scan.TechnicalFatalCodes.Count
          + "｜业务阻断=" + _scan.BusinessBlockers.Count
          + "｜模式=" + _scan.Mode
          + "｜允许导出=" + (_scan.AllowExport ? "是" : "否")
          + "｜Scan SHA-256=" + _scan.ScanHash;
      RenderSelectedCheck();
    }

    private void RenderSelectedCheck()
    {
      _detailPanel.Children.Clear();
      ChecklistListItem selected = (_checklist.SelectedItem as ListViewItem)?.Tag
        as ChecklistListItem;
      if (selected == null)
      {
        _detailPanel.Children.Add(new TextBlock
        {
          Text = _scan == null
            ? "先执行“扫描与预检”。"
            : "当前筛选没有可显示检查项。",
          Margin = new Thickness(8)
        });
        return;
      }
      NativeStage03ChecklistItem check = selected.Check;
      _detailPanel.Children.Add(new TextBlock
      {
        Text = check.DisplayName + " · "
          + NativeStage03ChecklistPresentation.StatusText(check.Status),
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8)
      });
      AddDetail("来源与依据", check.SourceStage + "｜" + Empty(check.ApplicableBasis));
      AddDetail("当前值", Empty(check.CurrentValue) + " " + Empty(check.Unit));
      AddDetail("问题说明", Empty(check.IssueMessage));
      if (check.Status == NativeStage03ChecklistStatus.Failed
        || check.Status == NativeStage03ChecklistStatus.Warning)
      {
        NativeIssueRecord issue = NativeStage03IssueCompiler.Compile(check);
        issue.DocumentFingerprint = _scan?.DocumentFingerprint ?? string.Empty;
        var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        var navigate = ActionButton("进入处理入口", 110);
        navigate.Click += (_, __) => NavigateIssue(issue);
        actions.Children.Add(navigate);
        var recheck = ActionButton("复查该项", 95);
        recheck.ToolTip = "为避免依赖过期，本操作会重新读取完整清单。";
        recheck.Click += (_, __) => RequestRecheck(check.CheckId);
        actions.Children.Add(recheck);
        _detailPanel.Children.Add(actions);
        if (issue.Elements.Count > 0)
        {
          _detailPanel.Children.Add(NavigationActions(issue));
        }
      }
    }

    private GridView ChecklistColumns()
    {
      var grid = new GridView();
      grid.Columns.Add(CheckColumn("检查项名称", "DisplayName", 150));
      grid.Columns.Add(CheckColumn("来源阶段", "SourceStage", 82));
      grid.Columns.Add(CheckColumn("适用依据", "ApplicableBasis", 150));
      grid.Columns.Add(CheckColumn("当前值", "CurrentValue", 130));
      grid.Columns.Add(CheckColumn("状态", "StatusText", 64));
      grid.Columns.Add(CheckColumn("问题说明", "IssueMessage", 180));
      grid.Columns.Add(CheckColumn("处理入口", "ActionText", 90));
      return grid;
    }

    private static GridViewColumn CheckColumn(
      string header,
      string path,
      double width)
    {
      return new GridViewColumn
      {
        Header = header,
        Width = width,
        DisplayMemberBinding = new Binding(path)
      };
    }

    private void SelectFocusedCheck()
    {
      if (string.IsNullOrWhiteSpace(_focusCheckId)) return;
      ListViewItem match = _checklist.Items.Cast<ListViewItem>().FirstOrDefault(
        value => string.Equals(
          (value.Tag as ChecklistListItem)?.Check.CheckId,
          _focusCheckId,
          StringComparison.Ordinal));
      if (match == null) return;
      _checklist.SelectedItem = match;
      match.BringIntoView();
      _focusCheckId = string.Empty;
    }

    private void RequestRecheck(string checkId)
    {
      if (_busy) return;
      if (!NativeStage03OutputDirectoryStore.TryNormalizeOutputDirectory(
        _outputDirectory.Text,
        out string output))
      {
        SetStatus("Stage03 输出目录必须是绝对路径。" );
        return;
      }
      _outputDirectory.Text = output;
      if (!RememberOutputDirectory()) return;
      RequestScan(checkId);
    }

    private void NavigateIssue(NativeIssueRecord issue)
    {
      if (issue == null) return;
      if (issue.Route == NativeIssueNavigationAction.Select)
      {
        RequestRevitAction(issue, NativeIssueNavigationAction.Select);
        return;
      }
      NavigationRequested?.Invoke(issue);
    }

    private FrameworkElement NavigationActions(NativeIssueRecord issue)
    {
      var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
      foreach (NativeIssueNavigationAction action in new[]
      {
        NativeIssueNavigationAction.Zoom,
        NativeIssueNavigationAction.Isolate,
        NativeIssueNavigationAction.RestoreView
      })
      {
        var button = ActionButton(NavigationLabel(action), 70);
        button.Click += (_, __) => RequestRevitAction(issue, action);
        panel.Children.Add(button);
      }
      return panel;
    }

    private void RequestRevitAction(
      NativeIssueRecord issue,
      NativeIssueNavigationAction action)
    {
      if (_busy || issue == null) return;
      SetBusy(true, "正在通过 Revit ExternalEvent 执行问题定位……" );
      try
      {
        RevitExternalEventDispatcher.RequestIssueNavigation(
          new NativeIssueNavigationRequest
          {
            IssueId = issue.IssueId,
            Action = action,
            DocumentFingerprint = issue.DocumentFingerprint,
            Elements = issue.Elements
          },
          ApplyNavigationResult,
          ApplyFailure);
      }
      catch (Exception exception)
      {
        ApplyFailure(exception);
      }
    }

    private void ApplyNavigationResult(NativeIssueNavigationResult result)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(
          new Action<NativeIssueNavigationResult>(ApplyNavigationResult),
          result);
        return;
      }
      SetBusy(false, result != null && result.Succeeded
        ? "问题定位完成：" + result.Action
        : "问题定位失败：" + (result?.Code ?? "ISSUE_NAVIGATION_FAILED"));
    }

    private void PublishChecklistIssues(NativeStage03ScanResult result)
    {
      if (result == null || string.IsNullOrWhiteSpace(result.DocumentFingerprint))
        return;
      _issueHub.ResetForDocument(result.DocumentFingerprint);
      NativeIssueRecord[] issues = (result.Checklist
          ?? Array.Empty<NativeStage03ChecklistItem>())
        .Where(value => value != null && (value.Status
          == NativeStage03ChecklistStatus.Failed
          || value.Status == NativeStage03ChecklistStatus.Warning))
        .Select(NativeStage03IssueCompiler.Compile)
        .Select(value =>
        {
          value.DocumentFingerprint = result.DocumentFingerprint;
          return value;
        })
        .ToArray();
      _issueHub.Replace("STAGE03", issues);
    }

    private static string NavigationLabel(NativeIssueNavigationAction action)
    {
      switch (action)
      {
        case NativeIssueNavigationAction.Zoom: return "缩放";
        case NativeIssueNavigationAction.Isolate: return "隔离";
        default: return "恢复视图";
      }
    }

    private static Brush BrushFromArgbHex(string value)
    {
      string hex = value ?? "#FFE5E7EB";
      return new SolidColorBrush(Color.FromArgb(
        Convert.ToByte(hex.Substring(1, 2), 16),
        Convert.ToByte(hex.Substring(3, 2), 16),
        Convert.ToByte(hex.Substring(5, 2), 16),
        Convert.ToByte(hex.Substring(7, 2), 16)));
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
        {
          _outputDirectory.Text = dialog.SelectedPath;
          RememberOutputDirectory();
        }
      }
    }

    private void OpenOutputDirectory()
    {
      if (!NativeStage03OutputDirectoryStore.TryNormalizeOutputDirectory(
        _outputDirectory.Text,
        out string directory))
      {
        SetStatus("Stage03 输出目录必须是绝对路径。" );
        return;
      }
      try
      {
        Directory.CreateDirectory(directory);
        _outputDirectory.Text = directory;
        if (!RememberOutputDirectory()) return;
        Process.Start(new ProcessStartInfo
        {
          FileName = "explorer.exe",
          Arguments = "\"" + directory + "\"",
          UseShellExecute = true
        });
      }
      catch (Exception exception)
      {
        SetStatus("无法打开 Stage03 导出位置：" + exception.Message);
      }
    }

    private void ApplyModeState()
    {
      _scan = null;
      _lastResult = null;
      _exportButton.IsEnabled = false;
      _revalidateButton.IsEnabled = false;
      UpdateOutputDirectoryButtonState();
      RenderChecklist();
    }

    private void SetBusy(bool busy, string status)
    {
      _busy = busy;
      _scanButton.IsEnabled = !busy;
      _exportButton.IsEnabled = !busy && _scan != null && _scan.AllowExport;
      _revalidateButton.IsEnabled = !busy && _lastResult?.Paths != null
        && File.Exists(_lastResult.Paths.FinalIfcPath);
      _strictMode.IsEnabled = !busy;
      _forcedMode.IsEnabled = !busy;
      _outputDirectory.IsEnabled = !busy;
      _problemsOnly.IsEnabled = !busy;
      UpdateOutputDirectoryButtonState();
      if (!string.IsNullOrWhiteSpace(status)) SetStatus(status);
    }

    private void SetActiveDocumentPath(string documentPath)
    {
      string normalized = string.Empty;
      if (!string.IsNullOrWhiteSpace(documentPath))
      {
        try
        {
          normalized = NativeStage03OutputDirectoryStore.NormalizeDocumentPath(
            documentPath);
        }
        catch (ArgumentException)
        {
          normalized = string.Empty;
        }
      }
      if (string.Equals(
        normalized,
        _activeDocumentPath,
        StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      _activeDocumentPath = normalized;
      _outputDirectory.Text = normalized.Length == 0
        ? string.Empty
        : _outputDirectoryStore.Resolve(normalized);
      UpdateOutputDirectoryButtonState();
    }

    private bool RememberOutputDirectory()
    {
      if (string.IsNullOrWhiteSpace(_activeDocumentPath)) return true;
      if (!_outputDirectoryStore.TryRemember(
        _activeDocumentPath,
        _outputDirectory.Text,
        out string error))
      {
        SetStatus("无法按当前 Revit 模型记录导出位置：" + error);
        return false;
      }
      return true;
    }

    private void UpdateOutputDirectoryButtonState()
    {
      _openDirectoryButton.IsEnabled = !_busy
        && NativeStage03OutputDirectoryStore.TryNormalizeOutputDirectory(
          _outputDirectory.Text,
          out _);
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

    private sealed class ChecklistListItem
    {
      internal ChecklistListItem(NativeStage03ChecklistItem check)
      {
        Check = check;
      }

      internal NativeStage03ChecklistItem Check { get; }
      public string DisplayName => Check.DisplayName;
      public string SourceStage => Check.SourceStage.ToString();
      public string ApplicableBasis => Check.ApplicableBasis;
      public string CurrentValue => Empty(Check.CurrentValue) + " " + Empty(Check.Unit);
      public string StatusText => NativeStage03ChecklistPresentation.StatusText(Check.Status);
      public string IssueMessage => Empty(Check.IssueMessage);
      public string ActionText => Check.Status == NativeStage03ChecklistStatus.Failed
        || Check.Status == NativeStage03ChecklistStatus.Warning ? "处理/复查" : "—";
    }
  }
}
