using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using BIMBaoGui.Stage01.Core;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;

namespace BIMBaoGui.Stage01.UI
{
  internal sealed class Stage01ComponentAttributes : GH_ComponentAttributes
  {
    private const float CardWidth = 540f;
    private const float CardHeight = 570f;
    private const int PageSize = 8;

    private static readonly Color Primary = Color.FromArgb(31, 92, 166);
    private static readonly Color PrimaryDark = Color.FromArgb(22, 68, 124);
    private static readonly Color Background = Color.FromArgb(247, 249, 252);
    private static readonly Color Surface = Color.White;
    private static readonly Color Border = Color.FromArgb(205, 214, 225);
    private static readonly Color Text = Color.FromArgb(31, 42, 55);
    private static readonly Color Muted = Color.FromArgb(102, 116, 133);
    private static readonly Color Success = Color.FromArgb(34, 139, 94);
    private static readonly Color Warning = Color.FromArgb(202, 124, 28);
    private static readonly Color Error = Color.FromArgb(190, 53, 53);

    private readonly Stage01Component _owner;
    private readonly List<FieldHit> _fieldHits = new List<FieldHit>();
    private readonly List<ConditionHit> _conditionHits = new List<ConditionHit>();
    private RectangleF _cardBounds;
    private RectangleF _previousGroup;
    private RectangleF _nextGroup;
    private RectangleF _previousPage;
    private RectangleF _nextPage;
    private RectangleF _previousOrganization;
    private RectangleF _nextOrganization;
    private RectangleF _addOrganization;
    private RectangleF _removeOrganization;
    private RectangleF _confirmBlank;
    private RectangleF _allowReinitialize;
    private RectangleF _showAllFields;
    private RectangleF _readButton;
    private RectangleF _validateButton;
    private RectangleF _commitButton;
    private RectangleF _resetButton;

    public Stage01ComponentAttributes(Stage01Component owner) : base(owner)
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
      _fieldHits.Clear();
      _conditionHits.Clear();
      DrawCard(graphics);
      DrawHeader(graphics);
      DrawGroupNavigation(graphics);
      DrawBody(graphics);
      DrawFooter(graphics);
      RenderComponentParameters(canvas, graphics, Owner, GH_Skin.palette_normal_standard);
    }

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
      if (e.Button != MouseButtons.Left)
        return base.RespondToMouseDown(sender, e);

      PointF point = e.CanvasLocation;
      if (_previousGroup.Contains(point)) { _owner.MoveGroup(-1); return GH_ObjectResponse.Handled; }
      if (_nextGroup.Contains(point)) { _owner.MoveGroup(1); return GH_ObjectResponse.Handled; }
      if (_previousPage.Contains(point)) { _owner.SetScrollOffset(_owner.Model.ScrollOffset - PageSize, PageSize); return GH_ObjectResponse.Handled; }
      if (_nextPage.Contains(point)) { _owner.SetScrollOffset(_owner.Model.ScrollOffset + PageSize, PageSize); return GH_ObjectResponse.Handled; }
      if (_previousOrganization.Contains(point)) { _owner.MoveOrganization(-1); return GH_ObjectResponse.Handled; }
      if (_nextOrganization.Contains(point)) { _owner.MoveOrganization(1); return GH_ObjectResponse.Handled; }
      if (_addOrganization.Contains(point)) { _owner.AddOrganization(); return GH_ObjectResponse.Handled; }
      if (_removeOrganization.Contains(point)) { _owner.RemoveCurrentOrganization(); return GH_ObjectResponse.Handled; }
      if (_confirmBlank.Contains(point)) { _owner.ToggleConfirmBlank(); return GH_ObjectResponse.Handled; }
      if (_allowReinitialize.Contains(point)) { _owner.ToggleAllowReinitialize(); return GH_ObjectResponse.Handled; }
      if (_showAllFields.Contains(point)) { _owner.ToggleShowAllFields(); return GH_ObjectResponse.Handled; }
      if (_readButton.Contains(point)) { _owner.ReadCurrentRevitFile(); return GH_ObjectResponse.Handled; }
      if (_validateButton.Contains(point)) { _owner.ValidateNow(); return GH_ObjectResponse.Handled; }
      if (_commitButton.Contains(point)) { _owner.CommitInitialization(); return GH_ObjectResponse.Handled; }
      if (_resetButton.Contains(point)) { _owner.ResetForm(); return GH_ObjectResponse.Handled; }

      foreach (ConditionHit hit in _conditionHits)
      {
        if (!hit.Bounds.Contains(point)) continue;
        _owner.ToggleCondition(hit.Key);
        return GH_ObjectResponse.Handled;
      }

      foreach (FieldHit hit in _fieldHits)
      {
        if (!hit.Bounds.Contains(point)) continue;
        EditField(sender, hit.Definition, hit.Bounds);
        return GH_ObjectResponse.Handled;
      }

      return base.RespondToMouseDown(sender, e);
    }

    private void EditField(GH_Canvas canvas, FieldDefinition definition, RectangleF bounds)
    {
      if (definition == null || definition.ReadOnly) return;
      if (definition.Kind == FieldKind.Boolean)
      {
        _owner.ToggleBooleanField(definition);
        return;
      }
      string current = _owner.GetFieldValue(definition);
      if (definition.Kind == FieldKind.Enum && definition.AllowedValues.Count > 0)
      {
        InlineEditor.ShowChoice(canvas, bounds, current, definition.AllowedValues.ToArray(), value => _owner.SetFieldValue(definition, value));
      }
      else
      {
        InlineEditor.ShowText(canvas, bounds, current, value => _owner.SetFieldValue(definition, value));
      }
    }

    private void DrawCard(Graphics graphics)
    {
      using (GraphicsPath shadowPath = IconFactory.RoundedRectangle(new RectangleF(_cardBounds.X + 4, _cardBounds.Y + 5, _cardBounds.Width, _cardBounds.Height), 10))
      using (var shadow = new SolidBrush(Color.FromArgb(36, 20, 35, 55)))
        graphics.FillPath(shadow, shadowPath);
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
      RectangleF header = new RectangleF(_cardBounds.X, _cardBounds.Y, _cardBounds.Width, 76f);
      using (GraphicsPath path = HeaderPath(header, 10f))
      using (var brush = new LinearGradientBrush(header, PrimaryDark, Primary, LinearGradientMode.Horizontal))
        graphics.FillPath(brush, path);

      using (var titleFont = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold, GraphicsUnit.Point))
      using (var subFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
      using (var white = new SolidBrush(Color.White))
      using (var whiteMuted = new SolidBrush(Color.FromArgb(210, 235, 245, 255)))
      {
        graphics.DrawString("湖北BIM报规｜01 文件初始化", titleFont, white, _cardBounds.X + 20, _cardBounds.Y + 15);
        string environment = BuildEnvironmentText();
        graphics.DrawString(environment, subFont, whiteMuted, _cardBounds.X + 20, _cardBounds.Y + 45);
      }

      string status = _owner.CurrentStatus;
      Color statusColor = StatusColor(status);
      RectangleF pill = new RectangleF(_cardBounds.Right - 128, _cardBounds.Y + 18, 106, 30);
      FillRounded(graphics, pill, Color.FromArgb(235, Color.White), 15);
      using (var dot = new SolidBrush(statusColor)) graphics.FillEllipse(dot, pill.X + 10, pill.Y + 10, 10, 10);
      using (var statusFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point))
      using (var statusBrush = new SolidBrush(statusColor))
        DrawCentered(graphics, status, statusFont, statusBrush, new RectangleF(pill.X + 24, pill.Y, pill.Width - 28, pill.Height));
    }

    private void DrawGroupNavigation(Graphics graphics)
    {
      float y = _cardBounds.Y + 88f;
      RectangleF nav = new RectangleF(_cardBounds.X + 16, y, _cardBounds.Width - 32, 38);
      FillRounded(graphics, nav, Surface, 7);
      DrawBorder(graphics, nav, Border, 7);

      _previousGroup = new RectangleF(nav.X + 4, nav.Y + 4, 34, nav.Height - 8);
      _nextGroup = new RectangleF(nav.Right - 38, nav.Y + 4, 34, nav.Height - 8);
      DrawSmallButton(graphics, _previousGroup, "‹", false);
      DrawSmallButton(graphics, _nextGroup, "›", false);
      using (var font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point))
      using (var brush = new SolidBrush(Text))
        DrawCentered(graphics, _owner.GetGroupDisplayName(_owner.Model.ActiveGroup), font, brush,
          new RectangleF(_previousGroup.Right + 6, nav.Y, _nextGroup.Left - _previousGroup.Right - 12, nav.Height));
    }

    private void DrawBody(Graphics graphics)
    {
      RectangleF body = new RectangleF(_cardBounds.X + 16, _cardBounds.Y + 138, _cardBounds.Width - 32, 300);
      FillRounded(graphics, body, Surface, 8);
      DrawBorder(graphics, body, Border, 8);

      switch (_owner.Model.ActiveGroup)
      {
        case "10_项目条件": DrawConditions(graphics, body); break;
        case "11_提交与校验": DrawExecution(graphics, body); break;
        default: DrawFields(graphics, body); break;
      }
    }

    private void DrawFields(Graphics graphics, RectangleF body)
    {
      IReadOnlyList<FieldDefinition> allFields = _owner.GetFieldsForActiveGroup();
      int offset = Math.Max(0, Math.Min(_owner.Model.ScrollOffset, Math.Max(0, allFields.Count - PageSize)));
      IReadOnlyList<FieldDefinition> visible = allFields.Skip(offset).Take(PageSize).ToList();
      float top = body.Y + 12;

      if (_owner.Model.ActiveGroup == "06_参建组织")
      {
        RectangleF orgBar = new RectangleF(body.X + 10, top, body.Width - 20, 31);
        _previousOrganization = new RectangleF(orgBar.X, orgBar.Y, 30, 27);
        _nextOrganization = new RectangleF(_previousOrganization.Right + 4, orgBar.Y, 30, 27);
        _addOrganization = new RectangleF(orgBar.Right - 70, orgBar.Y, 30, 27);
        _removeOrganization = new RectangleF(orgBar.Right - 34, orgBar.Y, 30, 27);
        DrawSmallButton(graphics, _previousOrganization, "‹", false);
        DrawSmallButton(graphics, _nextOrganization, "›", false);
        DrawSmallButton(graphics, _addOrganization, "+", false);
        DrawSmallButton(graphics, _removeOrganization, "−", false);
        using (var font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point))
        using (var brush = new SolidBrush(Muted))
          DrawCentered(graphics, "参建单位 " + (_owner.Model.OrganizationIndex + 1) + " / " + _owner.Model.Organizations.Count,
            font, brush, new RectangleF(_nextOrganization.Right + 5, orgBar.Y, _addOrganization.Left - _nextOrganization.Right - 10, 27));
        top += 36;
      }
      else
      {
        _previousOrganization = _nextOrganization = _addOrganization = _removeOrganization = RectangleF.Empty;
      }

      if (visible.Count == 0)
      {
        using (var font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point))
        using (var brush = new SolidBrush(Muted))
          DrawCentered(graphics, "当前分组没有需要手工填写的字段。\n可在提交与校验中启用“显示全部 MVD 字段”。", font, brush, body);
        _previousPage = _nextPage = RectangleF.Empty;
        return;
      }

      float rowHeight = _owner.Model.ActiveGroup == "06_参建组织" ? 27f : 31f;
      float labelWidth = 164f;
      using (var labelFont = new Font("Microsoft YaHei UI", 8.3f, FontStyle.Regular, GraphicsUnit.Point))
      using (var valueFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
      {
        for (int index = 0; index < visible.Count; ++index)
        {
          FieldDefinition definition = visible[index];
          RectangleF row = new RectangleF(body.X + 10, top + index * rowHeight, body.Width - 20, rowHeight - 4);
          if (index % 2 == 1)
          {
            using (var alternate = new SolidBrush(Color.FromArgb(247, 249, 252))) graphics.FillRectangle(alternate, row);
          }
          RectangleF labelRect = new RectangleF(row.X + 6, row.Y, labelWidth - 10, row.Height);
          RectangleF valueRect = new RectangleF(row.X + labelWidth, row.Y + 2, row.Width - labelWidth - 6, row.Height - 4);
          using (var labelBrush = new SolidBrush(definition.Deferred ? Color.FromArgb(160, Muted) : Text))
            DrawLeftCentered(graphics, definition.Label, labelFont, labelBrush, labelRect);

          string value = _owner.GetFieldValue(definition);
          Color valueBackground = definition.ReadOnly ? Color.FromArgb(239, 243, 247) : Color.White;
          Color valueColor = string.IsNullOrWhiteSpace(value) ? Color.FromArgb(155, 165, 175) : Text;
          FillRounded(graphics, valueRect, valueBackground, 4);
          DrawBorder(graphics, valueRect, definition.ReadOnly ? Color.FromArgb(220, 226, 233) : Color.FromArgb(190, 203, 218), 4);
          string display = string.IsNullOrWhiteSpace(value) ? (definition.ReadOnly ? "自动读取／生成" : "点击填写") : Compact(value, 34);
          using (var valueBrush = new SolidBrush(valueColor))
            DrawLeftCentered(graphics, display, valueFont, valueBrush, new RectangleF(valueRect.X + 7, valueRect.Y, valueRect.Width - 22, valueRect.Height));
          if (!definition.ReadOnly)
          {
            using (var arrowFont = new Font("Segoe UI Symbol", 7f, FontStyle.Regular, GraphicsUnit.Point))
            using (var arrowBrush = new SolidBrush(Muted))
              DrawCentered(graphics, definition.Kind == FieldKind.Enum ? "▾" : "✎", arrowFont, arrowBrush,
                new RectangleF(valueRect.Right - 19, valueRect.Y, 16, valueRect.Height));
            _fieldHits.Add(new FieldHit(definition, valueRect));
          }
        }
      }

      bool hasPaging = allFields.Count > PageSize;
      if (hasPaging)
      {
        float pageY = body.Bottom - 29;
        _previousPage = new RectangleF(body.Right - 96, pageY, 30, 23);
        _nextPage = new RectangleF(body.Right - 60, pageY, 30, 23);
        DrawSmallButton(graphics, _previousPage, "‹", offset <= 0);
        DrawSmallButton(graphics, _nextPage, "›", offset + PageSize >= allFields.Count);
        using (var pageFont = new Font("Microsoft YaHei UI", 7.8f, FontStyle.Regular, GraphicsUnit.Point))
        using (var pageBrush = new SolidBrush(Muted))
          DrawLeftCentered(graphics, (offset + 1) + "–" + Math.Min(offset + PageSize, allFields.Count) + " / " + allFields.Count,
            pageFont, pageBrush, new RectangleF(body.X + 16, pageY, 120, 23));
      }
      else
      {
        _previousPage = _nextPage = RectangleF.Empty;
      }
    }

    private void DrawConditions(Graphics graphics, RectangleF body)
    {
      _previousPage = _nextPage = _previousOrganization = _nextOrganization = _addOrganization = _removeOrganization = RectangleF.Empty;
      float columnWidth = (body.Width - 30) / 2f;
      using (var font = new Font("Microsoft YaHei UI", 8.7f, FontStyle.Regular, GraphicsUnit.Point))
      using (var textBrush = new SolidBrush(Text))
      {
        for (int index = 0; index < _owner.Registry.Conditions.Count; ++index)
        {
          ConditionDefinition condition = _owner.Registry.Conditions[index];
          int column = index % 2;
          int row = index / 2;
          RectangleF bounds = new RectangleF(body.X + 10 + column * (columnWidth + 10), body.Y + 18 + row * 47, columnWidth, 35);
          FillRounded(graphics, bounds, Color.FromArgb(249, 251, 253), 5);
          DrawBorder(graphics, bounds, Border, 5);
          DrawCheckbox(graphics, new RectangleF(bounds.X + 9, bounds.Y + 9, 17, 17), _owner.Model.GetCondition(condition.Key));
          DrawLeftCentered(graphics, condition.Label, font, textBrush, new RectangleF(bounds.X + 34, bounds.Y, bounds.Width - 40, bounds.Height));
          _conditionHits.Add(new ConditionHit(condition.Key, bounds));
        }
      }
      using (var noteFont = new Font("Microsoft YaHei UI", 7.8f, FontStyle.Regular, GraphicsUnit.Point))
      using (var noteBrush = new SolidBrush(Muted))
        graphics.DrawString("这里只采集当前总平文件是否涉及相应对象；后续建模与检测任务据此激活。", noteFont, noteBrush,
          new RectangleF(body.X + 12, body.Bottom - 42, body.Width - 24, 30));
    }

    private void DrawExecution(Graphics graphics, RectangleF body)
    {
      _previousPage = _nextPage = _previousOrganization = _nextOrganization = _addOrganization = _removeOrganization = RectangleF.Empty;
      float x = body.X + 15;
      float y = body.Y + 17;
      _confirmBlank = new RectangleF(x, y, body.Width - 30, 32);
      _allowReinitialize = new RectangleF(x, y + 39, body.Width - 30, 32);
      _showAllFields = new RectangleF(x, y + 78, body.Width - 30, 32);
      DrawToggleRow(graphics, _confirmBlank, "确认当前为新建／刚拆分、尚未正式建模的文件", _owner.Model.ConfirmBlankProject, true);
      DrawToggleRow(graphics, _allowReinitialize, "允许覆盖当前文件已有的初始化记录", _owner.Model.AllowReinitialize, false);
      DrawToggleRow(graphics, _showAllFields, "显示全部 102 项 Stage 01 MVD 字段", _owner.Model.ShowAllFields, false);

      float buttonY = y + 132;
      float gap = 8;
      float buttonWidth = (body.Width - 30 - gap * 3) / 4f;
      _readButton = new RectangleF(x, buttonY, buttonWidth, 38);
      _validateButton = new RectangleF(_readButton.Right + gap, buttonY, buttonWidth, 38);
      _commitButton = new RectangleF(_validateButton.Right + gap, buttonY, buttonWidth, 38);
      _resetButton = new RectangleF(_commitButton.Right + gap, buttonY, buttonWidth, 38);
      DrawActionButton(graphics, _readButton, "读取文件", Color.FromArgb(232, 240, 251), PrimaryDark, false);
      DrawActionButton(graphics, _validateButton, "执行校验", Color.FromArgb(235, 242, 247), Color.FromArgb(53, 74, 94), false);
      DrawActionButton(graphics, _commitButton, _owner.IsCommitting ? "提交中…" : "写入并回读", Primary, Color.White, _owner.IsCommitting);
      DrawActionButton(graphics, _resetButton, "重置表单", Color.FromArgb(247, 238, 238), Error, false);

      RectangleF summary = new RectangleF(x, buttonY + 52, body.Width - 30, 73);
      FillRounded(graphics, summary, Color.FromArgb(246, 248, 251), 5);
      string summaryText = BuildValidationSummary();
      using (var font = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular, GraphicsUnit.Point))
      using (var brush = new SolidBrush(Text))
        graphics.DrawString(summaryText, font, brush, new RectangleF(summary.X + 10, summary.Y + 8, summary.Width - 20, summary.Height - 16));
    }

    private void DrawFooter(Graphics graphics)
    {
      float top = _cardBounds.Y + 450;
      RectangleF messageBox = new RectangleF(_cardBounds.X + 16, top, _cardBounds.Width - 32, 102);
      FillRounded(graphics, messageBox, Surface, 8);
      DrawBorder(graphics, messageBox, Border, 8);

      using (var headingFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point))
      using (var bodyFont = new Font("Microsoft YaHei UI", 7.8f, FontStyle.Regular, GraphicsUnit.Point))
      using (var headingBrush = new SolidBrush(StatusColor(_owner.CurrentStatus)))
      using (var bodyBrush = new SolidBrush(Muted))
      {
        graphics.DrawString("状态｜" + _owner.CurrentStatus, headingFont, headingBrush, messageBox.X + 12, messageBox.Y + 10);
        string message = string.Join("\n", _owner.CurrentMessages.Take(3).Select(text => "• " + Compact(text, 74)));
        graphics.DrawString(message, bodyFont, bodyBrush, new RectangleF(messageBox.X + 12, messageBox.Y + 34, messageBox.Width - 24, messageBox.Height - 40));
      }

      using (var versionFont = new Font("Microsoft YaHei UI", 7f, FontStyle.Regular, GraphicsUnit.Point))
      using (var versionBrush = new SolidBrush(Color.FromArgb(145, 155, 165)))
        graphics.DrawString("Revit 2020 · Rhino 8 · BIMBaoGui Stage 01 v0.2.0", versionFont, versionBrush,
          _cardBounds.X + 18, _cardBounds.Bottom - 15);
    }

    private string BuildEnvironmentText()
    {
      if (!_owner.Snapshot.HostAvailable) return "等待 Rhino.Inside.Revit 活动文档";
      string title = string.IsNullOrWhiteSpace(_owner.Snapshot.DocumentTitle) ? "未命名文件" : _owner.Snapshot.DocumentTitle;
      return "Revit " + _owner.Snapshot.RevitVersion + " · " + Compact(title, 36);
    }

    private string BuildValidationSummary()
    {
      int errors = _owner.Validation?.ErrorCount ?? 0;
      int warnings = _owner.Validation?.WarningCount ?? 0;
      string environment = _owner.Snapshot.HostAvailable
        ? (_owner.Snapshot.Messages.Count == 0 ? "环境检查：通过" : "环境检查：" + _owner.Snapshot.Messages.Count + " 项阻断")
        : "环境检查：未连接 Revit";
      string blank = _owner.Snapshot.HostAvailable
        ? (_owner.Snapshot.IsBlank || _owner.Snapshot.IsInitialized ? "空白门禁：通过" : "空白门禁：未通过")
        : "空白门禁：等待读取";
      return environment + "\n" + blank + "\n字段校验：" + errors + " 个错误，" + warnings + " 个警告";
    }

    private static void DrawToggleRow(Graphics graphics, RectangleF bounds, string label, bool value, bool important)
    {
      FillRounded(graphics, bounds, important && !value ? Color.FromArgb(255, 247, 235) : Color.FromArgb(249, 251, 253), 5);
      DrawBorder(graphics, bounds, important && !value ? Color.FromArgb(231, 177, 102) : Border, 5);
      DrawCheckbox(graphics, new RectangleF(bounds.X + 9, bounds.Y + 7, 18, 18), value);
      using (var font = new Font("Microsoft YaHei UI", 8.4f, important ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point))
      using (var brush = new SolidBrush(Text))
        DrawLeftCentered(graphics, label, font, brush, new RectangleF(bounds.X + 36, bounds.Y, bounds.Width - 42, bounds.Height));
    }

    private static void DrawCheckbox(Graphics graphics, RectangleF bounds, bool value)
    {
      FillRounded(graphics, bounds, value ? Primary : Color.White, 3);
      DrawBorder(graphics, bounds, value ? Primary : Color.FromArgb(160, 176, 191), 3);
      if (!value) return;
      using (var pen = new Pen(Color.White, 1.8f))
      {
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        graphics.DrawLines(pen, new[]
        {
          new PointF(bounds.X + 4, bounds.Y + 9),
          new PointF(bounds.X + 7.5f, bounds.Y + 12.5f),
          new PointF(bounds.X + 14, bounds.Y + 5.5f)
        });
      }
    }

    private static void DrawActionButton(Graphics graphics, RectangleF bounds, string label, Color background, Color foreground, bool disabled)
    {
      Color actualBackground = disabled ? Color.FromArgb(205, 214, 224) : background;
      Color actualForeground = disabled ? Color.FromArgb(125, 137, 149) : foreground;
      FillRounded(graphics, bounds, actualBackground, 6);
      DrawBorder(graphics, bounds, Color.FromArgb(180, actualBackground), 6);
      using (var font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold, GraphicsUnit.Point))
      using (var brush = new SolidBrush(actualForeground))
        DrawCentered(graphics, label, font, brush, bounds);
    }

    private static void DrawSmallButton(Graphics graphics, RectangleF bounds, string label, bool disabled)
    {
      Color fill = disabled ? Color.FromArgb(241, 244, 247) : Color.FromArgb(235, 241, 248);
      Color text = disabled ? Color.FromArgb(185, 193, 201) : PrimaryDark;
      FillRounded(graphics, bounds, fill, 5);
      DrawBorder(graphics, bounds, Color.FromArgb(207, 216, 225), 5);
      using (var font = new Font("Segoe UI Symbol", 11f, FontStyle.Bold, GraphicsUnit.Point))
      using (var brush = new SolidBrush(text))
        DrawCentered(graphics, label, font, brush, bounds);
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
      if (bounds.IsEmpty) return;
      using (GraphicsPath path = IconFactory.RoundedRectangle(bounds, radius))
      using (var brush = new SolidBrush(color))
        graphics.FillPath(brush, path);
    }

    private static void DrawBorder(Graphics graphics, RectangleF bounds, Color color, float radius)
    {
      if (bounds.IsEmpty) return;
      using (GraphicsPath path = IconFactory.RoundedRectangle(bounds, radius))
      using (var pen = new Pen(color, 1f))
        graphics.DrawPath(pen, path);
    }

    private static void DrawCentered(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds)
    {
      using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
        graphics.DrawString(text ?? string.Empty, font, brush, bounds, format);
    }

    private static void DrawLeftCentered(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds)
    {
      using (var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
        graphics.DrawString(text ?? string.Empty, font, brush, bounds, format);
    }

    private static Color StatusColor(string status)
    {
      if (status == "初始化通过") return Success;
      if (status == "输入未完成" || status == "待提交" || status == "已修改待重新提交") return Warning;
      if (status == "提交中") return Primary;
      return Error;
    }

    private static string Compact(string value, int maximum)
    {
      string normalized = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
      return normalized.Length <= maximum ? normalized : normalized.Substring(0, Math.Max(0, maximum - 1)) + "…";
    }

    private sealed class FieldHit
    {
      public FieldHit(FieldDefinition definition, RectangleF bounds) { Definition = definition; Bounds = bounds; }
      public FieldDefinition Definition { get; }
      public RectangleF Bounds { get; }
    }

    private sealed class ConditionHit
    {
      public ConditionHit(string key, RectangleF bounds) { Key = key; Bounds = bounds; }
      public string Key { get; }
      public RectangleF Bounds { get; }
    }
  }
}
