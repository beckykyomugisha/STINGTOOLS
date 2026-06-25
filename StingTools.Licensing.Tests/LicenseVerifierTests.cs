using System;
using System.Security.Cryptography;
using StingTools.Core.Licensing;
using Xunit;

public class LicenseVerifierTests
{
    private const string Machine = "AAAA-BBBB-CCCC-DDDD-EEEE";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

    private static (string priv, string pub) Keys()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }
    private static string Mint(string priv, string machine, long expiryUnix) =>
        LicenseCrypto.Sign(new LicensePayload { LicenseId = "id", MachineCode = machine,
            Licensee = "T", IssuedUnix = 0, ExpiryUnix = expiryUnix, Schema = 1 }.ToJson(), priv);

    [Fact] public void Valid_license_accepted()
    {
        var (priv, pub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, Machine, 2_000_000), pub, Machine, Now);
        Assert.Equal(LicenseState.Valid, r.State);
    }
    [Fact] public void Empty_is_NoLicense()
    {
        var (_, pub) = Keys();
        Assert.Equal(LicenseState.NoLicense, LicenseVerifier.Verify("", pub, Machine, Now).State);
    }
    [Fact] public void Wrong_machine_rejected()
    {
        var (priv, pub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, "ZZZZ", 2_000_000), pub, Machine, Now);
        Assert.Equal(LicenseState.WrongMachine, r.State);
    }
    [Fact] public void Expired_rejected()
    {
        var (priv, pub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, Machine, 500_000), pub, Machine, Now);
        Assert.Equal(LicenseState.Expired, r.State);
    }
    [Fact] public void Bad_signature_rejected()
    {
        var (priv, _) = Keys();
        var (_, otherPub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, Machine, 2_000_000), otherPub, Machine, Now);
        Assert.Equal(LicenseState.BadSignature, r.State);
    }
}
