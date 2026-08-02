using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class IfcStepDocument
  {
    private readonly IReadOnlyList<Statement> _statements;
    private readonly Dictionary<int, IfcStepEntity> _entities;

    private IfcStepDocument(
      IReadOnlyList<Statement> statements,
      Dictionary<int, IfcStepEntity> entities,
      string schema)
    {
      _statements = statements;
      _entities = entities;
      Schema = schema;
    }

    public string Schema { get; }
    public IEnumerable<IfcStepEntity> Entities => _entities.Values
      .Where(entity => !entity.IsDeleted)
      .OrderBy(entity => entity.Id);

    public static IfcStepDocument Parse(string text)
    {
      if (text == null) throw new ArgumentNullException(nameof(text));

      var statements = new List<Statement>();
      var entities = new Dictionary<int, IfcStepEntity>();
      string schema = null;
      int start = 0;
      bool insideString = false;

      for (int index = 0; index < text.Length; index++)
      {
        char character = text[index];
        if (insideString)
        {
          if (character == '\'' && index + 1 < text.Length
            && text[index + 1] == '\'')
          {
            index++;
            continue;
          }
          if (character == '\'') insideString = false;
          continue;
        }

        if (character == '\'')
        {
          insideString = true;
          continue;
        }
        if (character != ';') continue;

        string segment = text.Substring(start, index - start + 1);
        IfcStepEntity entity = TryParseEntity(segment);
        if (entity != null)
        {
          if (entities.ContainsKey(entity.Id))
            throw new InvalidDataException("IFC STEP 实体编号重复：#" + entity.Id);
          entities.Add(entity.Id, entity);
        }
        else if (schema == null)
        {
          schema = TryParseSchema(segment);
        }
        statements.Add(new Statement(segment, entity));
        start = index + 1;
      }

      if (insideString)
        throw new InvalidDataException("IFC STEP 文件包含未闭合字符串。");
      if (start < text.Length)
        statements.Add(new Statement(text.Substring(start), null));
      if (entities.Count == 0)
        throw new InvalidDataException("IFC STEP 文件不包含实体。");

      return new IfcStepDocument(statements, entities, schema ?? string.Empty);
    }

    public IfcStepEntity GetEntity(int id)
    {
      if (!_entities.TryGetValue(id, out IfcStepEntity entity))
        throw new KeyNotFoundException("找不到 IFC STEP 实体：#" + id);
      return entity;
    }

    public bool TryGetEntity(int id, out IfcStepEntity entity)
    {
      return _entities.TryGetValue(id, out entity) && !entity.IsDeleted;
    }

    public IEnumerable<IfcStepEntity> OfType(string type)
    {
      if (string.IsNullOrWhiteSpace(type))
        return Enumerable.Empty<IfcStepEntity>();
      return _entities.Values
        .Where(entity => !entity.IsDeleted
          && string.Equals(entity.Type, type, StringComparison.OrdinalIgnoreCase))
        .OrderBy(entity => entity.Id);
    }

    public string Serialize()
    {
      var builder = new StringBuilder();
      foreach (Statement statement in _statements)
        builder.Append(statement.Entity == null
          ? statement.Original
          : statement.Entity.Serialize());
      return builder.ToString();
    }

    private static IfcStepEntity TryParseEntity(string segment)
    {
      int leadingLength = 0;
      while (leadingLength < segment.Length
        && char.IsWhiteSpace(segment[leadingLength]))
        leadingLength++;
      if (leadingLength >= segment.Length || segment[leadingLength] != '#')
        return null;

      int equalsIndex = segment.IndexOf('=', leadingLength + 1);
      if (equalsIndex < 0)
        throw new InvalidDataException("IFC STEP 实体缺少等号。");
      string idToken = segment.Substring(
        leadingLength + 1,
        equalsIndex - leadingLength - 1);
      if (!int.TryParse(idToken, out int id) || id <= 0)
        throw new InvalidDataException("IFC STEP 实体编号无效：#" + idToken);

      int openIndex = segment.IndexOf('(', equalsIndex + 1);
      int closeIndex = segment.LastIndexOf(')');
      if (openIndex < 0 || closeIndex < openIndex)
        throw new InvalidDataException("IFC STEP 实体参数无效：#" + id);
      string tail = segment.Substring(closeIndex + 1).Trim();
      if (!string.Equals(tail, ";", StringComparison.Ordinal))
        throw new InvalidDataException("IFC STEP 实体结尾无效：#" + id);

      string type = segment.Substring(
        equalsIndex + 1,
        openIndex - equalsIndex - 1).Trim().ToUpperInvariant();
      if (type.Length == 0)
        throw new InvalidDataException("IFC STEP 实体类型为空：#" + id);
      string argumentsText = segment.Substring(
        openIndex + 1,
        closeIndex - openIndex - 1);
      IReadOnlyList<string> arguments =
        IfcStepSyntax.SplitTopLevelArguments(argumentsText);
      return new IfcStepEntity(
        id,
        type,
        arguments,
        segment,
        segment.Substring(0, leadingLength));
    }

    private static string TryParseSchema(string segment)
    {
      string trimmed = segment.Trim();
      const string keyword = "FILE_SCHEMA";
      if (!trimmed.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
        || !trimmed.EndsWith(");", StringComparison.Ordinal))
        return null;
      int openIndex = trimmed.IndexOf('(', keyword.Length);
      if (openIndex < 0
        || trimmed.Substring(keyword.Length, openIndex - keyword.Length).Trim().Length != 0)
        return null;
      string body = trimmed.Substring(
        openIndex + 1,
        trimmed.Length - openIndex - 3).Trim();
      if (body.Length < 2 || body[0] != '(' || body[body.Length - 1] != ')')
        throw new InvalidDataException("IFC STEP FILE_SCHEMA 无效。");
      IReadOnlyList<string> schemas = IfcStepSyntax.SplitTopLevelArguments(
        body.Substring(1, body.Length - 2));
      if (schemas.Count == 0)
        throw new InvalidDataException("IFC STEP FILE_SCHEMA 为空。");
      return IfcStepSyntax.DecodeString(schemas[0]);
    }

    private sealed class Statement
    {
      public Statement(string original, IfcStepEntity entity)
      {
        Original = original;
        Entity = entity;
      }

      public string Original { get; }
      public IfcStepEntity Entity { get; }
    }
  }
}
