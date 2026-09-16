using CUE4Parse.UE4.Objects.UObject;

namespace AssetIndex;

internal sealed record SoftReferencePath(string? Target, string? Error)
{
    public static SoftReferencePath Read(FSoftObjectPath reference)
    {
        var emptyBase = reference.AssetPathName.IsNone || string.IsNullOrEmpty(reference.AssetPathName.Text);
        if (emptyBase && string.IsNullOrEmpty(reference.SubPathString)) return new(null, null);
        return new(reference.ToString(),
            emptyBase ? "Soft reference has a subobject path without an asset path." : null);
    }
}
