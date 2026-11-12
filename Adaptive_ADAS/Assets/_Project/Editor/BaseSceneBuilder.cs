// BaseSceneBuilder.cs  (Editor only: this file is never part of a build)
// Menu:  ADAS > Build Base Scene
// Builds the greybox test scene from primitive shapes using Unity's API and saves it as
//   Assets/_Project/Scenes/BaseScene.unity
// We never hand-write .unity files: re-run this menu item whenever the scene should
// be rebuilt (it OVERWRITES BaseScene.unity, so do not hand-edit that scene; change
// this script or the script defaults instead).
//
// Road geometry comes from the default values in RoadLayout.cs. Lane markings and
// roadside posts are NOT stored in the scene: RoadMarkings.cs creates them when Play
// starts (keeps BaseScene.unity small, see that file).
// Car and hazard parameters come from the default values in the controller scripts;
// they can be tuned in the Inspector after building (but a rebuild resets them).
// The ego car gets a greybox INTERIOR (dashboard, speedometer, rear-view mirror,
// A-pillars, windshield frame, side display): one object "Interior" that can be switched
// off as a whole (InteriorToggle). Geometry and gaze angles: Docs/DriverView.md.
// Invisible GAZE-ZONE colliders (layer "GazeZones", created here if missing) cover the
// interior parts and the lead car; DriverTwin casts the gaze ray against them
// (Docs/DriverTwin.md).

using System.Collections.Generic;
using System.IO;
using AdaptiveADAS.Experiment;
using AdaptiveADAS.HMI;
using AdaptiveADAS.Logging;
using AdaptiveADAS.Scenario;
using AdaptiveADAS.Twin;
using AdaptiveADAS.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AdaptiveADAS.EditorTools
{
    public static class BaseSceneBuilder
    {
        private const string ProjectRoot = "Assets/_Project";
        private const string ScenePath = ProjectRoot + "/Scenes/BaseScene.unity";
        private const string MaterialFolder = ProjectRoot + "/Materials";
        private const string GazeLayerName = "GazeZones";
        // Gaze zones around interior parts are this much larger than the part (m, total per
        // dimension): keeps the debug colours from flickering on the part's surface. Our choice
        // (0.5 cm per side is about 0.4 deg at 0.7 m, negligible for classification).
        private const float ZoneMargin = 0.01f;

        // ---- Scene layout choices (ours) ----
        private const float EgoStartZ = 50f;      // m from the road start
        private const int StartLane = 1;          // 0 = left lane, 1 = right lane
        private const float InitialGap = 40f;     // m bumper to bumper (1.6 s headway at 90 km/h)

        // ---- Greybox car dimensions (ours, roughly a mid-size car) ----
        private const float CarLength = 4.5f, CarWidth = 1.8f;
        private const float BodyBottom = 0.3f, BodyTop = 0.9f;          // lower body / hood
        // Cabin box z = -1.5 .. +0.95: the front end reaches the windshield base so that the
        // dashboard is inside the (opaque) cabin box when seen from outside (chase camera).
        private const float CabinTop = 1.45f, CabinLength = 2.45f, CabinCentreZ = -0.275f;
        private const float WheelRadius = 0.33f, WheelBase = 2.7f, TrackHalf = 0.8f;
        private const float WheelColliderY = 0.5f;                        // top of suspension, car coordinates
        // Driver's eye point in car coordinates (x right, y up from the ground, z forward from
        // the car centre). Left-hand drive. Reasons for each value: Docs/DriverView.md.
        private static readonly Vector3 DriverEye = new Vector3(-0.37f, 1.2f, -0.2f);

        // ---- Greybox interior geometry (our choice, car coordinates in m; see Docs/DriverView.md) ----
        // Windshield line (side view): from the base (y 0.98, z 0.95) up to the top (y 1.41, z 0.32).
        private const float ShieldBaseY = 0.98f, ShieldBaseZ = 0.95f, ShieldTopY = 1.41f, ShieldTopZ = 0.32f;
        private const float PillarX = 0.74f;                                   // A-pillars, left and right
        private static readonly Vector3 PillarSize = new Vector3(0.07f, 1f, 0.10f);   // y = length (set from the ends)
        private static readonly Vector3 DashCentre = new Vector3(0f, 0.86f, 0.60f);
        private static readonly Vector3 DashSize = new Vector3(1.56f, 0.24f, 0.70f);  // top at y 0.98, rear face at z 0.25
        private static readonly Vector3 BinnacleCentre = new Vector3(-0.37f, 1.04f, 0.42f); // instrument cluster, straight ahead of the eye
        private static readonly Vector3 BinnacleSize = new Vector3(0.40f, 0.14f, 0.18f);    // face towards the driver at z 0.33
        private const float SpeedDigitHeight = 0.045f;                          // m (about 4.7 deg at 0.55 m)
        private static readonly Vector3 MirrorCentre = new Vector3(0f, 1.31f, 0.41f);   // top touches the windshield
        private static readonly Vector2 MirrorGlassSize = new Vector2(0.26f, 0.08f);
        private const float MirrorYawDeg = 16f;                                 // turned towards the driver: about half of the 31 deg eye-to-mirror angle, so it reflects straight back
        private static readonly Vector3 MirrorCameraPos = new Vector3(0f, 1.31f, 0.38f);
        private static readonly Vector3 SideDisplayCentre = new Vector3(0.05f, 1.03f, 0.55f); // on top of the dash, right of the driver
        private static readonly Vector2 SideDisplaySize = new Vector2(0.26f, 0.16f);          // about a 12" screen (our choice)

        // ---- Lead car brake lights (our choice, see Docs/BaseScene_Parameters.md) ----
        private static readonly Vector3 BrakeLampSize = new Vector3(0.45f, 0.22f, 0.04f);  // left/right lamps (m)
        private const float BrakeLampX = 0.62f, BrakeLampY = 0.78f;
        private static readonly Vector3 CentreLampSize = new Vector3(0.60f, 0.08f, 0.04f); // centre high-mounted lamp
        private const float CentreLampY = 1.38f;
        // Emission brightness of a lit brake lamp. Values > 1 are "HDR": brighter than white,
        // which is what the bloom effect (threshold below) picks up. Our choice.
        private const float BrakeLampEmission = 6f;
        private const float BloomThreshold = 1.5f, BloomIntensity = 1.0f, BloomScatter = 0.6f;

        private class Mats
        {
            public Material asphalt, grass, line, concrete, post, egoBody, leadBody, glass, tyre, lightOff, lightOn, signRed, signWhite;
            public Material interiorDark, interiorTrim, screen, mirrorGlass;
            public Material signDigits;   // null if Textures/SpeedSignDigits.png is missing (signs then use TextMesh)
            public Dictionary<GazeZoneType, Material> zones = new Dictionary<GazeZoneType, Material>();
        }

        [MenuItem("ADAS/Build Base Scene")]
        public static void Build()
        {
            // Let the user save changes in the currently open scene first.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(ProjectRoot, "Scenes");
            EnsureFolder(ProjectRoot, "Materials");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            int gazeLayer = EnsureLayer(GazeLayerName);
            Mats m = CreateMaterials();
            BuildLighting();
            BuildPostProcessing();
            RoadLayout road = BuildRoad(m);

            float laneX = road.LaneCentreX(StartLane);
            var ego = BuildEgoCar(m, new Vector3(laneX, 0.1f, EgoStartZ),
                                  out BoxCollider egoBody, out Camera driverCam, out Camera chaseCam,
                                  out InteriorToggle interior, gazeLayer);
            float leadZ = EgoStartZ + CarLength * 0.5f + InitialGap + CarLength * 0.5f;
            var lead = BuildLeadCar(m, new Vector3(laneX, 0f, leadZ), out BoxCollider leadBody);
            // Gaze zone on the lead car: its whole bounding box (wheels to roof). Our choice: no extra
            // margin for the mouse; with an eye tracker the margin should follow its accuracy (TO DECIDE).
            CreateZone("GazeZone_LeadCar", lead.transform, GazeZoneType.LeadCar, new Vector3(0f, CabinTop * 0.5f, 0f),
                       Quaternion.identity, new Vector3(CarWidth, CabinTop, CarLength), m, gazeLayer);

            BuildSystems(road, ego, egoBody, lead, leadBody, driverCam, chaseCam, interior);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Selection.activeGameObject = ego.gameObject;
            Debug.Log($"[ADAS] Base scene built and saved to {ScenePath}. Press Play to drive.");
        }

        [MenuItem("ADAS/Open Drive Log Folder")]
        public static void OpenLogFolder()
        {
            // Same rule as DriveLogger: <Unity project>/../DriveLogs
            string folder = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "../DriveLogs"));
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder + Path.DirectorySeparatorChar);
        }

        // =====================================================================
        // Materials
        // =====================================================================
        private static Mats CreateMaterials()
        {
            const string lit = "Universal Render Pipeline/Lit";
            var mats = new Mats
            {
                asphalt = GetOrCreateMaterial("Asphalt", new Color(0.17f, 0.17f, 0.18f), lit),
                grass = GetOrCreateMaterial("Grass", new Color(0.26f, 0.42f, 0.20f), lit),
                line = GetOrCreateMaterial("LaneLine", new Color(0.95f, 0.95f, 0.95f), lit),
                concrete = GetOrCreateMaterial("Concrete", new Color(0.62f, 0.62f, 0.60f), lit),
                post = GetOrCreateMaterial("Post", Color.white, lit),
                egoBody = GetOrCreateMaterial("EgoBody", new Color(0.20f, 0.35f, 0.70f), lit),
                leadBody = GetOrCreateMaterial("LeadBody", new Color(0.85f, 0.85f, 0.87f), lit),
                glass = GetOrCreateMaterial("Glass", new Color(0.12f, 0.16f, 0.22f), lit),
                tyre = GetOrCreateMaterial("Tyre", new Color(0.05f, 0.05f, 0.05f), lit),
                signRed = GetOrCreateMaterial("SignRed", new Color(0.80f, 0.05f, 0.05f), lit),
                signWhite = GetOrCreateMaterial("SignWhite", new Color(0.95f, 0.95f, 0.95f), lit),
                // Brake lamps: "off" = dark red lens; "on" = EMISSIVE red, i.e. it glows by itself,
                // independent of sun angle and shadows, so its visibility does not depend on lighting.
                lightOff = GetOrCreateMaterial("BrakeLightOff", new Color(0.35f, 0.04f, 0.04f), lit),
                lightOn = GetOrCreateMaterial("BrakeLightOn", new Color(1.00f, 0.10f, 0.06f), lit,
                                              emission: new Color(1.00f, 0.06f, 0.03f) * BrakeLampEmission),
                // Interior: dark, matte greys (like a typical dashboard, avoids reflections in the view).
                interiorDark = GetOrCreateMaterial("InteriorDark", new Color(0.08f, 0.08f, 0.09f), lit),
                interiorTrim = GetOrCreateMaterial("InteriorTrim", new Color(0.20f, 0.20f, 0.21f), lit),
                // Display glass: slightly self-lit so it stays readable in the shadow of the roof
                // (emission well below the bloom threshold, so it does not glow).
                screen = GetOrCreateMaterial("Screen", new Color(0.02f, 0.03f, 0.05f), lit,
                                             emission: new Color(0.03f, 0.05f, 0.09f)),
                // Mirror glass: Unlit = shows the mirror camera's image as it is, no scene lighting on top.
                mirrorGlass = GetOrCreateMaterial("MirrorGlass", Color.white, "Universal Render Pipeline/Unlit"),
            };
            // Semi-transparent colours for the gaze-zone debug view (Z key in test mode).
            foreach (GazeZoneType z in new[] { GazeZoneType.LeadCar, GazeZoneType.Mirror, GazeZoneType.Speedometer,
                                               GazeZoneType.SideDisplay, GazeZoneType.Interior })
            {
                Color c = GazeDebugView.ZoneColor(z);
                c.a = z == GazeZoneType.Interior ? 0.25f : 0.40f;
                mats.zones[z] = GetOrCreateTransparentMaterial("GazeZone_" + z, c);
            }
            mats.signDigits = CreateSignDigitsMaterial();
            return mats;
        }

        /// <summary>
        /// Material for the speed-limit digits: Unlit, alpha-blended, with Textures/SpeedSignDigits.png
        /// (digits 0-9, black on transparent; made with Docs/tools/make_sign_digits.js).
        /// The texture is set to use MIPMAPS (pre-shrunk copies for far away) with trilinear filtering,
        /// so a far-away number becomes a soft blur instead of breaking up into wrong-looking digits.
        /// </summary>
        private static Material CreateSignDigitsMaterial()
        {
            string path = ProjectRoot + "/Textures/SpeedSignDigits.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null && File.Exists(path))
            {
                AssetDatabase.ImportAsset(path);    // file is there but Unity has not imported it yet
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }
            if (importer == null)
            {
                Debug.LogWarning($"[ADAS] {path} not found: speed-limit signs use the old TextMesh digits.");
                return null;
            }
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;                                     // the point of this texture
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;                        // smooth between mip levels
            importer.anisoLevel = 4;
            importer.npotScale = TextureImporterNPOTScale.None;                // 2048 x 256 is already power of two
            importer.textureCompression = TextureImporterCompression.Uncompressed;   // small texture, keep edges clean
            importer.SaveAndReimport();

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Material mat = GetOrCreateTransparentMaterial("SignDigits", Color.black);
            mat.SetTexture("_BaseMap", tex);       // URP Unlit texture slot
            mat.mainTexture = tex;
            mat.SetFloat("_Cull", 0f);              // draw both faces (URP "Render Face: Both"), so winding cannot hide it
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material GetOrCreateMaterial(string name, Color color, string shaderName, Color? emission = null)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[ADAS] Shader '{shaderName}' not found. Is URP the active render pipeline?");
                shader = Shader.Find("Hidden/InternalErrorShader");
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;               // e.g. old Unlit brake light -> Lit + emission
            }
            mat.SetColor("_BaseColor", color);   // URP colour property
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; // glow on screen only, no baked light
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
            mat.enableInstancing = true;           // identical objects -> fewer draw calls
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Unlit, alpha-blended (semi-transparent) URP material. The property names below are the
        /// ones URP's Unlit shader uses for "Surface Type = Transparent". If a zone shows up opaque,
        /// select its material and set Surface Type to Transparent by hand (and tell us).
        /// </summary>
        private static Material GetOrCreateTransparentMaterial(string name, Color color)
        {
            Material mat = GetOrCreateMaterial(name, color, "Universal Render Pipeline/Unlit");
            mat.SetFloat("_Surface", 1f);                                   // 0 = opaque, 1 = transparent
            mat.SetFloat("_Blend", 0f);                                     // 0 = alpha blending
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);                                    // see-through: do not hide what is behind
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Bloom = a post-processing effect that makes very bright (HDR) pixels glow into their
        /// surroundings. Threshold 1.5 means only things brighter than white bloom: in this scene
        /// practically only the lit brake lamps. Saved as Assets/_Project/Rendering/BrakeLightBloom.asset.
        /// Cost on an RTX 2050 at 1080p: small (a few downsampled blur passes); can be switched off by
        /// disabling the "PostProcessing (Bloom)" object.
        /// </summary>
        private static VolumeProfile GetOrCreateBloomProfile()
        {
            EnsureFolder(ProjectRoot, "Rendering");
            string path = ProjectRoot + "/Rendering/BrakeLightBloom.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            if (!profile.TryGet(out UnityEngine.Rendering.Universal.Bloom bloom))
            {
                bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
                bloom.name = "Bloom";
                AssetDatabase.AddObjectToAsset(bloom, profile);   // the effect is stored inside the profile asset
            }
            bloom.active = true;
            bloom.threshold.Override(BloomThreshold);
            bloom.intensity.Override(BloomIntensity);
            bloom.scatter.Override(BloomScatter);
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static void BuildPostProcessing()
        {
            var go = new GameObject("PostProcessing (Bloom)");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = GetOrCreateBloomProfile();
        }

        /// <summary>URP cameras only apply post-processing (bloom) when this flag is on.</summary>
        private static void EnablePostProcessing(Camera cam)
        {
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            data.renderPostProcessing = true;
        }

        // =====================================================================
        // Lighting: one sun, gradient ambient light, distance fog
        // =====================================================================
        private static void BuildLighting()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.sun = sun;

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            // Gradient ambient (sky / horizon / ground colours): works without baking lighting.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.60f, 0.66f, 0.76f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.46f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.24f, 0.20f);

            // Fog hides the far end of the 5 km road (our choice of distances).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 300f;
            RenderSettings.fogEndDistance = 1500f;
            RenderSettings.fogColor = new Color(0.74f, 0.79f, 0.85f);
        }

        // =====================================================================
        // Road: asphalt slab, median barrier, lane markings, roadside posts
        // =====================================================================
        private static RoadLayout BuildRoad(Mats m)
        {
            var env = new GameObject("Environment");
            var roadGo = new GameObject("Road");
            roadGo.transform.SetParent(env.transform);
            RoadLayout road = roadGo.AddComponent<RoadLayout>();   // uses the defaults in RoadLayout.cs

            float L = road.roadLength;
            float halfPaved = road.PavedOuterX;

            // Grass ground, slightly below the road surface (y = 0).
            var ground = CreateBox("Ground", env.transform, new Vector3(0f, -0.07f, L * 0.5f),
                      new Vector3(400f, 0.1f, L + 400f), m.grass, keepCollider: true);
            ground.AddComponent<CollisionLabel>().label = "Ground";

            // Asphalt for both carriageways; top surface at y = 0.
            var asphalt = CreateBox("Asphalt", roadGo.transform, new Vector3(0f, -0.1f, L * 0.5f),
                      new Vector3(2f * halfPaved, 0.2f, L), m.asphalt, keepCollider: true);
            asphalt.AddComponent<CollisionLabel>().label = "Road";   // only hit if the car body touches the road (rollover)

            // Concrete barrier in the middle of the median.
            var barrier = CreateBox("MedianBarrier", roadGo.transform, new Vector3(0f, 0.4f, L * 0.5f),
                      new Vector3(0.5f, 0.8f, L), m.concrete, keepCollider: true);
            barrier.AddComponent<CollisionLabel>().label = "Barrier";

            // Lane markings + roadside posts: generated at runtime (RoadMarkings.cs).
            var markings = roadGo.AddComponent<RoadMarkings>();
            markings.lineMaterial = m.line;
            markings.postMaterial = m.post;

            // Speed-limit signs: also generated at runtime (SpeedLimitSigns.cs). The DriveSession
            // reference (for the speed value) is set in BuildSystems.
            var signs = roadGo.AddComponent<SpeedLimitSigns>();
            signs.poleMaterial = m.concrete;
            signs.redMaterial = m.signRed;
            signs.whiteMaterial = m.signWhite;
            signs.digitMaterial = m.signDigits;

            MakeStatic(env);
            return road;
        }

        // =====================================================================
        // Cars
        // =====================================================================
        private static EgoCarController BuildEgoCar(Mats m, Vector3 position,
            out BoxCollider bodyCollider, out Camera driverCam, out Camera chaseCam, out InteriorToggle interior,
            int gazeLayer)
        {
            var root = new GameObject("EgoCar");
            root.transform.position = position;

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1500f;

            bodyCollider = BuildCarShell(root.transform, m.egoBody, m.glass, isEgo: true);

            var input = root.AddComponent<DriverInput>();
            var car = root.AddComponent<EgoCarController>();
            car.driverInput = input;
            car.testAssists = root.AddComponent<TestAssists>();   // test-only helpers (cruise, reset)

            // Four wheels: (x, z) in car coordinates. F = front, R = rear, L/R = left/right.
            car.wheelFL = CreateWheelCollider(root.transform, "WheelCollider_FL", -TrackHalf, +WheelBase * 0.5f);
            car.wheelFR = CreateWheelCollider(root.transform, "WheelCollider_FR", +TrackHalf, +WheelBase * 0.5f);
            car.wheelRL = CreateWheelCollider(root.transform, "WheelCollider_RL", -TrackHalf, -WheelBase * 0.5f);
            car.wheelRR = CreateWheelCollider(root.transform, "WheelCollider_RR", +TrackHalf, -WheelBase * 0.5f);
            car.visualFL = CreateWheelVisual(root.transform, "Wheel_FL", -TrackHalf, +WheelBase * 0.5f, m.tyre);
            car.visualFR = CreateWheelVisual(root.transform, "Wheel_FR", +TrackHalf, +WheelBase * 0.5f, m.tyre);
            car.visualRL = CreateWheelVisual(root.transform, "Wheel_RL", -TrackHalf, -WheelBase * 0.5f, m.tyre);
            car.visualRR = CreateWheelVisual(root.transform, "Wheel_RR", +TrackHalf, -WheelBase * 0.5f, m.tyre);

            // Driver's-seat camera (used in the study).
            var camGo = new GameObject("DriverCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = DriverEye;
            driverCam = camGo.AddComponent<Camera>();
            driverCam.nearClipPlane = 0.05f;
            driverCam.farClipPlane = 2000f;
            // FOV is set by DriverViewSettings (geometric from the screen size, or manual).
            camGo.AddComponent<DriverViewSettings>().Apply();

            // Chase camera (debug only, toggled with C).
            var chaseGo = new GameObject("ChaseCamera");
            chaseGo.transform.SetParent(root.transform, false);
            chaseGo.transform.localPosition = new Vector3(0f, 3f, -9f);
            chaseGo.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
            chaseCam = chaseGo.AddComponent<Camera>();
            chaseCam.fieldOfView = 60f;
            chaseCam.farClipPlane = 2000f;
            chaseCam.enabled = false;
            EnablePostProcessing(driverCam);
            EnablePostProcessing(chaseCam);

            root.AddComponent<AudioListener>();   // on the car, so it works with both cameras

            // Greybox interior (one object, switched on/off as a whole).
            GameObject interiorRoot = BuildInterior(m, root.transform, car, gazeLayer);
            interior = root.AddComponent<InteriorToggle>();
            interior.interiorRoot = interiorRoot;
            return car;
        }

        // =====================================================================
        // Interior (ego car only). All parts are visual only: NO colliders, because a collider
        // under the ego Rigidbody would become part of the car's physics body.
        // Positions are in car coordinates; the reasons and the resulting gaze angles from
        // the eye point are in Docs/DriverView.md.
        // =====================================================================
        private static GameObject BuildInterior(Mats m, Transform carRoot, EgoCarController car, int gazeLayer)
        {
            var interiorParts = new List<GameObject>();   // surfaces that form the "Interior" gaze zone
            var interior = new GameObject("Interior");
            interior.transform.SetParent(carRoot, false);
            Transform t = interior.transform;

            // Dashboard: top at y 0.98, from the windshield base (z 0.95) back to z 0.25.
            interiorParts.Add(CreateBox("Dashboard", t, DashCentre, DashSize, m.interiorDark, keepCollider: false));

            // Instrument cluster (binnacle) straight ahead of the driver, with a digital speedometer.
            CreateBox("InstrumentCluster", t, BinnacleCentre, BinnacleSize, m.interiorTrim, keepCollider: false);
            float faceZ = BinnacleCentre.z - BinnacleSize.z * 0.5f;      // z 0.33
            CreateBox("SpeedometerScreen", t, new Vector3(BinnacleCentre.x, BinnacleCentre.y, faceZ - 0.0025f),
                      new Vector3(0.30f, 0.11f, 0.005f), m.screen, keepCollider: false);
            var speedo = new GameObject("Speedometer");
            speedo.transform.SetParent(t, false);
            TextMesh speedText = CreateText("SpeedDigits", speedo.transform, "88",
                new Vector3(BinnacleCentre.x, BinnacleCentre.y + 0.01f, faceZ - 0.007f), Quaternion.identity,
                SpeedDigitHeight, new Color(0.90f, 0.95f, 1.00f));
            CreateText("Unit", speedo.transform, "km/h",
                new Vector3(BinnacleCentre.x, BinnacleCentre.y - 0.035f, faceZ - 0.007f), Quaternion.identity,
                0.012f, new Color(0.65f, 0.70f, 0.75f));
            var speedometer = speedo.AddComponent<Speedometer>();
            speedometer.egoCar = car;
            speedometer.speedText = speedText;

            // Windshield frame: two A-pillars along the windshield line + header at the top.
            // (No glass: a transparent windshield adds sorting/reflection issues and is not needed.)
            foreach (float x in new[] { -PillarX, PillarX })
            {
                Vector3 bottom = new Vector3(x, ShieldBaseY, ShieldBaseZ);
                Vector3 top = new Vector3(x, ShieldTopY - 0.01f, Mathf.Lerp(ShieldBaseZ, ShieldTopZ, (ShieldTopY - 0.01f - ShieldBaseY) / (ShieldTopY - ShieldBaseY)));
                var pillar = CreateBox(x < 0 ? "APillar_Left" : "APillar_Right", t, (bottom + top) * 0.5f,
                    new Vector3(PillarSize.x, Vector3.Distance(bottom, top), PillarSize.z), m.interiorTrim, keepCollider: false);
                pillar.transform.localRotation = Quaternion.FromToRotation(Vector3.up, top - bottom);  // lean back along the glass
                interiorParts.Add(pillar);
            }
            interiorParts.Add(CreateBox("WindshieldHeader", t, new Vector3(0f, ShieldTopY, ShieldTopZ),
                      new Vector3(2f * PillarX + 0.08f, 0.06f, 0.12f), m.interiorTrim, keepCollider: false));
            // Roof liner above the driver's head (short, so the mirror camera does not see it).
            interiorParts.Add(CreateBox("RoofLiner", t, new Vector3(0f, 1.43f, -0.15f), new Vector3(1.56f, 0.03f, 0.82f), m.interiorTrim, keepCollider: false));
            // Door trims (belt line) left and right, behind the dashboard.
            foreach (float x in new[] { -0.76f, 0.76f })
                interiorParts.Add(CreateBox(x < 0 ? "DoorTrim_Left" : "DoorTrim_Right", t, new Vector3(x, 0.875f, -0.625f),
                          new Vector3(0.06f, 0.19f, 1.75f), m.interiorTrim, keepCollider: false));

            // Rear-view mirror: housing + glass (quad) showing the mirror camera's image.
            var mirror = new GameObject("RearViewMirror");
            mirror.transform.SetParent(t, false);
            mirror.transform.localPosition = MirrorCentre;
            mirror.transform.localRotation = Quaternion.Euler(0f, MirrorYawDeg, 0f);
            CreateBox("Housing", mirror.transform, Vector3.zero,
                      new Vector3(MirrorGlassSize.x + 0.02f, MirrorGlassSize.y + 0.01f, 0.03f), m.interiorDark, keepCollider: false);
            var glass = GameObject.CreatePrimitive(PrimitiveType.Quad);   // a quad faces -z, i.e. towards the driver
            glass.name = "MirrorGlass";
            Object.DestroyImmediate(glass.GetComponent<Collider>());
            glass.transform.SetParent(mirror.transform, false);
            glass.transform.localPosition = new Vector3(0f, 0f, -0.016f);
            glass.transform.localScale = new Vector3(MirrorGlassSize.x, MirrorGlassSize.y, 1f);
            var glassRenderer = glass.GetComponent<Renderer>();
            glassRenderer.sharedMaterial = m.mirrorGlass;
            glassRenderer.shadowCastingMode = ShadowCastingMode.Off;

            // Mirror camera: at the mirror, looking straight back (camera settings in RearViewMirror.cs).
            var mirrorCamGo = new GameObject("MirrorCamera");
            mirrorCamGo.transform.SetParent(t, false);
            mirrorCamGo.transform.localPosition = MirrorCameraPos;
            mirrorCamGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var mirrorCam = mirrorCamGo.AddComponent<Camera>();
            mirrorCam.nearClipPlane = 0.3f;     // skips the mirror itself and the roof liner
            mirrorCam.farClipPlane = 600f;      // our choice: far enough for following traffic, cheap
            mirrorCam.depth = -10;
            mirrorCam.cullingMask = ~(1 << gazeLayer);   // never show the gaze-zone debug boxes in the mirror
            var rvm = mirror.AddComponent<RearViewMirror>();
            rvm.mirrorCamera = mirrorCam;
            rvm.mirrorSurface = glassRenderer;
            mirrorCam.fieldOfView = rvm.mirrorVerticalFov;

            // Side display (centre screen) on top of the dash, turned to face the driver.
            // Placeholder for the Step 5 distraction task.
            Vector3 toDisplay = SideDisplayCentre - DriverEye;
            var display = new GameObject("SideDisplay");
            display.transform.SetParent(t, false);
            display.transform.localPosition = SideDisplayCentre;
            display.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(toDisplay.x, toDisplay.z) * Mathf.Rad2Deg, 0f);
            CreateBox("Bezel", display.transform, new Vector3(0f, 0f, 0.012f),
                      new Vector3(SideDisplaySize.x + 0.02f, SideDisplaySize.y + 0.02f, 0.02f), m.interiorDark, keepCollider: false);
            CreateBox("Screen", display.transform, new Vector3(0f, 0f, 0f),
                      new Vector3(SideDisplaySize.x, SideDisplaySize.y, 0.004f), m.screen, keepCollider: false);
            CreateText("Label", display.transform, "SIDE DISPLAY", new Vector3(0f, 0.015f, -0.004f), Quaternion.identity,
                       0.018f, new Color(0.55f, 0.75f, 0.95f));
            CreateText("SubLabel", display.transform, "(task in Step 5)", new Vector3(0f, -0.025f, -0.004f), Quaternion.identity,
                       0.011f, new Color(0.45f, 0.55f, 0.65f));

            // ---- Gaze zones of the interior (invisible trigger colliders, see Docs/DriverTwin.md) ----
            // They sit under their own KINEMATIC Rigidbody: colliders directly under the ego car's
            // Rigidbody would become part of the car's physics body (and could change its inertia).
            var zoneRoot = new GameObject("GazeZones");
            zoneRoot.transform.SetParent(t, false);
            zoneRoot.layer = gazeLayer;
            var zoneBody = zoneRoot.AddComponent<Rigidbody>();
            zoneBody.isKinematic = true;
            zoneBody.useGravity = false;
            Transform zr = zoneRoot.transform;

            Vector3 margin = Vector3.one * ZoneMargin;
            // Mirror: housing + glass, same orientation as the mirror.
            CreateZone("Zone_Mirror", zr, GazeZoneType.Mirror, MirrorCentre, mirror.transform.localRotation,
                       new Vector3(MirrorGlassSize.x + 0.02f, MirrorGlassSize.y + 0.01f, 0.05f) + margin, m, gazeLayer);
            // Speedometer: the whole instrument cluster (its front reaches 1 cm in front of the digits).
            CreateZone("Zone_Speedometer", zr, GazeZoneType.Speedometer, BinnacleCentre - new Vector3(0f, 0f, 0.01f),
                       Quaternion.identity, BinnacleSize + new Vector3(0f, 0f, 0.02f) + margin, m, gazeLayer);
            // Side display: bezel + screen + text.
            CreateZone("Zone_SideDisplay", zr, GazeZoneType.SideDisplay,
                       display.transform.localPosition + display.transform.localRotation * new Vector3(0f, 0f, 0.008f),
                       display.transform.localRotation,
                       new Vector3(SideDisplaySize.x + 0.02f, SideDisplaySize.y + 0.02f, 0.04f) + margin, m, gazeLayer);
            // Interior: every other visible interior surface (copy of each part, slightly larger).
            foreach (GameObject part in interiorParts)
                CreateZone("Zone_Interior_" + part.name, zr, GazeZoneType.Interior, part.transform.localPosition,
                           part.transform.localRotation, part.transform.localScale + margin, m, gazeLayer);

            // Interior parts do not need to be static (they move with the car).
            return interior;
        }

        private static LeadCarController BuildLeadCar(Mats m, Vector3 position, out BoxCollider bodyCollider)
        {
            var root = new GameObject("LeadCar");
            root.transform.position = position;

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            bodyCollider = BuildCarShell(root.transform, m.leadBody, m.glass, isEgo: false);

            // Static wheels (the lead car is scripted, it has no WheelColliders).
            foreach (var (x, z) in new[] { (-TrackHalf, WheelBase * 0.5f), (TrackHalf, WheelBase * 0.5f),
                                           (-TrackHalf, -WheelBase * 0.5f), (TrackHalf, -WheelBase * 0.5f) })
            {
                var w = CreateWheelVisual(root.transform, "Wheel", x, z, m.tyre);
                w.localPosition = new Vector3(x, WheelRadius, z);
            }

            // Brake lamps: left + right at the rear of the body, plus a centre high-mounted
            // lamp at the top of the rear window (as on real cars). Slightly behind the body
            // surface so they are not hidden inside it.
            var lights = new List<Renderer>();
            foreach (float x in new[] { -BrakeLampX, BrakeLampX })
            {
                var l = CreateBox("BrakeLight", root.transform, Vector3.zero, BrakeLampSize, m.lightOff, keepCollider: false);
                l.transform.localPosition = new Vector3(x, BrakeLampY, -CarLength * 0.5f - 0.02f);
                lights.Add(l.GetComponent<Renderer>());
            }
            var centre = CreateBox("BrakeLight_CentreHigh", root.transform, Vector3.zero, CentreLampSize, m.lightOff, keepCollider: false);
            centre.transform.localPosition = new Vector3(0f, CentreLampY, CabinCentreZ - CabinLength * 0.5f - 0.02f);
            lights.Add(centre.GetComponent<Renderer>());

            root.AddComponent<CollisionLabel>().label = "Lead";

            var lead = root.AddComponent<LeadCarController>();
            lead.brakeLights = lights.ToArray();
            lead.brakeLightOff = m.lightOff;
            lead.brakeLightOn = m.lightOn;
            return lead;
        }

        /// <summary>Body box (with the car's only collider) + cabin box (visual only).</summary>
        private static BoxCollider BuildCarShell(Transform root, Material bodyMat, Material glassMat, bool isEgo)
        {
            float bodyH = BodyTop - BodyBottom;
            var body = CreateBox("Body", root, Vector3.zero, new Vector3(CarWidth, bodyH, CarLength), bodyMat, keepCollider: true);
            body.transform.localPosition = new Vector3(0f, BodyBottom + bodyH * 0.5f, 0f);

            float cabinH = CabinTop - BodyTop;
            var cabin = CreateBox("Cabin", root, Vector3.zero, new Vector3(CarWidth - 0.2f, cabinH, CabinLength), glassMat, keepCollider: false);
            cabin.transform.localPosition = new Vector3(0f, BodyTop + cabinH * 0.5f, CabinCentreZ);
            if (isEgo)
            {
                // The driver camera sits INSIDE this box. Its faces point outwards, so from the
                // inside they are not drawn (back-face culling); we also stop it casting a shadow
                // over the hood. From the chase camera it still looks like a cabin.
                cabin.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            return body.GetComponent<BoxCollider>();
        }

        private static WheelCollider CreateWheelCollider(Transform root, string name, float x, float z)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(x, WheelColliderY, z);
            var wc = go.AddComponent<WheelCollider>();
            wc.radius = WheelRadius;
            wc.mass = 20f;
            wc.wheelDampingRate = 0.25f;     // rolling resistance-like losses (Unity default)
            // Suspension: Unity's default values (about 1.5 Hz for a quarter-car of 375 kg).
            wc.suspensionDistance = 0.3f;
            wc.suspensionSpring = new JointSpring { spring = 35000f, damper = 4500f, targetPosition = 0.5f };
            // Tyre friction: Unity's default friction curves are kept for now (TO TUNE).
            return wc;
        }

        private static Transform CreateWheelVisual(Transform root, string name, float x, float z, Material mat)
        {
            // Parent object = moved by EgoCarController to the WheelCollider pose.
            // Child cylinder = rotated 90 degrees so its axis is the axle.
            var holder = new GameObject(name).transform;
            holder.SetParent(root, false);
            holder.localPosition = new Vector3(x, WheelColliderY - 0.15f, z);

            var tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tyre.name = "Tyre";
            Object.DestroyImmediate(tyre.GetComponent<Collider>());   // visuals must not collide
            tyre.transform.SetParent(holder, false);
            tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            tyre.transform.localScale = new Vector3(WheelRadius * 2f, 0.11f, WheelRadius * 2f);
            tyre.GetComponent<Renderer>().sharedMaterial = mat;
            return holder;
        }

        // =====================================================================
        // Systems: session clock, twin, logger, overlay, camera switch
        // =====================================================================
        private static void BuildSystems(RoadLayout road, EgoCarController ego, BoxCollider egoBody,
            LeadCarController lead, BoxCollider leadBody, Camera driverCam, Camera chaseCam, InteriorToggle interior)
        {
            var go = new GameObject("DriveSystems");

            var session = go.AddComponent<DriveSession>();
            var events = go.AddComponent<DriveEvents>();
            events.session = session;
            session.egoCar = ego.transform;
            session.events = events;
            session.endOfDriveZ = road.roadLength - 100f;

            var twin = go.AddComponent<VehicleTwin>();
            twin.egoCar = ego;
            twin.egoBodyCollider = egoBody;
            twin.leadCar = lead;
            twin.leadBodyCollider = leadBody;
            twin.road = road;

            lead.session = session;
            lead.twin = twin;
            lead.events = events;

            ego.events = events;
            var input = ego.driverInput;
            input.events = events;

            road.GetComponent<SpeedLimitSigns>().session = session;

            TestAssists assists = ego.testAssists;
            assists.session = session;
            assists.events = events;
            assists.egoCar = ego;
            assists.driverInput = input;
            assists.leadCar = lead;
            assists.road = road;

            // Driver part of the twin (gaze, attention buffer). Gaze source: mouse (home testing).
            var mouseGaze = go.AddComponent<MouseGazeSource>();
            mouseGaze.targetCamera = driverCam;
            var driverTwin = go.AddComponent<DriverTwin>();
            driverTwin.session = session;
            driverTwin.events = events;
            driverTwin.leadCar = lead;
            driverTwin.driverCamera = driverCam;
            driverTwin.gazeSource = mouseGaze;
            driverTwin.gazeLayerName = GazeLayerName;

            var gazeView = go.AddComponent<GazeDebugView>();
            gazeView.session = session;
            gazeView.driverTwin = driverTwin;

            // Eye tracker (Docs/EyeTracking.md). Both start switched OFF; GazeSourceSwitch turns them on
            // at Play when its Source is PupilCapture or PupilFake. One surface for now: the centre
            // monitor, shown by the driver camera (three monitors = part G).
            var pupil = go.AddComponent<PupilGazeSource>();
            pupil.session = session;
            pupil.events = events;
            pupil.surfaces = new List<PupilSurface> { new PupilSurface { surfaceName = "monitor_center", camera = driverCam } };
            pupil.enabled = false;
            var fakePupil = go.AddComponent<FakePupilServer>();
            fakePupil.targetCamera = driverCam;
            fakePupil.surfaceName = "monitor_center";
            fakePupil.enabled = false;
            var gazeSwitch = go.AddComponent<GazeSourceSwitch>();
            gazeSwitch.driverTwin = driverTwin;
            gazeSwitch.mouse = mouseGaze;
            gazeSwitch.pupil = pupil;
            gazeSwitch.fake = fakePupil;

            var logger = go.AddComponent<DriveLogger>();
            logger.session = session;
            logger.events = events;
            logger.twin = twin;
            logger.driverInput = input;
            logger.egoCar = ego;
            logger.leadCar = lead;
            logger.testAssists = assists;
            logger.driverView = driverCam.GetComponent<DriverViewSettings>();
            logger.interior = interior;
            logger.driverTwin = driverTwin;
            logger.pupil = pupil;

            // Scripted gaze test (T in test mode): fixed gaze sequence + comparison with the rules.
            var scripted = go.AddComponent<ScriptedGazeSource>();
            scripted.session = session;
            scripted.driverTwin = driverTwin;
            scripted.driverCamera = driverCam;
            var gazeTest = go.AddComponent<GazeBufferTest>();
            gazeTest.session = session;
            gazeTest.events = events;
            gazeTest.driverTwin = driverTwin;
            gazeTest.scripted = scripted;
            gazeTest.logger = logger;

            interior.session = session;
            interior.events = events;

            // Full name needed: UnityEngine.Rendering also has a class called DebugOverlay.
            var overlay = go.AddComponent<AdaptiveADAS.HMI.DebugOverlay>();
            overlay.session = session;
            overlay.twin = twin;
            overlay.driverInput = input;
            overlay.egoCar = ego;
            overlay.leadCar = lead;
            overlay.testAssists = assists;
            overlay.logger = logger;
            overlay.events = events;
            overlay.driverView = driverCam.GetComponent<DriverViewSettings>();
            overlay.interior = interior;
            overlay.driverTwin = driverTwin;
            overlay.gazeTest = gazeTest;
            overlay.pupil = pupil;
            overlay.fakePupil = fakePupil;

            var views = go.AddComponent<ViewSwitcher>();
            views.session = session;
            views.driverCamera = driverCam;
            views.chaseCamera = chaseCam;
        }

        // =====================================================================
        // Helpers
        // =====================================================================
        private static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 size,
                                            Material mat, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>
        /// Invisible gaze-zone box: trigger collider on the GazeZones layer + GazeZone tag +
        /// a renderer that is OFF unless the debug view (Z) switches it on.
        /// </summary>
        private static GameObject CreateZone(string name, Transform parent, GazeZoneType type, Vector3 localPos,
                                             Quaternion localRot, Vector3 size, Mats m, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = size;
            go.GetComponent<BoxCollider>().isTrigger = true;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m.zones[type];
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            go.AddComponent<GazeZone>().zone = type;
            return go;
        }

        /// <summary>
        /// Returns the index of the user layer 'name', creating it in the first free slot (8..31)
        /// of Project Settings > Tags and Layers if needed. This changes
        /// ProjectSettings/TagManager.asset (commit that file too).
        /// </summary>
        private static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = name;
                    tagManager.ApplyModifiedProperties();
                    Debug.Log($"[ADAS] Created layer '{name}' (index {i}) in Project Settings > Tags and Layers.");
                    return i;
                }
            }
            Debug.LogError($"[ADAS] No free layer for '{name}'. Free one in Project Settings > Tags and Layers.");
            return 0;
        }

        /// <summary>
        /// Legacy 3D text (TextMesh) with Unity's built-in font. FitTextHeight scales it at Play
        /// so the letters are 'height' metres tall. Readable from a camera looking along +z.
        /// </summary>
        private static TextMesh CreateText(string name, Transform parent, string text, Vector3 position,
                                           Quaternion rotation, float height, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            var tm = go.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.text = text;
            tm.fontSize = 100;            // large font size = sharp letters
            tm.characterSize = 0.01f;     // rescaled by FitTextHeight
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            // Best effort to get the right size already in the editor (if the text mesh exists yet);
            // FitTextHeight corrects it at Play in any case.
            float h = mr.bounds.size.y;
            if (h > 0.0001f) go.transform.localScale *= height / h;
            go.AddComponent<FitTextHeight>().targetHeight = height;
            return tm;
        }

        /// <summary>Marks an object and its children as static so Unity can batch them (faster rendering).</summary>
        private static void MakeStatic(GameObject go)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
                AssetDatabase.CreateFolder(parent, name);
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));   // index 0 = first scene
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
