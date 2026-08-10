using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BIMBaoGui.Stage01.Stage02
{
  public static class Stage02PreparationFieldDetailFormatter
  {
    public static string Format(
      Stage02MatchedElement matchedElement,
      Stage02WriteOperation operation)
    {
      if (matchedElement == null)
        throw new ArgumentNullException(nameof(matchedElement));
      if (matchedElement.Element == null)
        throw new ArgumentException(
          "Stage02 字段明细缺少元素引用。",
          nameof(matchedElement));
      if (operation == null)
        throw new ArgumentNullException(nameof(operation));

      var builder = new StringBuilder(512);
      bool firstProperty = true;
      builder.Append('{');
      AppendStringProperty(
        builder,
        ref firstProperty,
        "documentFingerprint",
        matchedElement.Element.DocumentFingerprint);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "documentTitle",
        matchedElement.Element.DocumentTitle);
      AppendNumberProperty(
        builder,
        ref firstProperty,
        "elementId",
        matchedElement.Element.ElementId);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "uniqueId",
        matchedElement.Element.UniqueId);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "elementName",
        matchedElement.Element.ElementName);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "category",
        matchedElement.Element.Category);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "role",
        matchedElement.RoleId);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "scope",
        operation.BindingScope);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "propertyId",
        operation.PropertyId);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "parameterGuid",
        operation.ParameterGuid.ToString("D"));
      AppendStringProperty(
        builder,
        ref firstProperty,
        "parameterName",
        operation.ParameterName);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "oldValue",
        operation.OldValue);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "suggestedValue",
        operation.SuggestedValue);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "source",
        operation.ValueSource);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "requirementLevel",
        operation.RequirementLevel);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "applicability",
        operation.Applicability);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "runtimeStatus",
        operation.RuntimeStatus);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "runtimeBlockCode",
        operation.RuntimeBlockCode);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "runtimeBlockReason",
        operation.RuntimeBlockReason);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "bindingAction",
        operation.BindingAction);
      AppendStringProperty(
        builder,
        ref firstProperty,
        "valueAction",
        operation.ValueAction);
      AppendPropertyName(builder, ref firstProperty, "blockers");
      AppendBlockers(builder, operation);
      builder.Append('}');
      return builder.ToString();
    }

    private static void AppendBlockers(
      StringBuilder builder,
      Stage02WriteOperation operation)
    {
      builder.Append('[');
      bool firstBlocker = true;
      foreach (Stage02Blocker blocker in operation.Blockers
        .Where(value => value != null)
        .OrderBy(value => value.Code, StringComparer.Ordinal)
        .ThenBy(value => value.Message, StringComparer.Ordinal))
      {
        if (!firstBlocker) builder.Append(',');
        firstBlocker = false;
        bool firstProperty = true;
        builder.Append('{');
        AppendStringProperty(
          builder,
          ref firstProperty,
          "code",
          blocker.Code);
        AppendStringProperty(
          builder,
          ref firstProperty,
          "message",
          blocker.Message);
        builder.Append('}');
      }
      builder.Append(']');
    }

    private static void AppendNumberProperty(
      StringBuilder builder,
      ref bool firstProperty,
      string name,
      int value)
    {
      AppendPropertyName(builder, ref firstProperty, name);
      builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendStringProperty(
      StringBuilder builder,
      ref bool firstProperty,
      string name,
      string value)
    {
      AppendPropertyName(builder, ref firstProperty, name);
      AppendJsonString(builder, value);
    }

    private static void AppendPropertyName(
      StringBuilder builder,
      ref bool firstProperty,
      string name)
    {
      if (!firstProperty) builder.Append(',');
      firstProperty = false;
      AppendJsonString(builder, name);
      builder.Append(':');
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
      builder.Append('"');
      foreach (char character in value ?? string.Empty)
      {
        switch (character)
        {
          case '"': builder.Append("\\\""); break;
          case '\\': builder.Append("\\\\"); break;
          case '\b': builder.Append("\\b"); break;
          case '\f': builder.Append("\\f"); break;
          case '\n': builder.Append("\\n"); break;
          case '\r': builder.Append("\\r"); break;
          case '\t': builder.Append("\\t"); break;
          default:
            if (character < 0x20
              || char.IsSurrogate(character)
              || character == '\u2028'
              || character == '\u2029')
            {
              builder.Append("\\u");
              builder.Append(((int) character).ToString(
                "x4",
                CultureInfo.InvariantCulture));
            }
            else
            {
              builder.Append(character);
            }
            break;
        }
      }
      builder.Append('"');
    }
  }
}
