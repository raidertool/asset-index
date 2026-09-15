using CUE4Parse.GameTypes.Theia.FileProvider;
using CUE4Parse.MappingsProvider;
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

    [Fact]
    public void AliasDecodesTheRealArcStructWithNestedValues()
    {
        using var provider = new TheiaFileProvider(AppContext.BaseDirectory, SearchOption.TopDirectoryOnly,
            new VersionContainer(EGame.GAME_ArcRaiders));
        provider.MappingsContainer = new FileUsmapTypeMappingsProvider(
            Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"));
        var mappings = provider.MappingsForGame!;
        // Two populated properties: filter byte, one-element array; nested sense byte and float 3.25.
        byte[] bytes = [0x00, 0x05, 1, 1, 0, 0, 0, 0x00, 0x05, 2, 0x00, 0x00, 0x50, 0x40];
        using var archive = new FAssetArchive(new FByteArchive("arc-transition", bytes, provider.Versions),
            new FixturePackage(provider));
        var missing = Assert.Throws<ParserException>(() => new FScriptStruct(archive, Original, null, ReadType.NORMAL));
        Assert.Contains("Missing prop mappings for type " + Alias, missing.Message);

        GameFiles.AddArcMappingsAlias(mappings);
        archive.Position = 0;
        var decoded = Assert.IsType<FStructFallback>(new FScriptStruct(archive, Original, null, ReadType.NORMAL).StructType);

        Assert.Same(mappings.Types[Original], mappings.Types[Alias]);
        Assert.Equal(Original, mappings.Types[Original].Name);
        Assert.Equal("EAISensingStatusFilter::" + mappings.Enums["EAISensingStatusFilter"][1],
            decoded.GetOrDefault<FName>("Filter").Text);
        var values = Assert.IsType<UScriptArray>(decoded.GetOrDefault<UScriptArray>("Values"));
        var item = Assert.IsType<StructProperty>(Assert.Single(values.Properties));
        var entry = Assert.IsType<FStructFallback>(Assert.IsType<FScriptStruct>(item.Value).StructType);
        Assert.Equal("EStimulusSense::" + mappings.Enums["EStimulusSense"][2], entry.GetOrDefault<FName>("Sense").Text);
        Assert.Equal(3.25f, entry.GetOrDefault<float>("ValueThreshold"));
        Assert.Equal(archive.Length, archive.Position);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("count")]
    [InlineData("filter-name")]
    [InlineData("filter-width")]
    [InlineData("filter-enum")]
    [InlineData("fixed-array")]
    [InlineData("value-struct")]
    [InlineData("slot")]
    public void IncompatibleLayoutsAreRejectedWithoutChangingMappings(string difference)
    {
        var mappings = LoadMappings();
        var source = mappings.Types[Original];
        var count = mappings.Types.Count;
        switch (difference)
        {
            case "parent": source.SuperType = "Object"; break;
            case "count": source.PropertyCount = 3; break;
            case "filter-name": source.Properties[0].Name = "Other"; break;
            case "filter-width": source.Properties[0].MappingType.InnerType!.Type = "IntProperty"; break;
            case "filter-enum": source.Properties[0].MappingType.EnumName = "OtherEnum"; break;
            case "fixed-array": source.Properties[1].ArraySize = 2; break;
            case "value-struct": source.Properties[1].MappingType.InnerType!.StructType = "OtherValue"; break;
            case "slot": source.Properties[2] = source.Properties[1]; source.Properties.Remove(1); break;
            default: throw new ArgumentOutOfRangeException(nameof(difference));
        }

        var error = Assert.Throws<InvalidDataException>(() => GameFiles.AddArcMappingsAlias(mappings));

        Assert.Contains("differs from the verified Filter/Values layout", error.Message);
        Assert.False(mappings.Types.ContainsKey(Alias));
        Assert.Equal(count, mappings.Types.Count);
    }

    [Fact]
    public void AnExistingAliasIsPreservedEvenWhenTheOriginalNamesAClass()
    {
        var mappings = LoadMappings();
        var existing = new Struct(mappings, Alias, null, [], 0);
        mappings.Types.Add(Alias, existing);
        mappings.Types[Original].SuperType = "Object";

        GameFiles.AddArcMappingsAlias(mappings);

        Assert.Same(existing, mappings.Types[Alias]);
    }

    [Fact]
    public void MissingOriginalDoesNotCreateAnInventedSchema()
    {
        var mappings = new TypeMappings();

        GameFiles.AddArcMappingsAlias(mappings);

        Assert.Empty(mappings.Types);
    }

    private static TypeMappings LoadMappings() => new FileUsmapTypeMappingsProvider(
        Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap")).MappingsForGame!;

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
