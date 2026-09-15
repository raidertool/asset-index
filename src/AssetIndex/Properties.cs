using System.Diagnostics.CodeAnalysis;
using CUE4Parse.UE4.Assets;
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

            var property = FindDirect(current.Properties, name, current.GetPathName());
            if (property is not null)
            {
                definedAt = current;
                return property;
            }
        }

        return null;
    }

    public static T Get<T>(FStructFallback source, string name, string path)
    {
        var property = FindDirect(source.Properties, name, path);
        return property?.Tag?.GetValue(typeof(T)) is T result ? result
            : throw new InvalidDataException($"Cannot read {path}.{name} as {typeof(T).Name}.");
    }

    private static FPropertyTag? FindDirect(IEnumerable<FPropertyTag> properties, string name, string path)
    {
        FPropertyTag? property = null;
        foreach (var candidate in properties)
        {
            if (!candidate.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (property is not null) throw new InvalidDataException($"Ambiguous property {path}.{name}.");
            property = candidate;
        }
        return property;
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

        return ResolveSubobject(target, reference.SubPathString);
    }

    internal static UObject ResolveSubobject(UObject target, string subpath)
    {
        foreach (var name in subpath.Split('.'))
        {
            var package = target.Owner ?? throw new InvalidDataException("Subobject has no owning package.");
            ResolvedObject? match = null;
            for (var index = 0; index < package.ExportMapLength; index++)
            {
                var candidate = package.ResolvePackageIndex(new FPackageIndex(package, index + 1));
                // Export names are only unique within an outer. A package-wide name
                // lookup can silently return another asset's identically named child.
                if (candidate is null || !candidate.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || !ReferenceEquals(candidate.Outer?.Object?.Value, target)) continue;
                if (match is not null) throw new InvalidDataException($"Ambiguous subobject {subpath}.");
                match = candidate;
            }
            target = match?.Object?.Value ?? throw new InvalidDataException($"Cannot resolve subobject {subpath}.");
        }
        return target;
    }
}
