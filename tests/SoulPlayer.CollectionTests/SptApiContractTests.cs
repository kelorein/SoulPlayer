using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using SoulPlayer.Cassettes;
using SoulPlayer.Recorder;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SptApiContractTests
    {
        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void RequiredSptProfileAndUsableItemMembersExist()
        {
            AssertPublicInstanceProperty(typeof(TarkovApplication), "Session", typeof(IEftSession));
            MethodInfo backendSession = typeof(TarkovApplication).GetMethod(
                "GetClientBackEndSession",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            Assert.NotNull(backendSession);
            Assert.Equal(typeof(IEftSession), backendSession.ReturnType);
            Assert.True(typeof(IProfileSession).IsAssignableFrom(backendSession.ReturnType));
            AssertPublicInstanceProperty(typeof(IProfileSession), "Profile", typeof(Profile));
            AssertPublicInstanceProperty(typeof(Profile), "ProfileId", typeof(string));
            AssertPublicInstanceProperty(typeof(Player), "ProfileId", typeof(string));
            AssertPublicInstanceProperty(typeof(Player), "HandsController", typeof(Player.AbstractHandsController));
            FieldInfo mainPlayer = typeof(GameWorld).GetField(
                "MainPlayer",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(mainPlayer);
            Assert.Equal(typeof(Player), mainPlayer.FieldType);

            PropertyInfo singletonInstance = typeof(Singleton<TarkovApplication>).GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(singletonInstance);
            Assert.Equal(typeof(TarkovApplication), singletonInstance.PropertyType);

            Assert.NotNull(typeof(Player.UsableItemController).GetMethod(
                "SetAim",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(bool) },
                null));
            Assert.NotNull(typeof(Player.UsableItemController).GetMethod(
                "Hide",
                BindingFlags.Public | BindingFlags.Instance));
            Assert.True(typeof(Player.UsableItemController).IsAssignableFrom(
                typeof(SoulRecorderUsableItemController)));
            Assert.True(typeof(IProfileIdProvider).IsAssignableFrom(typeof(SptProfileIdProvider)));
        }

        private static void AssertPublicInstanceProperty(
            Type type,
            string propertyName,
            Type expectedType)
        {
            PropertyInfo property = type.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            Assert.Equal(expectedType, property.PropertyType);
        }
    }
}
