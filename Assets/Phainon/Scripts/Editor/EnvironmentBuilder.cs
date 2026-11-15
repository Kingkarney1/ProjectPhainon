using Phainon.Environment;
using UnityEngine;
using UnityEngine.Rendering;

namespace Phainon.EditorTools
{
    /// <summary>
    /// Builds everything outside the cabin: the sky dome that draws space and the Earth, the Sun
    /// light, the launch complex, the exterior of the launch vehicle as seen by the hull camera,
    /// and the ascent effects.
    /// </summary>
    public class EnvironmentBuilder
    {
        public EnvironmentDirector Director { get; private set; }
        public AscentEffects Effects { get; private set; }
        public ExternalCameraFeed CameraFeed { get; private set; }
        public Camera ExternalCamera { get; private set; }

        private readonly AssetFactory assets;
        private readonly SpacecraftController spacecraft;
        private readonly GameManager game;

        private Material white, orange, darkMetal, nozzle, concrete, steel, towerRed, building;
        private ParticleSystem padBillows, padJets;
        private Transform worldRoot, groundFrame;
        private Material smoke;

        public EnvironmentBuilder(AssetFactory assets, SpacecraftController spacecraft, GameManager game)
        {
            this.assets = assets;
            this.spacecraft = spacecraft;
            this.game = game;
        }

        public void Build()
        {
            white = assets.Lit("VehicleWhite", new Color(0.86f, 0.87f, 0.88f), 0.1f, 0.45f);
            orange = assets.Lit("VehicleInsulation", new Color(0.62f, 0.30f, 0.10f), 0.0f, 0.2f);
            darkMetal = assets.Lit("VehicleDark", new Color(0.07f, 0.07f, 0.075f), 0.4f, 0.4f);
            nozzle = assets.Lit("NozzleMetal", new Color(0.20f, 0.19f, 0.18f), 0.85f, 0.45f);
            concrete = assets.Lit("PadConcrete", new Color(0.42f, 0.42f, 0.40f), 0.0f, 0.15f, assets.Grain, 0.4f);
            steel = assets.Lit("TowerSteel", new Color(0.36f, 0.37f, 0.39f), 0.7f, 0.4f);
            towerRed = assets.Lit("TowerRed", new Color(0.50f, 0.10f, 0.07f), 0.3f, 0.35f);
            building = assets.Lit("SiteBuilding", new Color(0.62f, 0.62f, 0.60f), 0.0f, 0.2f);

            Transform environment = SceneKit.Empty("Environment", null, Vector3.zero).transform;
            environment.gameObject.layer = BuildPaths.LayerEnvironment;

            BuildSkyDome(environment);
            Light sun = BuildSun(environment);

            // Everything under the world root is counter-rotated against the vehicle's attitude.
            worldRoot = SceneKit.Empty("World (rotates with attitude)", environment, Vector3.zero).transform;
            // Fixed to the ground under the launch point; the director slides it away as the vehicle flies.
            groundFrame = SceneKit.Empty("Ground Frame", worldRoot, Vector3.zero).transform;

            // One soft alpha-blended puff material serves every smoke and vapour effect.
            smoke = assets.Custom("CloudWisp", "Phainon/SoftParticle");
            smoke.SetColor("_Tint", Color.white);
            smoke.SetFloat("_Softness", 0.88f);
            smoke.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            smoke.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

            Transform launchSite = BuildLaunchSite();

            Director = environment.gameObject.AddComponent<EnvironmentDirector>();
            Director.spacecraft = spacecraft;
            Director.config = assets.Environment;
            Director.worldRoot = worldRoot;
            Director.groundFrame = groundFrame;
            Director.launchSite = launchSite;
            Director.sun = sun;

            BuildVehicleExterior(environment);
        }

        // ---------------------------------------------------------------- sky and sun

        private void BuildSkyDome(Transform parent)
        {
            Material sky = assets.Custom("SpaceSky", "Phainon/SpaceSky");
            Texture2D earth = AssetFactory.PrepareEarthTexture();
            if (earth != null)
            {
                sky.SetTexture("_EarthDayTex", earth);
                sky.SetFloat("_HasEarthTex", 1f);
            }
            else
            {
                // The shader falls back to a procedural planet when the day map is missing.
                sky.SetFloat("_HasEarthTex", 0f);
                Debug.LogWarning("[Phainon] Earth day map not found at " + BuildPaths.EarthDayTexture + ". Using the procedural planet.");
            }
            // Jupiter has a drawn fallback too, so a missing map costs the look and nothing else.
            Texture2D jupiter = AssetFactory.PrepareJupiterTexture();
            sky.SetTexture("_JupiterTex", jupiter);
            sky.SetFloat("_HasJupiterTex", jupiter != null ? 1f : 0f);
            if (jupiter == null) Debug.LogWarning("[Phainon] Jupiter map not found at " + BuildPaths.JupiterTexture + ". Using the drawn planet.");
            UnityEditor.EditorUtility.SetDirty(sky);

            // An inside-out sphere around the cabin. It is drawn first and writes no depth.
            GameObject dome = SceneKit.Sphere("Sky Dome", parent, Vector3.zero, 1500f, sky);
            var renderer = dome.GetComponent<MeshRenderer>();
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Light BuildSun(Transform parent)
        {
            GameObject go = SceneKit.Empty("Sun", parent, Vector3.zero, new Vector3(50f, -120f, 0f));
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.95f;
            light.shadowBias = 0.03f;
            light.shadowNormalBias = 0.3f;
            RenderSettings.sun = light;
            return light;
        }

        // ---------------------------------------------------------------- launch complex

        private Transform BuildLaunchSite()
        {
            // The site's origin is the top of the launch platform, where the vehicle stands.
            // +Y is up and +Z is downrange. On the pad the cabin window looks up and somewhat downrange
            // (towards +Z), so anything meant to be seen through it stands on that side.
            float stack = assets.Environment.stackHeight;
            Transform site = SceneKit.Empty("Launch Site", groundFrame, new Vector3(0f, -stack, 0f)).transform;
            const float ground = -7f;

            Material terrainMaterial = assets.Custom("LaunchTerrain", "Phainon/LaunchTerrain");
            Mesh terrainMesh = assets.SaveMesh(MeshFactory.GroundPlane("LaunchTerrain", 8000f, 24));
            GameObject terrain = SceneKit.MeshObject("Terrain", site, new Vector3(0f, ground, 0f), terrainMesh, terrainMaterial);
            terrain.GetComponent<MeshRenderer>().receiveShadows = false;

            // Mobile launcher platform and flame deflector.
            SceneKit.Box("Launch Platform", site, new Vector3(0f, ground * 0.5f, 0f), new Vector3(34f, -ground, 30f), concrete, false, default(Vector3), false);
            SceneKit.Box("Flame Deflector", site, new Vector3(0f, ground + 0.6f, -24f), new Vector3(16f, 1.2f, 14f), concrete, false, new Vector3(-14f, 0f, 0f), false);

            // Service tower beside the vehicle, rising well above the capsule so it shows in the window.
            Transform tower = SceneKit.Empty("Service Tower", site, new Vector3(13.5f, ground, 10f)).transform;
            const float towerHeight = 110f;
            const float half = 3.6f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    SceneKit.Box("Leg", tower, new Vector3(sx * half, towerHeight * 0.5f, sz * half), new Vector3(0.7f, towerHeight, 0.7f), steel, false, default(Vector3), false);
            for (float y = 6f; y <= towerHeight; y += 6f)
            {
                SceneKit.Box("Deck", tower, new Vector3(0f, y, 0f), new Vector3(half * 2f + 0.7f, 0.35f, half * 2f + 0.7f), y % 12f < 1f ? towerRed : steel, false, default(Vector3), false);
                // Cross bracing on the two faces the crew can see.
                SceneKit.Box("Brace", tower, new Vector3(-half, y - 3f, 0f), new Vector3(0.28f, 9.4f, 0.28f), steel, false, new Vector3(50f, 0f, 0f), false);
                SceneKit.Box("Brace", tower, new Vector3(0f, y - 3f, -half), new Vector3(0.28f, 9.4f, 0.28f), steel, false, new Vector3(0f, 0f, 50f), false);
            }
            SceneKit.Box("Mast Cap", tower, new Vector3(0f, towerHeight + 0.5f, 0f), new Vector3(4.2f, 1f, 4.2f), steel, false, default(Vector3), false);
            SceneKit.Box("Lightning Mast", tower, new Vector3(0f, towerHeight + 14f, 0f), new Vector3(0.5f, 28f, 0.5f), towerRed, false, default(Vector3), false);

            // Crew access arm and white room reaching across to the capsule hatch, plus umbilical arms.
            float armHeight = stack - ground - 2.5f;
            SceneKit.Box("Crew Access Arm", tower, new Vector3(-6.5f, armHeight, 0f), new Vector3(9f, 1.6f, 2.2f), white, false, default(Vector3), false);
            SceneKit.Box("White Room", tower, new Vector3(-10.5f, armHeight, -4.4f), new Vector3(3f, 2.6f, 8.4f), white, false, default(Vector3), false);
            SceneKit.Box("Umbilical Arm", tower, new Vector3(-6.2f, 40f, -3f), new Vector3(6f, 1.2f, 1.6f), steel, false, new Vector3(0f, 25f, 0f), false);
            SceneKit.Box("Umbilical Arm", tower, new Vector3(-6.2f, 22f, -3f), new Vector3(6f, 1.2f, 1.6f), steel, false, new Vector3(0f, 25f, 0f), false);

            // Three tall lightning protection towers. Two stand downrange so their tips frame the window.
            Vector2[] masts = { new Vector2(-30f, 50f), new Vector2(38f, 46f), new Vector2(2f, -62f) };
            foreach (Vector2 at in masts)
            {
                SceneKit.Box("Lightning Tower", site, new Vector3(at.x, ground + 84f, at.y), new Vector3(1.6f, 168f, 1.6f), steel, false, default(Vector3), false);
                SceneKit.Box("Lightning Rod", site, new Vector3(at.x, ground + 178f, at.y), new Vector3(0.4f, 20f, 0.4f), towerRed, false, default(Vector3), false);
            }

            // Site buildings and tanks in the middle distance.
            SceneKit.Box("Assembly Building", site, new Vector3(-1500f, ground + 80f, 260f), new Vector3(210f, 160f, 160f), building, false, default(Vector3), false);
            SceneKit.Box("Launch Control", site, new Vector3(-1380f, ground + 14f, 60f), new Vector3(120f, 28f, 60f), building, false, default(Vector3), false);
            SceneKit.Sphere("Propellant Sphere", site, new Vector3(150f, ground + 13f, 180f), 12f, white);
            SceneKit.Sphere("Propellant Sphere", site, new Vector3(-190f, ground + 13f, -170f), 12f, white);
            SceneKit.Cylinder("Water Tower", site, new Vector3(110f, ground + 45f, -150f), 7f, 90f, white, Axis.Y);

            BuildPadCloud(site, ground);

            SceneKit.SetLayer(site.gameObject, BuildPaths.LayerEnvironment);
            return site;
        }

        // ---------------------------------------------------------------- vehicle exterior and effects

        private void BuildVehicleExterior(Transform parent)
        {
            // The stack runs aft along -Z from the cabin. It is only drawn by the hull camera.
            // Its axis is the thrust axis, which sits above the cabin's window axis by the cabin tilt.
            Transform exterior = SceneKit.Empty("Vehicle Exterior", parent, Vector3.zero, new Vector3(-assets.Environment.cabinTiltDegrees, 0f, 0f)).transform;
            exterior.gameObject.layer = BuildPaths.LayerExterior;

            Mesh capsuleMesh = assets.SaveMesh(MeshFactory.Lathe("CapsuleHull", new[]
            {
                new Vector2(0.0f, 2.6f), new Vector2(0.55f, 2.5f), new Vector2(2.45f, -1.0f), new Vector2(2.5f, -1.35f)
            }, 40));
            SceneKit.MeshObject("Capsule", exterior, Vector3.zero, capsuleMesh, white);

            // Service module: stays with the capsule. Its engine sits inside the upper stage's adapter until that is dropped.
            Mesh serviceMesh = assets.SaveMesh(MeshFactory.Lathe("ServiceModuleHull", new[]
            {
                new Vector2(2.5f, -1.35f), new Vector2(2.5f, -6.2f), new Vector2(1.0f, -6.7f)
            }, 40));
            SceneKit.MeshObject("Service Module", exterior, Vector3.zero, serviceMesh, white);
            SceneKit.Cylinder("Service Module Band", exterior, new Vector3(0f, 0f, -3.8f), 2.53f, 1.0f, darkMetal, Axis.Z);
            Mesh serviceNozzleMesh = assets.SaveMesh(MeshFactory.Lathe("ServiceNozzle", new[]
            {
                new Vector2(0.35f, -6.6f), new Vector2(0.6f, -7.2f), new Vector2(0.95f, -8.1f)
            }, 28));
            SceneKit.MeshObject("Service Module Nozzle", exterior, Vector3.zero, serviceNozzleMesh, nozzle);

            // Upper stage group: everything that falls away when it is dropped in orbit.
            Transform upperStage = SceneKit.Empty("Upper Stage", exterior, Vector3.zero).transform;
            Mesh upperMesh = assets.SaveMesh(MeshFactory.Lathe("UpperStageHull", new[]
            {
                new Vector2(2.5f, -6.2f), new Vector2(2.75f, -7.4f), new Vector2(2.75f, -21.0f), new Vector2(1.2f, -22.2f)
            }, 40));
            SceneKit.MeshObject("Upper Stage Hull", upperStage, Vector3.zero, upperMesh, white);

            Mesh upperNozzleMesh = assets.SaveMesh(MeshFactory.Lathe("UpperNozzle", new[]
            {
                new Vector2(0.45f, -21.6f), new Vector2(0.9f, -22.8f), new Vector2(1.45f, -24.4f)
            }, 28));
            SceneKit.MeshObject("Upper Stage Nozzle", upperStage, Vector3.zero, upperNozzleMesh, nozzle);

            // Booster group: everything that falls away at stage separation.
            Transform booster = SceneKit.Empty("Core Booster", exterior, Vector3.zero).transform;
            Mesh boosterMesh = assets.SaveMesh(MeshFactory.Lathe("BoosterHull", new[]
            {
                new Vector2(2.75f, -21.0f), new Vector2(4.2f, -24.5f), new Vector2(4.2f, -58.5f), new Vector2(3.6f, -60.0f)
            }, 44));
            SceneKit.MeshObject("Core", booster, Vector3.zero, boosterMesh, orange);
            SceneKit.Cylinder("Interstage Band", booster, new Vector3(0f, 0f, -25.5f), 4.23f, 1.6f, white, Axis.Z);
            SceneKit.Cylinder("Aft Skirt", booster, new Vector3(0f, 0f, -57.5f), 4.25f, 3.0f, darkMetal, Axis.Z);
            for (int side = -1; side <= 1; side += 2)
            {
                SceneKit.Cylinder("Strap-on Booster", booster, new Vector3(0f, side * 6.0f, -42.5f), 1.85f, 34f, white, Axis.Z);
                SceneKit.Cylinder("Strap-on Nozzle", booster, new Vector3(0f, side * 6.0f, -60.4f), 1.5f, 2.0f, nozzle, Axis.Z);
            }
            Mesh boosterNozzleMesh = assets.SaveMesh(MeshFactory.Lathe("BoosterNozzle", new[]
            {
                new Vector2(0.7f, -59.6f), new Vector2(1.5f, -62.0f)
            }, 24));
            foreach (Vector2 offset in new[] { new Vector2(1.9f, 1.9f), new Vector2(-1.9f, 1.9f), new Vector2(1.9f, -1.9f), new Vector2(-1.9f, -1.9f) })
                SceneKit.MeshObject("Engine Nozzle", booster, new Vector3(offset.x, offset.y, 0f), boosterNozzleMesh, nozzle);

            // Plumes: one cone mesh, scaled per engine. Each engine group has a wide outer plume and a
            // narrow, brighter core inside it.
            Material plumeMaterial = assets.Custom("Plume", "Phainon/Plume");
            Material coreMaterial = assets.Custom("PlumeCore", "Phainon/Plume");
            coreMaterial.SetColor("_CoreColor", new Color(5.0f, 4.8f, 4.4f));
            coreMaterial.SetColor("_TailColor", new Color(3.2f, 2.0f, 0.8f));
            Mesh plumeMesh = assets.SaveMesh(MeshFactory.Lathe("PlumeCone", new[]
            {
                new Vector2(0.55f, 0f), new Vector2(1.0f, -0.25f), new Vector2(0.8f, -0.6f), new Vector2(0.0f, -1f)
            }, 28));
            var boosterPlumes = new System.Collections.Generic.List<Renderer>();
            var upperPlumes = new System.Collections.Generic.List<Renderer>();
            var servicePlumes = new System.Collections.Generic.List<Renderer>();
            System.Action<System.Collections.Generic.List<Renderer>, string, Vector3, float, float> addPlume = (list, name, at, radius, length) =>
            {
                GameObject outer = SceneKit.MeshObject(name + " Plume", exterior, at, plumeMesh, plumeMaterial);
                outer.transform.localScale = new Vector3(radius, radius, length);
                GameObject core = SceneKit.MeshObject(name + " Plume Core", exterior, at, plumeMesh, coreMaterial);
                core.transform.localScale = new Vector3(radius * 0.42f, radius * 0.42f, length * 0.6f);
                list.Add(outer.GetComponent<Renderer>());
                list.Add(core.GetComponent<Renderer>());
            };
            addPlume(boosterPlumes, "Core Booster", new Vector3(0f, 0f, -61.0f), 7.5f, 95f);
            addPlume(boosterPlumes, "Strap-on A", new Vector3(0f, 6.0f, -61.4f), 3.2f, 62f);
            addPlume(boosterPlumes, "Strap-on B", new Vector3(0f, -6.0f, -61.4f), 3.2f, 62f);
            addPlume(upperPlumes, "Upper Stage", new Vector3(0f, 0f, -24.2f), 2.6f, 34f);
            addPlume(servicePlumes, "Service Module", new Vector3(0f, 0f, -8.0f), 1.1f, 14f);

            Light plumeLight = SceneKit.PointLight("Plume Light", exterior, new Vector3(0f, 0f, -66f), new Color(1f, 0.7f, 0.4f), 0f, 130f);

            SceneKit.SetLayer(exterior.gameObject, BuildPaths.LayerExterior);
            plumeLight.cullingMask = (1 << BuildPaths.LayerExterior) | (1 << BuildPaths.LayerEnvironment);

            // Window wisps: a zero-angle cone emits along its axis from a ring. The ring's inner edge
            // stays 18 m from the flight path, so wisps stream past the cabin and never through it.
            ParticleSystem wisps = Puffs("Cloud Wisps", parent, new Vector3(0f, 0f, 90f), Vector3.zero, smoke, BuildPaths.LayerEnvironment,
                new Vector2(1.0f, 1.8f), new Vector2(90f, 90f), new Vector2(8f, 18f),
                new Color(1f, 1f, 1f, 0.10f), new Color(0.92f, 0.95f, 1f, 0.26f), 160, 1f);
            SetSpace(wisps, ParticleSystemSimulationSpace.World, null);
            ParticleSystem.ShapeModule wispShape = wisps.shape;
            wispShape.shapeType = ParticleSystemShapeType.Cone;
            wispShape.angle = 0f;
            wispShape.radius = 36f;
            wispShape.radiusThickness = 0.5f;

            // Vapour collars: rings of condensation at the two places the hull widens, swept aft.
            ParticleSystem capsuleCollar = BuildCollar(exterior, "Capsule Vapour Collar", -1.0f, 2.7f);
            ParticleSystem interstageCollar = BuildCollar(exterior, "Interstage Vapour Collar", -23.5f, 4.4f);

            // Ascent trail: puffs are simulated in the ground frame, so they stay where they were laid.
            ParticleSystem trail = Puffs("Ascent Trail", parent, Vector3.zero, Vector3.zero, smoke, BuildPaths.LayerEnvironment,
                new Vector2(16f, 20f), new Vector2(0f, 3f), new Vector2(14f, 26f),
                new Color(1f, 1f, 1f, 0.34f), new Color(0.86f, 0.86f, 0.88f, 0.5f), 700, 3.2f);
            SetSpace(trail, ParticleSystemSimulationSpace.Custom, groundFrame);
            ParticleSystem.ShapeModule trailShape = trail.shape;
            trailShape.shapeType = ParticleSystemShapeType.Sphere;
            trailShape.radius = 3f;
            AddTurbulence(trail, 1.4f, 0.05f);

            // Stage separation: a bright flash and a puff of gas at the joint.
            Material flashMaterial = assets.Custom("SeparationFlash", "Phainon/SoftParticle");
            flashMaterial.SetColor("_Tint", new Color(2.4f, 1.9f, 1.3f, 1f));
            flashMaterial.SetFloat("_Softness", 0.9f);
            flashMaterial.SetFloat("_SrcBlend", (float)BlendMode.One);
            flashMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
            ParticleSystem flash = BuildBurst(exterior, "Separation Flash", flashMaterial, new Vector2(0.5f, 1.4f), new Vector2(4f, 16f), new Vector2(2.5f, 7f),
                new Color(1f, 0.95f, 0.85f, 0.9f), new Color(1f, 0.7f, 0.4f, 0.7f), 46, 1f);
            ParticleSystem gas = BuildBurst(exterior, "Separation Gas", smoke, new Vector2(1.6f, 3.2f), new Vector2(6f, 20f), new Vector2(3f, 8f),
                new Color(1f, 1f, 1f, 0.45f), new Color(0.8f, 0.8f, 0.82f, 0.6f), 34, 2.6f);

            Effects = parent.gameObject.AddComponent<AscentEffects>();
            Effects.spacecraft = spacecraft;
            Effects.environment = Director;
            Effects.config = assets.Environment;
            Effects.vehicle = exterior;
            Effects.boosterPlumes = boosterPlumes.ToArray();
            Effects.upperPlumes = upperPlumes.ToArray();
            Effects.servicePlumes = servicePlumes.ToArray();
            Effects.upperStage = upperStage;
            Effects.plumeLight = plumeLight;
            Effects.padBillows = padBillows;
            Effects.padJets = padJets;
            Effects.cloudWisps = wisps;
            Effects.vapourCollars = new[] { capsuleCollar, interstageCollar };
            Effects.trail = trail;
            Effects.booster = booster;
            Effects.separationFlash = flash;
            Effects.separationGas = gas;

            BuildExternalCamera(exterior);
        }

        // ---------------------------------------------------------------- particle helpers

        /// <summary>
        /// A soft-puff particle system with the settings every effect here shares: random rotation,
        /// growth over life, fade in and out, no shadows, and emission left at zero for the runtime
        /// script to drive. Callers then set the shape and anything specific to the effect.
        /// </summary>
        private static ParticleSystem Puffs(string name, Transform parent, Vector3 position, Vector3 euler, Material material, int layer,
            Vector2 lifetime, Vector2 speed, Vector2 size, Color colorA, Color colorB, int maxParticles, float growth)
        {
            GameObject go = SceneKit.Empty(name, parent, position, euler);
            go.layer = layer;
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = maxParticles;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;

            if (growth > 1f)
            {
                ParticleSystem.SizeOverLifetimeModule grow = system.sizeOverLifetime;
                grow.enabled = true;
                grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.45f, 1f, growth));
            }

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private static void SetSpace(ParticleSystem system, ParticleSystemSimulationSpace space, Transform custom)
        {
            ParticleSystem.MainModule main = system.main;
            main.simulationSpace = space;
            if (custom != null) main.customSimulationSpace = custom;
        }

        /// <summary>Slow curl in the motion, so smoke rolls instead of sliding in straight lines.</summary>
        private static void AddTurbulence(ParticleSystem system, float strength, float frequency)
        {
            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = 0.3f;
        }

        /// <summary>
        /// The cloud that rolls across the pad at ignition, in two layers: fast jets driven out along
        /// the flame trench in both directions, and slower billows that spread and rise. Both live in
        /// the launch site's own space, so they stay on the ground as the vehicle climbs away.
        /// </summary>
        private void BuildPadCloud(Transform site, float ground)
        {
            // Billows: laid flat, because a circle emitter throws particles outward in its own plane.
            padBillows = Puffs("Pad Billows", site, new Vector3(0f, ground + 3f, 0f), new Vector3(90f, 0f, 0f), smoke, BuildPaths.LayerEnvironment,
                new Vector2(6f, 10f), new Vector2(10f, 30f), new Vector2(16f, 34f),
                new Color(0.96f, 0.95f, 0.93f, 0.50f), new Color(0.72f, 0.70f, 0.67f, 0.65f), 360, 2.4f);
            ParticleSystem.ShapeModule billowShape = padBillows.shape;
            billowShape.shapeType = ParticleSystemShapeType.Circle;
            billowShape.radius = 9f;
            Slow(padBillows, 5f);
            // The emitter is rotated, so its local -Z is up: the cloud lifts as it spreads.
            ParticleSystem.ForceOverLifetimeModule lift = padBillows.forceOverLifetime;
            lift.enabled = true;
            lift.space = ParticleSystemSimulationSpace.Local;
            lift.z = -2.2f;
            AddTurbulence(padBillows, 4f, 0.08f);

            // Jets: the flame trench runs along the site's Z axis, so exhaust leaves both ends of it.
            padJets = Puffs("Trench Jets", site, new Vector3(0f, ground + 2.5f, 0f), Vector3.zero, smoke, BuildPaths.LayerEnvironment,
                new Vector2(3f, 5.5f), new Vector2(1f, 5f), new Vector2(9f, 18f),
                new Color(0.90f, 0.88f, 0.84f, 0.55f), new Color(0.66f, 0.62f, 0.56f, 0.7f), 300, 2.3f);
            ParticleSystem.ShapeModule jetShape = padJets.shape;
            jetShape.shapeType = ParticleSystemShapeType.Sphere;
            jetShape.radius = 5f;
            ParticleSystem.VelocityOverLifetimeModule drive = padJets.velocityOverLifetime;
            drive.enabled = true;
            drive.space = ParticleSystemSimulationSpace.Local;
            drive.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
            drive.y = new ParticleSystem.MinMaxCurve(0.5f, 6f);
            drive.z = new ParticleSystem.MinMaxCurve(-60f, 60f);
            Slow(padJets, 6f);
            AddTurbulence(padJets, 5f, 0.1f);
        }

        /// <summary>Air drag: particles shed speed down to a slow drift.</summary>
        private static void Slow(ParticleSystem system, float driftSpeed)
        {
            ParticleSystem.LimitVelocityOverLifetimeModule drag = system.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.06f;
            drag.limit = driftSpeed;
        }

        private ParticleSystem BuildCollar(Transform exterior, string name, float z, float radius)
        {
            // A circle emitter in the plane across the stack: puffs start on the hull, drift outward and are swept aft.
            ParticleSystem collar = Puffs(name, exterior, new Vector3(0f, 0f, z), Vector3.zero, smoke, BuildPaths.LayerExterior,
                new Vector2(0.45f, 0.9f), new Vector2(2f, 7f), new Vector2(2.5f, 5.5f),
                new Color(1f, 1f, 1f, 0.30f), new Color(0.95f, 0.97f, 1f, 0.5f), 140, 2.6f);
            ParticleSystem.ShapeModule shape = collar.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f;
            ParticleSystem.VelocityOverLifetimeModule sweep = collar.velocityOverLifetime;
            sweep.enabled = true;
            sweep.space = ParticleSystemSimulationSpace.Local;
            sweep.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            sweep.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            sweep.z = new ParticleSystem.MinMaxCurve(-75f, -40f);
            return collar;
        }

        private ParticleSystem BuildBurst(Transform exterior, string name, Material material, Vector2 lifetime, Vector2 speed, Vector2 size,
            Color colorA, Color colorB, short count, float growth)
        {
            ParticleSystem burst = Puffs(name, exterior, new Vector3(0f, 0f, -22f), Vector3.zero, material, BuildPaths.LayerExterior,
                lifetime, speed, size, colorA, colorB, count * 2, growth);
            ParticleSystem.MainModule main = burst.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1.2f;
            ParticleSystem.EmissionModule emission = burst.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count), new ParticleSystem.Burst(0.12f, (short)(count / 2)) });
            ParticleSystem.ShapeModule shape = burst.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 2.6f;
            return burst;
        }

        private void BuildExternalCamera(Transform exterior)
        {
            // Mounted on a short boom off the service module, on the side away from the service tower,
            // looking aft down the stack.
            Vector3 position = new Vector3(-5.4f, 1.1f, -2.0f);
            Vector3 lookAt = new Vector3(-0.9f, -0.4f, -62f);
            GameObject go = SceneKit.Empty("External Camera", exterior, position);
            go.transform.localRotation = Quaternion.LookRotation((lookAt - position).normalized, Vector3.up);

            ExternalCamera = go.AddComponent<Camera>();
            ExternalCamera.fieldOfView = 72f;
            ExternalCamera.nearClipPlane = 0.3f;
            ExternalCamera.farClipPlane = 6000f;
            ExternalCamera.clearFlags = CameraClearFlags.SolidColor;
            ExternalCamera.backgroundColor = Color.black;
            ExternalCamera.cullingMask = (1 << BuildPaths.LayerExterior) | (1 << BuildPaths.LayerEnvironment);
            ExternalCamera.targetTexture = assets.ExternalCameraTexture;
            ExternalCamera.allowHDR = true;
            ExternalCamera.depth = -10f;

            CameraFeed = go.AddComponent<ExternalCameraFeed>();
            CameraFeed.spacecraft = spacecraft;
            CameraFeed.game = game;
            CameraFeed.feedCamera = ExternalCamera;
            CameraFeed.config = assets.Environment;
            CameraFeed.levelFrame = worldRoot;
            CameraFeed.vehicle = exterior;
        }
    }
}
