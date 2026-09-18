namespace AssetIndex;

// Private input evidence. Public snapshots expose aggregate absence counts only.
internal sealed record PackageIndexRecord(IReadOnlyList<ContainerPackages> Containers,
    IReadOnlyList<IndexedPackage> Effective, IReadOnlyList<PackageImports> Imports);
internal sealed record ContainerPackages(string Path, bool Mounted, bool HasHeader,
    IReadOnlyList<string> PackageChunks, IReadOnlyList<StoredPackage> Normal,
    IReadOnlyList<StoredPackage> Optional, IReadOnlyList<PackageRedirect> Redirects,
    IReadOnlyList<string> Localized);
internal sealed record StoredPackage(string Id, IReadOnlyList<string> Imports);
internal sealed record PackageRedirect(string Source, string Target);
internal sealed record IndexedPackage(string Id, string Path, string Container);
internal sealed record PackageImports(string File, string Name, string Container, string Id,
    IReadOnlyList<string> ImportMap, IReadOnlyList<string> ExportHashes);
internal sealed record UnavailableReference(string PackageId, string? OwnerFile);
internal sealed record InputContainer(string Path, bool Mounted, int NormalPackages, int OptionalPackages,
    int PackageChunks, int Redirects, int LocalizedPackages);
