using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using MailManager.Core.Interfaces;

namespace MailManager.Data.Repositories;

[SupportedOSPlatform("windows")]
public sealed class WindowsSecretProtector : ISecretProtector
{
    public byte[] Protect(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string Unprotect(byte[] value)
    {
        var bytes = ProtectedData.Unprotect(value, null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
