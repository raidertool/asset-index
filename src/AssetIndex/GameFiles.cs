using CUE4Parse.Encryption.Aes;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;

namespace AssetIndex;

internal static class GameFiles
{
    // Public game-content key: https://github.com/ARC-Data-Raiders/DataRaiders/blob/main/aes.txt
    private const string DefaultKey = "0x047A8AC14396604CE1BAB46366C0A7FDBE40F66264D4625E2E6D11FF17272D7F";

    public static TheiaFileProvider Open(Options options)
    {
        var provider = new TheiaFileProvider(options.GameDirectory, SearchOption.AllDirectories,
            new VersionContainer(EGame.GAME_ArcRaiders), StringComparer.OrdinalIgnoreCase);
        try
        {
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(options.Usmap);
            AddArcMappingsAlias(provider.MappingsForGame!);
            provider.Initialize();
            provider.SubmitKey(new FGuid(), new FAesKey(Environment.GetEnvironmentVariable("ARC_AES_KEY") ?? DefaultKey));
            provider.Mount();
            provider.PostMount();
            var missing = provider.UnloadedVfs.Where(reader => reader.HasDirectoryIndex).ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException("Containers could not be mounted: " + string.Join(", ", missing.Select(reader => reader.Name)));
            if (provider.Files.Count == 0)
                throw new InvalidDataException("No game files were mounted.");
            provider.LoadVirtualPaths();
            provider.ChangeCulture("en");
            return provider;
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    internal static string ResolvePackagePath(TheiaFileProvider provider, string path)
    {
        // Keep normal mount precedence; use the IoStore identity for unmapped virtual roots.
        if (provider.TryGetGameFile(path, out var file)) return file.Path;
        return provider.FilesById.TryGetValue(FPackageId.FromName(path), out file) ? file.Path : path;
    }

    internal static void AddArcMappingsAlias(TypeMappings mappings)
    {
        const string original = "AISensingStatusTransition";
        const string alias = "AISensingStatusTransitionStruct";
        if (mappings.Types.ContainsKey(alias) || !mappings.Types.TryGetValue(original, out var source))
            return;

        // Pinned CUE4Parse renames this ARC struct to avoid a class-name collision.
        // This usmap uses the original name; accept only its verified wire layout.
        if (source.SuperType is not null || source.PropertyCount != 2 || source.Properties.Count != 2 ||
            !source.Properties.TryGetValue(0, out var filter) || filter is not
            {
                Name: "Filter", Index: 0, ArraySize: 1,
                MappingType: { Type: "EnumProperty", EnumName: "EAISensingStatusFilter", InnerType.Type: "ByteProperty" }
            } ||
            !source.Properties.TryGetValue(1, out var values) || values is not
            {
                Name: "Values", Index: 0, ArraySize: 1,
                MappingType: { Type: "ArrayProperty", InnerType: { Type: "StructProperty", StructType: "AISensingStatusValue" } }
            })
            throw new InvalidDataException($"Cannot alias {original}: the mapping differs from the verified Filter/Values layout.");

        mappings.Types.Add(alias, source);
    }
}
