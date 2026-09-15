namespace AssetIndex.Discovery;

internal sealed record ObjectEvidence(
    string Path,
    string Class,
    IReadOnlyList<ReferenceEvidence> References,
    IReadOnlyList<TextEvidence> Texts,
    IReadOnlyList<ValueEvidence> Values,
    IReadOnlyList<TableEntryEvidence> TableEntries,
    IReadOnlyList<EvidenceIssue> Issues);

internal sealed record ReferenceEvidence(
    string Pointer, string Kind, string Role, string? TargetPath, bool IsNull,
    string? Package, int? PackageIndex, int? ExportIndex, string? Error);

internal sealed record TextEvidence(
    string Pointer, uint Flags, string History, string? Namespace, string? Key,
    string? Source, string? TableId);

internal sealed record ValueEvidence(string Pointer, string Type, string Kind, string? Value);
internal sealed record TableEntryEvidence(string Pointer, string Namespace, string Key, string Source);
internal sealed record EvidenceIssue(string Pointer, string Type, string Message);
