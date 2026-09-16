using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class ClassSchemaTests
{
    private static readonly Lazy<TypeMappings> Mappings = new(() =>
        new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap")).MappingsForGame!);

    [Fact]
    public void RuntimePersistenceSubclassRetainsItsIdWithoutAUsmapEntry()
    {
        var source = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        RuntimeClassFixture.Derive(source);
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([source], Mappings.Value, issues));

        Assert.Equal(42, asset.Id);
        Assert.Equal(source.GetPathName(), Assert.Single(asset.Definitions).Reference.Path);
        Assert.Empty(issues);
        Assert.False(Mappings.Value.Types.ContainsKey(source.ExportType));
    }

    [Fact]
    public void RuntimeCurrencySubclassKeepsItsMetadataAndNativeTextRoles()
    {
        var source = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var ui = Object("UI", "UICurrencyMetaDataItem", ("PersistenceDataAsset", Reference(source)),
            ("LongName", new TextProperty(new FText("Experience points"))));
        RuntimeClassFixture.Derive(ui);
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([source, ui], Mappings.Value, issues));
        var text = Text.Read(asset, issues);

        Assert.Equal(ui.GetPathName(), Assert.Single(asset.Metadata).Reference.Path);
        Assert.Equal("Experience points", text.Name?.Source);
        Assert.Empty(issues);
    }

    [Fact]
    public void RuntimeDeclaredPersistenceLinkUsesItsTypedDeclaration()
    {
        var identity = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var source = Object("Definition", "DataAsset", ("persistenceDATAasset", Reference(identity)));
        var declaration = RuntimeClassFixture.Derive(source);
        declaration.ChildProperties = [new FObjectProperty { Name = "PersistenceDataAsset", ArrayDim = 1 }];
        var issues = new List<ExtractionIssue>();

        var asset = Assert.Single(Assets.Collect([source], Mappings.Value, issues));

        Assert.Equal(42, asset.Id);
        Assert.Contains(asset.Definitions, value => value.Reference.Path == source.GetPathName());
        Assert.Contains(asset.Definitions, value => value.Reference.Path == identity.GetPathName());
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("PersistenceDataAsset")]
    [InlineData("UICurrencyMetaDataItem")]
    [InlineData("MappedRuntimeClass")]
    public void RuntimeShortNameCannotImpersonateANativePolicyClass(string name)
    {
        var source = Object("Unrelated", "Actor", ("AssetId", new Int64Property(42)),
            ("bOverrideAssetId", new BoolProperty(true)), ("OverrideAssetId", new Int64Property(42)),
            ("LongName", new TextProperty(new FText("Not currency"))));
        RuntimeClassFixture.Derive(source, name);
        var mappings = new TypeMappings(new(Mappings.Value.Types, StringComparer.OrdinalIgnoreCase), Mappings.Value.Enums);
        mappings.Types["MappedRuntimeClass"] = new(mappings, "MappedRuntimeClass", "PersistenceDataAsset", [], 0);
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([source], mappings, issues));
        Assert.Empty(TextRoles.For(source, mappings));
        Assert.Empty(issues);
        Assert.Equal([name, "Actor", "Object"], ClassSchema.Read(source, mappings).Ancestry);
    }

    [Fact]
    public void MissingRuntimeParentDoesNotFallBackToASameNamedMapping()
    {
        var source = Object("Broken", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var declaration = RuntimeClassFixture.Derive(source, "PersistenceDataAsset");
        declaration.Super = null;
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([source], Mappings.Value, issues));

        Assert.Contains(issues, issue => issue.Stage == "asset" && issue.Message.Contains("Runtime superclass metadata is missing"));
    }

    [Fact]
    public void RuntimeCyclesAndMissingNativeMappingsAreExplicitFailures()
    {
        var source = Object("Cycle", "DataAsset");
        var declaration = RuntimeClassFixture.Derive(source);
        declaration.Super = source.Class;
        var cyclic = ClassSchema.Read(source, Mappings.Value);
        var unknown = ClassSchema.Read(Object("Unknown", "UnmappedNativeClass"), Mappings.Value);

        Assert.Contains("Runtime class ancestry repeats", cyclic.Error);
        Assert.Throws<InvalidDataException>(() => cyclic.IsA("DataAsset"));
        Assert.Contains("has no mapping", unknown.Error);
        Assert.Throws<InvalidDataException>(() => unknown.HasProperty("PersistenceDataAsset"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateDeclarationsCannotChooseAFieldByName(bool sameOwner)
    {
        var source = Object("Definition", sameOwner ? "DataAsset" : "QuestDefinition");
        var declaration = RuntimeClassFixture.Derive(source);
        declaration.ChildProperties = sameOwner
            ? [new FObjectProperty { Name = "PersistenceDataAsset", ArrayDim = 1 }, new FObjectProperty { Name = "persistencedataasset", ArrayDim = 1 }]
            : [new FObjectProperty { Name = "PersistenceDataAsset", ArrayDim = 1 }];

        var schema = ClassSchema.Read(source, Mappings.Value);

        Assert.Contains("Ambiguous property declaration", Assert.Throws<InvalidDataException>(() => schema.HasProperty("PersistenceDataAsset")).Message);
    }

    [Fact]
    public void RuntimeArrayDeclarationIsNotAScalarIdentityLink()
    {
        var source = Object("Definition", "DataAsset");
        var declaration = RuntimeClassFixture.Derive(source);
        declaration.ChildProperties = [new FObjectProperty { Name = "PersistenceDataAsset", ArrayDim = 2 }];

        Assert.Contains("not scalar", Assert.Throws<InvalidDataException>(() =>
            ClassSchema.Read(source, Mappings.Value).HasProperty("PersistenceDataAsset")).Message);
    }

    [Fact]
    public void MissingTextSchemaReportsOneSourceAndStillReadsOtherSources()
    {
        var broken = Object("Broken", "UICurrencyMetaDataItem", ("LongName", new TextProperty(new FText("Broken name"))));
        RuntimeClassFixture.Derive(broken).Super = null;
        var valid = Object("Valid", "UICurrencyMetaDataItem", ("LongName", new TextProperty(new FText("Valid name"))));
        var issues = new List<ExtractionIssue>();

        var text = Text.Read(new CatalogAsset(42, [], new[] { broken, valid }.Select(source => new CatalogSource(
            new(source.Name, source.ExportType, source.GetPathName()), Text.Capture(source, Mappings.Value, issues), [])).ToArray()), issues);

        Assert.Equal("Valid name", text.Name?.Source);
        Assert.Equal("Broken", Assert.Single(issues).Path);
    }

    [Fact]
    public void DisabledMetadataOverrideCannotFollowAnUndeclaredIdentityReference()
    {
        var identity = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var source = Object("Metadata", "UIMetaDataItem", ("PersistenceDataAsset", Reference(identity)),
            ("bOverrideAssetId", new BoolProperty(false)), ("OverrideAssetId", new Int64Property(99)));
        RuntimeClassFixture.Derive(source).ChildProperties =
        [
            new FBoolProperty { Name = "bOverrideAssetId", ArrayDim = 1 },
            new FInt64Property { Name = "OverrideAssetId", ArrayDim = 1 }
        ];
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([source], Mappings.Value, issues));

        Assert.Contains("no resolvable PersistenceDataAsset", Assert.Single(issues).Message);
    }

    [Fact]
    public void DecodedReferenceCannotOverrideAContradictoryDeclarationType()
    {
        var identity = Object("Identity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var source = Object("Definition", "DataAsset", ("PersistenceDataAsset", Reference(identity)));
        RuntimeClassFixture.Derive(source).ChildProperties =
            [new FInt64Property { Name = "PersistenceDataAsset", ArrayDim = 1 }];
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([source], Mappings.Value, issues));

        Assert.Contains(issues, issue => issue.Stage == "asset" && issue.Message.Contains("has type Int64Property"));
    }

    [Theory]
    [InlineData("Object", false)]
    [InlineData("Actor", true)]
    public void IdentityLinksRejectUnrelatedTargetsEvenWithAnAssetId(string nativeClass, bool impostor)
    {
        var target = Object("WrongTarget", nativeClass, ("AssetId", new Int64Property(42)));
        if (impostor) RuntimeClassFixture.Derive(target, "PersistenceDataAsset");
        var definition = Object("Definition", "QuestDefinition", ("PersistenceDataAsset", Reference(target)));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([definition], Mappings.Value, issues));

        Assert.Contains("identity target is not a persistence class", Assert.Single(issues).Message);
    }

    [Theory]
    [InlineData("PersistenceDataAsset", "Actor")]
    [InlineData("Actor", "PersistenceDataAsset")]
    public void ContradictoryRuntimeParentsCannotAssignOrSilentlyDropIdentity(string headerParent, string bodyParent)
    {
        var source = Object("Contradiction", headerParent, ("AssetId", new Int64Property(42)));
        var declaration = RuntimeClassFixture.Derive(source);
        declaration.SuperStruct = new FPackageIndex(new FixturePackage(new UScriptClass(bodyParent)
        { Outer = new ResolvedPackageObject(new FixturePackage { Name = "/Script/Fixture" }) }), 1);
        var issues = new List<ExtractionIssue>();

        // Header inventory is metadata-only, while projection validates the layout parent.
        Assert.Null(ClassSchema.Read(source.Class!, Mappings.Value).Error);
        Assert.Empty(Assets.Collect([source], Mappings.Value, issues));

        Assert.Contains(issues, issue => issue.Stage == "asset" && issue.Message.Contains("superclass disagrees"));
    }

    [Fact]
    public void RuntimeParentComparisonIncludesTheNativePackageIdentity()
    {
        var source = Object("Contradiction", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var declaration = RuntimeClassFixture.Derive(source);
        declaration.SuperStruct = new FPackageIndex(new FixturePackage(new UScriptClass("PersistenceDataAsset")
        { Outer = new ResolvedPackageObject(new FixturePackage { Name = "/Script/DifferentModule" }) }), 1);
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Assets.Collect([source], Mappings.Value, issues));

        Assert.Contains(issues, issue => issue.Stage == "asset" && issue.Message.Contains("superclass disagrees"));
    }

    private static ObjectProperty Reference(UObject source) => new(new FPackageIndex(new FixturePackage(source), 1));

    private static UObject Object(string name, string type, params (string Name, FPropertyTagType Value)[] properties) =>
        new(properties.Select(property => new FPropertyTag { Name = property.Name, Tag = property.Value }).ToList())
        { Name = name, Class = new ResolvedLoadedObject(new UScriptClass(type)) };
}
