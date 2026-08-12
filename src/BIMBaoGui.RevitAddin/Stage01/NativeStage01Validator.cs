using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage01
{
  internal static class NativeStage01ValidationCodes
  {
    internal const string RequiredValueMissing = "REQUIRED_VALUE_MISSING";
    internal const string InvalidNumber = "INVALID_NUMBER";
    internal const string InvalidInteger = "INVALID_INTEGER";
    internal const string InvalidBoolean = "INVALID_BOOLEAN";
    internal const string InvalidDateTime = "INVALID_DATETIME";
    internal const string InvalidGuid = "INVALID_GUID";
    internal const string InvalidEnum = "INVALID_ENUM";
    internal const string InvalidPostalCode = "INVALID_POSTAL_CODE";
    internal const string InvalidPhone = "INVALID_PHONE";
    internal const string InvalidEmail = "INVALID_EMAIL";
    internal const string InvalidCreditCode = "INVALID_CREDIT_CODE";
    internal const string TrueNorthOutOfRange = "TRUE_NORTH_OUT_OF_RANGE";
    internal const string UnknownModelProfile = "UNKNOWN_MODEL_PROFILE";
    internal const string PayloadVersionMismatch =
      "PAYLOAD_VERSION_MISMATCH";
    internal const string ConditionMissing = "CONDITION_MISSING";
    internal const string ProjectConditionDeclarationMissing =
      "PROJECT_CONDITION_DECLARATION_MISSING";
    internal const string ProjectConditionDeclarationConflict =
      "PROJECT_CONDITION_DECLARATION_CONFLICT";
    internal const string OrganizationMissing = "ORGANIZATION_MISSING";
  }

  internal sealed class NativeStage01ValidationMessage
  {
    internal string Code { get; set; } = string.Empty;
    internal string FieldKey { get; set; } = string.Empty;
    internal string Message { get; set; } = string.Empty;
  }

  internal sealed class NativeStage01ValidationResult
  {
    internal NativeStage01ValidationResult(
      IEnumerable<NativeStage01ValidationMessage> messages)
    {
      Messages = new ReadOnlyCollection<NativeStage01ValidationMessage>(
        (messages ?? Array.Empty<NativeStage01ValidationMessage>()).ToArray());
    }

    internal IReadOnlyList<NativeStage01ValidationMessage> Messages { get; }
    internal bool IsValid => Messages.Count == 0;
  }

  internal static class NativeStage01Validator
  {
    private static readonly HashSet<string> RequiredFieldKeys =
      new HashSet<string>(StringComparer.Ordinal)
      {
        NativeStage01Keys.ProjectNumber,
        NativeStage01Keys.ProjectName,
        NativeStage01Keys.SubitemName,
        NativeStage01Keys.ModelFileType,
        NativeStage01Keys.ModelScope,
        NativeStage01Keys.BaseX,
        NativeStage01Keys.BaseY,
        NativeStage01Keys.BaseElevation,
        NativeStage01Keys.CoordinateSystem,
        NativeStage01Keys.ElevationSystem,
        NativeStage01Keys.TrueNorthAngle
      };

    private static readonly Regex PostalCode = new Regex(
      "^\\d{6}$",
      RegexOptions.CultureInvariant);
    private static readonly Regex Phone = new Regex(
      "^\\+?[0-9][0-9\\- ]{6,19}$",
      RegexOptions.CultureInvariant);
    private static readonly Regex CreditCode = new Regex(
      "^[0-9A-HJ-NPQRTUWXY]{18}$",
      RegexOptions.CultureInvariant);

    internal static bool IsRequired(NativeStage01FieldDefinition field)
    {
      return field != null
        && !field.Deferred
        && (field.Essential || RequiredFieldKeys.Contains(field.FieldKey));
    }

    internal static NativeStage01ValidationResult Validate(
      NativeStage01Model model,
      NativeRuleCatalog catalog)
    {
      if (model == null) throw new ArgumentNullException(nameof(model));
      if (catalog == null) throw new ArgumentNullException(nameof(catalog));

      var messages = new List<NativeStage01ValidationMessage>();
      string workflowVersion = model.GetValue(
        NativeStage01Keys.WorkflowVersion).Trim();
      if (!string.Equals(
        workflowVersion,
        NativeStage01Canonicalizer.PayloadSchemaVersion,
        StringComparison.Ordinal))
      {
        Add(
          messages,
          NativeStage01ValidationCodes.PayloadVersionMismatch,
          NativeStage01Keys.WorkflowVersion,
          "Stage01 模型 WorkflowVersion 必须为当前 Payload 协议 "
            + NativeStage01Canonicalizer.PayloadSchemaVersion
            + "。" );
      }
      foreach (NativeStage01FieldDefinition field in catalog.Stage01Fields
        .Where(value => !value.Deferred))
      {
        bool required = IsRequired(field);
        if (field.IsOrganization)
        {
          if (model.Organizations.Count == 0)
          {
            if (required)
            {
              Add(
                messages,
                NativeStage01ValidationCodes.OrganizationMissing,
                field.FieldKey,
                DescribeField(field, string.Empty)
                  + "：至少需要一条参建组织记录。");
            }
            continue;
          }
          for (int index = 0; index < model.Organizations.Count; index++)
          {
            ValidateValue(
              field,
              model.GetOrganizationValue(index, field.FieldKey),
              required,
              messages,
              "组织 " + (index + 1).ToString(CultureInfo.InvariantCulture)
                + "：");
          }
        }
        else
        {
          ValidateValue(
            field,
            model.GetValue(field.FieldKey),
            required,
            messages,
            string.Empty);
        }
      }

      string modelFileType = model.GetValue(
        NativeStage01Keys.ModelFileType).Trim();
      if (modelFileType.Length > 0
        && !catalog.ModelProfiles.Any(profile => string.Equals(
          profile.ProfileId,
          modelFileType,
          StringComparison.Ordinal)))
      {
        Add(
          messages,
          NativeStage01ValidationCodes.UnknownModelProfile,
          NativeStage01Keys.ModelFileType,
          "模型文件类型（" + NativeStage01Keys.ModelFileType
            + "）：不属于当前 HBR 数据库的 model profile。");
      }

      foreach (NativeConditionDefinition condition in catalog.Conditions)
      {
        if (!model.Conditions.ContainsKey(condition.ConditionId))
        {
          Add(
            messages,
            NativeStage01ValidationCodes.ConditionMissing,
            condition.ConditionId,
            (condition.DisplayName ?? condition.ConditionId)
              + "（" + condition.ConditionId
              + "）：项目条件键缺失；不得按 false 静默补猜。" );
        }
      }
      if (!model.Conditions.ContainsKey(
        NativeProjectConditionDeclarationPolicy.NoneConditionId))
      {
        Add(
          messages,
          NativeStage01ValidationCodes.ConditionMissing,
          NativeProjectConditionDeclarationPolicy.NoneConditionId,
          "无上述项目条件声明键缺失；不得把未声明解释为 false。" );
      }

      NativeProjectConditionDeclarationDecision declaration =
        NativeProjectConditionDeclarationPolicy.Evaluate(model, catalog);
      if (declaration.State
        == NativeProjectConditionDeclarationState.Missing)
      {
        Add(
          messages,
          NativeStage01ValidationCodes.ProjectConditionDeclarationMissing,
          NativeProjectConditionDeclarationPolicy.NoneConditionId,
          "项目条件为必填声明：请选择至少一个实际条件，或勾选“无上述项目条件（已确认）”。" );
      }
      else if (declaration.State
        == NativeProjectConditionDeclarationState.Conflict)
      {
        Add(
          messages,
          NativeStage01ValidationCodes.ProjectConditionDeclarationConflict,
          NativeProjectConditionDeclarationPolicy.NoneConditionId,
          "实际项目条件与“无上述项目条件（已确认）”不能同时选择。" );
      }

      return new NativeStage01ValidationResult(messages);
    }

    private static void ValidateValue(
      NativeStage01FieldDefinition field,
      string value,
      bool required,
      ICollection<NativeStage01ValidationMessage> messages,
      string prefix)
    {
      string normalized = (value ?? string.Empty).Trim();
      string subject = DescribeField(field, prefix);
      if (normalized.Length == 0)
      {
        if (required)
        {
          Add(
            messages,
            NativeStage01ValidationCodes.RequiredValueMissing,
            field.FieldKey,
            subject + "：该字段为必填项。" );
        }
        return;
      }

      string label = field.Label ?? string.Empty;
      if (label.Contains("邮政编码") && !PostalCode.IsMatch(normalized))
      {
        Add(
          messages,
          NativeStage01ValidationCodes.InvalidPostalCode,
          field.FieldKey,
          subject + "：邮政编码应为 6 位数字。" );
      }
      if ((label.Contains("手机")
          || label.Contains("电话")
          || label.Contains("联系电话"))
        && !Phone.IsMatch(normalized))
      {
        Add(
          messages,
          NativeStage01ValidationCodes.InvalidPhone,
          field.FieldKey,
          subject + "：电话号码格式不正确。" );
      }
      if (label.Contains("邮箱") && !IsValidEmail(normalized))
      {
        Add(
          messages,
          NativeStage01ValidationCodes.InvalidEmail,
          field.FieldKey,
          subject + "：邮箱格式不正确。" );
      }
      if ((label.Contains("统一信用代码")
          || label.Contains("社会统一信用代码"))
        && !CreditCode.IsMatch(normalized.ToUpperInvariant()))
      {
        Add(
          messages,
          NativeStage01ValidationCodes.InvalidCreditCode,
          field.FieldKey,
          subject + "：统一社会信用代码应为 18 位有效字符。" );
      }

      switch (field.Kind)
      {
        case NativeStage01FieldKind.Number:
          if (!TryDouble(normalized, out double number))
          {
            Add(
              messages,
              NativeStage01ValidationCodes.InvalidNumber,
              field.FieldKey,
              subject + "：应填写有效数值。" );
          }
          else if (string.Equals(
              field.FieldKey,
              NativeStage01Keys.TrueNorthAngle,
              StringComparison.Ordinal)
            && (number < -180.0 || number > 180.0))
          {
            Add(
              messages,
              NativeStage01ValidationCodes.TrueNorthOutOfRange,
              field.FieldKey,
              subject + "：真北角度必须位于 -180° 到 180°。" );
          }
          break;
        case NativeStage01FieldKind.Integer:
          if (!int.TryParse(
              normalized,
              NumberStyles.Integer,
              CultureInfo.InvariantCulture,
              out _)
            && !int.TryParse(
              normalized,
              NumberStyles.Integer,
              CultureInfo.CurrentCulture,
              out _))
          {
            Add(
              messages,
              NativeStage01ValidationCodes.InvalidInteger,
              field.FieldKey,
              subject + "：应填写整数。" );
          }
          break;
        case NativeStage01FieldKind.Boolean:
          if (!bool.TryParse(normalized, out _)
            && !string.Equals(normalized, "是", StringComparison.Ordinal)
            && !string.Equals(normalized, "否", StringComparison.Ordinal))
          {
            Add(
              messages,
              NativeStage01ValidationCodes.InvalidBoolean,
              field.FieldKey,
              subject + "：应填写 true/false 或 是/否。" );
          }
          break;
        case NativeStage01FieldKind.DateTime:
          if (!DateTime.TryParse(
              normalized,
              CultureInfo.CurrentCulture,
              DateTimeStyles.None,
              out _)
            && !DateTime.TryParse(
              normalized,
              CultureInfo.InvariantCulture,
              DateTimeStyles.RoundtripKind,
              out _))
          {
            Add(
              messages,
              NativeStage01ValidationCodes.InvalidDateTime,
              field.FieldKey,
              subject + "：日期或时间格式不正确。" );
          }
          break;
        case NativeStage01FieldKind.Guid:
          if (!Guid.TryParse(normalized, out Guid guid)
            || guid == Guid.Empty)
          {
            Add(
              messages,
              NativeStage01ValidationCodes.InvalidGuid,
              field.FieldKey,
              subject + "：GUID 格式不正确或为空 GUID。" );
          }
          break;
        case NativeStage01FieldKind.Enum:
          if (field.AllowedValues.Count > 0
            && !field.AllowedValues.Contains(
              normalized,
              StringComparer.Ordinal))
          {
            Add(
              messages,
              NativeStage01ValidationCodes.InvalidEnum,
              field.FieldKey,
              subject + "：值不属于数据库批准的枚举集合。" );
          }
          break;
      }
    }

    private static string DescribeField(
      NativeStage01FieldDefinition field,
      string prefix)
    {
      string label = (field?.Label ?? string.Empty).Trim();
      string key = (field?.FieldKey ?? string.Empty).Trim();
      string name = label.Length > 0 ? label : key;
      if (key.Length > 0
        && !string.Equals(name, key, StringComparison.Ordinal))
      {
        name += "（" + key + "）";
      }
      return (prefix ?? string.Empty) + name;
    }

    private static bool TryDouble(string value, out double number)
    {
      return double.TryParse(
          value,
          NumberStyles.Float | NumberStyles.AllowThousands,
          CultureInfo.InvariantCulture,
          out number)
        || double.TryParse(
          value,
          NumberStyles.Float | NumberStyles.AllowThousands,
          CultureInfo.CurrentCulture,
          out number);
    }

    private static bool IsValidEmail(string value)
    {
      try
      {
        var address = new MailAddress(value);
        return string.Equals(
          address.Address,
          value,
          StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return false;
      }
    }

    private static void Add(
      ICollection<NativeStage01ValidationMessage> messages,
      string code,
      string fieldKey,
      string message)
    {
      messages.Add(new NativeStage01ValidationMessage
      {
        Code = code ?? string.Empty,
        FieldKey = fieldKey ?? string.Empty,
        Message = message ?? string.Empty
      });
    }
  }
}
