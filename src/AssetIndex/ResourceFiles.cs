namespace AssetIndex;

// Preserve the package directory and exported object's name, including nested outers.
internal static class ResourceFiles
{
    public static string ImagePath(string objectPath)
    {
        var path = objectPath.StartsWith('/') ? objectPath[1..] : objectPath;
        var slash = path.LastIndexOf('/');
        var separator = path.IndexOf('.', slash + 1);
        var directory = path[..(slash + 1)];
        var name = separator >= 0 ? path[(separator + 1)..] : path[(slash + 1)..];
        var relative = directory + name.Replace(':', '/').Replace('.', '/');
        if (path.Split('/').Any(segment => segment is "" or "." or "..") ||
            separator >= 0 && InvalidSegment(path[(slash + 1)..separator]) ||
            relative.Split('/').Any(InvalidSegment))
            throw new InvalidDataException($"Image resource has an unsafe output path: {objectPath}.");
        return $"images/{relative}.png";
    }

    public static bool IsImagePath(string path) => path.StartsWith("images/", StringComparison.Ordinal) &&
        path.EndsWith(".png", StringComparison.Ordinal) && !path.Split('/').Any(InvalidSegment);

    private static bool InvalidSegment(string segment)
    {
        if (segment.Length == 0 || segment.EndsWith(' ') || segment.EndsWith('.') ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Any(character => char.IsControl(character) || "<>:\"\\|?*%#".Contains(character))) return true;
        var basename = segment.Split('.')[0].ToUpperInvariant();
        return basename.Length == 0 || basename is "CON" or "PRN" or "AUX" or "NUL" ||
            basename.Length == 4 && basename[3] is >= '1' and <= '9' &&
            (basename.StartsWith("COM", StringComparison.Ordinal) || basename.StartsWith("LPT", StringComparison.Ordinal));
    }
}
