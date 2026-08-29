using System;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using Diz.Skinning;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.Visual;
using RootMotion.FinalIK;
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
            AssertPublicInstanceProperty(typeof(Player.AbstractHandsController), "Item", typeof(Item));
            AssertPublicInstanceProperty(
                typeof(Player.AbstractHandsController),
                "WeaponRoot",
                typeof(Transform));
            AssertPublicInstanceProperty(
                typeof(Player.AbstractHandsController),
                "ControllerGameObject",
                typeof(GameObject));
            AssertPublicInstanceProperty(typeof(Player), "HealthController", typeof(IHealthController));
            AssertPublicInstanceProperty(typeof(Player), "CameraPosition", typeof(Transform));
            AssertPublicInstanceProperty(typeof(Player), "Position", typeof(Vector3));
            AssertPublicInstanceProperty(
                typeof(Player),
                "Transform",
                typeof(BifacialTransform));
            FieldInfo originalTransform = typeof(BifacialTransform).GetField(
                "Original",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(originalTransform);
            Assert.Equal(typeof(Transform), originalTransform.FieldType);
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
            Assert.NotNull(typeof(Player).GetMethod(
                "DropCurrentController",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Action), typeof(bool), typeof(Item) },
                null));
            Assert.NotNull(typeof(Player.UsableItemController).GetMethod(
                "InitializeController",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Player), typeof(WeaponPrefab) },
                null));
            Assert.NotNull(typeof(ItemFactory).GetMethod(
                "CreateItem",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string), typeof(string), typeof(UnparsedData) },
                null));
            AssertPublicInstanceProperty(typeof(ItemFactory), "NextId", typeof(MongoID));
            Assert.NotNull(typeof(ObjectsFactory).GetMethod(
                "CreateItemUsablePrefab",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Item), typeof(IPlayer) },
                null));
            Assert.NotNull(typeof(Player).GetMethod(
                "SetEmptyHands",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Callback<IEmptyHandsController>) },
                null));
            Assert.NotNull(typeof(Player).GetMethod(
                "SetInHands",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Item), typeof(Callback<IHandsController>) },
                null));
            Assert.NotNull(typeof(Player).GetMethod(
                "SetInHandsUsableItem",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Item), typeof(Callback<IUsableItemController>) },
                null));
            // The direct smethod_1<T>/Delegate8 call is compile-time pinned by
            // SoulRecorderHandsControllerTransition. Reflecting Delegate8 itself
            // is unsafe on this obfuscated build because its metadata intentionally
            // violates the CLR delegate sealed-type rule.
            Assert.NotNull(typeof(Player).GetMethod(
                "SpawnController",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Player.AbstractHandsController), typeof(Action) },
                null));
            Assert.True(typeof(SoulRecorderNativeHandsController).IsPublic);
            Assert.True(typeof(Player.UsableItemController).IsAssignableFrom(
                typeof(SoulRecorderNativeHandsController)));
            Assert.NotNull(typeof(IHealthController).GetProperty(
                "IsAlive",
                BindingFlags.Public | BindingFlags.Instance));
            Assert.NotNull(typeof(Player).GetEvent(
                "OnIPlayerDeadOrUnspawn",
                BindingFlags.Public | BindingFlags.Instance));
            AssertPublicInstanceProperty(
                typeof(Player),
                "MovementContext",
                typeof(MovementContext));
            FieldInfo ignoreDeltaMovement = typeof(MovementContext).GetField(
                "IgnoreDeltaMovement",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(ignoreDeltaMovement);
            Assert.Equal(typeof(bool), ignoreDeltaMovement.FieldType);
            Assert.NotNull(typeof(Player).GetMethod(
                "Teleport",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Vector3), typeof(bool) },
                null));
            AssertPublicInstanceProperty(
                typeof(ActiveHealthController),
                "DamageCoeff",
                typeof(float));
            AssertPublicInstanceProperty(
                typeof(ActiveHealthController),
                "FallSafeHeight",
                typeof(float));
            Assert.NotNull(typeof(ActiveHealthController).GetMethod(
                "SetDamageCoeff",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(float) },
                null));
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
            Assert.NotNull(typeof(Physics).GetMethod(
                "RaycastAll",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[]
                {
                    typeof(Ray),
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
            Assert.NotNull(typeof(Camera).GetMethod(
                "CopyFrom",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Camera) },
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

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void Spt41xExposesUsableItemDonorButNoNativeAudioRecorderController()
        {
            Type usableItemController = typeof(Player.UsableItemController);
            Type[] assemblyTypes;
            try
            {
                assemblyTypes = typeof(Player).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                assemblyTypes = ex.Types.Where(type => type != null).ToArray();
            }

            Assert.NotEmpty(assemblyTypes);
            Type[] recorderTypes = assemblyTypes
                .Where(type =>
                    type.FullName != null &&
                    (type.FullName.IndexOf("Recorder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     type.FullName.IndexOf("Dictaphone", StringComparison.OrdinalIgnoreCase) >= 0))
                .ToArray();
            Type[] audioRecorderPresentationTypes = assemblyTypes
                .Where(type =>
                    type.FullName != null &&
                    (type.FullName.IndexOf("AudioRecorder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     type.FullName.IndexOf("TapeRecorder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     type.FullName.IndexOf("Dictaphone", StringComparison.OrdinalIgnoreCase) >= 0))
                .ToArray();
            Type[] recorderControllers = recorderTypes
                .Where(type => usableItemController.IsAssignableFrom(type))
                .ToArray();

            Assert.Empty(audioRecorderPresentationTypes);
            Assert.Empty(recorderControllers);
            Assert.True(usableItemController.IsAssignableFrom(
                typeof(EFT.PortableRangeFinderController)));
            Assert.True(usableItemController.IsAssignableFrom(
                typeof(EFT.RadioTransmitterController)));
        }

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void Spt412ExposesNativeFirstPersonHandsAndLimbIkContracts()
        {
            AssertPublicInstanceProperty(typeof(Player), "PlayerBody", typeof(PlayerBody));
            AssertPublicInstanceProperty(typeof(Player), "PointOfView", typeof(EPointOfView));
            AssertPublicInstanceProperty(typeof(Player), "IsYourPlayer", typeof(bool));

            FieldInfo bodySkins = typeof(PlayerBody).GetField(
                "BodySkins", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(bodySkins);
            Assert.Equal(
                typeof(System.Collections.Generic.Dictionary<EBodyModelPart, LoddedSkin>),
                bodySkins.FieldType);
            FieldInfo skeletonHands = typeof(PlayerBody).GetField(
                "SkeletonHands", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(skeletonHands);
            Assert.Equal(typeof(Skeleton), skeletonHands.FieldType);
            AssertPublicInstanceProperty(
                typeof(PlayerBody), "MeshTransform", typeof(Transform));

            FieldInfo lods = typeof(LoddedSkin).GetField(
                "_lods", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(lods);
            Assert.Equal(typeof(AbstractSkin[]), lods.FieldType);
            AssertPublicInstanceProperty(
                typeof(AbstractSkin), "SkinnedMeshRenderer", typeof(SkinnedMeshRenderer));
            FieldInfo bones = typeof(Skeleton).GetField(
                "Bones", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(bones);

            FieldInfo solver = typeof(LimbIK).GetField(
                "solver", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(solver);
            Assert.Equal(typeof(IKSolverLimb), solver.FieldType);
            Assert.NotNull(typeof(IKSolverTrigonometric).GetField(
                "target", BindingFlags.Public | BindingFlags.Instance));
            Assert.NotNull(typeof(IKSolverLimb).GetField(
                "bendGoal", BindingFlags.Public | BindingFlags.Instance));
            Assert.NotNull(typeof(IKSolver).GetField(
                "IKPositionWeight", BindingFlags.Public | BindingFlags.Instance));
            Assert.NotNull(typeof(IKSolverTrigonometric).GetField(
                "IKRotationWeight", BindingFlags.Public | BindingFlags.Instance));
            Assert.NotNull(typeof(IKSolver).GetField(
                "OnPostUpdate", BindingFlags.Public | BindingFlags.Instance));
            Assert.NotNull(typeof(IKSolverTrigonometric).GetMethod(
                "SetChain",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Transform), typeof(Transform), typeof(Transform), typeof(Transform) },
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
