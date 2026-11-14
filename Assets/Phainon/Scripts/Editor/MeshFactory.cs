using System.Collections.Generic;
using UnityEngine;

namespace Phainon.EditorTools
{
    /// <summary>
    /// Procedural meshes used where Unity's primitives are not enough: surfaces of revolution for
    /// the capsule, nozzles and exhaust plumes, and a large ground plane.
    /// </summary>
    public static class MeshFactory
    {
        /// <summary>
        /// Revolves a profile around the Z axis. Each profile point is (radius, z). UVs run around
        /// the axis in x and along the profile in y.
        /// </summary>
        public static Mesh Lathe(string name, Vector2[] profile, int segments)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            int rings = profile.Length;
            int stride = segments + 1;
            for (int ring = 0; ring < rings; ring++)
            {
                float v = rings > 1 ? ring / (float)(rings - 1) : 0f;
                for (int s = 0; s <= segments; s++)
                {
                    float angle = s / (float)segments * Mathf.PI * 2f;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * profile[ring].x, Mathf.Sin(angle) * profile[ring].x, profile[ring].y));
                    uvs.Add(new Vector2(s / (float)segments, v));
                }
            }

            // Profiles may run in either direction along Z. The winding is chosen so that the side
            // facing away from the axis is always the front face.
            bool descending = profile[rings - 1].y < profile[0].y;
            for (int ring = 0; ring < rings - 1; ring++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = ring * stride + s;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    if (descending)
                    {
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                        triangles.Add(b); triangles.Add(c); triangles.Add(d);
                    }
                    else
                    {
                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                        triangles.Add(b); triangles.Add(d); triangles.Add(c);
                    }
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private const string BoxAssetPath = BuildPaths.Meshes + "/Boxes.asset";
        private static readonly Dictionary<Vector3, Mesh> boxes = new Dictionary<Vector3, Mesh>();
        private static Mesh boxContainer;

        /// <summary>Call once at the start of a scene build: box meshes are regenerated from scratch.</summary>
        public static void ResetBoxes()
        {
            boxes.Clear();
            boxContainer = null;
            UnityEditor.AssetDatabase.DeleteAsset(BoxAssetPath);
        }

        /// <summary>
        /// A box of the given size in metres whose UVs are also in metres. Unity's cube stretches one
        /// copy of a texture over every face whatever its size; these keep a texture's scale constant
        /// across the whole cockpit. Boxes of the same size share a mesh, and all of them are stored
        /// in one asset so prefabs can reference them.
        /// </summary>
        public static Mesh Box(Vector3 size)
        {
            Mesh mesh;
            if (boxes.TryGetValue(size, out mesh)) return mesh;

            var vertices = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var triangles = new List<int>(36);
            Vector3 half = size * 0.5f;
            Vector3[] faceNormals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };

            foreach (Vector3 n in faceNormals)
            {
                // v runs up the face and u to the right as seen from outside.
                Vector3 v = Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up;
                Vector3 u = Vector3.Cross(n, v);
                float hu = Mathf.Abs(Vector3.Dot(u, half)), hv = Mathf.Abs(Vector3.Dot(v, half));
                Vector3 centre = Vector3.Scale(n, half);
                int first = vertices.Count;
                Vector2[] corners = { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) };
                foreach (Vector2 c in corners)
                {
                    vertices.Add(centre + u * (c.x * hu) + v * (c.y * hv));
                    normals.Add(n);
                    uvs.Add(new Vector2(c.x * hu, c.y * hv));
                }
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
            }

            mesh = new Mesh { name = "Box " + size.x.ToString("0.###") + " x " + size.y.ToString("0.###") + " x " + size.z.ToString("0.###") };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            if (boxContainer == null) { UnityEditor.AssetDatabase.CreateAsset(mesh, BoxAssetPath); boxContainer = mesh; }
            else UnityEditor.AssetDatabase.AddObjectToAsset(mesh, boxContainer);
            boxes[size] = mesh;
            return mesh;
        }

        /// <summary>A flat square in the XZ plane, facing up, with sides of the given length in metres.</summary>
        public static Mesh GroundPlane(string name, float size, int divisions)
        {
            int stride = divisions + 1;
            var vertices = new Vector3[stride * stride];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[divisions * divisions * 6];

            for (int z = 0; z <= divisions; z++)
            {
                for (int x = 0; x <= divisions; x++)
                {
                    float u = x / (float)divisions;
                    float v = z / (float)divisions;
                    vertices[z * stride + x] = new Vector3((u - 0.5f) * size, 0f, (v - 0.5f) * size);
                    uvs[z * stride + x] = new Vector2(u, v);
                }
            }

            int t = 0;
            for (int z = 0; z < divisions; z++)
            {
                for (int x = 0; x < divisions; x++)
                {
                    int a = z * stride + x;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }

            var mesh = new Mesh { name = name, vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
