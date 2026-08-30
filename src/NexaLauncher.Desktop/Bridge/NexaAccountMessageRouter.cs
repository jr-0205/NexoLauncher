using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using NexoLauncher.Minecraft.Launching;

namespace NexaLauncher.Desktop;

/// <summary>
/// Narrow IPC boundary for premium account operations. Credentials, access tokens and local file paths never cross into React.
/// </summary>
internal sealed class NexaAccountMessageRouter
{
    private readonly CoreWebView2 webView;
    private readonly NexaPremiumAccountService account;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    public NexaAccountMessageRouter(CoreWebView2 webView, NexaPremiumAccountService account)
    {
        this.webView = webView;
        this.account = account;
    }

    public async Task<bool> TryHandleAsync(CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        AccountRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<AccountRequest>(eventArgs.WebMessageAsJson, json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Method)) return false;

        // Refresh the premium bearer token immediately before the existing launch bridge builds the process.
        // Returning false intentionally lets profiles.launch continue through the normal NEXA bridge afterwards.
        if (string.Equals(request.Method, "profiles.launch", StringComparison.Ordinal))
        {
            await SynchronizeLaunchIdentityAsync();
            return false;
        }

        if (!request.Method.StartsWith("account.", StringComparison.Ordinal)) return false;

        try
        {
            object result = request.Method switch
            {
                "account.status" => await StatusAsync(),
                "account.signIn" => await SignInAsync(),
                "account.signOut" => await SignOutAsync(),
                "account.skin.upload" => await UploadSkinAsync(request.Payload),
                _ => throw new NotSupportedException($"El método '{request.Method}' no está disponible en NEXA Premium.")
            };
            Post(new AccountResponse(request.Id, true, result, null));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Post(new AccountResponse(request.Id, false, null, exception.Message));
        }
        catch (OperationCanceledException)
        {
            Post(new AccountResponse(request.Id, false, null, "La operación fue cancelada."));
        }

        return true;
    }

    private async Task<NexaPremiumAccountSnapshot> StatusAsync()
    {
        var snapshot = await account.GetSnapshotAsync();
        await SynchronizeLaunchIdentityAsync();
        return snapshot;
    }

    private async Task<NexaPremiumAccountSnapshot> SignInAsync()
    {
        var snapshot = await account.SignInAsync();
        await SynchronizeLaunchIdentityAsync();
        return snapshot;
    }

    private async Task<NexaPremiumAccountSnapshot> SignOutAsync()
    {
        MinecraftAuthenticatedSession.Clear();
        return await account.SignOutAsync();
    }

    private async Task SynchronizeLaunchIdentityAsync()
    {
        var identity = await account.GetLaunchIdentityAsync();
        if (identity is null)
        {
            MinecraftAuthenticatedSession.Clear();
            return;
        }

        MinecraftAuthenticatedSession.Set(identity.Id, identity.Name, identity.AccessToken);
    }

    private async Task<NexaPremiumAccountSnapshot> UploadSkinAsync(JsonElement payload)
    {
        var request = payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? new SkinUploadRequest("classic")
            : JsonSerializer.Deserialize<SkinUploadRequest>(payload.GetRawText(), json) ?? new SkinUploadRequest("classic");

        var dialog = new OpenFileDialog
        {
            Title = "Seleccionar skin de Minecraft",
            Filter = "Skin PNG (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false,
            ValidateNames = true
        };

        if (dialog.ShowDialog() != true)
            return await account.GetSnapshotAsync();

        return await account.UploadSkinAsync(dialog.FileName, request.Variant);
    }

    private void Post(AccountResponse response)
        => webView.PostWebMessageAsJson(JsonSerializer.Serialize(response, json));

    private sealed record AccountRequest(string Id, string Method, JsonElement Payload);
    private sealed record AccountResponse(string Id, bool Ok, object? Result, string? Error);
    private sealed record SkinUploadRequest(string Variant);
}
