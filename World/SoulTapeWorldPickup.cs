using SoulPlayer.Cassettes;
using UnityEngine;

namespace SoulPlayer.World
{
    /// <summary>
    /// Replaceable SoulPlayer-owned placeholder presentation for a world cassette.
    /// It contains no EFT assets or inventory behavior.
    /// </summary>
    internal sealed class SoulTapeWorldPickup : MonoBehaviour
    {
        internal const float CassetteHalfThickness = SoulTapeCassetteVisual.HalfThickness;
        internal const float InteractionFocusClearance = 0.015f;
        internal const int VisibilitySampleCount = 3;

        internal static readonly Vector3 InteractionTargetSize =
            new Vector3(0.28f, 0.14f, 0.20f);
        internal static readonly Vector3 InteractionFocusLocalOffset =
            new Vector3(0f, CassetteHalfThickness + InteractionFocusClearance, 0f);

        internal SoulTapeCatalogEntry Cassette { get; private set; }
        internal BoxCollider InteractionTarget { get; private set; }
        internal Vector3 InteractionFocusPoint
        {
            get { return transform.TransformPoint(InteractionFocusLocalOffset); }
        }

        internal Vector3 GetVisibilitySamplePoint(int index)
        {
            return transform.TransformPoint(GetVisibilitySampleLocalOffset(index));
        }

        internal static Vector3 GetVisibilitySampleLocalOffset(int index)
        {
            switch (index)
            {
                case 0:
                    return InteractionFocusLocalOffset;
                case 1:
                    return InteractionFocusLocalOffset + new Vector3(-0.035f, 0f, 0f);
                case 2:
                    return InteractionFocusLocalOffset + new Vector3(0.035f, 0f, 0f);
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(index));
            }
        }

        internal static string GetVisibilitySampleName(int index)
        {
            switch (index)
            {
                case 0:
                    return "top center";
                case 1:
                    return "top left";
                case 2:
                    return "top right";
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(index));
            }
        }

        internal void Initialize(SoulTapeCatalogEntry cassette)
        {
            Cassette = cassette;
        }

        internal bool CreateInteractionTarget(ISoulTapeLog log)
        {
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer < 0)
            {
                log.Error(
                    "SoulTape interaction target could not be created because Unity's Ignore Raycast layer is unavailable.");
                return false;
            }

            GameObject target = new GameObject("SoulTape Authoritative Interaction Target");
            target.layer = ignoreRaycastLayer;
            target.transform.SetParent(transform, false);
            target.transform.localPosition = new Vector3(0f, 0.035f, 0f);
            target.transform.localRotation = Quaternion.identity;
            InteractionTarget = target.AddComponent<BoxCollider>();
            InteractionTarget.size = InteractionTargetSize;
            InteractionTarget.isTrigger = true;
            return true;
        }

    }

    internal static class SoulTapeWorldPickupFactory
    {
        internal static SoulTapeWorldPickup Create(
            SoulTapeSpawnPlanEntry planned,
            ISoulTapeLog log)
        {
            GameObject root = null;
            try
            {
                root = new GameObject("SoulTape World Cassette - " + planned.Cassette.Id);
                root.transform.SetPositionAndRotation(
                    ToUnity(planned.Anchor.Position),
                    Quaternion.Euler(ToUnity(planned.Anchor.RotationEuler)));

                SoulTapeWorldPickup pickup = root.AddComponent<SoulTapeWorldPickup>();
                pickup.Initialize(planned.Cassette);
                if (!pickup.CreateInteractionTarget(log))
                {
                    Object.Destroy(root);
                    return null;
                }

                GameObject packagedVisual;
                if (Plugin.WorldCassetteVisualAssets != null &&
                    Plugin.WorldCassetteVisualAssets.TryCreate(
                        root.transform,
                        out packagedVisual))
                {
                    return pickup;
                }

                Shader shader = SoulPlayerVisualShader.FindOpaque();
                SoulTapeCassetteVisual fallback = shader == null
                    ? null
                    : SoulTapeCassetteVisual.Create(
                        root.transform,
                        shader,
                        SoulTapeWorldVisualTuning.VisualChildName);
                if (fallback == null)
                {
                    Object.Destroy(root);
                    log.Error("SoulTape world cassette visual factory returned no visual.");
                    return null;
                }
                SoulTapeWorldVisualTuning.Apply(fallback.transform);
                return pickup;
            }
            catch (System.Exception ex)
            {
                if (root != null)
                {
                    Object.Destroy(root);
                }
                log.Error(
                    "SoulTape world cassette placeholder construction failed: " + ex);
                return null;
            }
        }

        private static Vector3 ToUnity(SoulTapeVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }
    }
}
