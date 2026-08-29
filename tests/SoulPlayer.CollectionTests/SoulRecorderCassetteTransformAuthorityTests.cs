using SoulPlayer.Recorder;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulRecorderCassetteTransformAuthorityTests
    {
        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void PackagedAnimatedGripDisablesProceduralTransport()
        {
            SoulRecorderCassetteTransformAuthority authority =
                new SoulRecorderCassetteTransformAuthority();

            authority.BindToGrip();

            Assert.True(authority.IsHandOwned);
            Assert.False(authority.IsRecorderOwned);
            Assert.False(authority.ProceduralTransportAllowed);
            Assert.Equal(
                SoulRecorderCassettePoseAuthority.CassetteGrip,
                authority.Current);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void RecorderOwnedCassetteHasSlotAsItsOnlyAuthority()
        {
            SoulRecorderCassetteTransformAuthority authority =
                new SoulRecorderCassetteTransformAuthority();

            authority.BindToSlot();

            Assert.False(authority.IsHandOwned);
            Assert.True(authority.IsRecorderOwned);
            Assert.False(authority.ProceduralTransportAllowed);
            Assert.Equal(
                SoulRecorderCassettePoseAuthority.CassetteSlot,
                authority.Current);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void ProceduralTransportIsAvailableOnlyToFallback()
        {
            SoulRecorderCassetteTransformAuthority authority =
                new SoulRecorderCassetteTransformAuthority();

            authority.UseProceduralFallback();

            Assert.True(authority.ProceduralTransportAllowed);
            Assert.False(authority.IsHandOwned);
            Assert.False(authority.IsRecorderOwned);
            Assert.Equal(
                SoulRecorderCassettePoseAuthority.ProceduralFallback,
                authority.Current);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void OwnershipTransitionsAlwaysReplaceThePreviousAuthority()
        {
            SoulRecorderCassetteTransformAuthority authority =
                new SoulRecorderCassetteTransformAuthority();

            authority.BindToGrip();
            Assert.Equal(SoulRecorderCassettePoseAuthority.CassetteGrip, authority.Current);
            authority.BindToSlot();
            Assert.Equal(SoulRecorderCassettePoseAuthority.CassetteSlot, authority.Current);
            authority.BindToGrip();
            Assert.Equal(SoulRecorderCassettePoseAuthority.CassetteGrip, authority.Current);

            Assert.False(authority.ProceduralTransportAllowed);
            Assert.True(authority.IsHandOwned);
            Assert.False(authority.IsRecorderOwned);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void NewInsertionRefreshesAuthorityAfterPreviousEjectCycle()
        {
            SoulRecorderCassetteTransformAuthority authority =
                new SoulRecorderCassetteTransformAuthority();

            authority.BindToGrip();
            authority.BindToSlot();
            authority.BindToGrip();
            authority.BindToGrip();

            Assert.Equal(SoulRecorderCassettePoseAuthority.CassetteGrip, authority.Current);
            Assert.Equal("Animator hierarchy / CassetteGrip", authority.WriterLabel);
        }
    }
}
