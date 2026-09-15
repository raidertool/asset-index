using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex;

internal sealed record PresentationName(long AssetId, string Role, string ContainerType,
    string FramePath, int ContainerIndex, string SlotPath, string? ContainerPath, UObject Metadata);

internal static class Presentation
{
    public static IReadOnlyList<PresentationName> Read(IReadOnlyList<UObject> objects,
        TypeMappings mappings, ICollection<ExtractionIssue> issues)
    {
        var names = new List<PresentationName>();
        var metadata = new Dictionary<string, List<UObject>>(StringComparer.OrdinalIgnoreCase);
        var instances = objects.Where(source => !source.Flags.HasFlag(EObjectFlags.RF_ClassDefaultObject)).ToArray();
        var frames = new List<UObject>();
        foreach (var source in instances)
        {
            try
            {
                var schema = ClassSchema.Read(source, mappings);
                if (schema.IsA("LoadoutFrameItemDataAsset")) frames.Add(source);
                if (schema.IsA("UIInventoryContainerMetaDataItem") && schema.HasProperty("ContainerType", "EnumProperty", "ByteProperty") &&
                    Properties.TryGet<FName>(source, "ContainerType", out var type))
                {
                    if (!metadata.TryGetValue(type.Text, out var entries)) metadata[type.Text] = entries = [];
                    entries.Add(source);
                }
            }
            catch (Exception error) { issues.Add(new("presentation", source.GetPathName(), error.Message)); }
        }
        foreach (var frame in frames)
        {
            try
            {
                if (!ClassSchema.Read(frame, mappings).HasProperty("Containers", "ArrayProperty") ||
                    !Properties.TryGet<FStructFallback[]>(frame, "Containers", out var containers)) continue;
                for (var index = 0; index < containers.Length; index++)
                    ReadContainer(frame, index, containers[index], metadata, mappings, names, issues);
            }
            catch (Exception error) { issues.Add(new("presentation", frame.GetPathName(), error.Message)); }
        }
        return names.Distinct().OrderBy(name => name.AssetId).ThenBy(name => name.FramePath, StringComparer.Ordinal)
            .ThenBy(name => name.ContainerIndex).ThenBy(name => name.Metadata.GetPathName(), StringComparer.Ordinal).ToArray();
    }

    private static void ReadContainer(UObject frame, int index, FStructFallback entry,
        IReadOnlyDictionary<string, List<UObject>> metadata, TypeMappings mappings,
        List<PresentationName> names, ICollection<ExtractionIssue> issues)
    {
        var path = $"{frame.GetPathName()}.Containers[{index}]";
        try
        {
            var type = Properties.Get<FName>(entry, "Type", path).Text;
            if (!metadata.TryGetValue(type, out var labels))
                throw new InvalidDataException($"No container presentation metadata for {type}.");
            var slot = Properties.Get<FPackageIndex>(entry, "ContainerSlotDataAsset", path).Load()
                ?? throw new InvalidDataException("ContainerSlotDataAsset could not be loaded.");
            RequireType(slot, "InventoryContainerSlotDataAsset", mappings);
            var slotId = Assets.DefinitionId(slot, mappings, out _)
                ?? throw new InvalidDataException("Container slot identity could not be resolved.");
            foreach (var label in labels)
                names.Add(new(slotId, "container-slot", type, frame.GetPathName(), index, slot.GetPathName(), null, label));

            var container = Properties.Reference(slot, "DefaultContainer");
            if (container is null) return;
            RequireType(container, "InventoryContainerItemDataAsset", mappings);
            var containerId = Assets.DefinitionId(container, mappings, out _)
                ?? throw new InvalidDataException("Default container identity could not be resolved.");
            foreach (var label in labels)
                names.Add(new(containerId, "default-container", type, frame.GetPathName(), index,
                    slot.GetPathName(), container.GetPathName(), label));
        }
        catch (Exception error) { issues.Add(new("presentation", path, error.Message)); }
    }

    private static void RequireType(UObject source, string expected, TypeMappings mappings)
    {
        if (!ClassSchema.Read(source, mappings).IsA(expected))
            throw new InvalidDataException($"Expected {expected}, found {source.ExportType} at {source.GetPathName()}.");
    }
}
