using System;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.UI;
using SoulPlayer.Cassettes;
using SoulPlayer.Patches;
using SoulPlayer.Recorder;
using UnityEngine;
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
            AssertPublicInstanceProperty(typeof(Player), "CameraPosition", typeof(Transform));
            AssertPublicInstanceProperty(typeof(Player), "Position", typeof(Vector3));
            AssertPublicInstanceProperty(
                typeof(Player),
                "ActiveHealthController",
                typeof(ActiveHealthController));
            AssertPublicInstanceProperty(typeof(GameWorld), "LocationId", typeof(string));
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

            MethodBase menuShow = MenuScreenShowContract.FindTargetMethod();
            Assert.NotNull(menuShow);
            Assert.Equal(typeof(MenuScreen), menuShow.DeclaringType);
            Assert.Contains(
                menuShow.GetParameters(),
                parameter => parameter.ParameterType == typeof(Profile));
            Assert.Contains(
                menuShow.GetParameters(),
                parameter => parameter.ParameterType == typeof(ESessionMode));

            MethodInfo ensureProfile = typeof(SoulTapeCollectionController).GetMethod(
                "EnsureProfile",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(Player) },
                null);
            Assert.NotNull(ensureProfile);
            Assert.Equal(typeof(bool), ensureProfile.ReturnType);
            MethodInfo ensureProfileId = typeof(SoulTapeCollectionController).GetMethod(
                "EnsureProfileId",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);
            Assert.NotNull(ensureProfileId);
            Assert.Equal(typeof(bool), ensureProfileId.ReturnType);

            Assert.NotNull(typeof(Player).GetMethod(
                "Teleport",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Vector3), typeof(bool) },
                null));
            Assert.NotNull(typeof(MovementContext).GetField(
                "IgnoreDeltaMovement",
                BindingFlags.Public | BindingFlags.Instance));
            Assert.NotNull(typeof(ActiveHealthController).GetMethod(
                "SetDamageCoeff",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(float) },
                null));
            AssertPublicInstanceProperty(
                typeof(ActiveHealthController),
                "DamageCoeff",
                typeof(float));
            AssertPublicInstanceProperty(
                typeof(ActiveHealthController),
                "FallSafeHeight",
                typeof(float));

            Assert.NotNull(typeof(Physics).GetMethod(
                "Raycast",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[]
                {
                    typeof(Ray),
                    typeof(RaycastHit).MakeByRefType(),
                    typeof(float),
                    typeof(int),
                    typeof(QueryTriggerInteraction)
                },
                null));
            Assert.NotNull(typeof(Camera).GetMethod(
                "ViewportPointToRay",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Vector3) },
                null));
            Assert.NotNull(typeof(Camera).GetProperty(
                "main",
                BindingFlags.Public | BindingFlags.Static));
            Assert.NotNull(typeof(Camera).GetProperty(
                "allCameras",
                BindingFlags.Public | BindingFlags.Static));
            Assert.NotNull(typeof(Physics).GetMethod(
                "CheckBox",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[]
                {
                    typeof(Vector3),
                    typeof(Vector3),
                    typeof(Quaternion),
                    typeof(int),
                    typeof(QueryTriggerInteraction)
                },
                null));
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
