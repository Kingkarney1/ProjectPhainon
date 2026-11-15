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
        public StoryCampaign Story { get; private set; }

        // ---------------------------------------------------------------- shared art

        public Sprite Circle { get; private set; }
        public Sprite Ring { get; private set; }
        public Sprite RoundedPanel { get; private set; }
        public Sprite Vignette { get; private set; }
        public RenderTexture ExternalCameraTexture { get; private set; }

        /// <summary>
        /// A tileable surface finish: a near-white tint map and a matching normal map. Where the finish
        /// is not evenly shiny (streaks, scratches, a weave) the tint map's alpha says how smooth each point is.
        /// </summary>
        public class Surface
        {
            public Texture2D tint;
            public Texture2D normal;
            public bool hasGloss;
            /// <summary>For a photo-scanned set: metalness in red, occlusion in green, smoothness in alpha.</summary>
            public Texture2D mask;
            public Texture2D height;
            /// <summary>For a photo-scanned set: the average colour of its colour map, so a material can be given the colour it asks for.</summary>
            public Color mean = Color.white;
            /// <summary>For a photo-scanned set: the real width one tile of the scan covers, in metres.</summary>
            public float metres = 1f;
            /// <summary>For a photo-scanned set: how much of the scan's own shine and relief is kept (1 = all of it).</summary>
            public float shine = 1f, relief = 1f;
        }

        /// <summary>Fine moulded grain for powder-coated panels and plastic caps, with a few handling scratches.</summary>
        public Surface Grain { get; private set; }
        /// <summary>Fine directional brushing for bare aluminium: the streaks also vary how it shines.</summary>
        public Surface Brushed { get; private set; }
        /// <summary>Stitched padding for seat upholstery. One tile holds four by four cells.</summary>
        public Surface Quilt { get; private set; }
        /// <summary>Sprayed paint over metal: a soft orange-peel with light scuffs.</summary>
        public Surface Paint { get; private set; }
        /// <summary>Anodised metal: a very fine satin with slight variation in the finish.</summary>
        public Surface Anodised { get; private set; }
        /// <summary>Woven composite, two over two under. The two thread directions catch the light differently.</summary>
        public Surface Weave { get; private set; }
        /// <summary>Moulded rubber with a diamond grip pattern, for knobs and hand grips. Always generated: the pattern is a design, not a scan.</summary>
        public Surface Knurl { get; private set; }
        /// <summary>Plain rubber, for seals and gaskets.</summary>
        public Surface Rubber { get; private set; }
        /// <summary>The seat covering.</summary>
        public Surface Upholstery { get; private set; }
        /// <summary>Smooth moulded plastic for button caps and guards.</summary>
        public Surface Plastic { get; private set; }

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
            Story = LoadOrCreate<StoryCampaign>(BuildPaths.Data + "/StoryCampaign.asset");
            Story.spec = Phainon.Simulation.StoryCampaignContent.Create();
            EditorUtility.SetDirty(Story);
            // The instrument palette is defined here, so a restyle reaches a theme asset that already exists.
            Theme.textPrimary = new Color(0.88f, 0.92f, 0.94f);
            Theme.textDim = new Color(0.44f, 0.52f, 0.58f);
            Theme.nominalGreen = new Color(0.38f, 0.86f, 0.56f);
            Theme.accentBlue = new Color(0.36f, 0.72f, 0.94f);
            Theme.cautionAmber = new Color(0.98f, 0.70f, 0.20f);
            Theme.warningRed = new Color(0.98f, 0.30f, 0.26f);
            Theme.screenBackground = new Color(0.008f, 0.013f, 0.020f);
            Theme.panelFill = new Color(0.026f, 0.040f, 0.054f);
            Theme.panelOutline = new Color(0.13f, 0.21f, 0.28f);
            Theme.gaugeTrack = new Color(0.07f, 0.11f, 0.15f);
            EditorUtility.SetDirty(Theme);

            // The cabin is built with the window tilt the simulation sights its instruments along.
            Environment.cabinTiltDegrees = (float)Vehicle.spec.windowTiltDegrees;
            EditorUtility.SetDirty(Environment);
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
            // Darkens towards the corners, as the glass of a real display unit does.
            Vignette = CreateSprite("UiVignette", 128, 0, (x, y) =>
                Mathf.Pow(Mathf.Clamp01((new Vector2(x - 0.5f, y - 0.5f).magnitude - 0.36f) / 0.35f), 1.6f));

            Grain = CreateSurface("SurfaceGrain", 1.6f,
                (x, y) => 0.5f * TileNoise(x, y, 48) + 0.3f * TileNoise(x, y, 96) + 0.2f * TileNoise(x, y, 192),
                (x, y) => 0.82f + 0.18f * TileNoise(x, y, 10) - 0.30f * Scratches(x, y));
            Brushed = CreateSurface("SurfaceBrushed", 1.0f,
                (x, y) => 0.45f * TileNoise(x, y, 4, 256) + 0.35f * TileNoise(x, y, 9, 128) + 0.20f * TileNoise(x, y, 20, 64),
                (x, y) => 0.62f + 0.38f * TileNoise(x, y, 3, 200) - 0.25f * Scratches(y, x));
            Quilt = CreateSurface("SurfaceQuilt", 3.0f, (x, y) =>
                Mathf.Pow(Mathf.Abs(Mathf.Sin(x * Mathf.PI * 4f) * Mathf.Sin(y * Mathf.PI * 4f)), 0.3f) * 0.92f + 0.08f * TileNoise(x, y, 64));
            Paint = CreateSurface("SurfacePaint", 0.7f,
                (x, y) => 0.55f * TileNoise(x, y, 28) + 0.30f * TileNoise(x, y, 56) + 0.15f * TileNoise(x, y, 144),
                (x, y) => 0.86f + 0.14f * TileNoise(x, y, 6) - 0.35f * Scratches(x, y));
            Anodised = CreateSurface("SurfaceAnodised", 0.5f,
                (x, y) => 0.6f * TileNoise(x, y, 170) + 0.4f * TileNoise(x, y, 250),
                (x, y) => 0.84f + 0.16f * TileNoise(x, y, 9));
            Weave = CreateSurface("SurfaceWeave", 1.4f,
                (x, y) => Twill(x, y, 32, false) * (0.9f + 0.1f * TileNoise(x, y, 128)),
                (x, y) => Twill(x, y, 32, true));
            Knurl = CreateSurface("SurfaceRubber", 1.8f,
                (x, y) => Mathf.Sqrt(Mathf.Abs(Mathf.Sin((x + y) * Mathf.PI * 24f) * Mathf.Sin((x - y) * Mathf.PI * 24f))) * 0.85f + 0.15f * TileNoise(x, y, 96),
                (x, y) => 0.80f + 0.20f * TileNoise(x, y, 12));

            // Photo-scanned sets take the place of the generated finishes wherever they are in the project.
            // The size is the largest the colour and normal maps are imported at: big for surfaces seen large and close.
            // The last number is the real width of one tile, which keeps every finish at its true scale.
            Surface generatedGrain = Grain;
            Paint = ScannedSurface("PaintedPanel", 4096, 0.6f) ?? Paint;
            // The panel coat's scan is of textured metal: laid small, dulled and flattened, and not as a metal, it reads as a matt coat that labels stay legible on.
            Grain = ScannedSurface("PowderCoat", 4096, 0.09f, 0.55f, 0.45f, false) ?? Grain;
            Brushed = ScannedSurface("BrushedAluminium", 2048, 0.4f) ?? Brushed;
            Anodised = ScannedSurface("AnodisedAluminium", 2048, 0.4f) ?? Anodised;
            Weave = ScannedSurface("CarbonComposite", 2048, 0.08f) ?? Weave;
            Rubber = ScannedSurface("Rubber", 1024, 0.75f) ?? Knurl;
            Plastic = ScannedSurface("MouldedPlastic", 1024, 0.3f, 0.6f) ?? generatedGrain;
            Upholstery = ScannedSurface("SeatLeather", 1024, 0.35f) ?? Quilt;

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
            if (surface != null && surface.mask != null)
            {
                // A scan has its own colour. Dividing by its average gives the material the colour asked for
                // while keeping the scan's variation; it can darken but never brighten past the scan itself.
                Color want = color.linear, have = surface.mean.linear;
                Color scaled = new Color(Mathf.Min(1f, want.r / Mathf.Max(0.004f, have.r)), Mathf.Min(1f, want.g / Mathf.Max(0.004f, have.g)),
                                         Mathf.Min(1f, want.b / Mathf.Max(0.004f, have.b)), 1f).gamma;
                material.SetColor("_BaseColor", scaled);
                material.SetColor("_Color", scaled);
            }
            bool scanned = surface != null && surface.mask != null;
            if (surface != null && surface.tint != null)
            {
                material.SetTexture("_BaseMap", surface.tint);
                // A scan is laid at its real size and keeps its own relief and shine; the tiling, bump and
                // smoothness asked for apply to a generated finish.
                material.SetTextureScale("_BaseMap", Vector2.one * (scanned ? 1f / surface.metres : tilesPerMetre));
                if (scanned) material.SetFloat("_Smoothness", surface.shine);
                if (surface.normal != null)
                {
                    material.SetTexture("_BumpMap", surface.normal);
                    material.SetFloat("_BumpScale", scanned ? surface.relief : bump);
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
            // Smoothness then comes from the tint map's alpha, scaled by the material's own smoothness.
            bool gloss = surface != null && surface.hasGloss;
            material.SetFloat("_SmoothnessTextureChannel", gloss ? 1f : 0f);
            if (gloss) material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            else material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");

            // A scanned set: metalness and smoothness come from its mask (the smoothness value scales it),
            // with the scan's own occlusion, and its height map for a shallow parallax on the coarser finishes.
            Texture2D mask = surface != null ? surface.mask : null;
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetTexture("_OcclusionMap", mask);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", mask != null);
            SetKeyword(material, "_OCCLUSIONMAP", mask != null);
            if (mask != null) material.SetFloat("_OcclusionStrength", 1f);
            Texture2D relief = surface != null ? surface.height : null;
            material.SetTexture("_ParallaxMap", relief);
            SetKeyword(material, "_PARALLAXMAP", relief != null);
            if (relief != null) material.SetFloat("_Parallax", 0.012f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
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
        /// The optional gloss function (0 dull to 1 as smooth as the material allows) goes in the tint map's alpha.
        /// </summary>
        private static Surface CreateSurface(string name, float bumpStrength, Coverage height, Coverage gloss = null)
        {
            // Enough for about three texels a millimetre on a panel seen from the seat at the tilings used.
            const int size = 512;
            string tintPath = BuildPaths.Textures + "/" + name + ".png";
            string normalPath = BuildPaths.Textures + "/" + name + "Normal.png";

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
                        byte shine = gloss == null ? (byte)255 : (byte)Mathf.RoundToInt(Mathf.Clamp01(gloss(x / (float)size, y / (float)size)) * 255f);
                        tint[y * size + x] = new Color32(shade, shade, shade, shine);

                        // Central differences with wrap-around keep the normal map seamless. The slope is per
                        // texel, so it is scaled to stay the same steepness whatever the texture size.
                        float dx = heights[y * size + (x + 1) % size] - heights[y * size + (x + size - 1) % size];
                        float dy = heights[((y + 1) % size) * size + x] - heights[((y + size - 1) % size) * size + x];
                        Vector3 n = new Vector3(-dx * bumpStrength * (size / 64f), -dy * bumpStrength * (size / 64f), 1f).normalized;
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
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath),
                hasGloss = gloss != null
            };
        }

        /// <summary>
        /// A photo-scanned set from Textures/Cockpit/&lt;name&gt;: colour, normal and roughness maps, with
        /// metalness, occlusion and height where the scan has them. Returns null if the set is not in
        /// the project, so the caller can fall back to a generated finish.
        /// </summary>
        private static Surface ScannedSurface(string name, int maxSize, float metres, float shine = 1f, float relief = 1f, bool metal = true)
        {
            string folder = BuildPaths.CockpitTextures + "/" + name;
            string colour = folder + "/Color.jpg", normal = folder + "/Normal.jpg", roughness = folder + "/Roughness.jpg";
            string metalness = folder + "/Metalness.jpg", occlusion = folder + "/Occlusion.jpg", height = folder + "/Height.jpg";
            string mask = folder + "/Mask.png";
            if (!File.Exists(colour) || !File.Exists(normal) || !File.Exists(roughness))
            {
                Debug.Log("[Phainon] Texture set '" + name + "' is not in the project, so a generated finish is used. Tools/fetch_textures.py downloads it.");
                return null;
            }

            // The render pipeline wants metalness, occlusion and smoothness in one texture. It is made once,
            // at half the size of the colour map: these change more slowly across a surface than colour and relief do.
            if (!File.Exists(mask) || File.GetLastWriteTimeUtc(mask) < File.GetLastWriteTimeUtc(roughness))
            {
                int size = maxSize / 2;
                Color32[] rough = LoadRaw(roughness, size), metalMap = metal ? LoadRaw(metalness, size) : null, shade = LoadRaw(occlusion, size);
                var pixels = new Color32[size * size];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(metalMap != null ? metalMap[i].r : (byte)0, shade != null ? shade[i].r : (byte)255, 0, (byte)(255 - rough[i].r));
                var packed = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                packed.SetPixels32(pixels);
                File.WriteAllBytes(mask, packed.EncodeToPNG());
                Object.DestroyImmediate(packed);
                AssetDatabase.ImportAsset(mask, ImportAssetOptions.ForceSynchronousImport);
            }

            ConfigureScan(colour, TextureImporterType.Default, true, maxSize);
            ConfigureScan(normal, TextureImporterType.NormalMap, false, maxSize);
            ConfigureScan(mask, TextureImporterType.Default, false, maxSize / 2);
            // These three only feed the mask. Imported tiny, they cost nothing in the editor and are not in a build.
            ConfigureScan(roughness, TextureImporterType.Default, false, 32);
            if (File.Exists(metalness)) ConfigureScan(metalness, TextureImporterType.Default, false, 32);
            if (File.Exists(occlusion)) ConfigureScan(occlusion, TextureImporterType.Default, false, 32);
            if (File.Exists(height)) ConfigureScan(height, TextureImporterType.Default, false, maxSize / 2);

            // The smallest mip of the colour map is its average colour.
            var probe = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            probe.LoadImage(File.ReadAllBytes(colour));
            Color mean = probe.GetPixels(probe.mipmapCount - 1)[0];
            Object.DestroyImmediate(probe);

            return new Surface
            {
                tint = AssetDatabase.LoadAssetAtPath<Texture2D>(colour),
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normal),
                mask = AssetDatabase.LoadAssetAtPath<Texture2D>(mask),
                height = File.Exists(height) ? AssetDatabase.LoadAssetAtPath<Texture2D>(height) : null,
                mean = mean,
                metres = metres, shine = shine, relief = relief
            };
        }

        /// <summary>Reads an image file's values as they are (no colour conversion), resized to a square of the given size. Null if there is no file.</summary>
        private static Color32[] LoadRaw(string path, int size)
        {
            if (!File.Exists(path)) return null;
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            source.LoadImage(File.ReadAllBytes(path));
            RenderTexture target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, target);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var copy = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Color32[] pixels = copy.GetPixels32();
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(copy);
            return pixels;
        }

        private static void ConfigureScan(string path, TextureImporterType type, bool colour, int maxSize)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) return;
            }
            if (importer.textureType == type && importer.sRGBTexture == colour && importer.maxTextureSize == maxSize
                && importer.wrapMode == TextureWrapMode.Repeat && importer.anisoLevel == 8) return;
            importer.textureType = type;
            importer.sRGBTexture = colour;
            importer.maxTextureSize = maxSize;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 8;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        /// <summary>Writes the image only if it differs from what is on disk, so an unchanged finish is not imported again.</summary>
        private static void WritePng(string path, int size, Color32[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            if (File.Exists(path) && System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(path), png)) return;
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>A few thin, long marks across the tile: 1 on a scratch, 0 elsewhere.</summary>
        private static float Scratches(float x, float y)
        {
            return Mathf.SmoothStep(0.80f, 0.90f, TileNoise(x, y, 3, 170)) * Mathf.SmoothStep(0.35f, 0.65f, TileNoise(x, y, 5, 7));
        }

        /// <summary>
        /// A two-over, two-under twill of the given number of threads per tile (a multiple of four).
        /// As a height it is the rounded back of whichever thread is on top; as a sheen it tells the two directions apart.
        /// </summary>
        private static float Twill(float x, float y, int threads, bool sheen)
        {
            float u = Mathf.Repeat(x, 1f) * threads, v = Mathf.Repeat(y, 1f) * threads;
            bool warpOnTop = (Mathf.FloorToInt(u) + Mathf.FloorToInt(v)) % 4 < 2;
            if (sheen) return warpOnTop ? 1f : 0.68f;
            float across = warpOnTop ? u - Mathf.Floor(u) : v - Mathf.Floor(v);
            return 0.35f + 0.65f * Mathf.Sqrt(Mathf.Sin(across * Mathf.PI));
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
        public static Texture2D PrepareEarthTexture() { return PrepareBodyTexture(BuildPaths.EarthDayTexture, 2048); }
        // The Cassini map is 3601 pixels wide; at 2048 half its detail was thrown away.
        public static Texture2D PrepareJupiterTexture() { return PrepareBodyTexture(BuildPaths.JupiterTexture, 4096); }

        /// <summary>Sets up an equirectangular map of a planet: wraps round in longitude, clamps at the poles.</summary>
        private static Texture2D PrepareBodyTexture(string path, int maxSize)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            if (importer.wrapModeU != TextureWrapMode.Repeat || importer.wrapModeV != TextureWrapMode.Clamp || importer.maxTextureSize != maxSize)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.maxTextureSize = maxSize;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
