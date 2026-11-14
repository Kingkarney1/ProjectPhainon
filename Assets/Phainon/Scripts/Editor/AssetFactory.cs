using System.IO;
using Phainon.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Phainon.EditorTools
{
    /// <summary>
    /// Creates (or reuses) the generated assets: config ScriptableObjects, materials, meshes,
    /// sprites and the render texture. Existing assets are loaded rather than overwritten, so values
    /// tuned in the Inspector survive a rebuild of the scene.
    /// </summary>
    public class AssetFactory
    {
        public const string LitShader = "Universal Render Pipeline/Lit";

        // ---------------------------------------------------------------- config assets

        public VehicleConfig Vehicle { get; private set; }
        public SystemsConfig Systems { get; private set; }
        public MissionProfile Mission { get; private set; }
        public GroundControlScript GroundControl { get; private set; }
        public CockpitTheme Theme { get; private set; }
        public InteractionSettings Interaction { get; private set; }
        public EnvironmentConfig Environment { get; private set; }
        public AudioLibrary Audio { get; private set; }

        // ---------------------------------------------------------------- shared art

        public Sprite Circle { get; private set; }
        public Sprite Ring { get; private set; }
        public Sprite RoundedPanel { get; private set; }
        public RenderTexture ExternalCameraTexture { get; private set; }

        /// <summary>A tileable surface finish: a near-white tint map and a matching normal map.</summary>
        public class Surface
        {
            public Texture2D tint;
            public Texture2D normal;
        }

        /// <summary>Fine powder-coat grain for panels, linings and painted metal.</summary>
        public Surface Grain { get; private set; }
        /// <summary>Directional streaks for bare and brushed metal.</summary>
        public Surface Brushed { get; private set; }
        /// <summary>Stitched padding for wall pads and seat upholstery. One tile holds four by four cells.</summary>
        public Surface Quilt { get; private set; }

        public void CreateAll()
        {
            EnsureFolder(BuildPaths.Data);
            EnsureFolder(BuildPaths.Materials);
            EnsureFolder(BuildPaths.Meshes);
            EnsureFolder(BuildPaths.Textures);
            EnsureFolder(BuildPaths.Prefabs);
            EnsureFolder(BuildPaths.Scenes);
            EnsureFolder(BuildPaths.Audio);

            Vehicle = LoadOrCreate<VehicleConfig>(BuildPaths.Data + "/VehicleConfig.asset");
            Systems = LoadOrCreate<SystemsConfig>(BuildPaths.Data + "/SystemsConfig.asset");
            Mission = LoadOrCreate<MissionProfile>(BuildPaths.Data + "/MissionProfile.asset");
            GroundControl = LoadOrCreate<GroundControlScript>(BuildPaths.Data + "/GroundControlScript.asset");
            Theme = LoadOrCreate<CockpitTheme>(BuildPaths.Data + "/CockpitTheme.asset");
            Interaction = LoadOrCreate<InteractionSettings>(BuildPaths.Data + "/InteractionSettings.asset");
            Environment = LoadOrCreate<EnvironmentConfig>(BuildPaths.Data + "/EnvironmentConfig.asset");
            Audio = LoadOrCreate<AudioLibrary>(BuildPaths.Data + "/AudioLibrary.asset");

            // The specs are defined in code. Rewriting them is what carries new defaults into assets that already exist.
            Vehicle.spec = new Phainon.Simulation.VehicleSpec();
            Systems.spec = new Phainon.Simulation.SystemsSpec();
            Mission.spec = new Phainon.Simulation.MissionSpec();
            GroundControl.spec = new Phainon.Simulation.CommsScriptSpec();
            EditorUtility.SetDirty(Vehicle);
            EditorUtility.SetDirty(Systems);
            EditorUtility.SetDirty(Mission);
            EditorUtility.SetDirty(GroundControl);

            // The cockpit raycast must see controls and the panels that can block them, nothing else.
            Interaction.raycastMask = (1 << BuildPaths.LayerCockpit) | (1 << BuildPaths.LayerInteractable);
            EditorUtility.SetDirty(Interaction);

            AssignAudioClips();

            Circle = CreateSprite("UiCircle", 128, 0, (x, y) => Disc(x, y, 0.5f, 0.5f, 0.485f, 0.012f));
            Ring = CreateSprite("UiRing", 128, 0, (x, y) => Disc(x, y, 0.5f, 0.5f, 0.485f, 0.012f) - Disc(x, y, 0.5f, 0.5f, 0.445f, 0.012f));
            RoundedPanel = CreateSprite("UiPanel", 64, 20, (x, y) => RoundedBox(x, y, 0.18f, 0.02f));

            Grain = CreateSurface("SurfaceGrain", 1.6f, (x, y) =>
                0.5f * TileNoise(x, y, 48) + 0.3f * TileNoise(x, y, 96) + 0.2f * TileNoise(x, y, 192));
            Brushed = CreateSurface("SurfaceBrushed", 1.2f, (x, y) =>
                0.6f * TileNoise(x, y, 6, 180) + 0.4f * TileNoise(x, y, 14, 90));
            Quilt = CreateSurface("SurfaceQuilt", 3.0f, (x, y) =>
                Mathf.Pow(Mathf.Abs(Mathf.Sin(x * Mathf.PI * 4f) * Mathf.Sin(y * Mathf.PI * 4f)), 0.3f) * 0.92f + 0.08f * TileNoise(x, y, 64));

            ExternalCameraTexture = LoadOrCreateRenderTexture(BuildPaths.Textures + "/ExternalCameraFeed.renderTexture");
        }

        // ---------------------------------------------------------------- materials

        /// <summary>A standard lit surface. Emission is enabled so highlights and lamps can drive it per renderer.</summary>
        public Material Lit(string name, Color color, float metallic, float smoothness, bool emissionReady = false)
        {
            return Lit(name, color, metallic, smoothness, null, 0f, emissionReady);
        }

        /// <summary>
        /// A lit surface with a tiled finish. Tiling is in repeats per metre on box geometry. Bump is
        /// the normal-map strength: around 0.2 reads as a finish, 1 reads as rough plaster.
        /// </summary>
        public Material Lit(string name, Color color, float metallic, float smoothness, Surface surface, float tilesPerMetre,
            bool emissionReady = false, float bump = 0.2f)
        {
            string path = BuildPaths.Materials + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(LitShader);
                if (shader == null) shader = Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            if (emissionReady)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (surface != null && surface.tint != null)
            {
                material.SetTexture("_BaseMap", surface.tint);
                material.SetTextureScale("_BaseMap", Vector2.one * tilesPerMetre);
                if (surface.normal != null)
                {
                    material.SetTexture("_BumpMap", surface.normal);
                    material.SetFloat("_BumpScale", bump);
                    material.EnableKeyword("_NORMALMAP");
                }
            }
            else
            {
                // Materials are reused across rebuilds, so a finish that was removed must be cleared.
                material.SetTexture("_BaseMap", null);
                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A lit surface that glows with a fixed colour, for light fixtures and markings.</summary>
        public Material Emissive(string name, Color color, Color emission)
        {
            Material material = Lit(name, color, 0f, 0.4f, true);
            material.SetColor("_EmissionColor", emission);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A material that uses one of the project's own shaders.</summary>
        public Material Custom(string name, string shaderName)
        {
            string path = BuildPaths.Materials + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("[Phainon] Shader not found: " + shaderName + ". The material '" + name + "' will render pink until it compiles.");
                shader = Shader.Find(LitShader);
            }

            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        public Mesh SaveMesh(Mesh mesh)
        {
            string path = BuildPaths.Meshes + "/" + mesh.name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // Refresh the stored mesh in place so references from the scene stay valid.
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = mesh.name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ---------------------------------------------------------------- helpers

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static RenderTexture LoadOrCreateRenderTexture(string path)
        {
            RenderTexture texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (texture != null) return texture;
            texture = new RenderTexture(640, 400, 24, RenderTextureFormat.Default)
            {
                name = "ExternalCameraFeed",
                antiAliasing = 1,
                useMipMap = false,
                filterMode = FilterMode.Bilinear
            };
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        /// <summary>Hooks up any clip in the audio folder whose file name matches a library field.</summary>
        private void AssignAudioClips()
        {
            var serialized = new SerializedObject(Audio);
            SerializedProperty property = serialized.GetIterator();
            bool enterChildren = true;
            int assigned = 0;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                SerializedProperty clip = property.FindPropertyRelative("clip");
                if (clip == null) continue;

                // Field "engineLoop" expects a file called "engine_loop.wav".
                string fileName = ToSnakeCase(property.name);
                AudioClip found = AssetDatabase.LoadAssetAtPath<AudioClip>(BuildPaths.Audio + "/" + fileName + ".wav");
                if (found == null) found = AssetDatabase.LoadAssetAtPath<AudioClip>(BuildPaths.Audio + "/" + fileName + ".ogg");
                if (found != null && clip.objectReferenceValue == null)
                {
                    clip.objectReferenceValue = found;
                    assigned++;
                }
                else if (found == null && clip.objectReferenceValue == null)
                {
                    Debug.LogWarning("[Phainon] No audio clip found for '" + property.name + "' (expected " + fileName + ".wav).");
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (assigned > 0) EditorUtility.SetDirty(Audio);
        }

        private static string ToSnakeCase(string camelCase)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < camelCase.Length; i++)
            {
                char c = camelCase[i];
                if (char.IsUpper(c) && i > 0) builder.Append('_');
                builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        // ---------------------------------------------------------------- generated sprites

        private delegate float Coverage(float x, float y);

        private static Sprite CreateSprite(string name, int size, int border, Coverage coverage)
        {
            string path = BuildPaths.Textures + "/" + name + ".png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float alpha = Mathf.Clamp01(coverage((x + 0.5f) / size, (y + 0.5f) / size));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && (importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != Vector4.one * border))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.spriteBorder = Vector4.one * border;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Writes a tileable finish from a height function over the unit square: a tint map that only
        /// darkens the base colour slightly in the low spots, and a normal map from the height slope.
        /// </summary>
        private static Surface CreateSurface(string name, float bumpStrength, Coverage height)
        {
            const int size = 256;
            string tintPath = BuildPaths.Textures + "/" + name + ".png";
            string normalPath = BuildPaths.Textures + "/" + name + "Normal.png";

            if (!File.Exists(tintPath) || !File.Exists(normalPath))
            {
                var heights = new float[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        heights[y * size + x] = Mathf.Clamp01(height(x / (float)size, y / (float)size));

                var tint = new Color32[size * size];
                var normal = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float h = heights[y * size + x];
                        byte shade = (byte)Mathf.RoundToInt(Mathf.Lerp(0.74f, 1f, h) * 255f);
                        tint[y * size + x] = new Color32(shade, shade, shade, 255);

                        // Central differences with wrap-around keep the normal map seamless.
                        float dx = heights[y * size + (x + 1) % size] - heights[y * size + (x + size - 1) % size];
                        float dy = heights[((y + 1) % size) * size + x] - heights[((y + size - 1) % size) * size + x];
                        Vector3 n = new Vector3(-dx * bumpStrength * 4f, -dy * bumpStrength * 4f, 1f).normalized;
                        normal[y * size + x] = new Color32((byte)(n.x * 127.5f + 127.5f), (byte)(n.y * 127.5f + 127.5f), (byte)(n.z * 127.5f + 127.5f), 255);
                    }
                }
                WritePng(tintPath, size, tint);
                WritePng(normalPath, size, normal);
            }

            ConfigureTiledTexture(tintPath, TextureImporterType.Default);
            ConfigureTiledTexture(normalPath, TextureImporterType.NormalMap);
            return new Surface
            {
                tint = AssetDatabase.LoadAssetAtPath<Texture2D>(tintPath),
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath)
            };
        }

        private static void WritePng(string path, int size, Color32[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ConfigureTiledTexture(string path, TextureImporterType type)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || (importer.textureType == type && importer.wrapMode == TextureWrapMode.Repeat)) return;
            importer.textureType = type;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }

        /// <summary>Smooth value noise that repeats exactly every unit, with the given number of cells per unit.</summary>
        private static float TileNoise(float x, float y, int cells) { return TileNoise(x, y, cells, cells); }

        /// <summary>As above with a different cell count on each axis, which stretches the pattern into streaks.</summary>
        private static float TileNoise(float x, float y, int cellsX, int cellsY)
        {
            x = Mathf.Repeat(x, 1f) * cellsX;
            y = Mathf.Repeat(y, 1f) * cellsY;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int x1 = (x0 + 1) % cellsX, y1 = (y0 + 1) % cellsY;
            int seed = cellsX * 31 + cellsY;
            return Mathf.Lerp(Mathf.Lerp(Lattice(x0, y0, seed), Lattice(x1, y0, seed), fx),
                              Mathf.Lerp(Lattice(x0, y1, seed), Lattice(x1, y1, seed), fx), fy);
        }

        private static float Lattice(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        private static float Disc(float x, float y, float cx, float cy, float radius, float feather)
        {
            float distance = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return Mathf.Clamp01((radius - distance) / feather);
        }

        private static float RoundedBox(float x, float y, float cornerRadius, float feather)
        {
            // Signed distance to a rounded square filling the texture.
            float qx = Mathf.Abs(x - 0.5f) - (0.5f - cornerRadius);
            float qy = Mathf.Abs(y - 0.5f) - (0.5f - cornerRadius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            float distance = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - cornerRadius;
            return Mathf.Clamp01(-distance / feather);
        }

        /// <summary>Makes sure the Earth day map is imported with settings that suit a wrapped sphere map.</summary>
        public static Texture2D PrepareEarthTexture()
        {
            var importer = AssetImporter.GetAtPath(BuildPaths.EarthDayTexture) as TextureImporter;
            if (importer == null) return null;
            if (importer.wrapModeU != TextureWrapMode.Repeat || importer.wrapModeV != TextureWrapMode.Clamp || importer.maxTextureSize < 2048)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.maxTextureSize = 2048;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(BuildPaths.EarthDayTexture);
        }
    }
}
