using Shorebound.Infrastructure.Persistence;

namespace Shorebound.Tests.Infrastructure.Persistence;

/// <summary>Checks that save management stays within the configured save directory.</summary>
public sealed class SaveCatalogTests
{
    [Fact]
    public void only_world_files_in_the_save_directory_are_listed()
    {
        using var directory = new TemporaryDirectory();
        var savePath = Path.Combine(directory.Path, "active.world");
        File.WriteAllText(savePath, "active");
        File.WriteAllText(Path.Combine(directory.Path, "notes.txt"), "not a save");
        var nested = Directory.CreateDirectory(Path.Combine(directory.Path, "nested"));
        File.WriteAllText(Path.Combine(nested.FullName, "nested.world"), "not in this directory");

        var saves = SaveCatalog.List(savePath);

        Assert.Equal([savePath], saves);
    }

    [Fact]
    public void a_save_can_be_renamed_without_replacing_another_world()
    {
        using var directory = new TemporaryDirectory();
        var original = Path.Combine(directory.Path, "first.world");
        var existing = Path.Combine(directory.Path, "occupied.world");
        File.WriteAllText(original, "first");
        File.WriteAllText(existing, "occupied");

        Assert.Throws<IOException>(() => SaveCatalog.Rename(original, "occupied"));

        var renamed = SaveCatalog.Rename(original, "renamed");

        Assert.False(File.Exists(original));
        Assert.Equal("first", File.ReadAllText(renamed));
        Assert.Equal("occupied", File.ReadAllText(existing));
    }

    [Fact]
    public void a_save_name_cannot_escape_its_directory()
    {
        using var directory = new TemporaryDirectory();
        var original = Path.Combine(directory.Path, "first.world");
        File.WriteAllText(original, "first");

        Assert.Throws<ArgumentException>(() => SaveCatalog.Rename(original, "../outside"));

        Assert.True(File.Exists(original));
    }

    [Fact]
    public void a_world_name_becomes_a_world_file_in_the_configured_save_directory()
    {
        using var directory = new TemporaryDirectory();
        var configuredPath = Path.Combine(directory.Path, "default.world");

        var path = SaveCatalog.PathForName(configuredPath, "Tidal Reach.world");

        Assert.Equal(Path.Combine(directory.Path, "Tidal Reach.world"), path);
    }

    [Fact]
    public void a_world_name_cannot_be_a_path_or_blank()
    {
        using var directory = new TemporaryDirectory();
        var configuredPath = Path.Combine(directory.Path, "default.world");

        Assert.Throws<ArgumentException>(() => SaveCatalog.PathForName(configuredPath, "../outside"));
        Assert.Throws<ArgumentException>(() => SaveCatalog.PathForName(configuredPath, " .world "));
    }

    [Fact]
    public void the_selected_world_can_be_deleted()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "selected.world");
        File.WriteAllText(path, "world");

        SaveCatalog.Delete(path);

        Assert.False(File.Exists(path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("shorebound-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}