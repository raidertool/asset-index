using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.GameplayTags;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class VisualSlotLabelTests
{
    private static readonly Lazy<TypeMappings> Mappings = new(() => new FileUsmapTypeMappingsProvider(
        Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap")).MappingsForGame!);
    private const string Category = "Ui.Category.A";

    [Fact]
    public void QueryMembershipAndTypedUiJoinSupplyTheLabelWithEveryMember()
    {
        var objects = Fixture();
        var issues = new List<ExtractionIssue>();

        var result = Assert.Single(VisualSlotLabels.Read(objects, Mappings.Value, issues));

        Assert.Equal(42, result.AssetId);
        Assert.Equal("OpaqueSlot", result.SlotPath);
        Assert.Equal(Category, result.TypeTag);
        Assert.Equal("OpaqueNavigation", result.Metadata.Path);
        Assert.Equal(2, result.Members.Count);
        Assert.Equal([10L, 11L], result.Members.Select(member => member.AssetId));
        Assert.All(result.Members, member => Assert.Contains(member.PersistencePath, new[] { "IdentityA", "IdentityB" }));
        var text = Assert.Single(result.Text);
        Assert.Equal("Visible category", text.Reference.Source);
        Assert.Equal("visual-slot", text.SourceKind);
        Assert.Equal("DisplayName", text.Field);
        Assert.Equal(new byte[] { 0, 1, 2, 2, 0, 1 }, result.Query.Tokens);
        Assert.Empty(result.TextIssues);
        Assert.Empty(issues);
    }

    [Fact]
    public void ObservationsSurvivePackageMutationAndReversedArrivalOrder()
    {
        var objects = Fixture();
        var issues = new List<ExtractionIssue>();
        var collector = new VisualSlotLabelCollector(Mappings.Value, issues);
        foreach (var source in objects.AsEnumerable().Reverse()) collector.Observe(source);
        foreach (var source in objects) source.Properties.Clear();

        var result = Assert.Single(collector.Complete());

        Assert.Equal(2, result.Members.Count);
        Assert.Equal("Visible category", Assert.Single(result.Text).Reference.Source);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mixed-category")]
    [InlineData("contradictory-identity")]
    public void EveryMatchingMemberMustHaveConsistentUiMetadata(string failure)
    {
        var objects = Fixture();
        var metadata = objects.Single(source => source.Name == "OpaqueUiB");
        if (failure == "missing") objects.Remove(metadata);
        if (failure == "mixed-category") Set(metadata, "TypeTag", Tag("Ui.Category.Other"));
        if (failure == "contradictory-identity")
        {
            var skin = objects.Single(source => source.Name == "OpaqueSkinB");
            Add(skin, "bOverrideItemAssetId", new BoolProperty(true));
            Add(skin, "OverrideItemAssetId", new Int64Property(99));
        }
        var issues = new List<ExtractionIssue>();

        Assert.Empty(VisualSlotLabels.Read(objects, Mappings.Value, issues));

        Assert.NotEmpty(issues);
    }

    [Fact]
    public void SameNumericIdentityDoesNotReplaceTheActualPersistenceReference()
    {
        var objects = Fixture();
        var ui = objects.Single(source => source.Name == "OpaqueUiB");
        var other = Object("DifferentIdentityObject", "PersistenceDataAsset", ("AssetId", new Int64Property(11)));
        Set(ui, "PersistenceDataAsset", Reference(other));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(VisualSlotLabels.Read(objects, Mappings.Value, issues));

        Assert.Contains(issues, issue => issue.Message.Contains("missing or contradictory UI identity"));
    }

    [Fact]
    public void AnUnmatchedSkinNeedsNoUiMetadata()
    {
        var objects = Fixture();
        var skin = objects.Single(source => source.Name == "OpaqueSkinB");
        Set(skin, "Tags", Tags("Other.Slot"));
        objects.RemoveAll(source => source.Name == "OpaqueUiB");
        var issues = new List<ExtractionIssue>();

        Assert.Single(Assert.Single(VisualSlotLabels.Read(objects, Mappings.Value, issues)).Members);

        Assert.Empty(issues);
    }

    [Fact]
    public void SlotsSelectingOtherItemKindsHaveNoSkinLabelRelation()
    {
        var objects = Fixture();
        Set(objects.Single(source => source.Name == "OpaqueSlot"), "ItemsQuery", Query(["Item.Setting"], [0, 1, 1, 1, 0]));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(VisualSlotLabels.Read(objects, Mappings.Value, issues));

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidSkinTypeWithoutMatchingNavigationHasNoLabelRelation(bool unrelatedNavigation)
    {
        var objects = Fixture();
        var navigation = objects.Single(source => source.Name == "OpaqueNavigation");
        if (unrelatedNavigation) Set(navigation, "CharacterCustomizationTypeTag", Tag("Other.Category"));
        else objects.Remove(navigation);
        var issues = new List<ExtractionIssue>();

        Assert.Empty(VisualSlotLabels.Read(objects, Mappings.Value, issues));

        Assert.Empty(issues);
    }

    [Fact]
    public void MalformedNavigationCannotBecomeAnOrdinaryMissingLabel()
    {
        var objects = Fixture();
        Set(objects.Single(source => source.Name == "OpaqueNavigation"), "CharacterCustomizationTypeTag", new Int64Property(1));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(VisualSlotLabels.Read(objects, Mappings.Value, issues));

        Assert.Contains(issues, issue => issue.Stage == "visual-slot" && issue.Path == "OpaqueNavigation");
    }

    [Fact]
    public void UnreadableSkinTagsCannotSilentlyNarrowTheMatchingPopulation()
    {
        var objects = Fixture();
        Set(objects.Single(source => source.Name == "OpaqueSkinB"), "Tags", new Int64Property(1));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(VisualSlotLabels.Read(objects, Mappings.Value, issues));

        Assert.Contains(issues, issue => issue.Message.Contains("cannot exclude unreadable"));
    }

    [Fact]
    public void SlotIdentityUsesItsEnabledOverride()
    {
        var objects = Fixture();
        var slot = objects.Single(source => source.Name == "OpaqueSlot");
        Add(slot, "bOverrideItemAssetId", new BoolProperty(true));
        Add(slot, "OverrideItemAssetId", new Int64Property(123));
        var issues = new List<ExtractionIssue>();

        Assert.Equal(123, Assert.Single(VisualSlotLabels.Read(objects, Mappings.Value, issues)).AssetId);

        Assert.Empty(issues);
    }

    [Fact]
    public void MultipleNavigationLabelsRemainExplicitAlternatives()
    {
        var objects = Fixture();
        objects.Add(Navigation("SecondNavigation", Category, "Other label"));
        var issues = new List<ExtractionIssue>();

        var results = VisualSlotLabels.Read(objects, Mappings.Value, issues);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, results.SelectMany(result => result.Text).Select(text => text.Reference).Distinct().Count());
        Assert.Empty(issues);
    }

    [Fact]
    public void UnrelatedClassesAndClassDefaultsCannotSupplyMembersOrLabels()
    {
        var objects = Fixture();
        objects.RemoveAll(source => source.Name == "OpaqueUiB");
        var fake = Object("Fake", "UIMetaDataItem", ("PersistenceDataAsset", Reference(objects.Single(source => source.Name == "IdentityB"))),
            ("TypeTag", Tag(Category)));
        objects.Add(fake);
        var defaultUi = SkinUi("DefaultUI", objects.Single(source => source.Name == "IdentityB"), Category);
        defaultUi.Flags |= EObjectFlags.RF_ClassDefaultObject;
        objects.Add(defaultUi);
        var issues = new List<ExtractionIssue>();

        Assert.Empty(VisualSlotLabels.Read(objects, Mappings.Value, issues));
    }

    [Fact]
    public void RuntimeSubclassesAndInheritedPropertiesRetainTheTypedJoin()
    {
        var objects = Fixture();
        var slot = objects.Single(source => source.Name == "OpaqueSlot");
        var template = Object("Template", "CharacterVisualSlotOnlineItemDataAsset");
        template.Properties.AddRange(slot.Properties);
        slot.Properties.Clear();
        slot.Template = new ResolvedLoadedObject(template);
        foreach (var source in objects) RuntimeClassFixture.Derive(source);
        var issues = new List<ExtractionIssue>();

        var result = Assert.Single(VisualSlotLabels.Read(objects, Mappings.Value, issues));

        Assert.Equal("Template", result.Query.DefinedAt);
        Assert.Empty(issues);
    }

    [Fact]
    public void DuplicateQueryFieldsAreRejectedEvenWhenIdentical()
    {
        var objects = Fixture();
        var slot = objects.Single(source => source.Name == "OpaqueSlot");
        Assert.True(Properties.TryGet<FStructFallback>(slot, "ItemsQuery", out var query));
        query.Properties.Add(query.Properties.Single(property => property.Name.Text == "QueryTokenStream"));
        var issues = new List<ExtractionIssue>();

        Assert.Empty(VisualSlotLabels.Read(objects, Mappings.Value, issues));

        Assert.Contains("Ambiguous property", Assert.Single(issues).Message);
    }

    [Fact]
    public void WrongArrayElementTypesCannotDefaultToValidZeroTokens()
    {
        var source = Query(["A"], [0, 1, 1, 1, 0]);
        var query = (FStructFallback)source.Value!.StructType;
        var tokens = Properties.Get<UScriptArray>(query, "QueryTokenStream", "Test");
        tokens.Properties[0] = new IntProperty(0);

        Assert.Throws<InvalidDataException>(() => GameplayTagQueryMatch.Capture(query, "Test"));
    }

    [Fact]
    public void DuplicateTagNamesCannotChooseAnArbitraryDictionaryEntry()
    {
        var source = Query(["A"], [0, 1, 1, 1, 0]);
        var query = (FStructFallback)source.Value!.StructType;
        var dictionary = Properties.Get<UScriptArray>(query, "TagDictionary", "Test");
        var tag = (FStructFallback)((StructProperty)dictionary.Properties[0]).Value!.StructType;
        tag.Properties.Add(new FPropertyTag { Name = "TAGNAME", Tag = new NameProperty("B") });

        Assert.Throws<InvalidDataException>(() => GameplayTagQueryMatch.Capture(query, "Test"));
    }

    [Fact]
    public void UnreferencedDictionaryTagsDoNotInventParentRequirements()
    {
        Assert.True(GameplayTagQueryMatch.Matches(new("Test", ["A", "B"], [0, 1, 1, 1, 0]), ["A", "B.Child"]));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 1, 1, 1, 0 })]
    [InlineData(new byte[] { 0, 2 })]
    [InlineData(new byte[] { 0, 1, 7, 0 })]
    [InlineData(new byte[] { 0, 1, 2, 1 })]
    [InlineData(new byte[] { 0, 1, 1, 2, 0, 99 })]
    [InlineData(new byte[] { 0, 1, 1, 1, 0, 0 })]
    public void MalformedQueryNeverBecomesAnOrdinaryNonmatch(byte[] tokens)
    {
        var query = new VisualSlotQuery("Source", ["A"], tokens);

        Assert.Throws<InvalidDataException>(() => GameplayTagQueryMatch.Matches(query, ["A"]));
    }

    [Theory]
    [InlineData(new byte[] { 0, 1, 1, 2, 0, 1 }, true)]
    [InlineData(new byte[] { 0, 1, 2, 2, 0, 1 }, false)]
    [InlineData(new byte[] { 0, 1, 3, 1, 1 }, true)]
    [InlineData(new byte[] { 0, 1, 4, 2, 1, 1, 0, 1, 1, 1 }, true)]
    [InlineData(new byte[] { 0, 1, 5, 2, 1, 1, 0, 3, 1, 1 }, true)]
    [InlineData(new byte[] { 0, 1, 6, 1, 1, 1, 1 }, true)]
    [InlineData(new byte[] { 0, 0 }, false)]
    public void SupportedExpressionsUseTheValidatedUpstreamEvaluator(byte[] tokens, bool expected)
    {
        Assert.Equal(expected, GameplayTagQueryMatch.Matches(new("Source", ["A", "B"], tokens), ["A"]));
    }

    [Theory]
    [InlineData("A", "A.Child", true)]
    [InlineData("A", "A.Child.Grandchild", true)]
    [InlineData("a.child", "A.Child.Grandchild", true)]
    [InlineData("A.Child", "A", false)]
    [InlineData("A", "AB.Child", false)]
    [InlineData("A.Child", "A.Children", false)]
    public void QueriesIncludeParentsWithDirectionalSegmentMatching(string queryTag, string tag, bool expected)
    {
        var query = new VisualSlotQuery("Source", [queryTag], [0, 1, 1, 1, 0]);

        Assert.Equal(expected, GameplayTagQueryMatch.Matches(query, [tag]));
    }

    [Theory]
    [InlineData(new byte[] { 0, 1, 2, 2, 0, 1 }, true)]
    [InlineData(new byte[] { 0, 1, 3, 1, 0 }, false)]
    [InlineData(new byte[] { 0, 1, 6, 1, 1, 1, 0 }, false)]
    public void ParentMatchesApplyToAllAndNegativeExpressions(byte[] tokens, bool expected)
    {
        Assert.Equal(expected, GameplayTagQueryMatch.Matches(new("Source", ["A", "B"], tokens), ["A.Child", "B.Child"]));
    }

    [Fact]
    public void ParentMatchesDoNotSkipValidationOfUnusedBranches()
    {
        var query = new VisualSlotQuery("Source", ["A"], [0, 1, 4, 2, 1, 1, 0, 7, 0]);

        Assert.Throws<InvalidDataException>(() => GameplayTagQueryMatch.Matches(query, ["A.Child"]));
    }

    [Theory]
    [InlineData(" A.Child")]
    [InlineData("A.Child ")]
    [InlineData("A.Child\t")]
    [InlineData("A.\nChild")]
    [InlineData("A.\rChild")]
    public void InvalidTagCharactersCannotProduceParentMatches(string invalid)
    {
        var query = new VisualSlotQuery("Source", ["A"], [0, 1, 1, 1, 0]);

        Assert.Throws<InvalidDataException>(() => GameplayTagQueryMatch.Matches(query, [invalid]));
        var unusedInvalid = query with { TagDictionary = ["A", invalid] };
        Assert.Throws<InvalidDataException>(() => GameplayTagQueryMatch.Matches(unusedInvalid, ["A"]));
    }

    private static List<UObject> Fixture()
    {
        var first = Object("IdentityA", "PersistenceDataAsset", ("AssetId", new Int64Property(10)));
        var second = Object("IdentityB", "PersistenceDataAsset", ("AssetId", new Int64Property(11)));
        var identity = Object("SlotIdentity", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var slot = Object("OpaqueSlot", "CharacterVisualSlotOnlineItemDataAsset", ("PersistenceDataAsset", Reference(identity)),
            ("ItemsQuery", Query(["Item.Skin", "Slot.A"], [0, 1, 2, 2, 0, 1])));
        return [slot, first, second, identity, Skin("OpaqueSkinA", first), Skin("OpaqueSkinB", second),
            SkinUi("OpaqueUiA", first, Category), SkinUi("OpaqueUiB", second, Category), Navigation("OpaqueNavigation", Category, "Visible category")];
    }

    private static UObject Skin(string name, UObject identity) => Object(name, "CharacterVisualSkinOnlineItemDataAsset",
        ("PersistenceDataAsset", Reference(identity)), ("Tags", Tags("Item.Skin", "Slot.A")));
    private static UObject SkinUi(string name, UObject identity, string type) => Object(name, "UICharacterVisualSkinMetaDataItem",
        ("PersistenceDataAsset", Reference(identity)), ("TypeTag", Tag(type)));
    private static UObject Navigation(string name, string type, string text) => Object(name, "UICharacterCustomizationQuickNavTabMetaDataItem",
        ("CharacterCustomizationTypeTag", Tag(type)), ("DisplayName", new TextProperty(new FText("Labels", text, text))));
    private static StructProperty Tag(string text) => new(new FScriptStruct(new FStructFallback([
        new FPropertyTag { Name = "TagName", Tag = new NameProperty(new FName(text)) }])));
    private static StructProperty Tags(params string[] tags) => new(new FScriptStruct(new FGameplayTagContainer(tags.Select(tag => new FGameplayTag(tag)).ToArray())));
    private static StructProperty Query(string[] tags, byte[] tokens) => new(new FScriptStruct(new FStructFallback([
        new FPropertyTag { Name = "TagDictionary", Tag = new ArrayProperty(new UScriptArray(tags.Select(tag => (FPropertyTagType) Tag(tag)).ToList(), "StructProperty")) },
        new FPropertyTag { Name = "QueryTokenStream", Tag = new ArrayProperty(new UScriptArray(tokens.Select(value => (FPropertyTagType) new ByteProperty(value)).ToList(), "ByteProperty")) }
    ])));
    private static ObjectProperty Reference(UObject source) => new(new FPackageIndex(new FixturePackage(source), 1));
    private static void Set(UObject source, string name, FPropertyTagType value) => source.Properties.Single(property => property.Name.Text == name).Tag = value;
    private static void Add(UObject source, string name, FPropertyTagType value) => source.Properties.Add(new FPropertyTag { Name = name, Tag = value });
    private static UObject Object(string name, string type, params (string Name, FPropertyTagType Value)[] properties) =>
        new(properties.Select(property => new FPropertyTag { Name = property.Name, Tag = property.Value }).ToList())
        { Name = name, Class = new ResolvedLoadedObject(new UScriptClass(type)) };
}
