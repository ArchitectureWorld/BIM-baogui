using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BIMBaoGui.Stage01.Mvd;

namespace BIMBaoGui.HifcCore
{
  public sealed class OfficialCarrierProbeSeedItem
  {
    public string PropertyId { get; set; } = string.Empty;
    public string IfcEntity { get; set; } = string.Empty;
    public string IfcPropertySet { get; set; } = string.Empty;
    public string IfcProperty { get; set; } = string.Empty;
    public string ExactSourceName { get; set; } = string.Empty;
    public string DeclaredIfcType { get; set; } = string.Empty;
    public string CanonicalUnit { get; set; } = string.Empty;
    public string CandidateUniqueId { get; set; } = string.Empty;
    public string CandidateCategoryBuiltInId { get; set; } = string.Empty;
    public string CandidateElementClass { get; set; } = string.Empty;
    public string ParameterGuid { get; set; } = string.Empty;
    public string Sentinel { get; set; } = string.Empty;
    public string Readback { get; set; } = string.Empty;
  }

  public sealed class OfficialCarrierProbeSeedManifest
  {
    public string SchemaVersion { get; set; } = string.Empty;
    public string ContextSha256 { get; set; } = string.Empty;
    public string ProbeRvtSha256 { get; set; } = string.Empty;
    public IReadOnlyList<OfficialCarrierProbeSeedItem> Items { get; set; } =
      Array.Empty<OfficialCarrierProbeSeedItem>();
  }

  public sealed class OfficialCarrierProbeInspectionItem
  {
    public string PropertyId { get; set; } = string.Empty;
    public string CandidateUniqueId { get; set; } = string.Empty;
    public string OwnerGlobalId { get; set; } = string.Empty;
    public string DeclaredIfcType { get; set; } = string.Empty;
    public string CanonicalValue { get; set; } = string.Empty;
    public int MatchCount { get; set; }
  }

  public sealed class OfficialCarrierProbeInspectionResult
  {
    public bool Success { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IReadOnlyList<OfficialCarrierProbeInspectionItem> Items { get; set; }
      = Array.Empty<OfficialCarrierProbeInspectionItem>();
  }

  public sealed class OfficialAcceptanceIdentity
  {
    public string DocumentFingerprint { get; set; } = string.Empty;
    public string RulePackageSha256 { get; set; } = string.Empty;
    public string Stage01ResultHash { get; set; } = string.Empty;
    public string Stage02AResultHash { get; set; } = string.Empty;
    public string Stage02BResultHash { get; set; } = string.Empty;
    public string ManifestSha256 { get; set; } = string.Empty;
    public string GoldenRvtSha256 { get; set; } = string.Empty;
    public string OfficialIfcSha256 { get; set; } = string.Empty;
  }

  public sealed class OfficialAcceptancePropertyDefinition
  {
    public string PropertyId { get; set; } = string.Empty;
    public string Identity { get; set; } = string.Empty;
    public string IfcEntity { get; set; } = string.Empty;
    public string IfcPropertySet { get; set; } = string.Empty;
    public string IfcProperty { get; set; } = string.Empty;
    public string DeclaredIfcType { get; set; } = string.Empty;
    public string CanonicalUnit { get; set; } = string.Empty;
    public string ParameterGuid { get; set; } = string.Empty;
    public string BindingScope { get; set; } = string.Empty;
    public string SourceStage { get; set; } = string.Empty;
  }

  public sealed class OfficialAcceptanceManifest
  {
    public string SchemaVersion { get; set; } = string.Empty;
    public string ManifestVersion { get; set; } = string.Empty;
    public OfficialAcceptanceIdentity Identity { get; set; }
    public IReadOnlyList<OfficialAcceptancePropertyDefinition> Definitions
    {
      get;
      set;
    } = Array.Empty<OfficialAcceptancePropertyDefinition>();
  }

  public sealed class OfficialAcceptanceRevitReadback
  {
    public string PropertyId { get; set; } = string.Empty;
    public string OwnerGlobalId { get; set; } = string.Empty;
    public string OwnerRevitUniqueId { get; set; } = string.Empty;
    public string ParameterGuid { get; set; } = string.Empty;
    public string CanonicalValue { get; set; } = string.Empty;
    public string SourceStage { get; set; } = string.Empty;
    public string SourceResultHash { get; set; } = string.Empty;
  }

  public sealed class OfficialPropertyReadbackRequest
  {
    public string GoldenRvtPath { get; set; } = string.Empty;
    public string OfficialIfcPath { get; set; } = string.Empty;
    public OfficialAcceptanceManifest Manifest { get; set; }
    public IReadOnlyList<OfficialAcceptanceRevitReadback> RevitReadbacks
    {
      get;
      set;
    } = Array.Empty<OfficialAcceptanceRevitReadback>();
    public OfficialAcceptanceIdentity StrictValidationIdentity { get; set; }
    public OfficialAcceptanceIdentity OfficialExportIdentity { get; set; }
  }

  public sealed class OfficialPropertyReadbackValue
  {
    public string OwnerGlobalId { get; set; } = string.Empty;
    public string OwnerRevitUniqueId { get; set; } = string.Empty;
    public string ParameterGuid { get; set; } = string.Empty;
    public string RevitCanonicalValue { get; set; } = string.Empty;
    public string SourceStage { get; set; } = string.Empty;
    public string SourceResultHash { get; set; } = string.Empty;
    public string OfficialIfcCanonicalValue { get; set; } = string.Empty;
    public string OfficialIfcDeclaredType { get; set; } = string.Empty;
    public string OfficialIfcUnit { get; set; } = string.Empty;
  }

  public sealed class OfficialPropertyReadbackRecord
  {
    public string PropertyId { get; set; } = string.Empty;
    public IReadOnlyList<OfficialPropertyReadbackValue> Values { get; set; } =
      Array.Empty<OfficialPropertyReadbackValue>();
  }

  public sealed class OfficialPropertyReadbackResult
  {
    public bool Success { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IReadOnlyList<OfficialPropertyReadbackRecord> Records { get; set; } =
      Array.Empty<OfficialPropertyReadbackRecord>();
  }

  public static class OfficialCarrierProbeInspector
  {
    public static OfficialPropertyReadbackResult ResolveFinalReadback(
      OfficialPropertyReadbackRequest request)
    {
      string validation = ValidateFinalRequest(request);
      if (validation.Length > 0) return RejectFinal(validation);
      string ifcText;
      try
      {
        ifcText = File.ReadAllText(request.OfficialIfcPath);
      }
      catch (Exception exception)
      {
        return RejectFinal("FINAL_IFC_READ_FAILED", exception.Message);
      }

      IReadOnlyList<FinalIfcValue> officialValues;
      try
      {
        officialValues = ReadFinalIfcValues(
          ifcText,
          request.Manifest.Definitions);
      }
      catch (InvalidDataException exception)
      {
        return RejectFinal(exception.Message);
      }
      catch (Exception exception)
      {
        return RejectFinal("FINAL_IFC_INVALID", exception.Message);
      }

      OfficialAcceptanceRevitReadback[] readbacks = request.RevitReadbacks
        .Where(value => value != null).ToArray();
      var records = new List<OfficialPropertyReadbackRecord>();
      foreach (OfficialAcceptancePropertyDefinition definition in
        request.Manifest.Definitions.OrderBy(
          value => value.PropertyId,
          StringComparer.Ordinal))
      {
        OfficialAcceptanceRevitReadback[] revit = readbacks.Where(value =>
          string.Equals(
            value.PropertyId,
            definition.PropertyId,
            StringComparison.Ordinal)).ToArray();
        FinalIfcValue[] official = officialValues.Where(value =>
          string.Equals(
            value.PropertyId,
            definition.PropertyId,
            StringComparison.Ordinal)).ToArray();
        string[] revitSet = revit.Select(value => value.OwnerGlobalId + "\n"
            + value.CanonicalValue)
          .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string[] officialSet = official.Select(value => value.OwnerGlobalId + "\n"
            + value.CanonicalValue)
          .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!revitSet.SequenceEqual(officialSet, StringComparer.Ordinal))
          return RejectFinal("FINAL_OWNER_VALUE_SET_MISMATCH");

        var values = new List<OfficialPropertyReadbackValue>();
        foreach (OfficialAcceptanceRevitReadback readback in revit
          .OrderBy(value => value.OwnerGlobalId, StringComparer.Ordinal)
          .ThenBy(value => value.OwnerRevitUniqueId, StringComparer.Ordinal))
        {
          FinalIfcValue match = official.Single(value => string.Equals(
              value.OwnerGlobalId,
              readback.OwnerGlobalId,
              StringComparison.Ordinal)
            && string.Equals(
              value.CanonicalValue,
              readback.CanonicalValue,
              StringComparison.Ordinal));
          values.Add(new OfficialPropertyReadbackValue
          {
            OwnerGlobalId = readback.OwnerGlobalId,
            OwnerRevitUniqueId = readback.OwnerRevitUniqueId,
            ParameterGuid = readback.ParameterGuid,
            RevitCanonicalValue = readback.CanonicalValue,
            SourceStage = readback.SourceStage,
            SourceResultHash = readback.SourceResultHash,
            OfficialIfcCanonicalValue = match.CanonicalValue,
            OfficialIfcDeclaredType = definition.DeclaredIfcType,
            OfficialIfcUnit = definition.CanonicalUnit ?? string.Empty
          });
        }
        records.Add(new OfficialPropertyReadbackRecord
        {
          PropertyId = definition.PropertyId,
          Values = values.ToArray()
        });
      }
      return new OfficialPropertyReadbackResult
      {
        Success = true,
        Records = records.ToArray()
      };
    }

    public static OfficialCarrierProbeInspectionResult InspectFile(
      string ifcPath,
      OfficialCarrierProbeSeedManifest manifest)
    {
      if (string.IsNullOrWhiteSpace(ifcPath) || !File.Exists(ifcPath))
        return Reject("PROBE_IFC_NOT_FOUND");
      string text;
      try
      {
        text = File.ReadAllText(ifcPath);
      }
      catch (Exception exception)
      {
        return Reject("PROBE_IFC_READ_FAILED", exception.Message);
      }
      return InspectText(text, manifest);
    }

    public static OfficialCarrierProbeInspectionResult InspectText(
      string ifcText,
      OfficialCarrierProbeSeedManifest manifest)
    {
      string manifestError = ValidateManifest(manifest);
      if (manifestError.Length > 0) return Reject(manifestError);
      IfcStepDocument document;
      try
      {
        document = IfcStepDocument.Parse(ifcText ?? string.Empty);
      }
      catch (Exception exception)
      {
        return Reject("PROBE_IFC_PARSE_FAILED", exception.Message);
      }
      if (!string.Equals(document.Schema, "IFC4",
        StringComparison.OrdinalIgnoreCase))
        return Reject("PROBE_IFC_SCHEMA_UNSUPPORTED");

      Dictionary<int, IfcStepEntity> properties = document
        .OfType("IFCPROPERTYSINGLEVALUE")
        .Where(value => value.Arguments.Count >= 4)
        .ToDictionary(value => value.Id);
      var propertySets = new List<PropertySet>();
      try
      {
        foreach (IfcStepEntity entity in document.OfType("IFCPROPERTYSET"))
        {
          if (entity.Arguments.Count < 5) continue;
          propertySets.Add(new PropertySet
          {
            Id = entity.Id,
            Name = IfcStepSyntax.DecodeString(entity.Arguments[2]),
            PropertyIds = IfcStepSyntax.ParseReferenceList(entity.Arguments[4])
          });
        }
      }
      catch (Exception exception)
      {
        return Reject("PROBE_IFC_STRUCTURE_INVALID", exception.Message);
      }
      var ownersByPropertySet = new Dictionary<int, HashSet<int>>();
      try
      {
        foreach (IfcStepEntity relationship in document
          .OfType("IFCRELDEFINESBYPROPERTIES"))
        {
          if (relationship.Arguments.Count < 6) continue;
          int psetId = IfcStepSyntax.ParseReference(
            relationship.Arguments[5]);
          if (!ownersByPropertySet.TryGetValue(psetId,
            out HashSet<int> ownerIds))
          {
            ownerIds = new HashSet<int>();
            ownersByPropertySet.Add(psetId, ownerIds);
          }
          foreach (int ownerId in IfcStepSyntax.ParseReferenceList(
            relationship.Arguments[4]))
            ownerIds.Add(ownerId);
        }
      }
      catch (Exception exception)
      {
        return Reject("PROBE_IFC_STRUCTURE_INVALID", exception.Message);
      }
      string sentinelIdentityError = ValidateSentinelIdentities(
        manifest.Items,
        properties,
        propertySets,
        ownersByPropertySet,
        document);
      if (sentinelIdentityError.Length > 0)
        return Reject(sentinelIdentityError);

      var results = new List<OfficialCarrierProbeInspectionItem>();
      foreach (OfficialCarrierProbeSeedItem seed in manifest.Items)
      {
        string expected;
        try
        {
          expected = CanonicalizeFinalValue(seed.DeclaredIfcType,
            seed.Sentinel);
          if (!string.Equals(expected,
            CanonicalizeFinalValue(seed.DeclaredIfcType, seed.Readback),
            StringComparison.Ordinal))
            return Reject("PROBE_SEED_READBACK_MISMATCH");
        }
        catch (Exception exception)
        {
          return Reject("PROBE_SEED_VALUE_INVALID", exception.Message);
        }

        var identityProperties = new List<IfcStepEntity>();
        var identityPsets = new List<PropertySet>();
        foreach (PropertySet pset in propertySets.Where(value => string.Equals(
          value.Name, seed.IfcPropertySet, StringComparison.Ordinal)))
        {
          foreach (int propertyId in pset.PropertyIds)
          {
            if (!properties.TryGetValue(propertyId,
              out IfcStepEntity property)) continue;
            string name;
            try { name = IfcStepSyntax.DecodeString(property.Arguments[0]); }
            catch { continue; }
            if (!string.Equals(name, seed.IfcProperty, StringComparison.Ordinal))
              continue;
            identityProperties.Add(property);
            identityPsets.Add(pset);
          }
        }
        if (identityProperties.Count == 0)
        {
          results.Add(Item(seed, string.Empty, expected, 0));
          continue;
        }

        var matches = new List<Tuple<PropertySet, IfcStepEntity, string>>();
        bool sawExpectedType = false;
        for (int index = 0; index < identityProperties.Count; index++)
        {
          IfcStepEntity property = identityProperties[index];
          if (!TryTypedValue(property.Arguments[2],
            out string declaredType, out string raw))
            return Reject("PROBE_IFC_TYPE_MISMATCH");
          if (!string.Equals(declaredType, seed.DeclaredIfcType,
            StringComparison.OrdinalIgnoreCase))
            continue;
          sawExpectedType = true;
          string canonical;
          try { canonical = CanonicalizeFinalValue(seed.DeclaredIfcType, raw); }
          catch { return Reject("PROBE_IFC_TYPE_MISMATCH"); }
          if (string.Equals(canonical, expected, StringComparison.Ordinal))
            matches.Add(Tuple.Create(identityPsets[index], property, canonical));
        }
        if (!sawExpectedType) return Reject("PROBE_IFC_TYPE_MISMATCH");
        if (matches.Count == 0) return Reject("PROBE_SENTINEL_UNKNOWN");

        var owners = new List<IfcStepEntity>();
        foreach (Tuple<PropertySet, IfcStepEntity, string> match in matches)
        {
          if (!ownersByPropertySet.TryGetValue(match.Item1.Id,
            out HashSet<int> ownerIds)) continue;
          foreach (int ownerId in ownerIds)
          {
            if (!document.TryGetEntity(ownerId, out IfcStepEntity owner))
              continue;
            if (string.Equals(owner.Type, seed.IfcEntity,
              StringComparison.OrdinalIgnoreCase)) owners.Add(owner);
          }
        }
        owners = owners.GroupBy(value => value.Id).Select(group => group.First())
          .ToList();
        if (owners.Count > 1) return Reject("PROBE_OWNER_AMBIGUOUS");
        if (matches.Count > 1) return Reject("PROBE_SENTINEL_MULTIPLE");
        if (owners.Count == 0)
          return Reject("PROBE_OWNER_NOT_FOUND");
        string ownerGlobalId;
        try { ownerGlobalId = IfcStepSyntax.DecodeString(owners[0].Arguments[0]); }
        catch { return Reject("PROBE_OWNER_GLOBALID_INVALID"); }
        results.Add(Item(seed, ownerGlobalId, expected, 1));
      }
      return new OfficialCarrierProbeInspectionResult
      {
        Success = true,
        Items = results.OrderBy(value => value.PropertyId, StringComparer.Ordinal)
          .ThenBy(value => value.CandidateUniqueId, StringComparer.Ordinal)
          .ToArray()
      };
    }

    public static string CanonicalizeFinalValue(
      string declaredIfcType,
      string rawValue)
    {
      string type = declaredIfcType ?? string.Empty;
      string raw = rawValue ?? string.Empty;
      switch (type)
      {
        case "IfcLabel":
        case "IfcText":
          return raw;
        case "IfcInteger":
          if (!long.TryParse(raw, NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out long integer))
            throw new FormatException("IFC integer is invalid.");
          return integer.ToString(CultureInfo.InvariantCulture);
        case "IfcReal":
          if (!double.TryParse(raw, NumberStyles.Float,
            CultureInfo.InvariantCulture, out double real)
            || double.IsNaN(real) || double.IsInfinity(real))
            throw new FormatException("IFC real is invalid or non-finite.");
          return real.ToString("G17", CultureInfo.InvariantCulture);
        case "IfcDateTime":
          if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset dateTime))
            throw new FormatException("IFC date-time is invalid.");
          return dateTime.ToUniversalTime().ToString(
            "O", CultureInfo.InvariantCulture);
        default:
          throw new InvalidDataException("Unknown IFC declared type: " + type);
      }
    }

    private static string ValidateFinalRequest(
      OfficialPropertyReadbackRequest request)
    {
      if (request == null || request.Manifest == null
        || request.Manifest.Identity == null
        || request.StrictValidationIdentity == null
        || request.OfficialExportIdentity == null)
        return "FINAL_REQUEST_INVALID";
      if (!string.Equals(
        request.Manifest.SchemaVersion,
        "HBR_OFFICIAL_ACCEPTANCE_MANIFEST_V1",
        StringComparison.Ordinal)
        || string.IsNullOrWhiteSpace(request.Manifest.ManifestVersion))
        return "FINAL_MANIFEST_INVALID";
      OfficialAcceptancePropertyDefinition[] definitions =
        (request.Manifest.Definitions
          ?? Array.Empty<OfficialAcceptancePropertyDefinition>())
        .Where(value => value != null).ToArray();
      if (definitions.Length == 0
        || definitions.Any(value => string.IsNullOrWhiteSpace(value.PropertyId)
          || string.IsNullOrWhiteSpace(value.IfcEntity)
          || string.IsNullOrWhiteSpace(value.IfcPropertySet)
          || string.IsNullOrWhiteSpace(value.IfcProperty)
          || CanonicalTypeName(value.DeclaredIfcType).Length == 0
          || !Guid.TryParse(value.ParameterGuid, out _))
        || definitions.GroupBy(value => value.PropertyId, StringComparer.Ordinal)
          .Any(group => group.Count() > 1))
        return "FINAL_MANIFEST_INVALID";
      OfficialAcceptanceIdentity manifestIdentity = request.Manifest.Identity;
      if (!IdentityIsValid(manifestIdentity)
        || !IdentityEquals(
          manifestIdentity,
          request.StrictValidationIdentity)
        || !IdentityEquals(
          manifestIdentity,
          request.OfficialExportIdentity))
        return "FINAL_IDENTITY_MISMATCH";
      string actualManifestSha256;
      try
      {
        actualManifestSha256 = OfficialAcceptanceManifestCanonicalizer
          .ComputeSha256(request.Manifest);
      }
      catch
      {
        return "FINAL_MANIFEST_INVALID";
      }
      if (!string.Equals(
          actualManifestSha256,
          manifestIdentity.ManifestSha256,
          StringComparison.Ordinal)
        || !string.Equals(
          actualManifestSha256,
          request.StrictValidationIdentity.ManifestSha256,
          StringComparison.Ordinal)
        || !string.Equals(
          actualManifestSha256,
          request.OfficialExportIdentity.ManifestSha256,
          StringComparison.Ordinal))
        return "FINAL_MANIFEST_SHA_MISMATCH";
      if (definitions.Any(value => !string.Equals(
        value.Identity,
        value.IfcEntity + "|" + value.IfcPropertySet + "|"
          + value.IfcProperty,
        StringComparison.Ordinal)))
        return "FINAL_MANIFEST_INVALID";
      if (string.IsNullOrWhiteSpace(request.GoldenRvtPath)
        || string.IsNullOrWhiteSpace(request.OfficialIfcPath)
        || !File.Exists(request.GoldenRvtPath)
        || !File.Exists(request.OfficialIfcPath))
        return "FINAL_ARTIFACT_NOT_FOUND";
      try
      {
        if (!string.Equals(
          HifcCoreService.ComputeSha256(request.GoldenRvtPath),
          manifestIdentity.GoldenRvtSha256,
          StringComparison.Ordinal)
          || !string.Equals(
            HifcCoreService.ComputeSha256(request.OfficialIfcPath),
            manifestIdentity.OfficialIfcSha256,
            StringComparison.Ordinal))
          return "FINAL_ARTIFACT_SHA_MISMATCH";
      }
      catch
      {
        return "FINAL_ARTIFACT_SHA_MISMATCH";
      }

      OfficialAcceptanceRevitReadback[] readbacks = (request.RevitReadbacks
          ?? Array.Empty<OfficialAcceptanceRevitReadback>())
        .Where(value => value != null).ToArray();
      var definitionById = definitions.ToDictionary(
        value => value.PropertyId,
        value => value,
        StringComparer.Ordinal);
      if (readbacks.Length == 0
        || readbacks.Any(value => !definitionById.TryGetValue(
            value.PropertyId,
            out OfficialAcceptancePropertyDefinition definition)
          || string.IsNullOrWhiteSpace(value.OwnerGlobalId)
          || string.IsNullOrWhiteSpace(value.OwnerRevitUniqueId)
          || !string.Equals(
            value.ParameterGuid,
            definition.ParameterGuid,
            StringComparison.OrdinalIgnoreCase)
          || !string.Equals(
            value.SourceStage,
            definition.SourceStage,
            StringComparison.Ordinal)
          || !string.Equals(
            ExpectedSourceHash(definition.SourceStage, manifestIdentity),
            value.SourceResultHash,
            StringComparison.Ordinal))
        || definitions.Any(definition => !readbacks.Any(value => string.Equals(
          value.PropertyId,
          definition.PropertyId,
          StringComparison.Ordinal)))
        || readbacks.GroupBy(value => value.PropertyId + "\n"
            + value.OwnerGlobalId + "\n" + value.OwnerRevitUniqueId,
          StringComparer.Ordinal).Any(group => group.Count() > 1))
        return "FINAL_REVIT_READBACK_INVALID";
      try
      {
        foreach (OfficialAcceptanceRevitReadback readback in readbacks)
        {
          OfficialAcceptancePropertyDefinition definition =
            definitionById[readback.PropertyId];
          readback.CanonicalValue = CanonicalizeFinalValue(
            definition.DeclaredIfcType,
            readback.CanonicalValue);
        }
      }
      catch
      {
        return "FINAL_REVIT_READBACK_INVALID";
      }
      return string.Empty;
    }

    private static IReadOnlyList<FinalIfcValue> ReadFinalIfcValues(
      string ifcText,
      IReadOnlyList<OfficialAcceptancePropertyDefinition> definitions)
    {
      IfcStepDocument document = IfcStepDocument.Parse(ifcText ?? string.Empty);
      if (!string.Equals(document.Schema, "IFC4",
        StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("FINAL_IFC_SCHEMA_UNSUPPORTED");
      Dictionary<int, IfcStepEntity> properties = document
        .OfType("IFCPROPERTYSINGLEVALUE")
        .Where(value => value.Arguments.Count >= 4)
        .ToDictionary(value => value.Id);
      PropertySet[] propertySets = document.OfType("IFCPROPERTYSET")
        .Where(value => value.Arguments.Count >= 5)
        .Select(value => new PropertySet
        {
          Id = value.Id,
          Name = IfcStepSyntax.DecodeString(value.Arguments[2]),
          PropertyIds = IfcStepSyntax.ParseReferenceList(value.Arguments[4])
        }).ToArray();
      var ownersByPropertySet = new Dictionary<int, HashSet<int>>();
      foreach (IfcStepEntity relationship in document
        .OfType("IFCRELDEFINESBYPROPERTIES"))
      {
        if (relationship.Arguments.Count < 6) continue;
        int psetId = IfcStepSyntax.ParseReference(relationship.Arguments[5]);
        if (!ownersByPropertySet.TryGetValue(psetId,
          out HashSet<int> owners))
        {
          owners = new HashSet<int>();
          ownersByPropertySet.Add(psetId, owners);
        }
        foreach (int owner in IfcStepSyntax.ParseReferenceList(
          relationship.Arguments[4])) owners.Add(owner);
      }

      var result = new List<FinalIfcValue>();
      foreach (OfficialAcceptancePropertyDefinition definition in definitions)
      {
        foreach (PropertySet pset in propertySets.Where(value => string.Equals(
          value.Name,
          definition.IfcPropertySet,
          StringComparison.Ordinal)))
        {
          foreach (int propertyId in pset.PropertyIds)
          {
            if (!properties.TryGetValue(propertyId,
              out IfcStepEntity property)
              || !string.Equals(
                IfcStepSyntax.DecodeString(property.Arguments[0]),
                definition.IfcProperty,
                StringComparison.Ordinal)) continue;
            if (!TryTypedValue(property.Arguments[2],
              out string declaredType,
              out string raw)
              || !string.Equals(
                declaredType,
                definition.DeclaredIfcType,
                StringComparison.Ordinal))
              throw new InvalidDataException("FINAL_IFC_TYPE_MISMATCH");
            string canonical = CanonicalizeFinalValue(declaredType, raw);
            if (!ownersByPropertySet.TryGetValue(pset.Id,
              out HashSet<int> ownerIds)) continue;
            foreach (int ownerId in ownerIds)
            {
              if (!document.TryGetEntity(ownerId, out IfcStepEntity owner)
                || !string.Equals(
                  owner.Type,
                  definition.IfcEntity,
                  StringComparison.OrdinalIgnoreCase)) continue;
              result.Add(new FinalIfcValue
              {
                PropertyId = definition.PropertyId,
                OwnerGlobalId = IfcStepSyntax.DecodeString(owner.Arguments[0]),
                CanonicalValue = canonical
              });
            }
          }
        }
      }
      if (definitions.Any(definition => !result.Any(value => string.Equals(
          value.PropertyId,
          definition.PropertyId,
          StringComparison.Ordinal)))
        || result.GroupBy(value => value.PropertyId + "\n"
            + value.OwnerGlobalId,
          StringComparer.Ordinal).Any(group => group.Count() > 1))
        throw new InvalidDataException("FINAL_IFC_VALUE_SET_INVALID");
      return result;
    }

    private static bool IdentityIsValid(OfficialAcceptanceIdentity value)
    {
      return value != null
        && !string.IsNullOrWhiteSpace(value.DocumentFingerprint)
        && IsSha256(value.RulePackageSha256)
        && IsSha256(value.Stage01ResultHash)
        && IsSha256(value.Stage02AResultHash)
        && IsSha256(value.Stage02BResultHash)
        && IsSha256(value.ManifestSha256)
        && IsSha256(value.GoldenRvtSha256)
        && IsSha256(value.OfficialIfcSha256);
    }

    private static bool IdentityEquals(
      OfficialAcceptanceIdentity left,
      OfficialAcceptanceIdentity right)
    {
      return left != null && right != null
        && string.Equals(left.DocumentFingerprint,
          right.DocumentFingerprint, StringComparison.Ordinal)
        && string.Equals(left.RulePackageSha256,
          right.RulePackageSha256, StringComparison.Ordinal)
        && string.Equals(left.Stage01ResultHash,
          right.Stage01ResultHash, StringComparison.Ordinal)
        && string.Equals(left.Stage02AResultHash,
          right.Stage02AResultHash, StringComparison.Ordinal)
        && string.Equals(left.Stage02BResultHash,
          right.Stage02BResultHash, StringComparison.Ordinal)
        && string.Equals(left.ManifestSha256,
          right.ManifestSha256, StringComparison.Ordinal)
        && string.Equals(left.GoldenRvtSha256,
          right.GoldenRvtSha256, StringComparison.Ordinal)
        && string.Equals(left.OfficialIfcSha256,
          right.OfficialIfcSha256, StringComparison.Ordinal);
    }

    private static string ExpectedSourceHash(
      string sourceStage,
      OfficialAcceptanceIdentity identity)
    {
      switch (sourceStage ?? string.Empty)
      {
        case "STAGE01": return identity.Stage01ResultHash;
        case "STAGE02A": return identity.Stage02AResultHash;
        case "STAGE02B": return identity.Stage02BResultHash;
        default: return string.Empty;
      }
    }

    private static string ValidateManifest(
      OfficialCarrierProbeSeedManifest manifest)
    {
      if (manifest == null
        || !string.Equals(manifest.SchemaVersion,
          "HBR_OFFICIAL_CARRIER_PROBE_SEED_V1", StringComparison.Ordinal)
        || !IsSha256(manifest.ContextSha256)
        || !IsSha256(manifest.ProbeRvtSha256))
        return "PROBE_SEED_MANIFEST_INVALID";
      OfficialCarrierProbeSeedItem[] items = (manifest.Items
          ?? Array.Empty<OfficialCarrierProbeSeedItem>())
        .Where(value => value != null).ToArray();
      if (items.Length == 0
        || items.Any(value => string.IsNullOrWhiteSpace(value.PropertyId)
          || string.IsNullOrWhiteSpace(value.IfcEntity)
          || string.IsNullOrWhiteSpace(value.IfcPropertySet)
          || string.IsNullOrWhiteSpace(value.IfcProperty)
          || string.IsNullOrWhiteSpace(value.DeclaredIfcType)
          || string.IsNullOrWhiteSpace(value.CandidateUniqueId)
          || string.IsNullOrWhiteSpace(value.Sentinel)))
        return "PROBE_SEED_MANIFEST_INVALID";
      if (items.GroupBy(value => value.PropertyId + "\n"
          + value.CandidateUniqueId, StringComparer.Ordinal)
        .Any(group => group.Count() > 1))
        return "PROBE_SEED_IDENTITY_DUPLICATE";
      if (items.GroupBy(value => value.DeclaredIfcType + "\n"
          + value.Sentinel, StringComparer.Ordinal)
        .Any(group => group.Count() > 1))
        return "PROBE_SENTINEL_IDENTITY_AMBIGUOUS";
      return string.Empty;
    }

    private static string ValidateSentinelIdentities(
      IReadOnlyList<OfficialCarrierProbeSeedItem> seeds,
      IReadOnlyDictionary<int, IfcStepEntity> properties,
      IReadOnlyList<PropertySet> propertySets,
      IReadOnlyDictionary<int, HashSet<int>> ownersByPropertySet,
      IfcStepDocument document)
    {
      foreach (OfficialCarrierProbeSeedItem seed in seeds
        ?? Array.Empty<OfficialCarrierProbeSeedItem>())
      {
        string expected;
        try
        {
          expected = CanonicalizeFinalValue(
            seed.DeclaredIfcType,
            seed.Sentinel);
        }
        catch
        {
          return "PROBE_SEED_VALUE_INVALID";
        }
        foreach (PropertySet pset in propertySets)
        {
          foreach (int propertyId in pset.PropertyIds)
          {
            if (!properties.TryGetValue(propertyId,
              out IfcStepEntity property)
              || !TryTypedValue(property.Arguments[2],
                out string declaredType,
                out string raw)
              || !string.Equals(
                declaredType,
                seed.DeclaredIfcType,
                StringComparison.OrdinalIgnoreCase)) continue;
            string canonical;
            try { canonical = CanonicalizeFinalValue(declaredType, raw); }
            catch { continue; }
            if (!string.Equals(canonical, expected, StringComparison.Ordinal))
              continue;
            string propertyName;
            try
            {
              propertyName = IfcStepSyntax.DecodeString(property.Arguments[0]);
            }
            catch
            {
              return "PROBE_IFC_STRUCTURE_INVALID";
            }
            if (!string.Equals(
                pset.Name,
                seed.IfcPropertySet,
                StringComparison.Ordinal)
              || !string.Equals(
                propertyName,
                seed.IfcProperty,
                StringComparison.Ordinal))
              return "PROBE_SENTINEL_IDENTITY_AMBIGUOUS";
            if (ownersByPropertySet.TryGetValue(
              pset.Id,
              out HashSet<int> ownerIds))
            {
              foreach (int ownerId in ownerIds)
              {
                if (document == null
                  || !document.TryGetEntity(ownerId, out IfcStepEntity owner))
                  return "PROBE_IFC_STRUCTURE_INVALID";
                if (!string.Equals(
                  owner.Type,
                  seed.IfcEntity,
                  StringComparison.OrdinalIgnoreCase))
                  return "PROBE_SENTINEL_IDENTITY_AMBIGUOUS";
              }
            }
          }
        }
      }
      return string.Empty;
    }

    private static bool TryTypedValue(
      string token,
      out string declaredType,
      out string raw)
    {
      declaredType = string.Empty;
      raw = string.Empty;
      string value = (token ?? string.Empty).Trim();
      int open = value.IndexOf('(');
      if (open <= 0 || !value.EndsWith(")", StringComparison.Ordinal))
        return false;
      declaredType = CanonicalTypeName(value.Substring(0, open));
      string inner = value.Substring(open + 1, value.Length - open - 2).Trim();
      if (declaredType == "IfcLabel" || declaredType == "IfcText"
        || declaredType == "IfcDateTime")
      {
        try { raw = IfcStepSyntax.DecodeString(inner); }
        catch { return false; }
      }
      else raw = inner;
      return declaredType.Length > 0;
    }

    private static string CanonicalTypeName(string value)
    {
      switch ((value ?? string.Empty).Trim().ToUpperInvariant())
      {
        case "IFCLABEL": return "IfcLabel";
        case "IFCTEXT": return "IfcText";
        case "IFCINTEGER": return "IfcInteger";
        case "IFCREAL": return "IfcReal";
        case "IFCDATETIME": return "IfcDateTime";
        default: return string.Empty;
      }
    }

    private static OfficialCarrierProbeInspectionItem Item(
      OfficialCarrierProbeSeedItem seed,
      string ownerGlobalId,
      string canonicalValue,
      int matchCount)
    {
      return new OfficialCarrierProbeInspectionItem
      {
        PropertyId = seed.PropertyId,
        CandidateUniqueId = seed.CandidateUniqueId,
        OwnerGlobalId = ownerGlobalId ?? string.Empty,
        DeclaredIfcType = seed.DeclaredIfcType,
        CanonicalValue = canonicalValue ?? string.Empty,
        MatchCount = matchCount
      };
    }

    private static bool IsSha256(string value)
    {
      string normalized = value ?? string.Empty;
      return normalized.Length == 64 && normalized.All(character =>
        (character >= '0' && character <= '9')
        || (character >= 'a' && character <= 'f'));
    }

    private static OfficialCarrierProbeInspectionResult Reject(
      string code,
      string message = null)
    {
      return new OfficialCarrierProbeInspectionResult
      {
        ErrorCode = code ?? string.Empty,
        Message = message ?? code ?? string.Empty
      };
    }

    private static OfficialPropertyReadbackResult RejectFinal(
      string code,
      string message = null)
    {
      return new OfficialPropertyReadbackResult
      {
        ErrorCode = code ?? string.Empty,
        Message = message ?? code ?? string.Empty
      };
    }

    private sealed class FinalIfcValue
    {
      internal string PropertyId { get; set; } = string.Empty;
      internal string OwnerGlobalId { get; set; } = string.Empty;
      internal string CanonicalValue { get; set; } = string.Empty;
    }

    private sealed class PropertySet
    {
      internal int Id { get; set; }
      internal string Name { get; set; } = string.Empty;
      internal IReadOnlyList<int> PropertyIds { get; set; } = Array.Empty<int>();
    }
  }
}
