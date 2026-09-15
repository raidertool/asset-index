using System.Diagnostics.CodeAnalysis;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex;

internal static class Properties
{
    public static FPropertyTag? Find(UObject source, string name) => Find(source, name, out _);

    public static FPropertyTag? Find(UObject source, string name, out UObject? definedAt)
    {
        definedAt = null;
        var visited = new HashSet<UObject>(ReferenceEqualityComparer.Instance);
        for (UObject? current = source; current is not null; current = current.Template?.Object?.Value)
        {
            if (!visited.Add(current))
                throw new InvalidDataException($"Template cycle while reading {source.GetPathName()}.{name}.");

            var property = current.Properties.Find(property => property.Name.Text == name);
            if (property is not null)
            {
                definedAt = current;
                return property;
            }
        }

        return null;
    }

    public static bool TryGet<T>(UObject source, string name, [MaybeNullWhen(false)] out T value) =>
        TryGet(source, name, out value, out _);

    public static bool TryGet<T>(UObject source, string name, [MaybeNullWhen(false)] out T value,
        out UObject? definedAt)
    {
        var property = Find(source, name, out definedAt);
        if (property is null)
        {
            value = default;
            return false;
        }

        if (property.Tag?.GetValue(typeof(T)) is T result)
        {
            value = result;
            return true;
        }

        throw new InvalidDataException($"Cannot read {source.GetPathName()}.{name} as {typeof(T).Name}.");
    }

    public static UObject? Reference(UObject source, string name)
    {
        var property = Find(source, name);
        if (property is null)
            return null;

        return property.Tag?.GenericValue switch
        {
            FPackageIndex { IsNull: true } => null,
            FPackageIndex hard => hard.Load()
                ?? throw new InvalidDataException($"Cannot load {source.GetPathName()}.{name}."),
            FSoftObjectPath soft when soft.AssetPathName.IsNone => null,
            FSoftObjectPath soft => LoadSoftReference(soft),
            _ => throw new InvalidDataException($"Unsupported reference at {source.GetPathName()}.{name}.")
        };
    }

    private static UObject LoadSoftReference(FSoftObjectPath reference)
    {
        var target = reference.Load();
        if (string.IsNullOrEmpty(reference.SubPathString))
            return target;

        foreach (var name in reference.SubPathString.Split('.'))
            target = target.Owner?.GetExport(name)
                ?? throw new InvalidDataException($"Cannot resolve subobject {reference}.");
        return target;
    }
}
