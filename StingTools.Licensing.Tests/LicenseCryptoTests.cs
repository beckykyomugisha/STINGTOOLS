using System.Security.Cryptography;
using StingTools.Core.Licensing;
using Xunit;

public class LicenseCryptoTests
{
    private static (string priv, string pub) Keys()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }

    [Fact] public void Valid_signature_extracts_payload()
    {
        var (priv, pub) = Keys();
        var lic = LicenseCrypto.Sign("{\"x\":1}", priv);
        Assert.Equal("{\"x\":1}", LicenseCrypto.VerifyAndExtract(lic, pub));
    }

    [Fact] public void Tampered_payload_rejected()
    {
        var (priv, pub) = Keys();
        var lic = LicenseCrypto.Sign("{\"x\":1}", priv);
        var tampered = "eyJ4IjoyfQ==" + lic.Substring(lic.IndexOf('.')); // swap payload, keep sig
        Assert.Null(LicenseCrypto.VerifyAndExtract(tampered, pub));
    }

    [Fact] public void Wrong_public_key_rejected()
    {
        var (priv, _) = Keys();
        var (_, otherPub) = Keys();
        var lic = LicenseCrypto.Sign("{\"x\":1}", priv);
        Assert.Null(LicenseCrypto.VerifyAndExtract(lic, otherPub));
    }

    [Fact] public void Garbage_rejected()
    {
        var (_, pub) = Keys();
        Assert.Null(LicenseCrypto.VerifyAndExtract("not-a-license", pub));
        Assert.Null(LicenseCrypto.VerifyAndExtract("", pub));
    }
}
