namespace Shorebound;

/// <summary>Lists and manages world files beside the configured save path.</summary>
/// <remarks>
/// Save files are self-contained binary worlds, so the catalogue only admits the game's
/// <c>.world</c> files and never reaches outside the configured save directory.
/// </remarks>
internal static class SaveCatalog
{
    private const string Extension = ".world";

    /// <summary>Returns world files in the configured save directory, newest first.</summary>
    /// <param name="savePath">The configured save path, used to find the directory.</param>
    public static string[] List(string savePath)
    {
        var directory = DirectoryFor(savePath);
        if (!Directory.Exists(directory))
            return [];

        return Directory
            .EnumerateFiles(directory, "*" + Extension, SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Renames a world file without overwriting another save.</summary>
    /// <param name="path">The existing save file.</param>
    /// <param name="name">The new file name without its extension.</param>
    /// <returns>The renamed file's full path.</returns>
    /// <exception cref="ArgumentException">The name is empty or contains invalid filename characters.</exception>
    /// <exception cref="IOException">The source is missing or the destination already exists.</exception>
    public static string Rename(string path, string name)
    {
        var destination = PathForName(path, name);
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(path);

        File.Move(path, destination, overwrite: false);
        return destination;
    }

    /// <summary>Builds a save path from a player-facing world name.</summary>
    /// <param name="savePath">A configured save path whose directory will contain the new world.</param>
    /// <param name="name">The name to use, with or without the <c>.world</c> extension.</param>
    /// <returns>The full path for the world file.</returns>
    /// <exception cref="ArgumentException">The name is empty or contains invalid filename characters.</exception>
    public static string PathForName(string savePath, string name)
    {
        var cleanName = name.Trim();
        if (cleanName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            cleanName = cleanName[..^Extension.Length].TrimEnd();

        if (
            cleanName.Length == 0
            || cleanName is "." or ".."
            || cleanName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || cleanName.Contains(Path.DirectorySeparatorChar)
            || cleanName.Contains(Path.AltDirectorySeparatorChar)
        )
            throw new ArgumentException("Enter a valid world name.", nameof(name));

        return Path.Combine(DirectoryFor(savePath), cleanName + Extension);
    }

    /// <summary>Deletes one selected world file.</summary>
    /// <param name="path">The save file to remove.</param>
    public static void Delete(string path) => File.Delete(path);

    private static string DirectoryFor(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        return string.IsNullOrEmpty(directory) ? Environment.CurrentDirectory : directory;
    }
}