using System.Runtime.InteropServices;
using System.Text;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// Windows Credential Manager（資格情報マネージャー）を使ったSSHパスワードの保存。
/// P/Invoke（advapi32.dll）のみで完結させ、新規ライブラリ依存を追加しない。
/// </summary>
public sealed class WindowsCredentialSshStore : ISshCredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    public void SavePassword(string profileId, string password)
    {
        var target = BuildTargetName(profileId);
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var blobPtr = Marshal.AllocHGlobal(passwordBytes.Length);
        var targetNamePtr = IntPtr.Zero;
        var userNamePtr = IntPtr.Zero;

        try
        {
            Marshal.Copy(passwordBytes, 0, blobPtr, passwordBytes.Length);
            targetNamePtr = Marshal.StringToCoTaskMemUni(target);
            userNamePtr = Marshal.StringToCoTaskMemUni("ExplorerAlternative");

            var credential = new NativeMethods.CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = targetNamePtr,
                CredentialBlobSize = (uint)passwordBytes.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = userNamePtr
            };

            if (!NativeMethods.CredWrite(ref credential, 0))
            {
                throw new AppOperationException($"パスワードの保存に失敗しました。(エラーコード: {Marshal.GetLastWin32Error()})");
            }
        }
        finally
        {
            Array.Clear(passwordBytes, 0, passwordBytes.Length);
            Marshal.FreeHGlobal(blobPtr);

            if (targetNamePtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(targetNamePtr);
            }

            if (userNamePtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(userNamePtr);
            }
        }
    }

    public string? TryGetPassword(string profileId)
    {
        var target = BuildTargetName(profileId);

        if (!NativeMethods.CredRead(target, CredTypeGeneric, 0, out var credentialPtr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            NativeMethods.CredFree(credentialPtr);
        }
    }

    public void DeletePassword(string profileId)
    {
        var target = BuildTargetName(profileId);
        NativeMethods.CredDelete(target, CredTypeGeneric, 0);
    }

    private static string BuildTargetName(string profileId) => $"ExplorerAlternative:Ssh:{profileId}";

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredFree")]
        public static extern void CredFree(IntPtr cred);
    }
}
