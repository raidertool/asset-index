using AssetIndex.Discovery;
using CUE4Parse.MappingsProvider.Usmap;

namespace AssetIndex.Tests;

public sealed class RegistryTests
{
    [Theory]
    [InlineData("PersistenceDataAsset", true, true)]
    [InlineData("UIItemMetaDataItem", true, true)]
    [InlineData("StringTable", true, true)]
    [InlineData("DataTable", true, true)]
    [InlineData("BlueprintGeneratedClass", true, true)]
    [InlineData("ClassAddedByNextGameUpdate", true, true)]
    [InlineData("Texture2D", false, true)]
    [InlineData("MaterialInstanceConstant", false, true)]
    [InlineData("SoundWave", false, false)]
    [InlineData("StaticMesh", false, false)]
    public void SelectionKeepsUnknownClassesAndSeparatesDefinitionsFromBinaryMedia(string type, bool selected, bool followed)
    {
        var mappings = new FileUsmapTypeMappingsProvider(Path.Combine(AppContext.BaseDirectory, "mappings", "ArcRaiders.usmap")).MappingsForGame!;
        Assert.Equal(selected, Registry.SelectClass(mappings, type));
        Assert.Equal(followed, Registry.FollowClass(mappings, type));
    }

    [Theory]
    [InlineData("/Game/NoIconName.NoIconName", "Texture2D", "TEXTUREGROUP_UI", true)]
    [InlineData("/Game/Icon_XP.Icon_XP", "Texture2D", "TEXTUREGROUP_World", false)]
    [InlineData("/Game/UI.UI", "Texture2DArray", "TEXTUREGROUP_UI", false)]
    public void UiTextureSelectionUsesTheRegistryGroup(string path, string type, string group, bool expected)
    {
        var asset = new RegisteredObject(path, path.Split('.')[0], type, new Dictionary<string, string> { ["LODGroup"] = group });
        Assert.Equal(expected, Registry.IsUiTexture(asset));
    }
}
