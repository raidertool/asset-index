using CUE4Parse.Encryption.Aes;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;

namespace AssetIndex;

internal static class GameFiles
{
    // Public game-content key: https://github.com/ARC-Data-Raiders/DataRaiders/blob/main/aes.txt
    private const string DefaultKey = "0x047A8AC14396604CE1BAB46366C0A7FDBE40F66264D4625E2E6D11FF17272D7F";

    public static PackageProvider Open(Options options)
    {
        // Package imports are not evidence that a particular material uses a texture.
        var provider = new PackageProvider(options.GameDirectory) { SkipReferencedTextures = true };
        try
        {
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(options.Usmap);
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
}
