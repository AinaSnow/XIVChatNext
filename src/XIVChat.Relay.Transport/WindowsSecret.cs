using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace XIVChat.Relay.Transport;

/// <summary>DPAPI current-user scope. No plaintext or cross-user fallback.</summary>
public static class WindowsSecret {
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    public static string Protect(string secret) => "dpapi:" + Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(secret), true));
    public static string Unprotect(string value) => value.StartsWith("dpapi:", StringComparison.Ordinal)
        ? Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value[6..]), false))
        : throw new CryptographicException("Relay credentials need to be entered and saved on this Windows account.");
    private static byte[] Transform(byte[] value, bool protect) {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The endpoint secret store requires Windows.");
        var input = new Blob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        Blob output = default;
        try {
            Marshal.Copy(value, 0, input.Data, value.Length);
            var success = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new CryptographicException("Windows could not access the relay secret.", new Win32Exception(Marshal.GetLastWin32Error()));
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        } finally {
            CryptographicOperations.ZeroMemory(value);
            for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
        }
    }
}
