using System.Text.Json.Serialization;

namespace AssetIndex;

internal sealed record ObjectReference(string Name, string Class, string Path);
internal sealed record Translation(string Locale, string DisplayName, string Description);
internal sealed record ContainerPresentation(string Role, string ContainerType, string FramePath,
    int ContainerIndex, string SlotPath, string? ContainerPath, string MetadataPath);
internal sealed record VisualSlotMemberPresentation(string ItemPath, string MetadataPath);
internal sealed record InventoryRootPresentation(string Role, string ContainerType, string RootPath,
    string RootField, string SlotPath, string? ContainerPath, string MetadataPath);
internal sealed record VisualSlotPresentation(string SlotPath, string TypeTag, string MetadataPath,
    IReadOnlyList<VisualSlotMemberPresentation> Members);
internal sealed record AssetPresentation(TextReference? Name, TextReference? Description,
    IReadOnlyList<TextCandidate> Candidates, IReadOnlyList<ContainerPresentation> Containers)
{
    public IReadOnlyList<VisualSlotPresentation> VisualSlots { get; init; } = [];
    public IReadOnlyList<InventoryRootPresentation> InventoryRoots { get; init; } = [];
}
internal sealed record AssetRecord(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id,
    IReadOnlyList<ObjectReference> Definitions, IReadOnlyList<ObjectReference> Metadata,
    IReadOnlyList<Translation> Text, IReadOnlyList<AssetImage> Images)
{
    public AssetPresentation Presentation { get; init; } = new(null, null, [], []);
}

internal sealed record TextReference(string Namespace, string Key, string Source, bool CultureInvariant = false);
internal sealed record TextCandidate(string Role, string SourceKind, string SourcePath, string SourceClass,
    string Field, string DefinedAt, TextReference Reference);
internal sealed record AssetImage(string Field, string Source, string? Resource, string Status,
    string? File = null, int? Width = null, int? Height = null);
