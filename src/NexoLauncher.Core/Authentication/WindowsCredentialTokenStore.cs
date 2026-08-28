using System.Runtime.InteropServices;

namespace NexoLauncher.Core.Authentication;

public interface ITokenStore
{
    string? ReadRefreshToken();
    void WriteRefreshToken(string token);
    void DeleteRefreshToken();
}

public sealed class WindowsCredentialTokenStore : ITokenStore
{
    private const string Target = "NexoLauncher/MicrosoftRefreshToken";
    private const uint Generic = 1;
    private const uint PersistLocalMachine = 2;

    public string? ReadRefreshToken()
    {
        if (!CredRead(Target, Generic, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return credential.CredentialBlob == IntPtr.Zero ? null : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally { CredFree(pointer); }
    }

    public void WriteRefreshToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var bytes = checked((uint)(token.Length * 2));
        var blob = Marshal.StringToCoTaskMemUni(token);
        try
        {
            var credential = new Credential { Type = Generic, TargetName = Target, CredentialBlobSize = bytes, CredentialBlob = blob, Persist = PersistLocalMachine, UserName = "Microsoft Account" };
            if (!CredWrite(ref credential, 0)) throw new InvalidOperationException($"Windows Credential Manager rechazó el token ({Marshal.GetLastWin32Error()}).");
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }

    public void DeleteRefreshToken()
    {
        if (!CredDelete(Target, Generic, 0) && Marshal.GetLastWin32Error() != 1168)
            throw new InvalidOperationException("No se pudo eliminar la credencial guardada.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags; public uint Type; public string TargetName; public string? Comment; public long LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist; public uint AttributeCount;
        public IntPtr Attributes; public string? TargetAlias; public string UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
}
