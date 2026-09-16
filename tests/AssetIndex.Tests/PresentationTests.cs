using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class PresentationTests
{
    private static readonly Lazy<TypeMappings> Mappings = new(() =>
        new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap")).MappingsForGame!);

    [Fact]
    public void SerializedEnumAndReferencesProvideNamesWithoutFilenameInference()
    {
        var (objects, slot, container, frame, label) = Fixture();
        var issues = new List<ExtractionIssue>();
        var assets = Assets.Collect(objects, Mappings.Value, issues);

        foreach (var id in new long[] { 42, 43 })
        {
            var asset = Assert.Single(assets, asset => asset.Id == id);
            var presentation = Assert.Single(asset.PresentationNames);
            Assert.Equal("Quick Use", Text.Read(asset, issues).Name?.Source);
            Assert.Equal(frame.GetPathName(), presentation.FramePath);
            Assert.Equal(slot.GetPathName(), presentation.SlotPath);
            Assert.Equal(id == 43 ? container.GetPathName() : null, presentation.ContainerPath);
            Assert.Equal(label.GetPathName(), presentation.Metadata.Path);
            Assert.DoesNotContain(asset.Definitions, source => source.Reference.Path == frame.GetPathName());
            Assert.DoesNotContain(asset.Metadata, source => source.Reference.Path == label.GetPathName());
        }
        Assert.Empty(issues);
    }

    [Fact]
    public void ConflictingEnumLabelsRemainAlternativesWithoutAnOrderDependentWinner()
    {
        var (objects, _, _, _, _) = Fixture();
        objects.Add(Label("OtherUI", "Belt", "A different label"));
        var forwardIssues = new List<ExtractionIssue>();
        var reverseIssues = new List<ExtractionIssue>();
        var forward = Assets.Collect(objects, Mappings.Value, forwardIssues).Single(asset => asset.Id == 42);
        objects.Reverse();
        var reverse = Assets.Collect(objects, Mappings.Value, reverseIssues).Single(asset => asset.Id == 42);

        var left = Text.Read(forward, forwardIssues);
        var right = Text.Read(reverse, reverseIssues);

        Assert.Null(left.Name);
        Assert.Null(right.Name);
        Assert.Equal(left.Candidates, right.Candidates);
        Assert.Equal(2, forward.PresentationNames.Count);
        Assert.Empty(forwardIssues);
        Assert.Empty(reverseIssues);
        Assert.Contains("no primary value", Assert.Single(left.Notices).Message);
        Assert.Equal(left.Notices, right.Notices);
    }

    [Fact]
    public void UiNameTakesPrecedenceButContainerProvenanceRemains()
    {
        var (objects, _, _, _, _) = Fixture();
        objects.Add(Object("SpecificUI", "UIGameplayItemMetaDataItem",
            ("bOverrideAssetId", new BoolProperty(true)), ("OverrideAssetId", new Int64Property(43)),
            ("ItemName", new TextProperty(new FText("Special carrier")))));
        var issues = new List<ExtractionIssue>();
        var asset = Assets.Collect(objects, Mappings.Value, issues).Single(asset => asset.Id == 43);

        var text = Text.Read(asset, issues);

        Assert.Equal("Special carrier", text.Name?.Source);
        Assert.Single(asset.PresentationNames);
        Assert.Contains(text.Candidates, candidate => candidate.SourceKind == "container"
            && candidate.Reference.Source == "Quick Use" && candidate.Field == "ContainerName");
        Assert.Empty(issues);
    }

    [Fact]
    public void SharedSlotWithDifferentUiTypesIsReportedAsANameConflict()
    {
        var (objects, slot, _, _, _) = Fixture();
        objects.Add(Frame("AnotherFrame", slot, "Weapon1"));
        objects.Add(Label("WeaponUI", "Weapon1", "Primary Weapon"));
        var issues = new List<ExtractionIssue>();
        var asset = Assets.Collect(objects, Mappings.Value, issues).Single(asset => asset.Id == 42);

        var text = Text.Read(asset, issues);

        Assert.Null(text.Name);
        Assert.Equal(2, asset.PresentationNames.Count);
        Assert.Empty(issues);
        Assert.Contains("Conflicting display-name", Assert.Single(text.Notices).Message);
    }

    [Fact]
    public void AnUnrelatedReferenceCannotActAsAContainerSlot()
    {
        var wrong = Object("NotASlot", "ItemDataAsset");
        var issues = new List<ExtractionIssue>();
        Assert.Empty(Presentation.Read([Frame("Frame", wrong, "Belt"), Label("UI", "Belt", "Quick Use")], Mappings.Value, issues));
        Assert.Contains("Expected InventoryContainerSlotDataAsset", Assert.Single(issues).Message);
    }

    [Fact]
    public void ClassDefaultsDoNotIntroduceConflictingNamesOrContextLinks()
    {
        var (objects, slot, _, _, _) = Fixture();
        var defaultLabel = Label("DefaultLabel", "Belt", "Default label");
        var defaultFrame = Frame("DefaultFrame", slot, "Weapon1");
        defaultLabel.Flags |= EObjectFlags.RF_ClassDefaultObject;
        defaultFrame.Flags |= EObjectFlags.RF_ClassDefaultObject;
        objects.AddRange([defaultLabel, defaultFrame]);
        var issues = new List<ExtractionIssue>();
        var asset = Assets.Collect(objects, Mappings.Value, issues).Single(asset => asset.Id == 42);

        Assert.Equal("Quick Use", Text.Read(asset, issues).Name?.Source);
        Assert.Single(asset.PresentationNames);
        Assert.Empty(issues);
    }

    [Fact]
    public void MissingTypeMetadataIsReportedWithoutGuessingFromTheObjectName()
    {
        var (objects, _, _, frame, _) = Fixture();
        var issues = new List<ExtractionIssue>();
        Assert.Empty(Presentation.Read([frame], Mappings.Value, issues));
        Assert.Contains("No container presentation metadata", Assert.Single(issues).Message);
    }

    [Fact]
    public void RuntimeSubclassesKeepTheTypedContainerJoin()
    {
        var (objects, slot, container, frame, label) = Fixture();
        foreach (var source in new[] { slot, container, frame, label }) RuntimeClassFixture.Derive(source);
        var issues = new List<ExtractionIssue>();

        var assets = Assets.Collect(objects, Mappings.Value, issues);

        foreach (var id in new long[] { 42, 43 })
        {
            var asset = Assert.Single(assets, asset => asset.Id == id);
            Assert.Single(asset.PresentationNames);
            Assert.Equal("Quick Use", Text.Read(asset, issues).Name?.Source);
        }
        Assert.Empty(issues);
    }

    [Fact]
    public void ContainerTypesUseFNameCaseInsensitiveIdentity()
    {
        var (objects, _, _, _, label) = Fixture();
        label.Properties.Single(property => property.Name.Text == "ContainerType").Tag =
            new EnumProperty(new FName("enewinventorycontainertype::belt"));
        var issues = new List<ExtractionIssue>();

        var names = Presentation.Read(objects, Mappings.Value, issues);

        Assert.Equal(2, names.Count);
        Assert.All(names, name => Assert.Equal("ENewInventoryContainerType::Belt", name.ContainerType));
        Assert.Empty(issues);
    }

    [Fact]
    public void BrokenRuntimeAncestryDoesNotAbortOtherPresentationSources()
    {
        var (objects, _, _, _, _) = Fixture();
        var broken = Label("Broken", "Belt", "Unproven label");
        RuntimeClassFixture.Derive(broken).Super = null;
        objects.Add(broken);
        var issues = new List<ExtractionIssue>();

        var names = Presentation.Read(objects, Mappings.Value, issues);

        Assert.Equal(2, names.Count);
        Assert.Contains("Runtime superclass metadata is missing", Assert.Single(issues).Message);
    }

    [Fact]
    public void ContainerStructFieldsUseCaseInsensitiveNames()
    {
        var (objects, _, _, frame, _) = Fixture();
        Assert.True(Properties.TryGet<FStructFallback[]>(frame, "Containers", out var containers));
        containers[0].Properties[0].Name = "tYPE";
        containers[0].Properties[1].Name = "containerSLOTDATAasset";
        var issues = new List<ExtractionIssue>();

        Assert.Equal(2, Presentation.Read(objects, Mappings.Value, issues).Count);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("Type")]
    [InlineData("ContainerSlotDataAsset")]
    public void EqualDuplicateContainerStructFieldsAreStillAmbiguous(string field)
    {
        var (objects, _, _, frame, _) = Fixture();
        Assert.True(Properties.TryGet<FStructFallback[]>(frame, "Containers", out var containers));
        var original = containers[0].Properties.Single(property => property.Name.Text == field);
        containers[0].Properties.Add(new FPropertyTag { Name = field.ToLowerInvariant(), Tag = original.Tag });
        var issues = new List<ExtractionIssue>();

        Assert.Empty(Presentation.Read(objects, Mappings.Value, issues));

        Assert.Contains("Ambiguous property", Assert.Single(issues).Message);
        Assert.Equal(frame.GetPathName() + ".Containers[0]", issues[0].Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedContainerTextIsReportedOnlyForAUsedLabel(bool joined)
    {
        var (objects, _, _, _, label) = Fixture();
        label.Properties.Single(property => property.Name.Text == "ContainerName").Tag = new Int64Property(123);
        var issues = new List<ExtractionIssue>();
        var input = joined ? objects : [label];

        var assets = Assets.Collect(input, Mappings.Value, issues);
        Assert.Empty(issues);
        foreach (var asset in assets) Text.Read(asset, issues);

        if (joined)
        {
            Assert.NotEmpty(issues);
            Assert.All(issues, issue => Assert.Equal("text", issue.Stage));
        }
        else Assert.Empty(issues);
    }

    [Fact]
    public void ContainerObservationsSurvivePackageMutationAndArrivalOrder()
    {
        var (objects, slot, container, frame, label) = Fixture();
        var issues = new List<ExtractionIssue>();
        var collector = new CatalogCollector(Mappings.Value, _ => throw new InvalidOperationException("No images expected."), issues);
        collector.Observe(frame);
        foreach (var source in objects.Where(source => source != frame)) collector.Observe(source);
        foreach (var source in new[] { slot, container, frame, label }) source.Properties.Clear();

        var assets = collector.Complete();

        foreach (var id in new long[] { 42, 43 })
        {
            var asset = Assert.Single(assets, asset => asset.Id == id);
            Assert.Equal("Quick Use", Text.Read(asset, issues).Name?.Source);
            Assert.Single(asset.PresentationNames);
        }
        Assert.Empty(issues);
    }

    private static (List<UObject> Objects, UObject Slot, UObject Container, UObject Frame, UObject Label) Fixture()
    {
        var containerIdentity = Object("IdentityA", "PersistenceDataAsset", ("AssetId", new Int64Property(43)));
        var container = Object("UninformativeA", "InventoryContainerItemDataAsset", ("PersistenceDataAsset", Reference(containerIdentity)));
        var slotIdentity = Object("IdentityB", "PersistenceDataAsset", ("AssetId", new Int64Property(42)));
        var slot = Object("UninformativeB", "InventoryContainerSlotDataAsset",
            ("PersistenceDataAsset", Reference(slotIdentity)), ("DefaultContainer", Reference(container)));
        var frame = Frame("UninformativeC", slot, "Belt");
        var label = Label("UninformativeD", "Belt", "Quick Use");
        return ([containerIdentity, container, slotIdentity, slot, frame, label], slot, container, frame, label);
    }

    private static UObject Frame(string name, UObject slot, string type)
    {
        var entry = new FStructFallback([
            new FPropertyTag { Name = "Type", Tag = new EnumProperty(new FName("ENewInventoryContainerType::" + type)) },
            new FPropertyTag { Name = "ContainerSlotDataAsset", Tag = Reference(slot) }
        ]);
        return Object(name, "LoadoutFrameItemDataAsset", ("bOverrideItemAssetId", new BoolProperty(true)),
            ("OverrideItemAssetId", new Int64Property(99)),
            ("Containers", new ArrayProperty(new UScriptArray([new StructProperty(new FScriptStruct(entry))], "StructProperty"))));
    }

    private static UObject Label(string name, string type, string text) => Object(name, "UIInventoryContainerMetaDataItem",
        ("ContainerType", new EnumProperty(new FName("ENewInventoryContainerType::" + type))),
        ("ContainerName", new TextProperty(new FText("inventory", text, text))));

    private static ObjectProperty Reference(UObject source) => new(new FPackageIndex(new FixturePackage(source), 1));

    private static UObject Object(string name, string type, params (string Name, FPropertyTagType Value)[] properties) =>
        new(properties.Select(property => new FPropertyTag { Name = property.Name, Tag = property.Value }).ToList())
        { Name = name, Class = new ResolvedLoadedObject(new UScriptClass(type)) };

}
