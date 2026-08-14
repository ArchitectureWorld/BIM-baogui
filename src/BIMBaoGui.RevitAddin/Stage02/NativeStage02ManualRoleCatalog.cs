using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using BIMBaoGui.RevitAddin.Rules;

namespace BIMBaoGui.RevitAddin.Stage02
{
  internal sealed class NativeStage02ManualRoleCatalog
  {
    private static readonly Lazy<NativeStage02ManualRoleCatalog> LazyCurrent =
      new Lazy<NativeStage02ManualRoleCatalog>(LoadCurrent, true);

    private NativeStage02ManualRoleCatalog(
      IEnumerable<NativeStage02ManualRoleContract> roles)
    {
      NativeStage02ManualRoleContract[] roleArray = (roles
          ?? Array.Empty<NativeStage02ManualRoleContract>())
        .Where(value => value != null)
        .OrderBy(value => value.RoleId, StringComparer.Ordinal)
        .ToArray();
      var byId = new Dictionary<string, NativeStage02ManualRoleContract>(
        StringComparer.Ordinal);
      foreach (NativeStage02ManualRoleContract role in roleArray)
      {
        if (string.IsNullOrWhiteSpace(role.RoleId)
          || byId.ContainsKey(role.RoleId))
        {
          throw new InvalidDataException(
            "HBR Stage02 manual role 无效或重复。" );
        }
        byId.Add(role.RoleId, role);
      }
      Roles = new ReadOnlyCollection<NativeStage02ManualRoleContract>(roleArray);
      RolesById = new ReadOnlyDictionary<string, NativeStage02ManualRoleContract>(
        byId);
    }

    internal static NativeStage02ManualRoleCatalog Current => LazyCurrent.Value;

    internal IReadOnlyList<NativeStage02ManualRoleContract> Roles { get; }
    internal IReadOnlyDictionary<string, NativeStage02ManualRoleContract>
      RolesById { get; }

    internal IReadOnlyList<NativeStage02ManualRoleContract> AvailableRoles(
      string modelFileType,
      IReadOnlyDictionary<string, bool> conditions)
    {
      string model = (modelFileType ?? string.Empty).Trim();
      return Roles.Where(role =>
      {
        bool modelAllowed = (role.ModelFileTypes ?? Array.Empty<string>())
          .Any(value => string.Equals(
            (value ?? string.Empty).Trim(),
            model,
            StringComparison.Ordinal));
        if (!modelAllowed) return false;
        string conditionId = (role.ConditionId ?? string.Empty).Trim();
        if (conditionId.Length == 0) return true;
        bool active;
        return conditions != null
          && conditions.TryGetValue(conditionId, out active)
          && active;
      }).ToArray();
    }

    private static NativeStage02ManualRoleCatalog LoadCurrent()
    {
      RulePackageEnvelope envelope = RulePackageIdentityReader
        .ReadEmbeddedEnvelope();
      var serializer = new JavaScriptSerializer
      {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 512
      };
      RulePackageDto dto;
      try
      {
        dto = serializer.Deserialize<RulePackageDto>(envelope.PayloadJson);
      }
      catch (Exception exception) when (
        exception is ArgumentException
        || exception is InvalidOperationException)
      {
        throw new InvalidDataException(
          "HBR 规则包无法投影为 Stage02 manual role 目录。",
          exception);
      }
      if (dto == null)
        throw new InvalidDataException("HBR 规则包为空。" );

      NativeStage02RuleCatalog stage02Catalog = NativeStage02RuleCatalog.Current;
      var roles = new List<NativeStage02ManualRoleContract>();
      foreach (CarrierRoleDto value in dto.carrierRoles
        ?? Array.Empty<CarrierRoleDto>())
      {
        if (value == null || value.manualCarriers == null
          || value.manualCarriers.Length == 0)
          continue;
        string roleId = Required(value.roleId, "carrierRoles.roleId");
        NativeCarrierRoleDefinition runtimeRole;
        if (!stage02Catalog.CarrierRolesById.TryGetValue(roleId, out runtimeRole))
          throw new InvalidDataException(
            "manual role 不存在于 Stage02 runtime role 目录：" + roleId);
        if (!string.Equals(
          runtimeRole.SelectionPolicy,
          "MANUAL_SEMANTIC_ASSIGNMENT",
          StringComparison.Ordinal))
          throw new InvalidDataException(
            "manual role selectionPolicy 无效：" + roleId);
        if (runtimeRole.RevitCategories.Count != 0
          || runtimeRole.AllowedElementKinds.Count != 0)
          throw new InvalidDataException(
            "manual role 不得同时进入自动识别载体白名单：" + roleId);

        IReadOnlyList<NativeStage02ManualCarrierDefinition> carriers =
          NativeStage02ManualCarrierPolicy.CanonicalizeCarriers(
            value.manualCarriers.Select(carrier =>
              new NativeStage02ManualCarrierDefinition
              {
                Category = carrier?.category ?? string.Empty,
                ElementKinds = carrier?.elementKinds ?? Array.Empty<string>()
              }));
        if (carriers.Count == 0)
          throw new InvalidDataException(
            "manual role 没有有效的 manualCarriers：" + roleId);

        roles.Add(new NativeStage02ManualRoleContract
        {
          RoleId = roleId,
          DisplayName = value.displayName ?? runtimeRole.DisplayName,
          ModelFileTypes = new ReadOnlyCollection<string>(
            (value.modelFileTypes ?? Array.Empty<string>())
              .Select(item => (item ?? string.Empty).Trim())
              .Where(item => item.Length > 0)
              .Distinct(StringComparer.Ordinal)
              .OrderBy(item => item, StringComparer.Ordinal)
              .ToArray()),
          ConditionId = (value.conditionId ?? string.Empty).Trim(),
          ManualCarriers = carriers,
          HasPropertyTemplate = stage02Catalog.PropertiesForRole(roleId).Count > 0,
          IfcOwnerStrategy = runtimeRole.IfcOwnerStrategy
        });
      }
      return new NativeStage02ManualRoleCatalog(roles);
    }

    private static string Required(string value, string path)
    {
      if (string.IsNullOrWhiteSpace(value))
        throw new InvalidDataException("HBR 字段为空：" + path);
      return value.Trim();
    }

    private sealed class RulePackageDto
    {
      public CarrierRoleDto[] carrierRoles { get; set; }
    }

    private sealed class CarrierRoleDto
    {
      public string roleId { get; set; }
      public string displayName { get; set; }
      public string[] modelFileTypes { get; set; }
      public string conditionId { get; set; }
      public ManualCarrierDto[] manualCarriers { get; set; }
    }

    private sealed class ManualCarrierDto
    {
      public string category { get; set; }
      public string[] elementKinds { get; set; }
    }
  }
}
