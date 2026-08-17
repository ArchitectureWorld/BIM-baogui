using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BIMBaoGui.RevitAddin.Issues;
using BIMBaoGui.RevitAddin.Runtime;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage02;
using BIMBaoGui.RevitAddin.Stage02B;
using BIMBaoGui.RevitAddin.Stage03;

namespace BIMBaoGui.RevitAddin
{
  internal sealed class WorkspaceControl : Page
  {
    private const int MaximumStatusSummaryLength = 120;
    private readonly TextBlock _documentText;
    private readonly TextBlock _ruleText;
    private readonly TextBlock _pluginIdentityText;
    private readonly TextBox _pluginPathText;
    private readonly TextBlock _statusText;
    private readonly Button _refreshButton;
    private readonly ContentControl _stageHost;
    private readonly NativeIssueHub _issueHub = new NativeIssueHub();
    private readonly NativeStage01View _stage01View;
    private readonly NativeStage02View _stage02View;
    private readonly NativeStage02BView _stage02BView;
    private readonly NativeStage03View _stage03View;
    private readonly NativeIssueCenterView _issueCenterView;

    internal WorkspaceControl()
    {
      Background = Brushes.White;
      var root = new Grid();
      root.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(190)
      });
      root.ColumnDefinitions.Add(new ColumnDefinition
      {
        Width = new GridLength(1, GridUnitType.Star)
      });

      var navigation = new StackPanel
      {
        Margin = new Thickness(12),
        Background = new SolidColorBrush(Color.FromRgb(246, 247, 249))
      };
      navigation.Children.Add(Header("湖北BIM报规"));
      navigation.Children.Add(StageButton("01 项目初始化", ShowStage01));
      navigation.Children.Add(StageButton("02A 构件与属性准备", ShowStage02));
      navigation.Children.Add(StageButton("02B 项目实际指标", ShowStage02B));
      navigation.Children.Add(StageButton("03 检测与 H-IFC", ShowStage03));
      navigation.Children.Add(StageButton("问题中心", ShowIssueCenter));
      Grid.SetColumn(navigation, 0);
      root.Children.Add(navigation);

      var content = new Grid { Margin = new Thickness(18) };
      content.RowDefinitions.Add(new RowDefinition
      {
        Height = GridLength.Auto
      });
      content.RowDefinitions.Add(new RowDefinition
      {
        Height = GridLength.Auto
      });
      content.RowDefinitions.Add(new RowDefinition
      {
        Height = new GridLength(1, GridUnitType.Star)
      });
      content.RowDefinitions.Add(new RowDefinition
      {
        Height = new GridLength(32)
      });

      var identityPanel = new StackPanel();
      identityPanel.Children.Add(Header("Revit 原生插件工作台"));
      PluginRuntimeIdentity plugin = PluginRuntimeIdentity.Read(
        typeof(WorkspaceControl).Assembly);
      _pluginIdentityText = Body(
        "插件版本：" + plugin.ProductVersion
        + "｜构建号：" + plugin.BuildNumber
        + "｜Commit：" + plugin.ShortCommitSha);
      _pluginPathText = new TextBox
      {
        Text = "DLL 路径：" + plugin.AssemblyPath,
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        TextWrapping = TextWrapping.NoWrap,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        ToolTip = plugin.AssemblyPath,
        Margin = new Thickness(0, 2, 0, 2)
      };
      _ruleText = Body("规则数据库：正在读取……");
      _documentText = Body("当前文档：尚未读取");
      identityPanel.Children.Add(_pluginIdentityText);
      identityPanel.Children.Add(_pluginPathText);
      identityPanel.Children.Add(_ruleText);
      identityPanel.Children.Add(_documentText);
      Grid.SetRow(identityPanel, 0);
      content.Children.Add(identityPanel);

      var actions = new StackPanel
      {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, 10, 0, 10)
      };
      _refreshButton = new Button
      {
        Content = "刷新当前文档状态",
        Padding = new Thickness(14, 7, 14, 7),
        MinWidth = 135
      };
      _refreshButton.Click += (_, __) => RequestRefresh();
      actions.Children.Add(_refreshButton);
      Grid.SetRow(actions, 1);
      content.Children.Add(actions);

      _statusText = new TextBlock
      {
        Text = "阶段状态：等待读取当前文件",
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        Padding = new Thickness(8, 4, 8, 4),
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 250))
      };

      _stage01View = new NativeStage01View(NavigateToMetric);
      _stage01View.StatusChanged += UpdateStageSummary;
      _stage02View = new NativeStage02View(_issueHub);
      _stage02View.StatusChanged += UpdateStageSummary;
      _stage02BView = new NativeStage02BView(_issueHub);
      _stage02BView.StatusChanged += UpdateStageSummary;
      _stage03View = new NativeStage03View(_issueHub);
      _stage03View.StatusChanged += UpdateStageSummary;
      _issueCenterView = new NativeIssueCenterView(
        _issueHub,
        NavigateToIssueSource,
        RequestIssueNavigation);
      _stageHost = new ContentControl { Content = _stage01View };
      Grid.SetRow(_stageHost, 2);
      content.Children.Add(_stageHost);

      Grid.SetRow(_statusText, 3);
      content.Children.Add(_statusText);
      Grid.SetColumn(content, 1);
      root.Children.Add(content);
      Content = root;
      ReadRuleIdentity();
    }

    internal void RequestRefresh()
    {
      _refreshButton.IsEnabled = false;
      UpdateStageSummary("正在读取当前 Revit 文档……");
      try
      {
        RevitExternalEventDispatcher.RequestDocumentSnapshot(
          ApplyDocumentSnapshot,
          ApplyRefreshFailure);
      }
      catch (Exception exception)
      {
        ApplyRefreshFailure(exception);
      }
    }

    private void ShowStage01()
    {
      ShowStage(_stage01View, "Stage01 项目初始化");
    }

    private void ShowStage02()
    {
      ShowStage(_stage02View, "Stage02A 构件与属性准备");
    }

    private void ShowStage02B()
    {
      ShowStage(_stage02BView, "Stage02B 项目实际指标");
    }

    private void ShowStage03()
    {
      ShowStage(_stage03View, "Stage03 检测、H-IFC 与 IFCFlux 人工验收");
    }

    private void ShowIssueCenter()
    {
      _issueCenterView.Refresh();
      ShowStage(_issueCenterView, "问题中心");
    }

    internal void NavigateToMetric(string propertyId)
    {
      ShowStage02B();
      _stage02BView.NavigateToMetric(propertyId);
    }

    internal void NavigateToField(string fieldKey)
    {
      ShowStage01();
      _stage01View.NavigateToField(fieldKey);
    }

    private void NavigateToIssueSource(NativeIssueRecord issue)
    {
      if (issue == null) return;
      switch (issue.Route)
      {
        case NativeIssueNavigationAction.OpenStage01:
          NavigateToField(issue.FieldKey);
          break;
        case NativeIssueNavigationAction.OpenStage02A:
          ShowStage02();
          break;
        case NativeIssueNavigationAction.OpenStage02B:
          NavigateToMetric(issue.PropertyId);
          break;
        case NativeIssueNavigationAction.StayStage03:
          ShowStage03();
          break;
        default:
          UpdateStageSummary("该问题没有可用的工作台定位目标");
          break;
      }
    }

    private void RequestIssueNavigation(NativeIssueNavigationRequest request)
    {
      try
      {
        RevitExternalEventDispatcher.RequestIssueNavigation(
          request,
          ApplyIssueNavigationResult,
          ApplyRefreshFailure);
      }
      catch (Exception exception)
      {
        ApplyRefreshFailure(exception);
      }
    }

    private void ApplyIssueNavigationResult(NativeIssueNavigationResult result)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(
          new Action<NativeIssueNavigationResult>(ApplyIssueNavigationResult),
          result);
        return;
      }
      UpdateStageSummary(result != null && result.Succeeded
        ? "Revit 定位已完成"
        : "Revit 定位失败｜" + (result?.Code ?? "ISSUE_RESULT_MISSING"));
    }

    private void ShowStage(FrameworkElement content, string status)
    {
      _stageHost.Content = content;
      UpdateStageSummary(status);
    }

    private void ReadRuleIdentity()
    {
      try
      {
        RulePackageIdentity identity = RulePackageIdentityReader.ReadEmbedded();
        _ruleText.Text = "规则数据库："
          + identity.PackageId
          + " / "
          + identity.PackageVersion
          + " / SHA-256 "
          + identity.RulePackageSha256;
      }
      catch (Exception exception)
      {
        _ruleText.Text = "规则数据库读取失败：" + exception.Message;
      }
    }

    private void ApplyDocumentSnapshot(CurrentDocumentSnapshot snapshot)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(new Action<CurrentDocumentSnapshot>(
          ApplyDocumentSnapshot), snapshot);
        return;
      }
      _refreshButton.IsEnabled = true;
      if (snapshot == null || !snapshot.HasDocument)
      {
        _issueHub.ResetForDocument(string.Empty);
        _stage03View.ApplyDocumentPath(string.Empty);
        _documentText.Text = "当前文档：Revit 中没有活动项目文档";
        UpdateStageSummary("等待打开项目文档");
        return;
      }
      _issueHub.ResetForDocument(snapshot);
      _stage03View.ApplyDocumentPath(
        snapshot.IsSaved ? snapshot.DocumentPath : string.Empty);
      _documentText.Text = "当前文档："
        + snapshot.DocumentTitle
        + "｜Revit "
        + snapshot.RevitVersion
        + "｜"
        + (snapshot.IsSaved ? snapshot.DocumentPath : "尚未保存")
        + "｜"
        + (snapshot.IsFamilyDocument ? "族文档" : "项目文档")
        + "｜"
        + (snapshot.IsReadOnly ? "只读" : "可写");
      if (!string.Equals(snapshot.RevitVersion, "2020", StringComparison.Ordinal))
        UpdateStageSummary("当前基础版本仅允许 Revit 2020");
      else if (snapshot.IsFamilyDocument)
        UpdateStageSummary("族文档不进入报规工作流");
      else if (!snapshot.IsSaved)
        UpdateStageSummary("请先保存 RVT");
      else if (snapshot.IsReadOnly)
        UpdateStageSummary("当前 RVT 为只读");
      else
        UpdateStageSummary("宿主与规则数据库均已就绪");
    }

    private void ApplyRefreshFailure(Exception exception)
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(new Action<Exception>(
          ApplyRefreshFailure), exception);
        return;
      }
      _refreshButton.IsEnabled = true;
      UpdateStageSummary(
        "读取失败｜" + (exception == null ? "未知错误" : exception.Message));
    }

    private void UpdateStageSummary(string fullStatus)
    {
      string normalized = NormalizeWhitespace(fullStatus);
      string summary = normalized.Split(new[] { '｜' }, 2)[0].Trim();
      if (summary.Length > MaximumStatusSummaryLength)
      {
        summary = summary.Substring(0, MaximumStatusSummaryLength - 1)
          + "…";
      }
      _statusText.Text = "阶段状态："
        + (summary.Length == 0 ? "—" : summary);
      _statusText.ToolTip = fullStatus;
    }

    private static string NormalizeWhitespace(string value)
    {
      return string.Join(
        " ",
        (value ?? string.Empty)
          .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static Button StageButton(string label, Action action)
    {
      var button = new Button
      {
        Content = label,
        Margin = new Thickness(0, 6, 0, 0),
        Padding = new Thickness(10, 9, 10, 9),
        HorizontalContentAlignment = HorizontalAlignment.Left
      };
      button.Click += (_, __) => action();
      return button;
    }

    private static TextBlock Header(string text)
    {
      return new TextBlock
      {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 8),
        TextWrapping = TextWrapping.Wrap
      };
    }

    private static TextBlock Body(string text)
    {
      return new TextBlock
      {
        Text = text,
        FontSize = 13,
        Margin = new Thickness(0, 4, 0, 4),
        TextWrapping = TextWrapping.Wrap
      };
    }
  }
}
