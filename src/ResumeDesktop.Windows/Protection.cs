using System.Runtime.InteropServices;
using ResumeDesktop.Core;

namespace ResumeDesktop.Windows;

public sealed class UserDataProtector : IDataProtector
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    public byte[] Protect(byte[] data) => Transform(data, true);
    public byte[] Unprotect(byte[] data) => Transform(data, false);
    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new Blob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            bool success = protect ? CryptProtectData(ref input, "Resume Desktop", 0, 0, 0, 1, out output) :
                CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!success) throw new System.Security.Cryptography.CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != 0) LocalFree(output.Data); }
    }
}
