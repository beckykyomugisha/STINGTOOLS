// DTW-174 — the extends fold must tell "this pack set lineWeightScale" from
// "this pack left it at the default 1.0". It copied any non-zero value, so a
// child's unstated default overwrote its parent's authored 0.6.

using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class PackLineWeightScaleStatedTests
    {
        [Fact]
        public void AnUnstatedScaleIsDefaultAndNotStated()
        {
            var p = JsonConvert.DeserializeObject<ViewStylePack>("{\"id\":\"child\",\"extends\":\"parent\"}");
            Assert.Equal(1.0, p.LineWeightScale);
            Assert.False(p.LineWeightScaleStated);
        }

        [Fact]
        public void AStatedScaleIsStatedEvenWhenItIsOne()
        {
            var p = JsonConvert.DeserializeObject<ViewStylePack>("{\"id\":\"p\",\"lineWeightScale\":1.0}");
            Assert.True(p.LineWeightScaleStated);
        }

        [Fact]
        public void AStatedScaleKeepsItsValue()
        {
            var p = JsonConvert.DeserializeObject<ViewStylePack>("{\"id\":\"p\",\"lineWeightScale\":0.6}");
            Assert.True(p.LineWeightScaleStated);
            Assert.Equal(0.6, p.LineWeightScale);
        }

        [Fact]
        public void TheFlagIsNotSerialised()
            => Assert.DoesNotContain("LineWeightScaleStated", JsonConvert.SerializeObject(new ViewStylePack { Id = "p" }));
    }
}
