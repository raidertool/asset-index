using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex;

// Runtime declarations are identified by their actual object references. Only
// native script declarations use usmap's short-name namespace.
internal sealed class ClassSchema
{
    private readonly List<string> ancestry = [];
    private readonly List<ResolvedObject> runtime = [];
    private readonly List<Struct> native = [];
    public IReadOnlyList<string> Ancestry => ancestry;
    public string? Error { get; private set; }

    public static ClassSchema Read(UObject source, TypeMappings mappings)
    {
        var result = Read(source.Class
            ?? throw new InvalidDataException($"Class metadata is missing for {source.GetPathName()}."), mappings);
        if (result.Error is not null) return result;
        try { result.ValidateRuntimeBodies(); }
        catch (Exception error) { result.Error = AssetDiscovery.DescribeError(error); }
        return result;
    }

    public static ClassSchema Read(ResolvedObject declaration, TypeMappings mappings)
    {
        var result = new ClassSchema();
        try { result.ReadAncestry(declaration, mappings); }
        catch (Exception error) { result.Error = AssetDiscovery.DescribeError(error); }
        return result;
    }

    public IReadOnlyList<string> NativeAncestry
    {
        get
        {
            RequireComplete();
            return native.Select(type => type.Name).ToArray();
        }
    }

    public bool IsA(string nativeClass)
    {
        RequireComplete();
        return native.Any(type => type.Name.Equals(nativeClass, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasProperty(string name, params string[] expectedKinds)
    {
        RequireComplete();
        var declarations = new List<(string Owner, string Kind)>();
        foreach (var reference in runtime)
        {
            if (reference.Object?.Value is not UStruct { ChildProperties: { } fields })
                throw new InvalidDataException($"Runtime property declarations are unavailable for {ObjectMetadata.Path(reference)}.");
            foreach (var field in fields.Where(field => field.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                if (field is not FProperty { ArrayDim: 1 } property)
                    throw new InvalidDataException($"Selected property {ObjectMetadata.Path(reference)}.{name} is not scalar.");
                declarations.Add((ObjectMetadata.Path(reference), property.GetType().Name[1..]));
            }
        }
        foreach (var type in native)
            foreach (var field in type.Properties.Values.Distinct().Where(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                if (field.ArraySize is > 1)
                    throw new InvalidDataException($"Selected property {type.Name}.{name} is not scalar.");
                declarations.Add((type.Name, field.MappingType.Type));
            }
        if (declarations.Count > 1)
            throw new InvalidDataException($"Ambiguous property declaration {name}: {string.Join(", ", declarations.Select(field => field.Owner))}.");
        if (declarations.Count == 0) return false;
        var selected = declarations[0];
        if (expectedKinds.Length != 0 && !expectedKinds.Contains(selected.Kind, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Selected property {selected.Owner}.{name} has type {selected.Kind}; expected {string.Join(" or ", expectedKinds)}.");
        return true;
    }

    private void ValidateRuntimeBodies()
    {
        foreach (var reference in runtime)
        {
            if (reference.Object?.Value is not UStruct { ChildProperties: not null } body)
                throw new InvalidDataException($"Runtime property declarations are unavailable for {ObjectMetadata.Path(reference)}.");
            var headerParent = ObjectMetadata.Super(reference);
            var bodyParent = body.SuperStruct?.ResolvedObject;
            if (headerParent is null || bodyParent is null || !SameDeclaration(headerParent, bodyParent))
                throw new InvalidDataException($"Runtime superclass disagrees between header and body for {ObjectMetadata.Path(reference)}.");
        }
    }

    private static bool SameDeclaration(ResolvedObject left, ResolvedObject right)
    {
        // Already-loaded synthetic/native placeholders may have no outer path.
        if (left is ResolvedLoadedObject && right is ResolvedLoadedObject)
            return ReferenceEquals(left.Object?.Value, right.Object?.Value);
        return ObjectMetadata.Path(left).Equals(ObjectMetadata.Path(right), StringComparison.OrdinalIgnoreCase);
    }

    private void ReadAncestry(ResolvedObject declaration, TypeMappings mappings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!IsNative(declaration))
        {
            if (!seen.Add(ObjectMetadata.Path(declaration)) || ancestry.Count >= 128)
                throw new InvalidDataException("Runtime class ancestry repeats or exceeds 128 levels.");
            ancestry.Add(declaration.Name.Text);
            runtime.Add(declaration);
            declaration = ObjectMetadata.Super(declaration)
                ?? throw new InvalidDataException($"Runtime superclass metadata is missing for {ObjectMetadata.Path(declaration)}.");
        }

        // The native continuation has its own namespace. A runtime class with the
        // same short name neither replaces nor establishes a native policy class.
        var current = declaration.Name.Text;
        seen.Clear();
        while (true)
        {
            if (!seen.Add(current) || ancestry.Count >= 128)
                throw new InvalidDataException("Mapped class ancestry repeats or exceeds 128 levels.");
            ancestry.Add(current);
            if (!mappings.Types.TryGetValue(current, out var type))
                throw new InvalidDataException($"Native class ancestry has no mapping for {current}.");
            native.Add(type);
            if (type.SuperType is null)
            {
                if (!current.Equals("Object", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Mapped class ancestry ends before Object at {current}.");
                return;
            }
            current = type.SuperType;
        }
    }

    private static bool IsNative(ResolvedObject declaration)
    {
        if (declaration.ExportIndex >= 0) return false;
        if (declaration is ResolvedLoadedObject)
            return declaration.Object?.Value is UScriptClass;
        return ObjectMetadata.Path(declaration).StartsWith("/Script/", StringComparison.OrdinalIgnoreCase);
    }

    private void RequireComplete()
    {
        if (Error is not null) throw new InvalidDataException(Error);
    }
}
