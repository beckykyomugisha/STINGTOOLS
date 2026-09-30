using Microsoft.AspNetCore.DataProtection;
using Planscape.Infrastructure.Security;

namespace Planscape.API.Services;

/// <summary>Binds <see cref="ISecretCipher"/> to an ASP.NET Core <see cref="IDataProtector"/>.</summary>
public sealed class DataProtectionSecretCipher : ISecretCipher
{
    private readonly IDataProtector _protector;
    public DataProtectionSecretCipher(IDataProtector protector) => _protector = protector;
    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
