using Hifc.Web.Client;
using Hifc.Web.Contracts;

internal static class PackageCases
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var assembly in new[] { typeof(WebApiClient).Assembly, typeof(RequestContext).Assembly })
        {
            var name = assembly.GetName().Name;
            test(name + " has no private rule-engine dependency", () => {
                if (assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("Hifc.Reference", StringComparison.Ordinal) || a.Name.StartsWith("Hifc.Web.RuleEngine", StringComparison.Ordinal) || a.Name.StartsWith("Hifc.Web.Server", StringComparison.Ordinal)))
                    throw new Exception("Private server assembly entered the client dependency graph");
            });
            test(name + " ships no frozen rules or private implementation", () => {
                if (assembly.GetManifestResourceNames().Any(n => n.Contains("Contract.json") || n.Contains("SuccessCase") || n.Contains("RecognitionEvidence"))
                    || assembly.GetTypes().Any(t => t.Namespace == "Hifc.Reference" || t.Name == "CaseWorkflow" || t.Name == "ReferenceCatalog"))
                    throw new Exception("Frozen rules or their implementation entered a client assembly");
            });
        }
    }
}
