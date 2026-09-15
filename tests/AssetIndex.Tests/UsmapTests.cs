using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;

namespace AssetIndex.Tests;

public sealed class UsmapTests
{
    private static readonly Lazy<TypeMappings> Mappings = new(() =>
        new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap"))
            .MappingsForGame ?? throw new InvalidDataException("The checked-in mapping contains no types."));

    [Theory]
    [InlineData("PlayerStatsRaiderTargetDataAsset")]
    [InlineData("WorldQuestDataAsset")]
    [InlineData("InteractQuestDataAsset")]
    [InlineData("XPEventCategoryDataAsset")]
    [InlineData("ActivityDataAsset")]
    public void NonInventoryDefinitionsInheritTheGameAssetId(string className)
    {
        Assert.True(Assets.IsA(Mappings.Value, className, "PersistenceDataAsset"));
        Assert.Equal("Int64Property", Property(className, "AssetId").MappingType.Type);
    }

    [Theory]
    [InlineData("UICurrencyMetaDataItem", "CurrencyIcon")]
    [InlineData("UICurrencyMetaDataItem", "CurrencyBigIcon")]
    [InlineData("UICurrencyMetaDataItem", "PersistenceDataAsset")]
    [InlineData("UIXPEventCategoryMetaDataItem", "XPEventCategoryDataAsset")]
    [InlineData("UIPlayerStatsRaiderTargetMetaDataItem", "PlayerStatsRaiderTargetDataAsset")]
    public void CurrencyAndProgressionLinksAreHardObjectReferences(string className, string field)
    {
        Assert.Equal("ObjectProperty", Property(className, field).MappingType.Type);
    }

    [Theory]
    [InlineData("ItemDataAssetBase", "bOverrideItemAssetId", "BoolProperty")]
    [InlineData("ItemDataAssetBase", "OverrideItemAssetId", "Int64Property")]
    [InlineData("ItemDataAssetBase", "PersistenceDataAsset", "ObjectProperty")]
    [InlineData("UIGameplayItemMetaDataItem", "bOverrideAssetId", "BoolProperty")]
    [InlineData("UIGameplayItemMetaDataItem", "OverrideAssetId", "Int64Property")]
    [InlineData("UIGeneratorItemMetaDataItem", "bOverrideAssetId", "BoolProperty")]
    [InlineData("UIGeneratorItemMetaDataItem", "OverrideAssetId", "Int64Property")]
    public void IdentityOverridesHaveExplicitEnablingFlags(string className, string field, string propertyType)
    {
        Assert.Equal(propertyType, Property(className, field).MappingType.Type);
    }

    [Theory]
    [InlineData("UIGameplayItemMetaDataItem", "ItemName")]
    [InlineData("UIGameplayItemMetaDataItem", "Description")]
    [InlineData("UICurrencyMetaDataItem", "ShortName")]
    [InlineData("UICurrencyMetaDataItem", "LongName")]
    [InlineData("UIXPEventCategoryMetaDataItem", "XPEventCategoryName")]
    public void DisplayFieldsCarryLocalizedTextInsteadOfPlainStrings(string className, string field)
    {
        Assert.Equal("TextProperty", Property(className, field).MappingType.Type);
    }

    private static PropertyInfo Property(string className, string field)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? current = className;
        while (current is not null && visited.Add(current))
        {
            var type = Mappings.Value.Types[current];
            var property = type.Properties.Values.FirstOrDefault(property => property.Name == field);
            if (property is not null) return property;
            current = type.SuperType;
        }
        throw new InvalidDataException($"The checked-in mapping has no {className}.{field} property.");
    }
}
