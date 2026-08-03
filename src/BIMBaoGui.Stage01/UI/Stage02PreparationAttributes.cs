using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;

namespace BIMBaoGui.Stage01.UI
{
  internal sealed class Stage02PreparationAttributes : GH_ComponentAttributes
  {
    private const float CardWidth = 760f;
    private const float CardHeight = 400f;
    private static readonly Color Primary = Color.FromArgb(34, 105, 139);
    private static readonly Color PrimaryDark = Color.FromArgb(18, 62, 84);
    private static readonly Color Surface = Color.White;
    private static readonly Color Background = Color.FromArgb(242, 247, 249);
    private static readonly Color Border = Color.FromArgb(191, 211, 220);
    private static readonly Color Text = Color.FromArgb(28, 41, 50);
    private static readonly Color Muted = Color.FromArgb(92, 112, 123);
    private static readonly Color Success = Color.FromArgb(31, 132, 85);
    private static readonly Color Warning = Color.FromArgb(179, 111, 18);
    private static readonly Color Error = Color.FromArgb(186, 52, 52);

    private readonly Stage02ElementPreparationComponent _owner;
    private RectangleF _cardBounds;
    private RectangleF _contentBounds;

    internal Stage02PreparationAttributes(
      Stage02ElementPreparationComponent owner)
      : base(owner)
    {
      _owner = owner;
    }

    protected override void Layout()
    {
      RectangleF componentBox = LayoutComponentBox(Owner);
      componentBox.Width = CardWidth;
      componentBox.Height = CardHeight;

      float leftInset = Owner.Params.Input.Count > 0 ? 152f : 24f;
      float rightInset = Owner.Params.Output.Count > 0 ? 162f : 24f;
      _contentBounds = new RectangleF(
        componentBox.Left + leftInset,
        componentBox.Top + 12f,
        componentBox.Width - leftInset - rightInset,
        componentBox.Height - 24f);
      _cardBounds = _contentBounds;
      Bounds = LayoutBounds(Owner, componentBox);
      LayoutInputParams(Owner, componentBox);
      LayoutOutputParams(Owner, componentBox);
    }

    protected override void Render(
      GH_Canvas canvas,
      Graphics graphics,
      GH_CanvasChannel channel)
    {
      if (channel == GH_CanvasChannel.Wires)
      {
        base.Render(canvas, graphics, channel);
        return;
      }
      if (channel != GH_CanvasChannel.Objects) return;

      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      Stage02PreparationUiSnapshot snapshot = _owner.GetUiSnapshot();
      DrawBackground(graphics);
      DrawHeader(graphics, snapshot);
      DrawBody(graphics, snapshot);
      DrawFooter(graphics, snapshot);
      RenderComponentParameters(
        canvas,
        graphics,
        Owner,
        GH_Skin.palette_normal_standard);
    }

    private void DrawBackground(Graphics graphics)
    {
      using (GraphicsPath shadow = IconFactory.RoundedRectangle(
        new RectangleF(
          _cardBounds.X + 4f,
          _cardBounds.Y + 5f,
          _cardBounds.Width,
          _cardBounds.Height),
        10f))
      using (var shadowBrush = new SolidBrush(
        Color.FromArgb(30, 18, 42, 54)))
      {
        graphics.FillPath(shadowBrush, shadow);
      }
      using (GraphicsPath card = IconFactory.RoundedRectangle(
        _cardBounds,
        10f))
      using (var fill = new SolidBrush(Background))
      using (var pen = new Pen(Border, 1f))
      {
        graphics.FillPath(fill, card);
        graphics.DrawPath(pen, card);
      }
    }

    private void DrawHeader(
      Graphics graphics,
      Stage02PreparationUiSnapshot snapshot)
    {
      var header = new RectangleF(
        _contentBounds.X,
        _contentBounds.Y,
        _contentBounds.Width,
        76f);
      using (GraphicsPath path = IconFactory.RoundedRectangle(header, 8f))
      using (var brush = new LinearGradientBrush(
        header,
        PrimaryDark,
        Primary,
        LinearGradientMode.Horizontal))
      {
        graphics.FillPath(brush, path);
      }

      using (var titleFont = new Font(
        "Microsoft YaHei UI",
        11.2f,
        FontStyle.Bold,
        GraphicsUnit.Point))
      using (var detailFont = new Font(
        "Microsoft YaHei UI",
        7.7f,
        FontStyle.Regular,
        GraphicsUnit.Point))
      using (var white = new SolidBrush(Color.White))
      using (var pale = new SolidBrush(Color.FromArgb(218, 238, 246)))
      {
        graphics.DrawString(
          "湖北BIM报规｜02 构件与属性准备",
          titleFont,
          white,
          header.X + 16f,
          header.Y + 11f);
        string host = string.IsNullOrWhiteSpace(snapshot.RevitVersion)
          ? "当前 RVT｜等待 Revit 上下文"
          : "当前 RVT｜Revit "
            + snapshot.RevitVersion
            + " · "
            + Compact(snapshot.DocumentTitle, 31);
        graphics.DrawString(
          host,
          detailFont,
          pale,
          header.X + 16f,
          header.Y + 42f);
        graphics.DrawString(
          "状态｜" + Compact(snapshot.Status, 18),
          detailFont,
          pale,
          header.Right - 122f,
          header.Y + 43f);
      }
    }

    private void DrawBody(
      Graphics graphics,
      Stage02PreparationUiSnapshot snapshot)
    {
      var body = new RectangleF(
        _contentBounds.X,
        _contentBounds.Y + 86f,
        _contentBounds.Width,
        238f);
      FillRounded(graphics, body, Surface, 8f);
      DrawRoundedBorder(graphics, body, Border, 8f);

      string ruleIdentity =
        Compact(snapshot.RulePackageId, 18)
        + " / "
        + Compact(snapshot.RulePackageVersion, 12)
        + " / "
        + ShortHash(snapshot.RulePackageSha256);
      string[,] rows =
      {
        { "规则身份", ruleIdentity },
        {
          "选择模式",
          Empty(snapshot.SelectionMode)
            + "｜选择 "
            + snapshot.SelectedCount
            + "｜匹配 "
            + snapshot.MatchedCount
        },
        {
          "预览状态",
          Empty(snapshot.Status)
            + "｜hash "
            + ShortHash(snapshot.PreviewHash)
        },
        {
          "参数安装",
          "待安装 "
            + snapshot.PendingInstallCount
            + "｜已安装 "
            + snapshot.InstalledCount
        },
        {
          "字段写入",
          "待写入 "
            + snapshot.PendingWriteCount
            + "｜已写入 "
            + snapshot.WrittenCount
        },
        {
          "首条阻断",
          string.IsNullOrWhiteSpace(snapshot.FirstBlocker)
            ? "无"
            : Compact(snapshot.FirstBlocker, 46)
        }
      };

      using (var labelFont = new Font(
        "Microsoft YaHei UI",
        8f,
        FontStyle.Bold,
        GraphicsUnit.Point))
      using (var valueFont = new Font(
        "Microsoft YaHei UI",
        8f,
        FontStyle.Regular,
        GraphicsUnit.Point))
      using (var labelBrush = new SolidBrush(Muted))
      using (var valueBrush = new SolidBrush(Text))
      {
        for (int index = 0; index < rows.GetLength(0); ++index)
        {
          float y = body.Y + 16f + index * 35f;
          graphics.DrawString(
            rows[index, 0],
            labelFont,
            labelBrush,
            body.X + 16f,
            y);
          graphics.DrawString(
            rows[index, 1],
            valueFont,
            valueBrush,
            body.X + 91f,
            y);
        }
      }
    }

    private void DrawFooter(
      Graphics graphics,
      Stage02PreparationUiSnapshot snapshot)
    {
      var footer = new RectangleF(
        _contentBounds.X,
        _contentBounds.Bottom - 42f,
        _contentBounds.Width,
        42f);
      Color statusColor = ResolveStatusColor(snapshot.Status);
      FillRounded(
        graphics,
        footer,
        Color.FromArgb(18, statusColor),
        7f);
      DrawRoundedBorder(graphics, footer, statusColor, 7f);
      string text = "当前状态｜" + Empty(snapshot.Status);
      if (!string.IsNullOrWhiteSpace(snapshot.FirstBlocker))
        text += "｜" + Compact(snapshot.FirstBlocker, 42);
      using (var font = new Font(
        "Microsoft YaHei UI",
        7.7f,
        FontStyle.Bold,
        GraphicsUnit.Point))
      using (var brush = new SolidBrush(statusColor))
      {
        DrawLeftCentered(
          graphics,
          text,
          font,
          brush,
          new RectangleF(
            footer.X + 12f,
            footer.Y,
            footer.Width - 24f,
            footer.Height));
      }
    }

    private static Color ResolveStatusColor(string status)
    {
      if (string.Equals(status, "写入成功", StringComparison.Ordinal)
        || string.Equals(status, "预览就绪", StringComparison.Ordinal))
      {
        return Success;
      }
      if (string.Equals(status, "预览阻断", StringComparison.Ordinal)
        || string.Equals(status, "写入失败", StringComparison.Ordinal))
      {
        return Error;
      }
      return Warning;
    }

    private static string Empty(string value)
    {
      return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private static string ShortHash(string value)
    {
      if (string.IsNullOrWhiteSpace(value)) return "—";
      return value.Length <= 12 ? value : value.Substring(0, 12) + "…";
    }

    private static string Compact(string value, int maximum)
    {
      string normalized = (value ?? string.Empty)
        .Replace("\r", " ")
        .Replace("\n", " ")
        .Trim();
      return normalized.Length <= maximum
        ? normalized
        : normalized.Substring(0, Math.Max(0, maximum - 1)) + "…";
    }

    private static void FillRounded(
      Graphics graphics,
      RectangleF bounds,
      Color color,
      float radius)
    {
      using (GraphicsPath path = IconFactory.RoundedRectangle(bounds, radius))
      using (var brush = new SolidBrush(color))
      {
        graphics.FillPath(brush, path);
      }
    }

    private static void DrawRoundedBorder(
      Graphics graphics,
      RectangleF bounds,
      Color color,
      float radius)
    {
      using (GraphicsPath path = IconFactory.RoundedRectangle(bounds, radius))
      using (var pen = new Pen(color, 1f))
      {
        graphics.DrawPath(pen, path);
      }
    }

    private static void DrawLeftCentered(
      Graphics graphics,
      string text,
      Font font,
      Brush brush,
      RectangleF bounds)
    {
      using (var format = new StringFormat
      {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap
      })
      {
        graphics.DrawString(text ?? string.Empty, font, brush, bounds, format);
      }
    }
  }
}
