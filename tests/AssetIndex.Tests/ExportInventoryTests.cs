using System.Reflection;
using System.Runtime.CompilerServices;
using AssetIndex.Discovery;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class ExportInventoryTests
{
    [Fact]
    public void ReadsEveryHeaderAndRuntimeAncestryWithoutLoadingAnInstance()
    {
        var package = new MetadataPackage("/Game/Map");
        var world = package.Add("Map", Native("World"));
        var actorClass = package.Add("BP_Spawner_C", Native("BlueprintGeneratedClass"));
        actorClass.ParentClass = Native("Actor");
        package.Add("Spawner", actorClass, world);
        package.Add("Definition", Native("dataasset"));
        package.Add("Metadata", Native("UIMetaDataItem"));

        var headers = ExportInventory.Read(package, "PioneerGame/Content/Map.umap", Mappings());

        Assert.Equal(package.ExportMapLength, headers.Count);
        Assert.Equal(Enumerable.Range(0, headers.Count), headers.Select(header => header.Index));
        Assert.All(headers, header =>
        {
            Assert.Equal("PioneerGame/Content/Map.umap", header.Package);
            Assert.True(header.AncestryComplete);
            Assert.Null(header.Error);
        });
        Assert.Equal(["World", "Object"], headers[0].Ancestry);
        Assert.Equal("/Game/Map.Map:Spawner", headers[2].Path);
        Assert.Equal("/Game/Map.BP_Spawner_C", headers[2].ClassPath);
        Assert.Equal(["BP_Spawner_C", "Actor", "Object"], headers[2].Ancestry);
        Assert.Equal(["dataasset", "Object"], headers[3].Ancestry);
        Assert.Equal(["UIMetaDataItem", "Object"], headers[4].Ancestry);
    }

    [Fact]
    public void EqualComponentNamesHaveDistinctFullOuterPaths()
    {
        var package = new MetadataPackage("/Game/Map");
        var world = package.Add("Map", Native("World"));
        var left = package.Add("Left", Native("Actor"), world);
        var right = package.Add("Right", Native("Actor"), world);
        package.Add("Component", Native("Object"), left);
        package.Add("Component", Native("Object"), right);

        var headers = ExportInventory.Read(package, "Map.umap", Mappings());

        Assert.Equal("/Game/Map.Map:Left.Component", headers[3].Path);
        Assert.Equal("/Game/Map.Map:Right.Component", headers[4].Path);
        Assert.All(headers, header => Assert.Null(header.Error));
    }

    [Fact]
    public void CaseInsensitivePathCollisionsAnnotateEveryIndex()
    {
        var package = new MetadataPackage("/Game/Map");
        package.Add("Same", Native("Object"));
        package.Add("same", Native("Object"));

        var headers = ExportInventory.Read(package, "Map.umap", Mappings());

        Assert.Equal(2, headers.Count);
        Assert.All(headers, header => Assert.Contains("Ambiguous export path", header.Error));
        Assert.All(headers, header => Assert.Contains("indices 0, 1", header.Error));
    }

    [Fact]
    public void UnknownClassesAndUnmappedRootsRemainIncomplete()
    {
        var package = new MetadataPackage("/Game/Map");
        package.Add("Unknown", Native("UnknownClass"));
        package.Add("WrongRoot", Native("DetachedClass"));
        var mappings = Mappings();
        mappings.Types.Add("DetachedClass", new(mappings, "DetachedClass", null, [], 0));

        var headers = ExportInventory.Read(package, "Map.umap", mappings);

        Assert.Equal(["UnknownClass"], headers[0].Ancestry);
        Assert.Contains("no mapping or superclass", headers[0].Error);
        Assert.Contains("ends before Object", headers[1].Error);
        Assert.All(headers, header => Assert.False(header.AncestryComplete));
    }

    [Fact]
    public void MissingMetadataRetainsItsIndexAndOtherHeaders()
    {
        var package = new MetadataPackage("/Game/Map");
        package.Add("Missing", null);
        package.Add("Good", Native("DataAsset"));
        package.MissingIndex = 0;

        var headers = ExportInventory.Read(package, "Map.umap", Mappings());

        Assert.Equal(2, headers.Count);
        Assert.Equal(0, headers[0].Index);
        Assert.Null(headers[0].Path);
        Assert.Contains("no resolved metadata", headers[0].Error);
        Assert.Null(headers[1].Error);
    }

    [Fact]
    public void MissingClassesAndCyclicMetadataAreExplicitErrors()
    {
        var package = new MetadataPackage("/Game/Map");
        package.Add("NoClass", null);
        var outerCycle = package.Add("OuterCycle", Native("Object"));
        outerCycle.Parent = outerCycle;
        var classCycle = package.Add("ClassCycle", Native("BlueprintGeneratedClass"));
        classCycle.ParentClass = classCycle;
        package.Add("Instance", classCycle);
        var mappings = Mappings();
        mappings.Types.Add("MappedCycle", new(mappings, "MappedCycle", "MappedCycle", [], 0));
        package.Add("Mapped", Native("MappedCycle"));

        var headers = ExportInventory.Read(package, "Map.umap", mappings);

        Assert.Contains("class metadata is missing", headers[0].Error);
        Assert.Contains("outer chain repeats", headers[1].Error);
        Assert.Contains("Runtime class ancestry repeats", headers[3].Error);
        Assert.Contains("Mapped class ancestry repeats", headers[4].Error);
        Assert.False(headers[3].AncestryComplete);
        Assert.False(headers[4].AncestryComplete);
    }

    [Fact]
    public void RuntimeSuperclassTakesPrecedenceOverSameNamedMapping()
    {
        var package = new MetadataPackage("/Game/Map");
        var declaration = package.Add("RuntimeClass", Native("BlueprintGeneratedClass"));
        declaration.ParentClass = Native("Actor");
        package.Add("Instance", declaration);
        var mappings = Mappings();
        mappings.Types.Add("RuntimeClass", new(mappings, "RuntimeClass", "DataAsset", [], 0));

        var headers = ExportInventory.Read(package, "Map.umap", mappings);

        Assert.Equal(["RuntimeClass", "Actor", "Object"], headers[1].Ancestry);
        Assert.Null(headers[1].Error);
    }

    [Fact]
    public void MissingNativeOuterCannotProduceACompleteClassPath()
    {
        var package = new MetadataPackage("/Game/Map");
        var type = Native("DataAsset");
        type.Parent = null;
        package.Add("Definition", type);

        var header = Assert.Single(ExportInventory.Read(package, "Map.umap", Mappings()));

        Assert.Contains("no package root", header.Error);
        Assert.False(header.AncestryComplete);
    }

    [Fact]
    public void MappedContinuationCannotLoopIntoEarlierRuntimeAncestry()
    {
        var package = new MetadataPackage("/Game/Map");
        var declaration = package.Add("RuntimeClass", Native("BlueprintGeneratedClass"));
        declaration.ParentClass = Native("Actor");
        package.Add("Instance", declaration);
        var mappings = Mappings();
        mappings.Types["Actor"] = new(mappings, "Actor", "RuntimeClass", [], 0);
        mappings.Types.Add("RuntimeClass", new(mappings, "RuntimeClass", "Object", [], 0));

        var header = ExportInventory.Read(package, "Map.umap", mappings)[1];

        Assert.Contains("Mapped class ancestry repeats", header.Error);
        Assert.False(header.AncestryComplete);
    }

    [Fact]
    public void RealIoMetadataKeepsMissingSerializedLinksAndInvalidIndicesVisible()
    {
        var missingImport = 1UL << FPackageObjectIndex.TypeShift;
        var entries = new[]
        {
            Entry(0, type: 0), Entry(1, type: 0, super: 0), Entry(2, type: 0, super: missingImport),
            Entry(3, type: 1), Entry(4, type: 1, outer: missingImport), Entry(5, type: 2),
            Entry(6, type: 1, outer: 99)
        };
        var forced = 0;
        var package = IoMetadata(entries, ["Object", "World", "Actor", "Map", "BadOuter", "BadSuper", "BadIndex"], () => forced++);
        // Upstream resolved wrappers hide a missing outer as package root and a
        // missing superclass as null; inventory must inspect the serialized links.
        Assert.IsType<ResolvedPackageObject>(package.ResolvePackageIndex(new(package, 5))!.Outer);
        Assert.Null(package.ResolvePackageIndex(new(package, 3))!.Super);

        var headers = ExportInventory.Read(package, "Map.umap", Mappings());

        Assert.Equal(entries.Length, headers.Count);
        Assert.Null(headers[3].Error);
        Assert.Equal(["World", "Object"], headers[3].Ancestry);
        Assert.Contains("Serialized outer index", headers[4].Error);
        Assert.Contains("Serialized superclass index", headers[5].Error);
        Assert.Contains("outside its package header", headers[6].Error);
        Assert.Equal(0, forced);
    }

    private static IoPackage IoMetadata(FExportMapEntry[] entries, string[] names, Action payloadRead)
    {
        // Install parsed headers only to isolate CUE's public metadata API. This
        // fixture does not claim to exercise mounting or the container parser.
        var package = (IoPackage)RuntimeHelpers.GetUninitializedObject(typeof(IoPackage));
        package.Name = "/Game/Map";
        Set(typeof(IoPackage), package, "ExportMap", entries);
        Set(typeof(IoPackage), package, "ImportMap", Array.Empty<FPackageObjectIndex>());
        Set(typeof(IoPackage), package, "<NameMap>k__BackingField", names.Select(name => new FNameEntrySerialized(name)).ToArray());
        var globals = (IoGlobalData)RuntimeHelpers.GetUninitializedObject(typeof(IoGlobalData));
        Set(typeof(IoGlobalData), globals, "ScriptObjectEntriesMap", new Dictionary<FPackageObjectIndex, FScriptObjectEntry>());
        Set(typeof(IoPackage), package, "_globalData", globals);
        Set(typeof(AbstractUePackage), package, "<ExportsLazy>k__BackingField", entries.Select(_ => new Lazy<UObject>(() =>
        {
            payloadRead();
            throw new InvalidOperationException("Inventory must not read an instance payload.");
        })).ToArray());
        return package;
    }

    private static FExportMapEntry Entry(int name, ulong type, ulong? super = null, ulong? outer = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(0UL); writer.Write(0UL); writer.Write((uint)name); writer.Write(0U);
            writer.Write(outer ?? FPackageObjectIndex.Invalid); writer.Write(type);
            writer.Write(super ?? FPackageObjectIndex.Invalid); writer.Write(FPackageObjectIndex.Invalid);
            writer.Write(0UL); writer.Write(0U); writer.Write(0U);
        }
        using var archive = new FByteArchive("Synthetic export header", stream.ToArray(), new VersionContainer(EGame.GAME_ArcRaiders));
        return new FExportMapEntry(archive);
    }

    private static void Set(Type type, object target, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    private static TypeMappings Mappings()
    {
        var mappings = new TypeMappings(new(StringComparer.OrdinalIgnoreCase), []);
        foreach (var name in new[] { "Object", "World", "Actor", "DataAsset", "UIMetaDataItem", "BlueprintGeneratedClass" })
            mappings.Types.Add(name, new(mappings, name, name == "Object" ? null : "Object", [], 0));
        return mappings;
    }

    private static MetadataReference Native(string name)
    {
        var package = new MetadataPackage("/Script/Fixture");
        return new(package, -1, name) { Parent = new ResolvedPackageObject(package) };
    }

    private sealed class MetadataReference(IPackage package, int index, string name) : ResolvedObject(package, index)
    {
        public ResolvedObject? Parent { get; set; }
        public ResolvedObject? Type { get; init; }
        public ResolvedObject? ParentClass { get; set; }
        public override FName Name => name;
        public override ResolvedObject? Outer => Parent;
        public override ResolvedObject? Class => Type;
        public override ResolvedObject? Super => ParentClass;
        public override Lazy<UObject>? Object => throw new InvalidOperationException("Metadata must not load an export.");
    }

    private sealed class MetadataPackage(string name) : AbstractUePackage(name, null)
    {
        private readonly List<MetadataReference> exports = [];
        public int? MissingIndex { get; set; }
        public MetadataReference Add(string name, ResolvedObject? type, ResolvedObject? outer = null)
        {
            var result = new MetadataReference(this, exports.Count, name)
            {
                Type = type,
                Parent = outer ?? new ResolvedPackageObject(this)
            };
            exports.Add(result);
            return result;
        }
        public override FPackageFileSummary Summary => throw new NotSupportedException();
        public override FNameEntrySerialized[] NameMap => [];
        public override int ImportMapLength => 0;
        public override int ExportMapLength => exports.Count;
        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) =>
            throw new InvalidOperationException("Short-name lookup loses outer identity.");
        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) =>
            index is { IsExport: true } && index.Index - 1 != MissingIndex ? exports[index.Index - 1] : null;
    }
}
