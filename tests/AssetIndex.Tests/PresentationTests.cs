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
            Assert.Same(label, presentation.Metadata);
            Assert.DoesNotContain(frame, asset.Definitions);
            Assert.DoesNotContain(label, asset.Metadata);
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
        Assert.Contains("no primary value", Assert.Single(forwardIssues).Message);
        Assert.Single(reverseIssues);
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

        Assert.Null(Text.Read(asset, issues).Name);
        Assert.Equal(2, asset.PresentationNames.Count);
        Assert.Contains("Conflicting display-name", Assert.Single(issues).Message);
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
        { Name = name, Class = new ResolvedLoadedObject(new UObject { Name = type }) };

}
