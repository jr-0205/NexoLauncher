# NEXA Premium · Microsoft account security model

## Objective

NEXA remains fully usable as a local/non-premium launcher. A Microsoft sign-in is optional and only enables the premium account layer: official Minecraft identity, authenticated online launches, profile appearance and skin management.

The React/WebView layer is never a credential boundary. Passwords, refresh tokens, Microsoft access tokens, Xbox tokens, XSTS tokens and Minecraft bearer tokens stay in native C# code.

## Authentication flow

1. React requests `account.signIn` through the narrow Desktop IPC router.
2. Native code invokes MSAL as a **public desktop client**.
3. Authentication opens in the **system browser**, not inside WebView2.
4. MSAL performs Authorization Code flow with PKCE and maintains its token cache.
5. The Microsoft access token is exchanged natively for Xbox Live and XSTS credentials.
6. XSTS is exchanged for a Minecraft Services access token.
7. NEXA verifies Minecraft ownership through the entitlements endpoint.
8. NEXA reads the official Minecraft profile and only returns sanitized profile metadata to React.
9. Immediately before `profiles.launch`, the native account router silently refreshes the session and creates a one-use authenticated launch identity.
10. The Minecraft runtime consumes that identity once while building `LaunchOptions`; the handoff is cleared immediately afterwards.

## Secret handling

NEXA is a public desktop client and therefore **must not contain a Microsoft client secret**. The only application identifier required by the client is the public Client ID.

Development configuration:

```powershell
$env:NEXA_MICROSOFT_CLIENT_ID="00000000-0000-0000-0000-000000000000"
```

The Client ID must belong to the NEXA Microsoft application registration and must be authorized for the Xbox/Minecraft authentication flow before production distribution.

The MSAL cache is created with `Microsoft.Identity.Client.Extensions.Msal` under `%LOCALAPPDATA%\NexoLauncher\auth`. NEXA intentionally does not enable the unprotected/plaintext cache fallback.

## IPC boundary

Allowed account methods:

- `account.status`
- `account.signIn`
- `account.signOut`
- `account.skin.upload`

React receives only:

- whether the Microsoft/Minecraft account is connected;
- whether premium mode is active;
- Minecraft UUID and profile name;
- a masked Microsoft account identifier;
- public skin/cape metadata and texture URLs.

React never receives:

- passwords;
- authorization codes;
- refresh tokens;
- Microsoft access tokens;
- Xbox/XSTS tokens;
- Minecraft bearer tokens;
- the local filesystem path selected for a skin.

## Skin upload

`account.skin.upload` opens a native Windows file picker. React supplies only the desired model (`classic` or `slim`). Native code validates the selected file before sending it to Minecraft Services:

- PNG signature and IHDR are checked;
- accepted dimensions are 64×64 and legacy 64×32;
- maximum file size is 1 MiB;
- the file is opened read-only;
- only the selected PNG stream is uploaded;
- no local path is returned to React.

## Fail-closed launch behavior

If no premium session exists, `profiles.launch` continues through the normal local launcher.

If NEXA believes a premium session exists but cannot refresh/validate it immediately before launch, the authenticated launch is blocked instead of silently launching under a different identity. The user must restore the Microsoft session or explicitly return to local mode by signing out.

## Production requirements

Before enabling the Microsoft button in a public release:

1. Register NEXA as a public desktop application in Microsoft Entra.
2. Configure the loopback redirect expected by MSAL desktop authentication.
3. Obtain the required Microsoft/Xbox/Minecraft authorization for the Client ID.
4. Test adult, child/family, missing-Xbox-profile, missing-Minecraft-license and revoked-session cases.
5. Verify installer upgrades preserve the protected MSAL cache without copying it into logs/backups.
6. Perform a release security review for token/log redaction and process-argument handling.

No production build should ever add a client secret, password collection form, embedded Microsoft login page or plaintext token cache.
