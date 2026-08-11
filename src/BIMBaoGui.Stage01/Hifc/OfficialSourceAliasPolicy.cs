using System;

namespace BIMBaoGui.Stage01.Hifc
{
  internal static class OfficialSourceAliasPolicy
  {
    private static readonly Guid Namespace =
      new Guid("475ad28f-fda6-5c58-8447-08f6f25aa09c");

    public static Guid CreateGuid(
      string bindingScope,
      string revitCategory,
      string carrier,
      string officialSourceParameterGroup,
      string officialExactSourceName,
      string officialSourceParameterType)
    {
      return DeterministicGuidV5.Create(
        Namespace,
        CreateIdentity(
          bindingScope,
          revitCategory,
          carrier,
          officialSourceParameterGroup,
          officialExactSourceName,
          officialSourceParameterType));
    }

    public static Guid CreateLegacyGuid(
      string bindingScope,
      string revitCategory,
      string carrier,
      string officialExactSourceName)
    {
      return DeterministicGuidV5.Create(
        Namespace,
        CreateLegacyIdentity(
          bindingScope,
          revitCategory,
          carrier,
          officialExactSourceName));
    }

    private static string CreateIdentity(
      string bindingScope,
      string revitCategory,
      string carrier,
      string officialSourceParameterGroup,
      string officialExactSourceName,
      string officialSourceParameterType)
    {
      return CreateLegacyIdentity(
          bindingScope,
          revitCategory,
          carrier,
          officialExactSourceName)
        + "|"
        + Normalize(officialSourceParameterGroup)
        + "|"
        + Normalize(officialSourceParameterType);
    }

    private static string CreateLegacyIdentity(
      string bindingScope,
      string revitCategory,
      string carrier,
      string officialExactSourceName)
    {
      return Normalize(bindingScope)
        + "|"
        + Normalize(revitCategory)
        + "|"
        + Normalize(carrier)
        + "|"
        + (officialExactSourceName ?? string.Empty).Trim();
    }

    private static string Normalize(string value)
    {
      return (value ?? string.Empty).Trim().ToUpperInvariant();
    }
  }
}
