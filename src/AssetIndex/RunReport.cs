namespace AssetIndex;

internal sealed record ExtractionIssue(string Stage, string Path, string Message);

internal sealed record RunReport(
    string Status,
    int RegisteredAssets,
    int Candidates,
    int Loaded,
    int AssetIds,
    int EnglishNames,
    int Descriptions,
    int Images,
    IReadOnlyList<ExtractionIssue> Issues);
