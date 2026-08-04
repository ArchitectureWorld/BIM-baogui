using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BIMBaoGui.Stage01.Mvd
{
  internal sealed class IfcStepDocument
  {
    private readonly List<Statement> _statements;
    private readonly Dictionary<int, IfcStepEntity> _entities;
    private int? _validatedDataEndIndex;
    private int _nextEntityId;
    private bool _entityIdExhausted;
    private readonly IHbrIfcOperationObserver _operationObserver;

    private IfcStepDocument(
      List<Statement> statements,
      Dictionary<int, IfcStepEntity> entities,
      string schema,
      int? validatedDataEndIndex,
      int nextEntityId,
      bool entityIdExhausted,
      IHbrIfcOperationObserver operationObserver)
    {
      _statements = statements;
      _entities = entities;
      Schema = schema;
      _validatedDataEndIndex = validatedDataEndIndex;
      _nextEntityId = nextEntityId;
      _entityIdExhausted = entityIdExhausted;
      _operationObserver = operationObserver;
    }

    public string Schema { get; }
    public IEnumerable<IfcStepEntity> Entities => EnumerateEntities(null);

    public static IfcStepDocument Parse(string text)
    {
      if (text == null) throw new ArgumentNullException(nameof(text));

      var statements = new List<Statement>();
      var entities = new Dictionary<int, IfcStepEntity>();
      string schema = null;
      int start = 0;
      bool insideString = false;
      bool insideComment = false;
      int maximumEntityId = 0;

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

        if (insideComment)
        {
          if (character == '*' && index + 1 < text.Length
            && text[index + 1] == '/')
          {
            insideComment = false;
            index++;
          }
          continue;
        }

        if (character == '\'')
        {
          insideString = true;
          continue;
        }
        if (character == '/' && index + 1 < text.Length
          && text[index + 1] == '*')
        {
          insideComment = true;
          index++;
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
          if (entity.Id > maximumEntityId) maximumEntityId = entity.Id;
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
      if (insideComment)
        throw new InvalidDataException("IFC STEP 文件包含未闭合块注释。");
      if (start < text.Length)
        statements.Add(new Statement(text.Substring(start), null));
      if (entities.Count == 0)
        throw new InvalidDataException("IFC STEP 文件不包含实体。");

      bool entityIdExhausted = maximumEntityId == int.MaxValue;
      return new IfcStepDocument(
        statements,
        entities,
        schema ?? string.Empty,
        null,
        entityIdExhausted ? 0 : maximumEntityId + 1,
        entityIdExhausted,
        null);
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
      return Entities.Where(entity => string.Equals(
        entity.Type,
        type,
        StringComparison.OrdinalIgnoreCase));
    }

    public IfcStepEntity AddEntity(
      string type,
      IEnumerable<string> arguments)
    {
      string canonicalType = ValidateEntityType(type);
      if (arguments == null)
        throw new ArgumentNullException(nameof(arguments));
      List<string> canonicalArguments = arguments
        .Select(IfcStepSyntax.NormalizeSingleArgument)
        .ToList();
      if (canonicalArguments.Count == 0)
        throw new ArgumentException(
          "IFC STEP 实体参数不能为空。",
          nameof(arguments));

      int endIndex = ValidateStructure();
      if (_entityIdExhausted)
        throw new InvalidDataException("IFC STEP 实体编号已耗尽。");
      int id = _nextEntityId;
      string leadingWhitespace = ReadLeadingWhitespace(
        _statements[endIndex].Original);
      string canonical = "#" + id + "=" + canonicalType + "("
        + string.Join(",", canonicalArguments) + ");";
      var entity = new IfcStepEntity(
        id,
        canonicalType,
        canonicalArguments,
        leadingWhitespace + canonical,
        leadingWhitespace);
      _statements.Insert(endIndex, new Statement(
        leadingWhitespace + canonical,
        entity));
      _entities.Add(id, entity);
      _validatedDataEndIndex = endIndex + 1;
      if (id == int.MaxValue)
      {
        _entityIdExhausted = true;
        _nextEntityId = 0;
      }
      else
      {
        _nextEntityId = id + 1;
      }
      return entity;
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

    internal IfcStepDocument Clone()
    {
      return Clone(null);
    }

    internal IfcStepDocument Clone(IHbrIfcOperationObserver observer)
    {
      Dictionary<int, IfcStepEntity> entities = _entities.ToDictionary(
        pair => pair.Key,
        pair => pair.Value.Clone());
      List<Statement> statements = _statements
        .Select(statement => new Statement(
          statement.Original,
          statement.Entity == null
            ? null
            : entities[statement.Entity.Id]))
        .ToList();
      var clone = new IfcStepDocument(
        statements,
        entities,
        Schema,
        _validatedDataEndIndex,
        _nextEntityId,
        _entityIdExhausted,
        observer);
      Observe(observer, HbrIfcOperationKind.CandidateClone);
      return clone;
    }

    internal void ReplaceWith(IfcStepDocument source)
    {
      if (source == null) throw new ArgumentNullException(nameof(source));
      if (ReferenceEquals(this, source)) return;
      source.ValidateStructure();
      if (!string.Equals(Schema, source.Schema, StringComparison.Ordinal))
        throw new InvalidOperationException(
          "不能用不同 schema 的 IFC STEP 文档替换当前状态。");

      foreach (KeyValuePair<int, IfcStepEntity> pair in _entities)
      {
        if (!source._entities.TryGetValue(
          pair.Key,
          out IfcStepEntity sourceEntity))
          throw new InvalidOperationException(
            "IFC STEP transaction candidate 丢失既有实体：#" + pair.Key);
        if (sourceEntity.IsDeleted)
          throw new InvalidOperationException(
            "IFC STEP transaction candidate 删除既有实体：#" + pair.Key);
        if (!string.Equals(
            pair.Value.Type,
            sourceEntity.Type,
            StringComparison.Ordinal)
          || pair.Value.Arguments.Count != sourceEntity.Arguments.Count)
          throw new InvalidOperationException(
            "IFC STEP transaction candidate 改变既有实体结构：#" + pair.Key);
      }

      var replacementEntities = new Dictionary<int, IfcStepEntity>();
      foreach (KeyValuePair<int, IfcStepEntity> pair in source._entities)
      {
        replacementEntities.Add(
          pair.Key,
          _entities.TryGetValue(pair.Key, out IfcStepEntity existing)
            ? existing
            : pair.Value);
      }
      List<Statement> replacementStatements = source._statements
        .Select(statement => new Statement(
          statement.Original,
          statement.Entity == null
            ? null
            : replacementEntities[statement.Entity.Id]))
        .ToList();

      foreach (KeyValuePair<int, IfcStepEntity> pair in _entities)
        pair.Value.CopyMutableStateFrom(source._entities[pair.Key]);
      _entities.Clear();
      foreach (KeyValuePair<int, IfcStepEntity> pair in replacementEntities)
        _entities.Add(pair.Key, pair.Value);
      _statements.Clear();
      _statements.AddRange(replacementStatements);
      _validatedDataEndIndex = source._validatedDataEndIndex;
      _nextEntityId = source._nextEntityId;
      _entityIdExhausted = source._entityIdExhausted;
      Observe(source._operationObserver, HbrIfcOperationKind.CommitTransfer);
    }

    private static IfcStepEntity TryParseEntity(string segment)
    {
      int leadingLength = ReadLeadingTriviaLength(segment);
      if (leadingLength >= segment.Length || segment[leadingLength] != '#')
        return null;

      FindEntityBounds(
        segment,
        leadingLength,
        out int equalsIndex,
        out int openIndex,
        out int closeIndex);
      string idToken = NormalizeSingleToken(segment.Substring(
        leadingLength + 1,
        equalsIndex - leadingLength - 1));
      if (!int.TryParse(idToken, out int id) || id <= 0)
        throw new InvalidDataException("IFC STEP 实体编号无效：#" + idToken);

      string tail = NormalizeSingleToken(
        segment.Substring(closeIndex + 1));
      if (!string.Equals(tail, ";", StringComparison.Ordinal))
        throw new InvalidDataException("IFC STEP 实体结尾无效：#" + id);

      string type = NormalizeSingleToken(segment.Substring(
        equalsIndex + 1,
        openIndex - equalsIndex - 1)).ToUpperInvariant();
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

    private static void FindEntityBounds(
      string segment,
      int leadingLength,
      out int equalsIndex,
      out int openIndex,
      out int closeIndex)
    {
      equalsIndex = -1;
      openIndex = -1;
      closeIndex = -1;
      int depth = 0;
      bool insideString = false;
      bool insideComment = false;

      for (int index = leadingLength + 1; index < segment.Length; index++)
      {
        char character = segment[index];
        if (insideString)
        {
          if (character == '\'' && index + 1 < segment.Length
            && segment[index + 1] == '\'')
          {
            index++;
            continue;
          }
          if (character == '\'') insideString = false;
          continue;
        }
        if (insideComment)
        {
          if (character == '*' && index + 1 < segment.Length
            && segment[index + 1] == '/')
          {
            insideComment = false;
            index++;
          }
          continue;
        }
        if (character == '\'')
        {
          insideString = true;
          continue;
        }
        if (character == '/' && index + 1 < segment.Length
          && segment[index + 1] == '*')
        {
          insideComment = true;
          index++;
          continue;
        }
        if (equalsIndex < 0)
        {
          if (character == '=') equalsIndex = index;
          continue;
        }
        if (openIndex < 0)
        {
          if (character == '(')
          {
            openIndex = index;
            depth = 1;
          }
          continue;
        }
        if (character == '(')
        {
          depth++;
          continue;
        }
        if (character != ')') continue;
        depth--;
        if (depth == 0)
        {
          closeIndex = index;
          break;
        }
        if (depth < 0) break;
      }

      if (equalsIndex < 0)
        throw new InvalidDataException("IFC STEP 实体缺少等号。");
      if (openIndex < 0 || closeIndex < openIndex)
        throw new InvalidDataException("IFC STEP 实体参数无效。");
    }

    private static string NormalizeSingleToken(string value)
    {
      IReadOnlyList<string> tokens =
        IfcStepSyntax.SplitTopLevelArguments(value);
      if (tokens.Count != 1)
        throw new InvalidDataException("IFC STEP token 包含顶层逗号。");
      return tokens[0];
    }

    private static string TryParseSchema(string segment)
    {
      string trimmed = IfcStepSyntax.RemoveCommentsOutsideStrings(
        segment).Trim();
      const string keyword = "FILE_SCHEMA";
      if (!trimmed.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
        || trimmed.Length == 0
        || trimmed[trimmed.Length - 1] != ';')
        return null;
      string record = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
      if (!record.EndsWith(")", StringComparison.Ordinal)) return null;
      int openIndex = record.IndexOf('(', keyword.Length);
      if (openIndex < 0
        || record.Substring(keyword.Length, openIndex - keyword.Length).Trim().Length != 0)
        return null;
      string body = record.Substring(
        openIndex + 1,
        record.Length - openIndex - 2).Trim();
      if (body.Length < 2 || body[0] != '(' || body[body.Length - 1] != ')')
        throw new InvalidDataException("IFC STEP FILE_SCHEMA 无效。");
      IReadOnlyList<string> schemas = IfcStepSyntax.SplitTopLevelArguments(
        body.Substring(1, body.Length - 2));
      if (schemas.Count == 0)
        throw new InvalidDataException("IFC STEP FILE_SCHEMA 为空。");
      return IfcStepSyntax.DecodeString(schemas[0]);
    }

    private static string ReadLeadingWhitespace(string value)
    {
      int length = 0;
      while (length < value.Length && char.IsWhiteSpace(value[length]))
        length++;
      return value.Substring(0, length);
    }

    private static bool IsStatement(Statement statement, string token)
    {
      if (statement.Entity != null
        || token.Length == 0
        || token[token.Length - 1] != ';')
        return false;
      string value = IfcStepSyntax.RemoveCommentsOutsideStrings(
        statement.Original).Trim();
      string keyword = token.Substring(0, token.Length - 1);
      if (!value.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        return false;
      int index = keyword.Length;
      while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
      return index == value.Length - 1 && value[index] == ';';
    }

    internal int ValidateStructure()
    {
      if (_validatedDataEndIndex.HasValue)
        return _validatedDataEndIndex.Value;
      Observe(_operationObserver, HbrIfcOperationKind.SectionBoundaryScan);
      ExchangeStructureState state = ExchangeStructureState.BeforeExchange;
      int dataEndIndex = -1;
      for (int index = 0; index < _statements.Count; index++)
      {
        Statement statement = _statements[index];
        if (IsTriviaOnly(statement)) continue;

        bool isExchangeStart = IsStatement(statement, "ISO-10303-21;");
        bool isHeader = IsStatement(statement, "HEADER;");
        bool isData = IsStatement(statement, "DATA;");
        bool isEnd = IsStatement(statement, "ENDSEC;");
        bool isExchangeEnd = IsStatement(
          statement,
          "END-ISO-10303-21;");
        bool isMarker = isExchangeStart
          || isHeader
          || isData
          || isEnd
          || isExchangeEnd;
        bool isHeaderRecord = IsHeaderRecord(statement);

        switch (state)
        {
          case ExchangeStructureState.BeforeExchange:
            if (!isExchangeStart) throw InvalidExchangeStructure();
            state = ExchangeStructureState.BeforeHeader;
            break;
          case ExchangeStructureState.BeforeHeader:
            if (!isHeader) throw InvalidExchangeStructure();
            state = ExchangeStructureState.InHeader;
            break;
          case ExchangeStructureState.InHeader:
            if (statement.Entity != null)
              throw InvalidExchangeStructure();
            if (isEnd)
            {
              state = ExchangeStructureState.BeforeData;
              break;
            }
            if (isMarker) throw InvalidExchangeStructure();
            break;
          case ExchangeStructureState.BeforeData:
            if (!isData) throw InvalidExchangeStructure();
            state = ExchangeStructureState.InData;
            break;
          case ExchangeStructureState.InData:
            if (isHeaderRecord) throw InvalidExchangeStructure();
            if (isEnd)
            {
              dataEndIndex = index;
              state = ExchangeStructureState.AfterData;
              break;
            }
            if (isMarker || statement.Entity == null)
              throw InvalidExchangeStructure();
            break;
          case ExchangeStructureState.AfterData:
            if (!isExchangeEnd) throw InvalidExchangeStructure();
            state = ExchangeStructureState.AfterExchange;
            break;
          default:
            throw InvalidExchangeStructure();
        }
      }
      if (state != ExchangeStructureState.AfterExchange)
        throw InvalidExchangeStructure();
      _validatedDataEndIndex = dataEndIndex;
      return dataEndIndex;
    }

    internal IEnumerable<IfcStepEntity> EnumerateEntities(
      IHbrIfcOperationObserver observer)
    {
      Observe(
        observer ?? _operationObserver,
        HbrIfcOperationKind.DocumentEntityEnumeration);
      foreach (IfcStepEntity entity in _entities.Values
        .Where(candidate => !candidate.IsDeleted)
        .OrderBy(candidate => candidate.Id))
        yield return entity;
    }

    private static void Observe(
      IHbrIfcOperationObserver observer,
      HbrIfcOperationKind kind,
      int itemCount = 1)
    {
      if (observer == null) return;
      try
      {
        observer.Observe(new HbrIfcOperationEvent(kind, itemCount));
      }
      catch
      {
      }
    }

    private static bool IsTriviaOnly(Statement statement)
    {
      return string.IsNullOrWhiteSpace(
        IfcStepSyntax.RemoveCommentsOutsideStrings(statement.Original));
    }

    private static bool IsHeaderRecord(Statement statement)
    {
      return IsRecord(statement, "FILE_DESCRIPTION")
        || IsRecord(statement, "FILE_NAME")
        || IsRecord(statement, "FILE_SCHEMA");
    }

    private static bool IsRecord(Statement statement, string keyword)
    {
      if (statement.Entity != null) return false;
      string value = IfcStepSyntax.RemoveCommentsOutsideStrings(
        statement.Original).Trim();
      if (!value.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        return false;
      int index = keyword.Length;
      while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
      return index < value.Length && value[index] == '(';
    }

    private static InvalidDataException InvalidExchangeStructure()
    {
      return new InvalidDataException(
        "IFC STEP 必须是完整交换文件，且实体与 header record 位于正确区段。");
    }

    private static int ReadLeadingTriviaLength(string value)
    {
      int index = 0;
      while (index < value.Length)
      {
        while (index < value.Length && char.IsWhiteSpace(value[index]))
          index++;
        if (index + 1 >= value.Length
          || value[index] != '/'
          || value[index + 1] != '*')
          break;
        int commentEnd = value.IndexOf(
          "*/",
          index + 2,
          StringComparison.Ordinal);
        if (commentEnd < 0)
          throw new InvalidDataException("IFC STEP 文件包含未闭合块注释。");
        index = commentEnd + 2;
      }
      return index;
    }

    private static string ValidateEntityType(string type)
    {
      if (string.IsNullOrWhiteSpace(type))
        throw new ArgumentException(
          "IFC STEP 实体类型不能为空。",
          nameof(type));
      string canonical = type.Trim().ToUpperInvariant();
      for (int index = 0; index < canonical.Length; index++)
      {
        char character = canonical[index];
        bool valid = character >= 'A' && character <= 'Z';
        if (index > 0)
          valid = valid
            || character >= '0' && character <= '9'
            || character == '_';
        if (!valid)
          throw new ArgumentException(
            "IFC STEP 实体类型包含非法字符：" + type,
            nameof(type));
      }
      return canonical;
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

    private enum ExchangeStructureState
    {
      BeforeExchange,
      BeforeHeader,
      InHeader,
      BeforeData,
      InData,
      AfterData,
      AfterExchange
    }
  }
}
