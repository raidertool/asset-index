using CUE4Parse.MappingsProvider;

namespace AssetIndex;

internal sealed record ClassScopeDeclaration(string ClassPath, string? SuperPath, bool Complete);

// Class-family evidence can exclude an object from the catalog without supplying
// its missing serialized layout. It must never be installed as a schema.
internal sealed class ClassScope(TypeMappings mappings, string? mappingSha256)
{
    private const string SupportedMapping = "61018eaea9fed46a1351fbdc5f7429e6f24df8f45fad576b4b15c534b1b359c4";
    private const string SensingClass = "/Script/Angelscript.AISensingStatusTransition";
    private const string WidgetClass = "/Script/UMG.WidgetBlueprintGeneratedClass";
    private const string BlueprintClass = "/Script/Engine.BlueprintGeneratedClass";
    private static readonly string[] CatalogRoots =
    [
        "DataAsset", "UIMetaDataItem", "DataTable", "CurveTable", "StringTable", "Blueprint", "BlueprintGeneratedClass",
        "PersistenceDataAsset", "OptionalPersistenceDataAsset", "ItemDataAssetBase",
        "LoadoutFrameItemDataAsset", "UIInventoryContainerMetaDataItem", "InventoryTreeRootAsset",
        "InventoryContainerItemDataAsset", "InventoryContainerSlotDataAsset", "CharacterVisualSlotOnlineItemDataAsset",
        "CharacterVisualSkinOnlineItemDataAsset", "UICharacterVisualSkinMetaDataItem", "UICharacterCustomizationQuickNavTabMetaDataItem"
    ];

    public bool CanOmitBody(string classPath, Func<string, ClassScopeDeclaration?> findDeclaration)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var widgetFamily = false;
        while (IsRuntimePath(classPath))
        {
            if (seen.Count >= 128 || !seen.Add(classPath)) return false;
            var declaration = findDeclaration(classPath);
            if (declaration is not { Complete: true, SuperPath: not null }) return false;
            if (Equal(declaration.ClassPath, WidgetClass)) widgetFamily = true;
            else if (!Equal(declaration.ClassPath, BlueprintClass)) return false;
            classPath = declaration.SuperPath;
        }

        var nativeName = NativeName(classPath);
        if (nativeName is null || seen.Count >= 128 || mappings.Types.ContainsKey(nativeName) ||
            CatalogRoots.Contains(nativeName, StringComparer.OrdinalIgnoreCase)) return false;
        // Unreal's WidgetBlueprintGeneratedClass contract fixes its instance
        // family to UserWidget, even when an intermediate layout is absent.
        if (widgetFamily) return !Equal(classPath, SensingClass) && MatchesChain("UserWidget", "Widget", "Visual", "Object");
        return Equal(classPath, SensingClass) && Equal(mappingSha256, SupportedMapping) &&
            MatchesChain("AIBSMTransitionInstance_Base", "SMTransitionInstance", "SMNodeInstance", "Object");
    }

    private bool MatchesChain(params string[] names)
    {
        for (var index = 0; index < names.Length; index++)
        {
            var parent = index + 1 < names.Length ? names[index + 1] : null;
            if (!mappings.Types.TryGetValue(names[index], out var type) ||
                !Equal(type.Name, names[index]) || !Equal(type.SuperType, parent)) return false;
        }
        return true;
    }

    private static string? NativeName(string path)
    {
        if (!path.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = path[8..].Split('.');
        return parts.Length == 2 && parts.All(part => part.Length > 0 &&
            part.All(character => char.IsLetterOrDigit(character) || character == '_')) ? parts[1] : null;
    }

    private static bool IsRuntimePath(string path)
    {
        if (!path.StartsWith('/') || path.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase) ||
            path.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '\\' or ':')) return false;
        var separator = path.LastIndexOf('.');
        return separator > path.LastIndexOf('/') + 1 && separator < path.Length - 1 &&
            path.IndexOf('.') == separator && !path.Contains("//", StringComparison.Ordinal);
    }

    private static bool Equal(string? first, string? second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
