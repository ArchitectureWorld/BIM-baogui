using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BIMBaoGui.RevitAddin.Stage01;
using BIMBaoGui.RevitAddin.Stage02;

namespace BIMBaoGui.RevitAddin
{
  internal sealed class WorkspaceControl : Page
  {
    private readonly TextBlock _documentText;
    private readonly TextBlock _ruleText;
    private readonly TextBlock _statusText;
    private readonly Button _refreshButton;
    private readonly ContentControl _stageHost;
    private readonly NativeStage01View _stage01View;
    private readonly NativeStage02View _stage02View;
    private readonly FrameworkElement _stage03Placeholder;

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
      navigation.Children.Add(StageButton("01 文件初始化", ShowStage01));
      navigation.Children.Add(StageButton("02 构件与属性准备", ShowStage02));
      navigation.Children.Add(StageButton(
        "03 检测与 H-IFC",
        () => ShowStage(_stage03Placeholder, "Stage03 等待开发")));
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
        Height = GridLength.Auto
      });

      var identityPanel = new StackPanel();
      identityPanel.Children.Add(Header("Revit 原生插件工作台"));
      _ruleText = Body("规则数据库：正在读取……");
      _documentText = Body("当前文档：尚未读取");
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

      _statusText = Body("阶段状态：等待读取当前文件");
      _statusText.Padding = new Thickness(8);
      _statusText.Background = new SolidColorBrush(
        Color.FromRgb(245, 247, 250));

      _stage01View = new NativeStage01View();
      _stage01View.StatusChanged += status =>
        _statusText.Text = "阶段状态：" + status;
      _stage02View = new NativeStage02View();
      _stage02View.StatusChanged += status =>
        _statusText.Text = "阶段状态：" + status;
      _stage03Placeholder = Placeholder(
        "03 检测与 H-IFC",
        "Stage03 将作为独立原生模块继续开发：模型检查、Strict/Force、IFC4 RAW、H-IFC exact 回读和证据链。" );
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
      _statusText.Text = "阶段状态：正在读取当前 Revit 文档……";
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
      ShowStage(_stage01View, "Stage01 文件初始化");
    }

    private void ShowStage02()
    {
      ShowStage(_stage02View, "Stage02 构件与属性准备");
    }

    private void ShowStage(FrameworkElement content, string status)
    {
      _stageHost.Content = content;
      _statusText.Text = "阶段状态：" + status;
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
        _documentText.Text = "当前文档：Revit 中没有活动项目文档";
        _statusText.Text = "阶段状态：等待打开项目文档";
        return;
      }
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
        _statusText.Text = "阶段状态：当前基础版本仅允许 Revit 2020";
      else if (snapshot.IsFamilyDocument)
        _statusText.Text = "阶段状态：族文档不进入报规工作流";
      else if (!snapshot.IsSaved)
        _statusText.Text = "阶段状态：请先保存 RVT";
      else if (snapshot.IsReadOnly)
        _statusText.Text = "阶段状态：当前 RVT 为只读";
      else
        _statusText.Text = "阶段状态：宿主与规则数据库均已就绪";
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
      _statusText.Text = "阶段状态：读取失败｜"
        + (exception == null ? "未知错误" : exception.Message);
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

    private static FrameworkElement Placeholder(string title, string body)
    {
      var panel = new StackPanel { Margin = new Thickness(12) };
      panel.Children.Add(Header(title));
      panel.Children.Add(Body(body));
      panel.Children.Add(Body(
        "该阶段不会复用 GHA 的 UI 或状态机，只继续消费同一 HBR 参考数据库。" ));
      return panel;
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
