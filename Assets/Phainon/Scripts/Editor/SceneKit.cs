using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Phainon.EditorTools
{
    public enum Axis { X, Y, Z }

    /// <summary>Small helpers for assembling scene geometry from primitives in a readable way.</summary>
    public static class SceneKit
    {
        public static GameObject Empty(string name, Transform parent, Vector3 localPosition, Vector3 localEuler = default(Vector3))
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            if (parent != null) go.layer = parent.gameObject.layer;
            return go;
        }

        /// <summary>A box with the given size in metres. Colliders are only kept where something must block a ray.</summary>
        public static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material,
            bool keepCollider = false, Vector3 localEuler = default(Vector3), bool castShadows = true)
        {
            // Built from a sized mesh, not a scaled cube, so textures are not stretched (see MeshFactory.Box).
            GameObject go = MeshObject(name, parent, localPosition, MeshFactory.Box(size), material, castShadows);
            go.transform.localRotation = Quaternion.Euler(localEuler);
            if (keepCollider) go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>A cylinder whose length runs along the chosen local axis of its parent.</summary>
        public static GameObject Cylinder(string name, Transform parent, Vector3 localPosition, float radius, float length,
            Material material, Axis axis = Axis.Y, bool castShadows = false)
        {
            GameObject go = Primitive(PrimitiveType.Cylinder, name, parent, localPosition, material, false, castShadows);
            // Unity's cylinder is 2 units tall along Y with a radius of 0.5.
            go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            if (axis == Axis.Z) go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            else if (axis == Axis.X) go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            return go;
        }

        public static GameObject Sphere(string name, Transform parent, Vector3 localPosition, float radius, Material material, bool castShadows = false)
        {
            GameObject go = Primitive(PrimitiveType.Sphere, name, parent, localPosition, material, false, castShadows);
            go.transform.localScale = Vector3.one * (radius * 2f);
            return go;
        }

        /// <summary>A flat rectangle facing the parent's -Z, the side a panel is viewed from.</summary>
        public static GameObject Quad(string name, Transform parent, Vector3 localPosition, Vector2 size, Material material)
        {
            GameObject go = Primitive(PrimitiveType.Quad, name, parent, localPosition, material, false, false);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            return go;
        }

        public static GameObject MeshObject(string name, Transform parent, Vector3 localPosition, Mesh mesh, Material material, bool castShadows = false)
        {
            GameObject go = Empty(name, parent, localPosition);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Material material,
            bool keepCollider, bool castShadows)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            if (parent != null) go.layer = parent.gameObject.layer;

            if (!keepCollider)
            {
                Collider collider = go.GetComponent<Collider>();
                if (collider != null) Object.DestroyImmediate(collider);
            }

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        /// <summary>
        /// Engraved-style panel text. The em size is given in millimetres and the box in centimetres,
        /// because the label object is scaled so that one local unit is one centimetre.
        /// </summary>
        public static TextMeshPro Label(string text, Transform parent, Vector3 localPosition, float emMillimetres, Color color,
            float widthCm = 12f, TextAlignmentOptions alignment = TextAlignmentOptions.Center, FontStyles style = FontStyles.Bold)
        {
            GameObject go = Empty("Label " + text.Replace('\n', ' '), parent, localPosition);
            go.transform.localScale = Vector3.one * 0.01f;

            var label = go.AddComponent<TextMeshPro>();
            label.text = text;
            label.fontSize = emMillimetres;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = style;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.characterSpacing = 3f;
            label.rectTransform.sizeDelta = new Vector2(widthCm, emMillimetres * 0.32f);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return label;
        }

        public static void SetLayer(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayer(child.gameObject, layer);
        }

        public static Light PointLight(string name, Transform parent, Vector3 localPosition, Color color, float intensity, float range)
        {
            GameObject go = Empty(name, parent, localPosition);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        public static Light SpotLight(string name, Transform parent, Vector3 localPosition, Vector3 localEuler, Color color,
            float intensity, float range, float angle)
        {
            GameObject go = Empty(name, parent, localPosition, localEuler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.6f;
            light.shadows = LightShadows.None;
            return light;
        }
    }
}
