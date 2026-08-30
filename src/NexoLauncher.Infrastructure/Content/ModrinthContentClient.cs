using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace NexoLauncher.Infrastructure.Content;

public sealed record ContentCatalogProject(string Id, string Title, string Description, string Author, string ProjectType, string? IconUrl, long Downloads);
public sealed record CatalogInstallResult(int FilesInstalled, IReadOnlyList<string> FileNames);

public sealed class ModrinthContentClient(HttpClient http)
{
    private const string Api = "https://api.modrinth.com/v2";
    private readonly JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<ContentCatalogProject>> SearchAsync(string query, string minecraftVersion, string loader,
        string projectType, CancellationToken token = default)
    {
        var typeFacet = projectType == "datapack" ? "all_project_types" : "project_type";
        var facets = new List<string[]> { new[] { $"versions:{minecraftVersion}" }, new[] { $"{typeFacet}:{projectType}" } };
        if (projectType == "mod") facets.Add(new[] { $"categories:{loader}" });
        var url = $"{Api}/search?query={Uri.EscapeDataString(query ?? string.Empty)}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}&index=downloads&limit=30";
        using var request = CreateRequest(url);
        using var response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        return document.RootElement.GetProperty("hits").EnumerateArray().Select(hit => new ContentCatalogProject(
            hit.GetProperty("project_id").GetString()!,
            hit.GetProperty("title").GetString()!,
            hit.GetProperty("description").GetString() ?? string.Empty,
            hit.GetProperty("author").GetString() ?? string.Empty,
            projectType,
            hit.TryGetProperty("icon_url", out var icon) && icon.ValueKind == JsonValueKind.String ? icon.GetString() : null,
            hit.TryGetProperty("downloads", out var downloads) ? downloads.GetInt64() : 0)).ToArray();
    }

    public async Task<CatalogInstallResult> InstallAsync(ContentCatalogProject project, string minecraftVersion,
        string loader, string gameDirectory, CancellationToken token = default)
    {
        var installed = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        await InstallProjectAsync(project.Id, project.ProjectType, minecraftVersion, loader, gameDirectory, installed, visited, token);
        return new CatalogInstallResult(installed.Count, installed);
    }

    private async Task InstallProjectAsync(string projectId, string projectType, string minecraftVersion, string loader,
        string gameDirectory, List<string> installed, HashSet<string> visited, CancellationToken token)
    {
        if (!visited.Add(projectId)) return;
        var loaderFilter = projectType == "mod"
            ? $"&loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}"
            : string.Empty;
        var versions = JsonSerializer.Serialize(new[] { minecraftVersion });
        var url = $"{Api}/project/{Uri.EscapeDataString(projectId)}/version?game_versions={Uri.EscapeDataString(versions)}{loaderFilter}&include_changelog=false";
        using var request = CreateRequest(url);
        using var response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        var candidates = await response.Content.ReadFromJsonAsync<List<VersionDto>>(json, token) ?? [];
        var version = candidates.OrderBy(item => item.VersionType == "release" ? 0 : item.VersionType == "beta" ? 1 : 2).ThenByDescending(item => item.DatePublished).FirstOrDefault()
            ?? throw new InvalidOperationException("El proyecto no tiene una versión compatible con esta instancia.");

        foreach (var dependency in version.Dependencies.Where(item => item.DependencyType == "required" && !string.IsNullOrWhiteSpace(item.ProjectId)))
            await InstallProjectAsync(dependency.ProjectId!, "mod", minecraftVersion, loader, gameDirectory, installed, visited, token);

        var file = version.Files.FirstOrDefault(item => item.Primary) ?? version.Files.FirstOrDefault()
            ?? throw new InvalidDataException("La versión compatible no contiene archivos descargables.");
        var folder = projectType switch { "resourcepack" => "resourcepacks", "shader" => "shaderpacks", "datapack" => "datapacks", _ => "mods" };
        var destination = Path.Combine(Path.GetFullPath(gameDirectory), folder, Path.GetFileName(file.Filename));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".download";
        try
        {
            using var downloadRequest = CreateRequest(file.Url);
            using var download = await http.SendAsync(downloadRequest, HttpCompletionOption.ResponseHeadersRead, token);
            download.EnsureSuccessStatusCode();
            await using (var input = await download.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, token);
            if (file.Hashes.TryGetValue("sha512", out var expected))
            {
                await using var check = File.OpenRead(temporary);
                var actual = Convert.ToHexString(await SHA512.HashDataAsync(check, token)).ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("La descarga no superó la verificación SHA-512.");
            }
            File.Move(temporary, destination, true);
            installed.Add(file.Filename);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("NexoLauncher/0.5.2 (github.com/jr-0205/NexoLauncher)");
        return request;
    }

    private sealed record VersionDto(
        [property: global::System.Text.Json.Serialization.JsonPropertyName("version_type")] string VersionType,
        [property: global::System.Text.Json.Serialization.JsonPropertyName("date_published")] DateTimeOffset DatePublished,
        IReadOnlyList<FileDto> Files,
        IReadOnlyList<DependencyDto> Dependencies);
    private sealed record FileDto(string Url, string Filename, bool Primary, Dictionary<string, string> Hashes);
    private sealed record DependencyDto(
        [property: global::System.Text.Json.Serialization.JsonPropertyName("project_id")] string? ProjectId,
        [property: global::System.Text.Json.Serialization.JsonPropertyName("dependency_type")] string DependencyType);
}