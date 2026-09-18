using AssetIndex.Discovery;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex.Tests;

public sealed class UnavailableReferenceTests
{
    [Fact]
    public void AbsentOrdinarySoftInputDoesNotBecomeAMountedPackageFailure()
    {
        using var provider = new CrawlerProvider(Source());
        var evidence = new List<ObjectEvidence>();

        var result = new ObjectCrawler(provider, evidence.Add, _ => { }).Read([], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Equal(1, result.Candidates);
        Assert.Equal(1, result.Loaded);
        Assert.Equal(1, result.UnavailableSoftReferences);
        var edge = Assert.Single(Assert.Single(evidence).References, edge => edge.Kind == "soft");
        Assert.Equal("/Game/Absent.Absent", edge.TargetPath);
        Assert.Equal(PackageInputIndex.Id(FPackageId.FromName("/Game/Absent").id), edge.Unavailable!.PackageId);
        Assert.False(edge.IsNull);
    }

    [Fact]
    public void AnAbsentTargetSharedByTwoOrdinaryFieldsRetainsBothOccurrences()
    {
        var package = Source();
        using var provider = new CrawlerProvider(package);
        var body = package.ExportsLazy[0].Value;
        var array = Assert.IsType<ArrayProperty>(body.Properties[0].Tag).Value!;
        array.Properties.Add(new SoftObjectProperty(new FSoftObjectPath("/Game/Absent.Absent", "")));

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Equal(2, result.UnavailableSoftReferences);
    }

    [Fact]
    public void ActualLogicalPackageNamesPreventAbsentInputClassification()
    {
        using var provider = new CrawlerProvider(Source(), new("OddMount/Present.uasset", "/Game/Absent", new CrawlerExport("Absent", "Actor")));

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Equal(0, result.UnavailableSoftReferences);
        Assert.True(provider.HasLoadedPackageId(FPackageId.FromName("/Game/Absent").id));
    }

    [Theory]
    [InlineData("PersistenceDataAsset")]
    [InlineData("Icon")]
    [InlineData("DefaultContainer")]
    [InlineData("MapAreas")]
    public void AProtectedMissingReferenceRemainsBlocking(string field)
    {
        using var provider = new CrawlerProvider(Source(field));

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Equal(0, result.UnavailableSoftReferences);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void UndeclaredExploratoryPropertyCannotBeWaived()
    {
        using var provider = new CrawlerProvider(Source("InventedField"));

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Equal(0, result.UnavailableSoftReferences);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void ATargetPresentInANewInputSetIsInventoriedAgain()
    {
        using var provider = new CrawlerProvider(Source(), new("PioneerGame/Content/Absent.uasset", "/Game/Absent", new CrawlerExport("Absent", "Actor")));

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Candidates);
        Assert.Equal(0, result.UnavailableSoftReferences);
    }

    [Theory]
    [InlineData("OutpostLevelDataAsset")]
    [InlineData("PLRLevelDataAsset")]
    public void ActorDefaultsRetainProvedAbsentOrdinarySoftReferences(string field)
    {
        using var provider = new CrawlerProvider(ActorSource(field));
        var evidence = new List<ObjectEvidence>();

        var result = new ObjectCrawler(provider, evidence.Add, _ => { }).Read([], (_, _) => { });

        Assert.Empty(result.Issues);
        Assert.Equal(1, result.Candidates);
        Assert.Equal(1, result.Loaded);
        Assert.Equal(1, result.UnavailableSoftReferences);
        var edge = Assert.Single(evidence.Single(row => row.Class == "MainMenuLevelScriptActor").References,
            reference => reference.Kind == "soft");
        Assert.Equal("/Game/Absent.Absent", edge.TargetPath);
        Assert.NotNull(edge.Unavailable);
    }

    [Theory]
    [InlineData("Icon", "SoftObjectProperty")]
    [InlineData("PersistenceDataAsset", "SoftObjectProperty")]
    [InlineData("InventedField", "SoftObjectProperty")]
    [InlineData("OutpostLevelDataAsset", "ArrayProperty")]
    public void ActorAbsenceStillRequiresAnUnprotectedDeclaredField(string field, string type)
    {
        using var provider = new CrawlerProvider(ActorSource(field, type));

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Equal(0, result.UnavailableSoftReferences);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void ActorReferenceToAPresentButUnreadablePackageRemainsBlocking()
    {
        using var provider = new CrawlerProvider(ActorSource(),
            new("PioneerGame/Content/Absent.uasset", "/Game/Absent", new CrawlerExport("Absent", "DataAsset")));
        provider.BeforeLoad = package =>
        {
            if (package.Name == "/Game/Absent") throw new InvalidDataException("Package cannot be decoded.");
        };

        var result = new ObjectCrawler(provider, _ => { }, _ => { }).Read([], (_, _) => { });

        Assert.Equal(0, result.UnavailableSoftReferences);
        Assert.Contains(result.Issues, issue => issue.Stage == "package" && issue.Message.Contains("Package cannot be decoded"));
    }

    private static CrawlerPackage ActorSource(string field = "OutpostLevelDataAsset", string type = "SoftObjectProperty")
    {
        CrawlerPackage? package = null;
        package = new("PioneerGame/Content/Scene.uasset", "/Game/Scene",
            new("SceneClass", "BlueprintGeneratedClass")
            {
                Factory = () => new UClass { ClassDefaultObject = new FPackageIndex(package!, 2) }
            },
            new("Default", "MainMenuLevelScriptActor")
            {
                OnLoad = source =>
                {
                    source.Flags |= EObjectFlags.RF_ClassDefaultObject;
                    source.Properties.Add(new FPropertyTag
                    {
                        Name = field,
                        PropertyType = type,
                        Tag = new SoftObjectProperty(new FSoftObjectPath("/Game/Absent.Absent", ""))
                    });
                }
            });
        return package;
    }

    private static CrawlerPackage Source(string field = "WorldItemClasses") => new("PioneerGame/Content/Source.uasset", "/Game/Source",
        new CrawlerExport("Source", "PreloadableEmbarkActorListAsset")
        {
            OnLoad = source => source.Properties.Add(new FPropertyTag
            {
                Name = field,
                PropertyType = "ArrayProperty",
                Tag = new ArrayProperty(new UScriptArray([new SoftObjectProperty(new FSoftObjectPath("/Game/Absent.Absent", ""))], "SoftObjectProperty"))
            })
        });
}
