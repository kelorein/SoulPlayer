using System.Collections.Generic;
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
        internal const float CassetteHalfThickness = 0.009f;
        internal const float InteractionFocusClearance = 0.015f;
        internal const int VisibilitySampleCount = 3;

        internal static readonly Vector3 InteractionTargetSize =
            new Vector3(0.28f, 0.14f, 0.20f);
        internal static readonly Vector3 InteractionFocusLocalOffset =
            new Vector3(0f, CassetteHalfThickness + InteractionFocusClearance, 0f);

        private readonly List<Material> _ownedMaterials = new List<Material>();

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

        internal void OwnMaterial(Material material)
        {
            if (material != null)
            {
                _ownedMaterials.Add(material);
            }
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

        private void OnDestroy()
        {
            foreach (Material material in _ownedMaterials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }
            _ownedMaterials.Clear();
        }
    }

    internal static class SoulTapeWorldPickupFactory
    {
        internal static SoulTapeWorldPickup Create(
            SoulTapeSpawnPlanEntry planned,
            ISoulTapeLog log)
        {
            Shader shader = Shader.Find("Standard") ??
                            Shader.Find("Legacy Shaders/Diffuse") ??
                            Shader.Find("Unlit/Color");
            if (shader == null)
            {
                log.Error(
                    "SoulTape world cassette could not be created because no supported placeholder shader is available.");
                return null;
            }

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

                Material body = CreateMaterial(shader, new Color(0.12f, 0.14f, 0.16f, 1f));
                Material label = CreateMaterial(shader, new Color(0.72f, 0.65f, 0.48f, 1f));
                Material reel = CreateMaterial(shader, new Color(0.33f, 0.35f, 0.38f, 1f));
                pickup.OwnMaterial(body);
                pickup.OwnMaterial(label);
                pickup.OwnMaterial(reel);

                AddPart(
                    root,
                    PrimitiveType.Cube,
                    "Cassette body",
                    Vector3.zero,
                    new Vector3(0.11f, 0.018f, 0.07f),
                    Quaternion.identity,
                    body);
                AddPart(
                    root,
                    PrimitiveType.Cube,
                    "Cassette label",
                    new Vector3(0f, 0.0095f, 0f),
                    new Vector3(0.085f, 0.002f, 0.045f),
                    Quaternion.identity,
                    label);
                AddPart(
                    root,
                    PrimitiveType.Cylinder,
                    "Left reel",
                    new Vector3(-0.025f, 0.011f, 0f),
                    new Vector3(0.018f, 0.004f, 0.018f),
                    Quaternion.identity,
                    reel);
                AddPart(
                    root,
                    PrimitiveType.Cylinder,
                    "Right reel",
                    new Vector3(0.025f, 0.011f, 0f),
                    new Vector3(0.018f, 0.004f, 0.018f),
                    Quaternion.identity,
                    reel);
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

        private static Material CreateMaterial(Shader shader, Color color)
        {
            Material material = new Material(shader);
            material.color = color;
            return material;
        }

        private static void AddPart(
            GameObject root,
            PrimitiveType primitive,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }

            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Object.Destroy(collider);
            }
        }

        private static Vector3 ToUnity(SoulTapeVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }
    }
}
