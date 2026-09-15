using AssetIndex.Discovery;
using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Exceptions;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace AssetIndex.Tests;

public sealed class ArcMappingsTests
{
    private const string Original = "AISensingStatusTransition";
    private const string Alias = "AISensingStatusTransitionStruct";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothStructRoutesDecodeNestedValues(bool useMappedReference)
    {
        using var provider = CreateProvider();
        var mappings = provider.MappingsForGame!;
        var type = useMappedReference
            ? mappings.Types["AITemplateDataAsset"].Properties[24].MappingType.StructType
            : Original;
        if (useMappedReference) Assert.Equal(Alias, type);
        // Filter byte, one-element array; nested sense byte and float threshold 3.25.
        byte[] bytes = [0x00, 0x05, 1, 1, 0, 0, 0, 0x00, 0x05, 2, 0x00, 0x00, 0x50, 0x40];
        using var archive = new FAssetArchive(new FByteArchive("arc-transition-struct", bytes, provider.Versions),
            new FixturePackage(provider));

        var decoded = Assert.IsType<FStructFallback>(new FScriptStruct(archive, type, null, ReadType.NORMAL).StructType);

        Assert.Equal("EAISensingStatusFilter::" + mappings.Enums["EAISensingStatusFilter"][1],
            decoded.GetOrDefault<FName>("Filter").Text);
        var values = Assert.IsType<UScriptArray>(decoded.GetOrDefault<UScriptArray>("Values"));
        var item = Assert.IsType<StructProperty>(Assert.Single(values.Properties));
        var entry = Assert.IsType<FStructFallback>(Assert.IsType<FScriptStruct>(item.Value).StructType);
        Assert.Equal("EStimulusSense::" + mappings.Enums["EStimulusSense"][2], entry.GetOrDefault<FName>("Sense").Text);
        Assert.Equal(3.25f, entry.GetOrDefault<float>("ValueThreshold"));
        Assert.Equal(archive.Length, archive.Position);
    }

    [Fact]
    public void UnverifiedNativeClassHasBlockingSchemaDiagnostic()
    {
        using var provider = CreateProvider();
        var mappings = provider.MappingsForGame!;
        var schema = ClassSchema.Read(new ResolvedLoadedObject(new UScriptClass(Original)), mappings);

        Assert.Contains("Native class ancestry has no mapping for " + Original, schema.Error);
        var error = Assert.Throws<InvalidDataException>(() => schema.IsA("Object"));
        Assert.Contains(Original, error.Message);
        Assert.True(Registry.SelectClass(mappings, Original));
    }

    [Fact]
    public void PopulatedNativeClassCannotDecodeUsingTheStructAlias()
    {
        using var provider = CreateProvider();
        // A populated unversioned slot requires an actual native class schema.
        using var archive = new FAssetArchive(new FByteArchive("arc-transition-slot", [0x02, 0x03, 2], provider.Versions),
            new FixturePackage(provider));

        var error = Assert.Throws<ParserException>(() => new FStructFallback(archive, new UScriptClass(Original)));

        Assert.Contains("Missing prop mappings for type " + Original, error.Message);
    }

    [Fact]
    public void MappingPreservesTheSuppliedStructLayoutUnderItsExplicitAlias()
    {
        using var provider = CreateProvider();
        var mappings = provider.MappingsForGame!;
        var condition = mappings.Types[Alias];

        Assert.False(mappings.Types.ContainsKey(Original));
        Assert.Null(condition.SuperType);
        Assert.Equal(2, condition.PropertyCount);
        Assert.Equal(["Filter", "Values"], condition.Properties.OrderBy(pair => pair.Key).Select(pair => pair.Value.Name));
        Assert.Equal(Alias, mappings.Types["AITemplateDataAsset"].Properties[24].MappingType.StructType);
    }

    private static TheiaFileProvider CreateProvider()
    {
        var provider = new TheiaFileProvider(AppContext.BaseDirectory, SearchOption.TopDirectoryOnly,
            new VersionContainer(EGame.GAME_ArcRaiders));
        provider.MappingsContainer = new FileUsmapTypeMappingsProvider(
            Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"));
        return provider;
    }

    private sealed class FixturePackage(TheiaFileProvider provider) : AbstractUePackage("Fixture", provider)
    {
        public override FPackageFileSummary Summary { get; } = new() { PackageFlags = EPackageFlags.PKG_UnversionedProperties };
        public override FNameEntrySerialized[] NameMap => [];
        public override int ImportMapLength => 0;
        public override int ExportMapLength => 0;
        public override int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => -1;
        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => null;
    }
}
