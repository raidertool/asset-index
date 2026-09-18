using System.Text.Json;

namespace PublishSnapshot;

internal static class SemanticChecks
{
    public static void Validate(JsonElement assets, IReadOnlyDictionary<string, SnapshotFile> files, JsonElement coverage,
        string? mappingPath = null)
    {
        mappingPath ??= Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap");
        var mappings = IdentitySchemas.LoadMappings(mappingPath,
            Preview.String(coverage.GetProperty("discovery"), "mappingSha256"));
        var schemas = new IdentitySchemas(mappings);
        JsonLines.Read(files["discovery/exports.jsonl.gz"], schemas.ObserveExport);
        var fields = IdentityContext.RootFields.Concat(PresentationRelationChecks.RootFields)
            .Concat(ImageOriginChecks.RootFields).Concat(TextRoleChecks.RootFields);
        var evidence = DecodedEvidence.Read(files["discovery/objects.jsonl.gz"], fields, schemas.ObserveObject);
        var identities = new IdentityContext(evidence, schemas);
        identities.Validate(assets);
        new PresentationRelationChecks(evidence, identities).Validate(assets);
        TextRoleChecks.Validate(assets, identities);
        ImageOriginChecks.Validate(assets, identities);
        using var resources = Preview.ReadJson(files, "resources.json");
        ImageOriginChecks.ValidateResources(resources.RootElement, identities);
    }
}
