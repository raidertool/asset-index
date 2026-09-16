using CUE4Parse.FileProvider.Objects;

namespace AssetIndex;

internal sealed record ObjectLocation(GameFile File, int ExportIndex, string Path);
