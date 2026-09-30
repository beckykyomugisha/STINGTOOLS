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

public class PortableLicenseTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);

    private static (string priv, string pub) Keys()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }
    private static string Mint(string priv, string machine, long expiryUnix) =>
        LicenseCrypto.Sign(new LicensePayload { LicenseId = "id", MachineCode = machine,
            Licensee = "T", IssuedUnix = 0, ExpiryUnix = expiryUnix, Schema = 1 }.ToJson(), priv);

    [Theory]
    [InlineData("AAAA-BBBB-CCCC-DDDD-EEEE")]
    [InlineData("1234-5678-9ABC-DEF0-1234")]
    public void Portable_license_valid_on_any_machine(string machine)
    {
        var (priv, pub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, LicensePayload.AnyMachine, 2_000_000), pub, machine, Now);
        Assert.Equal(LicenseState.Valid, r.State);
        Assert.True(r.IsPortable);
    }

    [Fact] public void Portable_license_still_expires()
    {
        var (priv, pub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, LicensePayload.AnyMachine, 500_000), pub, "AAAA-BBBB-CCCC-DDDD-EEEE", Now);
        Assert.Equal(LicenseState.Expired, r.State);
    }

    [Fact] public void Portable_license_cannot_be_forged_with_another_key()
    {
        var (priv, _) = Keys();
        var (_, otherPub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, LicensePayload.AnyMachine, 2_000_000), otherPub, "AAAA-BBBB-CCCC-DDDD-EEEE", Now);
        Assert.Equal(LicenseState.BadSignature, r.State);
    }

    [Fact] public void Machine_locked_license_reports_days_left()
    {
        var (priv, pub) = Keys();
        var r = LicenseVerifier.Verify(Mint(priv, "AAAA-BBBB-CCCC-DDDD-EEEE", Now.ToUnixTimeSeconds() + 90 * 86400L),
            pub, "AAAA-BBBB-CCCC-DDDD-EEEE", Now);
        Assert.Equal(90, r.DaysLeft);
        Assert.False(r.IsPortable);
    }
}
