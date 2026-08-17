using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BIMBaoGui.RevitAddin.Issues
{
  internal sealed class NativeIssueCenterView : UserControl
  {
    private readonly NativeIssueHub _hub;
    private readonly Action<NativeIssueRecord> _navigateToSource;
    private readonly Action<NativeIssueNavigationRequest> _requestRevitAction;
    private readonly StackPanel _items;

    internal NativeIssueCenterView(
      NativeIssueHub hub,
      Action<NativeIssueRecord> navigateToSource,
      Action<NativeIssueNavigationRequest> requestRevitAction)
    {
      _hub = hub ?? throw new ArgumentNullException(nameof(hub));
      _navigateToSource = navigateToSource
        ?? throw new ArgumentNullException(nameof(navigateToSource));
      _requestRevitAction = requestRevitAction
        ?? throw new ArgumentNullException(nameof(requestRevitAction));
      _items = new StackPanel();
      var root = new DockPanel
      {
        Margin = new Thickness(0, 8, 0, 0)
      };
      var heading = new TextBlock
      {
        Text = "问题中心｜构件名｜类别｜缺什么｜影响什么｜去哪里补",
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 5)
      };
      DockPanel.SetDock(heading, Dock.Top);
      root.Children.Add(heading);
      root.Children.Add(new ScrollViewer
      {
        Content = _items,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
      });
      Content = root;
      _hub.IssuesChanged += Refresh;
      Refresh();
    }

    internal void Refresh()
    {
      if (!Dispatcher.CheckAccess())
      {
        Dispatcher.BeginInvoke(new Action(Refresh));
        return;
      }
      _items.Children.Clear();
      NativeIssueRecord[] issues = _hub.Snapshot().ToArray();
      if (issues.Length == 0)
      {
        _items.Children.Add(new TextBlock
        {
          Text = "当前文档暂无问题。",
          Foreground = Brushes.DimGray
        });
        return;
      }
      foreach (NativeIssueRecord issue in issues)
        _items.Children.Add(CreateIssueRow(issue));
    }

    private UIElement CreateIssueRow(NativeIssueRecord issue)
    {
      NativeIssueElementReference element = issue.Elements?.FirstOrDefault();
      var panel = new StackPanel();
      panel.Children.Add(new TextBlock
      {
        Text = (element?.ElementName ?? "（无定位构件）")
          + "｜" + (element?.CategoryName ?? "-")
          + "｜" + (issue.Missing ?? string.Empty)
          + "｜" + (issue.Impact ?? string.Empty)
          + "｜" + (issue.Remediation ?? string.Empty),
        TextWrapping = TextWrapping.Wrap
      });
      var actions = new WrapPanel { Margin = new Thickness(0, 3, 0, 7) };
      Button repair = Button("去哪里补");
      repair.Click += (_, __) => _navigateToSource(
        NativeIssueHub.CloneIssue(issue));
      actions.Children.Add(repair);
      if (issue.Elements != null && issue.Elements.Count > 0)
      {
        AddRevitButton(actions, "选中", issue, NativeIssueNavigationAction.Select);
        AddRevitButton(actions, "缩放", issue, NativeIssueNavigationAction.Zoom);
        AddRevitButton(actions, "隔离", issue, NativeIssueNavigationAction.Isolate);
        AddRevitButton(actions, "恢复", issue, NativeIssueNavigationAction.RestoreView);
      }
      panel.Children.Add(actions);
      return panel;
    }

    private void AddRevitButton(
      Panel panel,
      string label,
      NativeIssueRecord issue,
      NativeIssueNavigationAction action)
    {
      Button button = Button(label);
      button.Click += (_, __) => _requestRevitAction(
        new NativeIssueNavigationRequest
        {
          IssueId = issue.IssueId ?? string.Empty,
          Action = action,
          DocumentFingerprint = issue.DocumentFingerprint ?? string.Empty,
          Elements = action == NativeIssueNavigationAction.RestoreView
            ? Array.Empty<NativeIssueElementReference>()
            : issue.Elements
        }.Clone());
      panel.Children.Add(button);
    }

    private static Button Button(string content)
    {
      return new Button
      {
        Content = content,
        Margin = new Thickness(0, 0, 6, 0),
        Padding = new Thickness(7, 2, 7, 2)
      };
    }
  }
}
