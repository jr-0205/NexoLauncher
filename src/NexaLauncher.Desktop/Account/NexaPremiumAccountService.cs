using System.Buffers.Binary;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using NexoLauncher.Core.Installation;

namespace NexaLauncher.Desktop;

internal sealed record NexaSkinSnapshot(string Id, string Url, string Variant, bool Active);
internal sealed record NexaCapeSnapshot(string Id, string Url, string Alias, bool Active);
internal sealed record NexaPremiumAccountSnapshot(
    bool Configured,
    bool SignedIn,
    bool Premium,
    string? MinecraftId,
    string? MinecraftName,
    string? MicrosoftAccount,
    IReadOnlyList<NexaSkinSnapshot> Skins,
    IReadOnlyList<NexaCapeSnapshot> Capes,
    string? ActiveSkinUrl,
    string? ActiveSkinVariant,
    string? Message);
internal sealed record NexaLaunchIdentity(string Id, string Name, string AccessToken);

/// <summary>
/// Owns every credential-bearing operation for the premium Microsoft account.
/// React only receives sanitized account/profile data; Microsoft and Minecraft tokens never cross the WebView IPC boundary.
/// </summary>
internal sealed class NexaPremiumAccountService
{
    private static readonly string[] Scopes = ["XboxLive.signin"];
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);
    private const int MaxSkinBytes = 1024 * 1024;

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string clientId;
    private readonly string cacheDirectory;
    private readonly Task initialization;

    private IPublicClientApplication? microsoft;
    private MsalCacheHelper? cacheHelper;
    private Session? current;

    public NexaPremiumAccountService(NexoPaths paths)
    {
        clientId = (Environment.GetEnvironmentVariable("NEXA_MICROSOFT_CLIENT_ID") ?? string.Empty).Trim();
        cacheDirectory = Path.Combine(paths.Root, "auth");
        initialization = InitializeAsync();
    }

    public bool IsConfigured => Guid.TryParse(clientId, out _);

    public async Task<NexaPremiumAccountSnapshot> GetSnapshotAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!IsConfigured)
                return SignedOut("NEXA necesita un Client ID público de Microsoft aprobado. Configura NEXA_MICROSOFT_CLIENT_ID.");

            try
            {
                var session = await EnsureSessionAsync(token);
                return session?.Snapshot ?? SignedOut(null);
            }
            catch (MsalUiRequiredException)
            {
                current = null;
                return SignedOut("La sesión de Microsoft requiere volver a iniciar sesión.");
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                return current?.Snapshot ?? SignedOut("No se pudo validar la sesión premium en este momento.");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<NexaPremiumAccountSnapshot> SignInAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            await initialization;
            EnsureConfigured();
            var app = microsoft!;
            var result = await app.AcquireTokenInteractive(Scopes)
                .WithUseEmbeddedWebView(false)
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync(token);

            current = await ExchangeAsync(result.AccessToken, result.Account?.Username, token);
            return current.Snapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<NexaPremiumAccountSnapshot> SignOutAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            await initialization;
            if (microsoft is not null)
            {
                foreach (var account in await microsoft.GetAccountsAsync())
                    await microsoft.RemoveAsync(account);
            }
            current = null;
            return SignedOut(null);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<NexaLaunchIdentity?> GetLaunchIdentityAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!IsConfigured) return null;
            var session = await EnsureSessionAsync(token);
            return session is null ? null : new NexaLaunchIdentity(session.Snapshot.MinecraftId!, session.Snapshot.MinecraftName!, session.MinecraftAccessToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<NexaPremiumAccountSnapshot> UploadSkinAsync(string path, string variant, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        variant = NormalizeVariant(variant);
        ValidateSkinPng(path);

        await gate.WaitAsync(token);
        try
        {
            var session = await EnsureSessionAsync(token)
                ?? throw new InvalidOperationException("Inicia sesión con Microsoft antes de cambiar la skin.");

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent(variant), "variant");
            using var file = new StreamContent(stream);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            multipart.Add(file, "file", Path.GetFileName(path));

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.minecraftservices.com/minecraft/profile/skins") { Content = multipart };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.MinecraftAccessToken);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            await EnsureSuccessAsync(response, "Minecraft rechazó el cambio de skin.", token);

            var snapshot = await FetchProfileAsync(session.MinecraftAccessToken, session.MicrosoftAccount, token);
            current = session with { Snapshot = snapshot };
            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task InitializeAsync()
    {
        if (!IsConfigured) return;
        Directory.CreateDirectory(cacheDirectory);

        microsoft = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority("https://login.microsoftonline.com/consumers")
            .WithRedirectUri("http://localhost")
            .Build();

        var storage = new StorageCreationPropertiesBuilder("msal-cache.bin", cacheDirectory).Build();
        cacheHelper = await MsalCacheHelper.CreateAsync(storage).ConfigureAwait(false);
        cacheHelper.RegisterCache(microsoft.UserTokenCache);
    }

    private async Task<Session?> EnsureSessionAsync(CancellationToken token)
    {
        await initialization;
        if (!IsConfigured || microsoft is null) return null;
        if (current is not null && current.ExpiresAt > DateTimeOffset.UtcNow.Add(RefreshSkew)) return current;

        var accounts = (await microsoft.GetAccountsAsync()).ToArray();
        if (accounts.Length == 0)
        {
            current = null;
            return null;
        }

        AuthenticationResult result;
        try
        {
            result = await microsoft.AcquireTokenSilent(Scopes, accounts[0]).ExecuteAsync(token);
        }
        catch (MsalUiRequiredException)
        {
            current = null;
            throw;
        }

        current = await ExchangeAsync(result.AccessToken, result.Account?.Username ?? accounts[0].Username, token);
        return current;
    }

    private async Task<Session> ExchangeAsync(string microsoftAccessToken, string? microsoftAccount, CancellationToken token)
    {
        var xblBody = new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoftAccessToken },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };
        using var xblResponse = await http.PostAsJsonAsync("https://user.auth.xboxlive.com/user/authenticate", xblBody, token);
        using var xbl = await ReadJsonAsync(xblResponse, "Xbox Live rechazó la autenticación.", token);
        var userToken = RequiredString(xbl.RootElement, "Token");

        var xstsBody = new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { userToken } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };
        using var xstsResponse = await http.PostAsJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", xstsBody, token);
        if (!xstsResponse.IsSuccessStatusCode)
            throw await CreateXstsExceptionAsync(xstsResponse, token);
        using var xsts = await JsonDocument.ParseAsync(await xstsResponse.Content.ReadAsStreamAsync(token), cancellationToken: token);
        var xstsToken = RequiredString(xsts.RootElement, "Token");
        var userHash = xsts.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()
            ?? throw new InvalidDataException("Xbox no devolvió el identificador de usuario esperado.");

        using var minecraftLogin = await http.PostAsJsonAsync(
            "https://api.minecraftservices.com/authentication/login_with_xbox",
            new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" },
            token);
        using var minecraftJson = await ReadMinecraftLoginAsync(minecraftLogin, token);
        var minecraftToken = RequiredString(minecraftJson.RootElement, "access_token");
        var expiresIn = minecraftJson.RootElement.TryGetProperty("expires_in", out var expiry) && expiry.TryGetInt32(out var seconds)
            ? Math.Max(300, seconds)
            : 86400;

        await EnsureEntitledAsync(minecraftToken, token);
        var snapshot = await FetchProfileAsync(minecraftToken, microsoftAccount, token);
        return new Session(minecraftToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn), microsoftAccount, snapshot);
    }

    private async Task EnsureEntitledAsync(string accessToken, CancellationToken token)
    {
        using var request = Authorized(HttpMethod.Get, "https://api.minecraftservices.com/entitlements/mcstore", accessToken);
        using var response = await http.SendAsync(request, token);
        using var json = await ReadJsonAsync(response, "No se pudo comprobar la licencia de Minecraft.", token);
        if (!json.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            throw new InvalidOperationException("Esta cuenta Microsoft no tiene una licencia válida de Minecraft: Java Edition.");
    }

    private async Task<NexaPremiumAccountSnapshot> FetchProfileAsync(string accessToken, string? microsoftAccount, CancellationToken token)
    {
        using var request = Authorized(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile", accessToken);
        using var response = await http.SendAsync(request, token);
        using var json = await ReadJsonAsync(response, "No se pudo cargar el perfil de Minecraft.", token);
        var root = json.RootElement;
        var id = RequiredString(root, "id");
        var name = RequiredString(root, "name");

        var skins = new List<NexaSkinSnapshot>();
        if (root.TryGetProperty("skins", out var skinArray) && skinArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var skin in skinArray.EnumerateArray())
            {
                var url = SanitizeTextureUrl(OptionalString(skin, "url"));
                if (url is null) continue;
                skins.Add(new NexaSkinSnapshot(
                    OptionalString(skin, "id") ?? string.Empty,
                    url,
                    (OptionalString(skin, "variant") ?? "CLASSIC").ToUpperInvariant(),
                    string.Equals(OptionalString(skin, "state"), "ACTIVE", StringComparison.OrdinalIgnoreCase)));
            }
        }

        var capes = new List<NexaCapeSnapshot>();
        if (root.TryGetProperty("capes", out var capeArray) && capeArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var cape in capeArray.EnumerateArray())
            {
                var url = SanitizeTextureUrl(OptionalString(cape, "url"));
                if (url is null) continue;
                capes.Add(new NexaCapeSnapshot(
                    OptionalString(cape, "id") ?? string.Empty,
                    url,
                    OptionalString(cape, "alias") ?? "Minecraft Cape",
                    string.Equals(OptionalString(cape, "state"), "ACTIVE", StringComparison.OrdinalIgnoreCase)));
            }
        }

        var activeSkin = skins.FirstOrDefault(skin => skin.Active) ?? skins.FirstOrDefault();
        return new NexaPremiumAccountSnapshot(
            Configured: true,
            SignedIn: true,
            Premium: true,
            MinecraftId: id,
            MinecraftName: name,
            MicrosoftAccount: MaskAccount(microsoftAccount),
            Skins: skins,
            Capes: capes,
            ActiveSkinUrl: activeSkin?.Url,
            ActiveSkinVariant: activeSkin?.Variant,
            Message: "Cuenta Microsoft verificada · Minecraft: Java Edition disponible");
    }

    private async Task<JsonDocument> ReadMinecraftLoginAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var text = await response.Content.ReadAsStringAsync(token);
            if (text.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Microsoft autenticó la cuenta, pero Minecraft Services aún no autoriza el Client ID de NEXA. El registro debe ser aprobado para acceso a Minecraft/Xbox antes de distribuir el inicio de sesión premium.");
        }
        return await ReadJsonAsync(response, "Minecraft Services rechazó la autenticación.", token);
    }

    private static async Task<Exception> CreateXstsExceptionAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
            if (json.RootElement.TryGetProperty("XErr", out var xerr) && xerr.TryGetInt64(out var code))
            {
                return code switch
                {
                    2148916233 => new InvalidOperationException("La cuenta Microsoft todavía no tiene un perfil de Xbox Live."),
                    2148916238 => new InvalidOperationException("La cuenta es infantil y necesita que la familia autorice el acceso a Xbox Live."),
                    _ => new InvalidOperationException($"Xbox XSTS rechazó la cuenta (XErr {code}).")
                };
            }
        }
        catch (JsonException) { }
        return new InvalidOperationException("Xbox XSTS rechazó la cuenta.");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, string fallback, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadErrorMessageAsync(response, token);
            throw new InvalidOperationException(message ?? fallback);
        }
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string fallback, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var message = await ReadErrorMessageAsync(response, token);
        throw new InvalidOperationException(message ?? fallback);
    }

    private static async Task<string?> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(token);
            if (string.IsNullOrWhiteSpace(text)) return null;
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            return OptionalString(root, "errorMessage") ?? OptionalString(root, "Message") ?? OptionalString(root, "error_description");
        }
        catch (JsonException) { return null; }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string RequiredString(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"La respuesta del servicio no contiene '{property}'.");

    private static string? OptionalString(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private NexaPremiumAccountSnapshot SignedOut(string? message) => new(
        Configured: IsConfigured,
        SignedIn: false,
        Premium: false,
        MinecraftId: null,
        MinecraftName: null,
        MicrosoftAccount: null,
        Skins: [],
        Capes: [],
        ActiveSkinUrl: null,
        ActiveSkinVariant: null,
        Message: message);

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Configura NEXA_MICROSOFT_CLIENT_ID con el Client ID público de una aplicación Microsoft registrada para escritorio. NEXA nunca necesita un client secret.");
    }

    private static string NormalizeVariant(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "classic" => "classic",
            "slim" => "slim",
            _ => throw new InvalidDataException("El modelo de skin debe ser classic o slim.")
        };

    private static void ValidateSkinPng(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("No se encontró la skin seleccionada.", path);
        if (info.Length <= 0 || info.Length > MaxSkinBytes)
            throw new InvalidDataException("La skin PNG debe pesar como máximo 1 MB.");

        Span<byte> header = stackalloc byte[24];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.ReadExactly(header);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!header[..8].SequenceEqual(signature) || !header.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("El archivo seleccionado no es un PNG válido.");

        var width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));
        if (width != 64 || (height != 64 && height != 32))
            throw new InvalidDataException("Minecraft Java requiere una skin de 64×64 (o 64×32 legacy).");
    }

    private static string? SanitizeTextureUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        if (!string.Equals(uri.Host, "textures.minecraft.net", StringComparison.OrdinalIgnoreCase)) return null;
        return uri.AbsoluteUri;
    }

    private static string? MaskAccount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var at = value.IndexOf('@');
        if (at <= 1) return value;
        var prefix = value[..at];
        var visible = Math.Min(2, prefix.Length);
        return prefix[..visible] + new string('•', Math.Max(2, prefix.Length - visible)) + value[at..];
    }

    private sealed record Session(string MinecraftAccessToken, DateTimeOffset ExpiresAt, string? MicrosoftAccount, NexaPremiumAccountSnapshot Snapshot);
}
