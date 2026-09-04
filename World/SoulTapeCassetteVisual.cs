using System.Collections.Generic;
using UnityEngine;

namespace SoulPlayer.World
{
    /// <summary>
    /// SoulPlayer-owned procedural cassette shared by raid pickups and the
    /// first-person recorder. It has no EFT assets, colliders, or physics.
    /// </summary>
    internal sealed class SoulTapeCassetteVisual : MonoBehaviour
    {
        internal static readonly Vector3 Dimensions = new Vector3(0.11f, 0.018f, 0.07f);
        internal const float HalfThickness = 0.009f;

        private readonly List<Material> _ownedMaterials = new List<Material>();

        internal Transform LeftReel { get; private set; }
        internal Transform RightReel { get; private set; }

        internal static SoulTapeCassetteVisual Create(
            Transform parent,
            Shader shader,
            string name)
        {
            if (parent == null || shader == null)
            {
                return null;
            }

            GameObject root = new GameObject(name ?? "SoulTape cassette visual");
            root.transform.SetParent(parent, false);
            SoulTapeCassetteVisual visual = root.AddComponent<SoulTapeCassetteVisual>();
            visual.Build(shader);
            return visual;
        }

        private void Build(Shader shader)
        {
            Material body = Own(new Material(shader));
            body.color = new Color(0.095f, 0.105f, 0.115f, 1f);
            Material edge = Own(new Material(shader));
            edge.color = new Color(0.18f, 0.19f, 0.20f, 1f);
            Material label = Own(new Material(shader));
            label.color = new Color(0.72f, 0.64f, 0.46f, 1f);
            Material reel = Own(new Material(shader));
            reel.color = new Color(0.38f, 0.40f, 0.42f, 1f);
            Material hub = Own(new Material(shader));
            hub.color = new Color(0.64f, 0.47f, 0.22f, 1f);

            AddPart(
                transform,
                PrimitiveType.Cube,
                "Dark plastic shell",
                Vector3.zero,
                Dimensions,
                Quaternion.identity,
                body);
            AddPart(
                transform,
                PrimitiveType.Cube,
                "Aged paper label",
                new Vector3(0f, 0.0095f, 0f),
                new Vector3(0.086f, 0.002f, 0.046f),
                Quaternion.identity,
                label);
            AddPart(
                transform,
                PrimitiveType.Cube,
                "Lower shell accent",
                new Vector3(0f, 0.0108f, -0.026f),
                new Vector3(0.094f, 0.0022f, 0.006f),
                Quaternion.identity,
                edge);

            LeftReel = AddReel(-0.026f, reel, hub);
            RightReel = AddReel(0.026f, reel, hub);
        }

        private Transform AddReel(float x, Material reel, Material hub)
        {
            Transform reelRoot = AddPart(
                transform,
                PrimitiveType.Cylinder,
                x < 0f ? "Left reel" : "Right reel",
                new Vector3(x, 0.0115f, 0f),
                new Vector3(0.018f, 0.0035f, 0.018f),
                Quaternion.identity,
                reel).transform;
            AddPart(
                reelRoot,
                PrimitiveType.Cylinder,
                "Amber reel hub",
                new Vector3(0f, 0.56f, 0f),
                new Vector3(0.47f, 0.30f, 0.47f),
                Quaternion.identity,
                hub);
            return reelRoot;
        }

        private Material Own(Material material)
        {
            _ownedMaterials.Add(material);
            return material;
        }

        internal static GameObject AddPart(
            Transform parent,
            PrimitiveType primitive,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
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
                Destroy(collider);
            }

            return part;
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

    internal static class SoulPlayerVisualShader
    {
        internal static Shader FindOpaque()
        {
            return Shader.Find("Standard") ??
                   Shader.Find("Legacy Shaders/Diffuse") ??
                   Shader.Find("Unlit/Color") ??
                   Shader.Find("Sprites/Default");
        }
    }
}
