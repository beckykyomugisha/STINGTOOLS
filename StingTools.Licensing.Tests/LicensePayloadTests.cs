using StingTools.Core.Licensing;
using Xunit;

public class LicensePayloadTests
{
    [Fact] public void Roundtrips_through_json()
    {
        var p = new LicensePayload { LicenseId = "id1", MachineCode = "AAAA-BBBB", Licensee = "T1", IssuedUnix = 100, ExpiryUnix = 200, Schema = 1 };
        var back = LicensePayload.FromJson(p.ToJson());
        Assert.Equal("AAAA-BBBB", back.MachineCode);
        Assert.Equal(200, back.ExpiryUnix);
        Assert.Equal("T1", back.Licensee);
    }
}
