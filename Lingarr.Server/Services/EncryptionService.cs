using Lingarr.Server.Interfaces.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Lingarr.Server.Services;

public class EncryptionService : IEncryptionService
{
    private readonly IDataProtector _protector;

    public EncryptionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Lingarr.Settings");
    }

    public string Encrypt(string plaintext)
    {
        return string.IsNullOrEmpty(plaintext) ? plaintext : _protector.Protect(plaintext);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            return ciphertext;
        }

        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return ciphertext;
        }
    }
}
