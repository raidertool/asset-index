using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

internal sealed class CrawlerExport(string name, string? type = "DataAsset")
{
    public string Name { get; } = name;
    public string? Type { get; } = type;
    public int? OuterIndex { get; init; }
    public Action<UObject>? OnLoad { get; init; }

    public static void Link(UObject source, string target) => source.Properties.Add(new FPropertyTag
    {
        Name = "Reference" + source.Properties.Count,
        PropertyType = "SoftObjectProperty",
        Tag = new SoftObjectProperty(new FSoftObjectPath(target, ""))
    });
}

internal sealed class CrawlerPackage : AbstractUePackage
{
    private readonly Node[] metadata;
    public string PhysicalPath { get; }
    public List<int> BodyReads { get; } = [];
    public int? MissingMetadata { get; set; }

    public CrawlerPackage(string path, string name, params CrawlerExport[] exports) : base(name, null)
    {
        PhysicalPath = path;
        metadata = exports.Select((spec, index) => new Node(this, index, spec.Name)).ToArray();
        for (var index = 0; index < metadata.Length; index++)
        {
            metadata[index].Parent = exports[index].OuterIndex is { } outer ? metadata[outer] : new ResolvedPackageObject(this);
            metadata[index].Type = exports[index].Type is { } type ? Native(type) : null;
        }
        ExportsLazy = exports.Select((spec, index) => new Lazy<UObject>(() =>
        {
            BodyReads.Add(index);
            var value = new UObject { Name = spec.Name, Class = metadata[index].Type, Outer = metadata[index].Parent };
            spec.OnLoad?.Invoke(value);
            return value;
        })).ToArray();
    }

    private static ResolvedObject Native(string type)
    {
        var package = new CrawlerPackage("", "/Script/Fixture");
        return new Node(package, -1, type) { Parent = new ResolvedPackageObject(package) };
    }

    public override FPackageFileSummary Summary { get; } = new();
    public override FNameEntrySerialized[] NameMap => [];
    public override int ImportMapLength => 0;
    public override int ExportMapLength => metadata.Length;
    public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) =>
        throw new InvalidOperationException("Crawler must resolve full paths, not short names.");
    public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) =>
        index is { IsExport: true } && index.Index - 1 != MissingMetadata ? metadata[index.Index - 1] : null;

    private sealed class Node(IPackage package, int index, string name) : ResolvedObject(package, index)
    {
        public ResolvedObject? Parent { get; set; }
        public ResolvedObject? Type { get; set; }
        public override FName Name => name;
        public override ResolvedObject? Outer => Parent;
        public override ResolvedObject? Class => Type;
    }
}

internal sealed class CrawlerProvider : TheiaFileProvider
{
    private readonly Dictionary<string, CrawlerPackage> packages;
    public Action<CrawlerPackage>? BeforeLoad { get; set; }
    public List<string> PackageReads { get; } = [];

    public CrawlerProvider(params CrawlerPackage[] inputs) : base(Path.GetTempPath(), SearchOption.TopDirectoryOnly,
        new VersionContainer(EGame.GAME_ArcRaiders), StringComparer.OrdinalIgnoreCase)
    {
        packages = inputs.ToDictionary(package => package.PhysicalPath, StringComparer.OrdinalIgnoreCase);
        MappingsContainer = new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"));
        Files.AddFiles(packages.Keys.ToDictionary(path => path, path => (GameFile)new InputFile(path), StringComparer.OrdinalIgnoreCase));
    }

    public override IPackage LoadPackage(GameFile file)
    {
        PackageReads.Add(file.Path);
        var package = packages[file.Path];
        BeforeLoad?.Invoke(package);
        return package;
    }

    private sealed class InputFile(string path) : GameFile(path, 0)
    {
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
    }
}
