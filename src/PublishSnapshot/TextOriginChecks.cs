using System.Text.Json;
using static PublishSnapshot.Preview;
using static PublishSnapshot.ResourceEvidence;

namespace PublishSnapshot;

internal sealed record TextOriginReference(string Namespace, string Key, string Source, bool CultureInvariant)
{
    public static TextOriginReference Read(JsonElement value)
    {
        Fields(value, "namespace", "key", "source", "cultureInvariant");
        return new(String(value, "namespace", allowEmpty: true), String(value, "key", allowEmpty: true),
            String(value, "source", allowEmpty: true), value.GetProperty("cultureInvariant").GetBoolean());
    }
}

internal sealed record TextOriginCandidate(string Source, string Class, string Field, string DefinedAt,
    TextOriginReference Reference);

internal static class TextOriginChecks
{
    // ResourceEvidence validates the stream's structure first. This check proves the
    // candidate's FText origin; asset association and presentation roles are separate.
    public static void Validate(JsonElement assets, SnapshotFile objects)
    {
        var candidates = assets.EnumerateArray()
            .SelectMany(asset => asset.GetProperty("presentation").GetProperty("candidates").EnumerateArray())
            .Select(candidate => new TextOriginCandidate(ObjectPath(candidate, "sourcePath"), String(candidate, "sourceClass"),
                String(candidate, "field"), ObjectPath(candidate, "definedAt"),
                TextOriginReference.Read(candidate.GetProperty("reference"))))
            .Distinct().ToArray();
        if (candidates.Length == 0) return;
        var evidence = TextOriginEvidence.Read(objects, candidates);
        foreach (var candidate in candidates)
        {
            Require(evidence.SourceClass(candidate.Source).Equals(candidate.Class, StringComparison.OrdinalIgnoreCase),
                $"Text candidate source class differs from object evidence: {candidate.Source}.");
            var owner = evidence.FirstOwner(candidate.Source, candidate.Field);
            Require(owner.Equals(candidate.DefinedAt, StringComparison.OrdinalIgnoreCase),
                $"Text candidate definedAt differs from the first property owner: {candidate.Source}.{candidate.Field}.");
            Require(evidence.Reference(owner, candidate.Field) == candidate.Reference,
                $"Text candidate differs from its FText evidence: {candidate.Source}.{candidate.Field}.");
        }
    }
}
