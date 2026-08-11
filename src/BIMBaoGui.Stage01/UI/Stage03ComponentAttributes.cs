using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using BIMBaoGui.Stage01.Stage03;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;

namespace BIMBaoGui.Stage01.UI
{
  internal sealed class Stage03ComponentAttributes : GH_ComponentAttributes
  {
    private const float InputChannelWidth = 152f;
    private const float CardWidth = 620f;
    private const float OutputChannelWidth = 162f;
    private const float CardHeight = 364f;
    private static readonly Color Primary = Color.FromArgb(32, 108, 125);
    private static readonly Color PrimaryDark = Color.FromArgb(20, 68, 82);
    private static readonly Color Background = Color.FromArgb(242, 248, 248);
    private static readonly Color Surface = Color.White;
    private static readonly Color Border = Color.FromArgb(194, 216, 219);
    private static readonly Color Text = Color.FromArgb(30, 44, 49);
    private static readonly Color Muted = Color.FromArgb(99, 119, 124);
    private static readonly Color Success = Color.FromArgb(35, 139, 94);
    private static readonly Color Warning = Color.FromArgb(196, 116, 25);
    private static readonly Color Error = Color.FromArgb(190, 53, 53);

    private readonly Stage03ValidationExportComponent _owner;
    private RectangleF _cardBounds;
    private RectangleF _contentBounds;

    internal Stage03ComponentAttributes(
      Stage03ValidationExportComponent owner)
      : base(owner)
    {
      _owner = owner;
    }

    protected override void Layout()
    {
      RectangleF componentBox = LayoutComponentBox(Owner);
      componentBox.Width = InputChannelWidth + CardWidth + OutputChannelWidth;
      componentBox.Height = CardHeight;
      _contentBounds = new RectangleF(
        componentBox.Left + InputChannelWidth,
        componentBox.Top,
        CardWidth,
        CardHeight);
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

      Stage03ComponentViewState view = _owner.ViewState;
      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      DrawCard(graphics);
      DrawHeader(graphics, view);
      DrawBody(graphics, view);
      DrawFooter(graphics, view);
      RenderComponentParameters(
        canvas,
        graphics,
        Owner,
        GH_Skin.palette_normal_standard);
    }

    private void DrawCard(Graphics graphics)
    {
      using (GraphicsPath shadow = IconFactory.RoundedRectangle(
        new RectangleF(
          _cardBounds.X + 4,
          _cardBounds.Y + 5,
          _cardBounds.Width,
          _cardBounds.Height),
        10))
      using (var shadowBrush = new SolidBrush(
        Color.FromArgb(32, 20, 35, 55)))
      using (GraphicsPath card = IconFactory.RoundedRectangle(
        _cardBounds,
        10))
      using (var fill = new SolidBrush(Background))
      using (var pen = new Pen(Border, 1f))
      {
        graphics.FillPath(shadowBrush, shadow);
        graphics.FillPath(fill, card);
        graphics.DrawPath(pen, card);
      }
    }

    private void DrawHeader(
      Graphics graphics,
      Stage03ComponentViewState view)
    {
      RectangleF header = new RectangleF(
        _cardBounds.X,
        _cardBounds.Y,
        _cardBounds.Width,
        74f);
      using (GraphicsPath path = HeaderPath(header, 10f))
      using (var brush = new LinearGradientBrush(
        header,
        PrimaryDark,
        Primary,
        LinearGradientMode.Horizontal))
      using (var titleFont = new Font(
        "Microsoft YaHei UI",
        12f,
        FontStyle.Bold,
        GraphicsUnit.Point))
      using (var subFont = new Font(
        "Microsoft YaHei UI",
        8.2f,
        FontStyle.Regular,
        GraphicsUnit.Point))
      using (var white = new SolidBrush(Color.White))
      using (var muted = new SolidBrush(Color.FromArgb(220, 238, 242)))
      {
        graphics.FillPath(brush, path);
        graphics.DrawString(
          "湖北BIM报规｜03 检测、导出与 H-IFC 转译",
          titleFont,
          white,
          _cardBounds.X + 18,
          _cardBounds.Y + 13);
        graphics.DrawString(
          "严格门禁 / 测试放行 · RAW → HIFC-MVD → fields JSON",
          subFont,
          muted,
          _cardBounds.X + 18,
          _cardBounds.Y + 46);
      }

      string mode = view.Mode == Stage03.Stage03GateMode.Strict
        ? "严格门禁"
        : "测试放行";
      Color modeColor = view.Mode == Stage03.Stage03GateMode.Strict
        ? Success
        : Warning;
      RectangleF pill = new RectangleF(
        _cardBounds.Right - 134,
        _cardBounds.Y + 20,
        110,
        30);
      FillRounded(graphics, pill, Color.FromArgb(238, Color.White), 15);
      using (var dot = new SolidBrush(modeColor))
      using (var font = new Font(
        "Microsoft YaHei UI",
        8f,
        FontStyle.Bold,
        GraphicsUnit.Point))
      using (var textBrush = new SolidBrush(modeColor))
      {
        graphics.FillEllipse(dot, pill.X + 12, pill.Y + 10, 10, 10);
        DrawCentered(
          graphics,
          mode,
          font,
          textBrush,
          new RectangleF(pill.X + 24, pill.Y, pill.Width - 28, pill.Height));
      }
    }

    private void DrawBody(
      Graphics graphics,
      Stage03ComponentViewState view)
    {
      RectangleF body = new RectangleF(
        _cardBounds.X + 14,
        _cardBounds.Y + 86,
        _cardBounds.Width - 28,
        230f);
      FillRounded(graphics, body, Surface, 8);
      DrawBorder(graphics, body, Border, 8);

      string mode = Stage03ComponentPresentationPolicy.ModeDescription(
        view.Mode);
      string[,] rows =
      {
        { "模式", mode },
        {
          "字段计数",
          "总计 " + view.TotalFields + "｜通过/不适用 "
            + view.PassedFields + "｜阻断 " + view.BlockedFields
            + "｜业务缺陷 " + view.BusinessBlockerCount
        },
        {
          "运行支持",
          "支持 " + view.RuntimeSupportedCount
            + "｜未实现 " + view.RuntimeNotImplementedCount
            + "｜需求待定 " + view.RuntimeUnclassifiedRequirementCount
            + "｜仅证据 " + view.RuntimeOfficialEvidenceOnlyCount
        },
        { "运行状态", view.Status },
        { "RAW IFC", PathText(view.RawIfcPath) },
        { "HIFC-MVD IFC", PathText(view.FinalIfcPath) },
        { "fields JSON", PathText(view.FieldsJsonPath) },
        { "规则哈希", ShortHash(view.RulePackageSha256) }
      };
      using (var labelFont = new Font(
        "Microsoft YaHei UI",
        8f,
        FontStyle.Bold,
        GraphicsUnit.Point))
      using (var valueFont = new Font(
        "Microsoft YaHei UI",
        7.8f,
        FontStyle.Regular,
        GraphicsUnit.Point))
      using (var labelBrush = new SolidBrush(Muted))
      using (var valueBrush = new SolidBrush(Text))
      {
        for (int index = 0; index < rows.GetLength(0); index++)
        {
          float y = body.Y + 13 + index * 26f;
          graphics.DrawString(
            rows[index, 0],
            labelFont,
            labelBrush,
            body.X + 16,
            y);
          graphics.DrawString(
            Compact(rows[index, 1], 72),
            valueFont,
            valueBrush,
            body.X + 112,
            y);
        }
      }
    }

    private void DrawFooter(
      Graphics graphics,
      Stage03ComponentViewState view)
    {
      RectangleF footer = new RectangleF(
        _cardBounds.X + 16,
        _cardBounds.Y + 326f,
        _cardBounds.Width - 32,
        25f);
      Color color = ResolveToneColor(
        Stage03ComponentPresentationPolicy.ResolveTone(
          view.Mode,
          view.Pending,
          view.AllowExport,
          view.Blockers.Count > 0,
          view.ForcedWithBusinessDefects));
      string text = view.Pending
        ? "执行中｜等待 Revit host phase 与 IFC 复读验收。"
        : view.Blockers.Count > 0
          ? view.Blockers[0]
          : view.Status;
      using (var font = new Font(
        "Microsoft YaHei UI",
        7.7f,
        FontStyle.Regular,
        GraphicsUnit.Point))
      using (var brush = new SolidBrush(color))
      {
        DrawLeftCentered(
          graphics,
          Compact(text, 88),
          font,
          brush,
          footer);
      }
    }

    private static Color ResolveToneColor(Stage03ComponentStatusTone tone)
    {
      switch (tone)
      {
        case Stage03ComponentStatusTone.Muted:
          return Muted;
        case Stage03ComponentStatusTone.Success:
          return Success;
        case Stage03ComponentStatusTone.Warning:
          return Warning;
        case Stage03ComponentStatusTone.Error:
          return Error;
        default:
          throw new ArgumentOutOfRangeException(nameof(tone));
      }
    }

    private static string PathText(string path)
    {
      return string.IsNullOrWhiteSpace(path) ? "—" : path;
    }

    private static string ShortHash(string value)
    {
      if (string.IsNullOrWhiteSpace(value)) return "—";
      return value.Length <= 18 ? value : value.Substring(0, 18) + "…";
    }

    private static string Compact(string value, int maximum)
    {
      string text = (value ?? string.Empty)
        .Replace("\r", " ")
        .Replace("\n", " ")
        .Trim();
      return text.Length <= maximum
        ? text
        : text.Substring(0, Math.Max(0, maximum - 1)) + "…";
    }

    private static GraphicsPath HeaderPath(
      RectangleF bounds,
      float radius)
    {
      float diameter = radius * 2;
      var path = new GraphicsPath();
      path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
      path.AddArc(
        bounds.Right - diameter,
        bounds.Top,
        diameter,
        diameter,
        270,
        90);
      path.AddLine(bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom);
      path.CloseFigure();
      return path;
    }

    private static void FillRounded(
      Graphics graphics,
      RectangleF bounds,
      Color color,
      float radius)
    {
      using (GraphicsPath path = IconFactory.RoundedRectangle(bounds, radius))
      using (var brush = new SolidBrush(color))
        graphics.FillPath(brush, path);
    }

    private static void DrawBorder(
      Graphics graphics,
      RectangleF bounds,
      Color color,
      float radius)
    {
      using (GraphicsPath path = IconFactory.RoundedRectangle(bounds, radius))
      using (var pen = new Pen(color, 1f))
        graphics.DrawPath(pen, path);
    }

    private static void DrawCentered(
      Graphics graphics,
      string text,
      Font font,
      Brush brush,
      RectangleF bounds)
    {
      using (var format = new StringFormat
      {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter
      })
        graphics.DrawString(text ?? string.Empty, font, brush, bounds, format);
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
        graphics.DrawString(text ?? string.Empty, font, brush, bounds, format);
    }
  }
}
