using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using BIMBaoGui.Stage01.TaskPlanning;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;

namespace BIMBaoGui.Stage01.UI
{
  internal sealed class Stage02ComponentAttributes : GH_ComponentAttributes
  {
    private const float CardWidth = 520f;
    private const float CardHeight = 310f;
    private static readonly Color Primary = Color.FromArgb(35, 104, 146);
    private static readonly Color PrimaryDark = Color.FromArgb(22, 69, 100);
    private static readonly Color Surface = Color.White;
    private static readonly Color Background = Color.FromArgb(243, 247, 250);
    private static readonly Color Border = Color.FromArgb(199, 214, 224);
    private static readonly Color Text = Color.FromArgb(31, 42, 55);
    private static readonly Color Muted = Color.FromArgb(101, 117, 132);
    private static readonly Color Success = Color.FromArgb(34, 139, 94);
    private static readonly Color Error = Color.FromArgb(190, 53, 53);

    private readonly Stage02TaskPlanComponent _owner;
    private RectangleF _cardBounds;

    public Stage02ComponentAttributes(Stage02TaskPlanComponent owner) : base(owner)
    {
      _owner = owner;
    }

    protected override void Layout()
    {
      RectangleF componentBox = LayoutComponentBox(Owner);
      componentBox.Width = CardWidth;
      componentBox.Height = CardHeight;
      _cardBounds = componentBox;
      Bounds = LayoutBounds(Owner, componentBox);
      LayoutInputParams(Owner, componentBox);
      LayoutOutputParams(Owner, componentBox);
    }

    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
      if (channel == GH_CanvasChannel.Wires)
      {
        base.Render(canvas, graphics, channel);
        return;
      }
      if (channel != GH_CanvasChannel.Objects) return;

      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      DrawCard(graphics);
      DrawHeader(graphics);
      DrawBody(graphics);
      DrawFooter(graphics);
      RenderComponentParameters(canvas, graphics, Owner, GH_Skin.palette_normal_standard);
    }

    private void DrawCard(Graphics graphics)
    {
      using (GraphicsPath shadow = IconFactory.RoundedRectangle(
        new RectangleF(_cardBounds.X + 4, _cardBounds.Y + 5, _cardBounds.Width, _cardBounds.Height), 10))
      using (var brush = new SolidBrush(Color.FromArgb(32, 20, 35, 55)))
        graphics.FillPath(brush, shadow);
      using (GraphicsPath path = IconFactory.RoundedRectangle(_cardBounds, 10))
      using (var fill = new SolidBrush(Background))
      using (var pen = new Pen(Border, 1f))
      {
        graphics.FillPath(fill, path);
        graphics.DrawPath(pen, path);
      }
    }

    private void DrawHeader(Graphics graphics)
    {
      RectangleF header = new RectangleF(_cardBounds.X, _cardBounds.Y, _cardBounds.Width, 72f);
      using (GraphicsPath path = HeaderPath(header, 10f))
      using (var brush = new LinearGradientBrush(header, PrimaryDark, Primary, LinearGradientMode.Horizontal))
        graphics.FillPath(brush, path);

      using (var titleFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold, GraphicsUnit.Point))
      using (var subFont = new Font("Microsoft YaHei UI", 8.1f, FontStyle.Regular, GraphicsUnit.Point))
      using (var white = new SolidBrush(Color.White))
      using (var muted = new SolidBrush(Color.FromArgb(215, 235, 245, 255)))
      {
        graphics.DrawString("湖北BIM报规｜02 模型任务与骨架分流", titleFont, white,
          _cardBounds.X + 18, _cardBounds.Y + 13);
        string document = _owner.Snapshot.HostAvailable
          ? "Revit " + _owner.Snapshot.RevitVersion + " · " + Compact(_owner.Snapshot.DocumentTitle, 34)
          : "等待 Rhino.Inside.Revit 活动文档";
        graphics.DrawString(document, subFont, muted, _cardBounds.X + 18, _cardBounds.Y + 44);
      }

      bool blocked = _owner.Blockers.Count > 0;
      RectangleF pill = new RectangleF(_cardBounds.Right - 142, _cardBounds.Y + 18, 120, 30);
      FillRounded(graphics, pill, Color.FromArgb(238, Color.White), 15);
      Color statusColor = blocked ? Error : (_owner.CurrentPlan == null ? Color.FromArgb(202, 124, 28) : Success);
      using (var dot = new SolidBrush(statusColor)) graphics.FillEllipse(dot, pill.X + 10, pill.Y + 10, 10, 10);
      using (var font = new Font("Microsoft YaHei UI", 7.8f, FontStyle.Bold, GraphicsUnit.Point))
      using (var brush = new SolidBrush(statusColor))
        DrawCentered(graphics, Compact(_owner.Status, 14), font, brush,
          new RectangleF(pill.X + 24, pill.Y, pill.Width - 28, pill.Height));
    }

    private void DrawBody(Graphics graphics)
    {
      RectangleF body = new RectangleF(_cardBounds.X + 14, _cardBounds.Y + 84, _cardBounds.Width - 28, 172);
      FillRounded(graphics, body, Surface, 8);
      DrawBorder(graphics, body, Border, 8);

      if (_owner.CurrentContext == null)
      {
        using (var font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point))
        using (var brush = new SolidBrush(Muted))
          DrawCentered(graphics,
            "将 01 文件初始化的“文件上下文”输出连接到左侧输入端。\n不要连接 JSON 文本。",
            font, brush, new RectangleF(body.X + 20, body.Y + 20, body.Width - 40, body.Height - 40));
        return;
      }

      HBRTaskPlan plan = _owner.CurrentPlan;
      string[,] rows =
      {
        { "模型类型", _owner.CurrentContext.ModelFileType },
        { "骨架路径", plan?.SkeletonPath ?? "等待编译" },
        { "激活任务", plan == null ? "—" : plan.ActiveTasks.Count + " 项" },
        { "条件任务", plan == null ? "—" : plan.ConditionalObjects.Count + " 项" },
        { "文件上下文", ShortHash(_owner.CurrentContext.FileContextHash) },
        { "任务计划", plan == null ? "—" : ShortHash(plan.TaskPlanHash) }
      };

      float rowHeight = 24f;
      using (var labelFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold, GraphicsUnit.Point))
      using (var valueFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular, GraphicsUnit.Point))
      using (var labelBrush = new SolidBrush(Muted))
      using (var valueBrush = new SolidBrush(Text))
      {
        for (int index = 0; index < rows.GetLength(0); ++index)
        {
          float y = body.Y + 14 + index * rowHeight;
          graphics.DrawString(rows[index, 0], labelFont, labelBrush, body.X + 16, y);
          graphics.DrawString(Compact(rows[index, 1], 48), valueFont, valueBrush, body.X + 108, y);
        }
      }
    }

    private void DrawFooter(Graphics graphics)
    {
      RectangleF footer = new RectangleF(_cardBounds.X + 14, _cardBounds.Y + 266, _cardBounds.Width - 28, 27);
      string message;
      Color color;
      if (_owner.Blockers.Count > 0)
      {
        message = "阻断｜" + _owner.Blockers.First();
        color = Error;
      }
      else if (_owner.Messages.Count > 0)
      {
        message = _owner.Messages.First();
        color = Success;
      }
      else
      {
        message = "等待文件上下文。";
        color = Muted;
      }

      using (var font = new Font("Microsoft YaHei UI", 7.7f, FontStyle.Regular, GraphicsUnit.Point))
      using (var brush = new SolidBrush(color))
        DrawLeftCentered(graphics, Compact(message, 78), font, brush, footer);

      using (var versionFont = new Font("Microsoft YaHei UI", 6.8f, FontStyle.Regular, GraphicsUnit.Point))
      using (var versionBrush = new SolidBrush(Color.FromArgb(145, 155, 165)))
        graphics.DrawString("Stage 02 v0.9.0", versionFont, versionBrush,
          _cardBounds.Right - 78, _cardBounds.Bottom - 14);
    }

    private static string ShortHash(string value)
    {
      if (string.IsNullOrWhiteSpace(value)) return "—";
      return value.Length <= 14 ? value : value.Substring(0, 14) + "…";
    }

    private static string Compact(string value, int maximum)
    {
      string normalized = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
      return normalized.Length <= maximum ? normalized : normalized.Substring(0, Math.Max(0, maximum - 1)) + "…";
    }

    private static GraphicsPath HeaderPath(RectangleF bounds, float radius)
    {
      float diameter = radius * 2;
      var path = new GraphicsPath();
      path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
      path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
      path.AddLine(bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom);
      path.CloseFigure();
      return path;
    }

    private static void FillRounded(Graphics graphics, RectangleF bounds, Color color, float radius)
    {
      using (GraphicsPath path = IconFactory.RoundedRectangle(bounds, radius))
      using (var brush = new SolidBrush(color))
        graphics.FillPath(brush, path);
    }

    private static void DrawBorder(Graphics graphics, RectangleF bounds, Color color, float radius)
    {
      using (GraphicsPath path = IconFactory.RoundedRectangle(bounds, radius))
      using (var pen = new Pen(color, 1f))
        graphics.DrawPath(pen, path);
    }

    private static void DrawCentered(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds)
    {
      using (var format = new StringFormat
      {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter
      })
        graphics.DrawString(text ?? string.Empty, font, brush, bounds, format);
    }

    private static void DrawLeftCentered(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds)
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
