namespace NexoLauncher.Minecraft.Launching;

public static class LibraryArtifactIdentity
{
    public static string FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = Path.GetFullPath(path);
        var versionDirectory = Path.GetDirectoryName(normalized);
        var artifactDirectory = versionDirectory is null ? null : Path.GetDirectoryName(versionDirectory);
        if (versionDirectory is null || artifactDirectory is null) return normalized;

        var artifact = Path.GetFileName(artifactDirectory);
        var version = Path.GetFileName(versionDirectory);
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        var expectedPrefix = $"{artifact}-{version}";
        var classifier = fileName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
            ? fileName[expectedPrefix.Length..]
            : "-" + fileName;

        return artifactDirectory + classifier;
    }
}
