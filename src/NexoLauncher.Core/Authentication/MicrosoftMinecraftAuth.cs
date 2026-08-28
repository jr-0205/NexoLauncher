using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NexoLauncher.Core.Authentication;

public sealed record DeviceLogin(string DeviceCode, string UserCode, string VerificationUri, string Message, int ExpiresIn, int Interval);
public sealed record MinecraftAccount(string Id, string Name, string AccessToken, DateTimeOffset ExpiresAt);

public sealed class MicrosoftMinecraftAuth(HttpClient http, string clientId, ITokenStore tokenStore)
{
    private const string Scope = "XboxLive.signin offline_access";
    private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    public bool IsConfigured => Guid.TryParse(clientId, out _);

    public async Task<DeviceLogin> BeginAsync(CancellationToken token = default)
    {
        EnsureConfigured();
        using var response = await http.PostAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", Form(new() { ["client_id"] = clientId, ["scope"] = Scope }), token);
        using var json = await ReadJson(response, token);
        return new(json.RootElement.GetProperty("device_code").GetString()!, json.RootElement.GetProperty("user_code").GetString()!, json.RootElement.GetProperty("verification_uri").GetString()!, json.RootElement.GetProperty("message").GetString()!, json.RootElement.GetProperty("expires_in").GetInt32(), json.RootElement.GetProperty("interval").GetInt32());
    }

    public async Task<MinecraftAccount> CompleteAsync(DeviceLogin login, IProgress<string>? progress = null, CancellationToken token = default)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(login.ExpiresIn);
        var interval = Math.Max(5, login.Interval);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), token);
            using var response = await http.PostAsync(TokenEndpoint, Form(new() { ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["client_id"] = clientId, ["device_code"] = login.DeviceCode }), token);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
            if (response.IsSuccessStatusCode)
            {
                var refresh = json.RootElement.GetProperty("refresh_token").GetString()!;
                tokenStore.WriteRefreshToken(refresh);
                progress?.Report("Conectando con Xbox…");
                return await ExchangeAsync(json.RootElement.GetProperty("access_token").GetString()!, token);
            }
            var error = json.RootElement.TryGetProperty("error", out var value) ? value.GetString() : null;
            if (error == "authorization_pending") continue;
            if (error == "slow_down") { interval += 5; continue; }
            throw new InvalidOperationException(error switch { "authorization_declined" => "Se canceló el inicio de sesión.", "expired_token" => "El código de Microsoft expiró.", _ => "Microsoft rechazó el inicio de sesión." });
        }
        throw new TimeoutException("El código de Microsoft expiró.");
    }

    public async Task<MinecraftAccount?> RestoreAsync(CancellationToken token = default)
    {
        var refresh = tokenStore.ReadRefreshToken();
        if (string.IsNullOrWhiteSpace(refresh) || !IsConfigured) return null;
        try
        {
            using var response = await http.PostAsync(TokenEndpoint, Form(new() { ["client_id"] = clientId, ["scope"] = Scope, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token" }), token);
            using var json = await ReadJson(response, token);
            tokenStore.WriteRefreshToken(json.RootElement.GetProperty("refresh_token").GetString() ?? refresh);
            return await ExchangeAsync(json.RootElement.GetProperty("access_token").GetString()!, token);
        }
        catch { tokenStore.DeleteRefreshToken(); return null; }
    }

    public void SignOut() => tokenStore.DeleteRefreshToken();

    private async Task<MinecraftAccount> ExchangeAsync(string microsoftToken, CancellationToken token)
    {
        var xblBody = new { Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoftToken }, RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT" };
        using var xblResponse = await http.PostAsJsonAsync("https://user.auth.xboxlive.com/user/authenticate", xblBody, token);
        using var xbl = await ReadJson(xblResponse, token);
        var userToken = xbl.RootElement.GetProperty("Token").GetString()!;

        var xstsBody = new { Properties = new { SandboxId = "RETAIL", UserTokens = new[] { userToken } }, RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT" };
        using var xstsResponse = await http.PostAsJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", xstsBody, token);
        using var xsts = await ReadJson(xstsResponse, token);
        var xstsToken = xsts.RootElement.GetProperty("Token").GetString()!;
        var userHash = xsts.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()!;

        using var mcResponse = await http.PostAsJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" }, token);
        using var mc = await ReadJson(mcResponse, token);
        var accessToken = mc.RootElement.GetProperty("access_token").GetString()!;
        var expires = mc.RootElement.TryGetProperty("expires_in", out var expiry) ? expiry.GetInt32() : 86400;

        using var entitlementRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/entitlements/mcstore");
        entitlementRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var entitlementResponse = await http.SendAsync(entitlementRequest, token);
        using var entitlements = await ReadJson(entitlementResponse, token);
        if (entitlements.RootElement.GetProperty("items").GetArrayLength() == 0) throw new InvalidOperationException("Esta cuenta no posee Minecraft: Java Edition.");

        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var profileResponse = await http.SendAsync(profileRequest, token);
        using var profile = await ReadJson(profileResponse, token);
        return new(profile.RootElement.GetProperty("id").GetString()!, profile.RootElement.GetProperty("name").GetString()!, accessToken, DateTimeOffset.UtcNow.AddSeconds(expires - 60));
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);
    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response, CancellationToken token)
    {
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        if (!response.IsSuccessStatusCode)
        {
            var message = json.RootElement.TryGetProperty("errorMessage", out var minecraftError) ? minecraftError.GetString() : json.RootElement.TryGetProperty("Message", out var xboxError) ? xboxError.GetString() : response.ReasonPhrase;
            json.Dispose(); throw new InvalidOperationException(message ?? "El servicio de autenticación rechazó la solicitud.");
        }
        return json;
    }
    private void EnsureConfigured() { if (!IsConfigured) throw new InvalidOperationException("Configura NEXO_MICROSOFT_CLIENT_ID con el Client ID público registrado para Nexo Launcher."); }
}
