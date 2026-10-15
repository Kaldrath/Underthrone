using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Underthrone.Client.Editor
{
    /// Builds the Phase 1 combat slice scene from code so it can be regenerated at any time:
    /// Underthrone > Create Dev Scene.
    public static class DevSceneBuilder
    {
        private const string SCENE_PATH = "Assets/Scenes/Dev.unity";
        private const string ART_FOLDER = "Assets/Art/Generated";

        [MenuItem("Underthrone/Create Dev Scene")]
        public static void CreateDevScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material groundMaterial = CreateGroundMaterial();
            BuildEnvironment(groundMaterial);

            // Player rig: body transform (yaw) with the eye camera as a child (pitch).
            var player = new GameObject("LocalPlayer");
            var eyes = new GameObject("Eyes");
            eyes.tag = "MainCamera";
            eyes.transform.SetParent(player.transform, false);
            Camera camera = eyes.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 200f;
            camera.fieldOfView = 75f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
            eyes.AddComponent<AudioListener>();
            eyes.AddComponent<RetroCamera>();
            LocalPlayer local = player.AddComponent<LocalPlayer>();

            var remote = new GameObject("RemoteEntities");
            RemoteEntities remoteEntities = remote.AddComponent<RemoteEntities>();

            var net = new GameObject("Net");
            NetClient netClient = net.AddComponent<NetClient>();
            DevHud hud = net.AddComponent<DevHud>();

            Wire(netClient, ("_localPlayer", local), ("_remoteEntities", remoteEntities));
            Wire(local, ("_net", netClient), ("_eyes", eyes.transform));
            Wire(remoteEntities, ("_net", netClient), ("_camera", eyes.transform));
            Wire(hud, ("_net", netClient), ("_player", local));

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), SCENE_PATH);
            AddToBuildSettings(SCENE_PATH);
            Debug.Log($"[Underthrone] Dev scene written to {SCENE_PATH}. Start the server, then press Play.");
        }

        private static void BuildEnvironment(Material groundMaterial)
        {
            var light = new GameObject("Sun");
            Light sun = light.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.4f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.05f, 0.05f, 0.07f);
            RenderSettings.fogStartDistance = 12f;
            RenderSettings.fogEndDistance = 55f;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(8f, 1f, 8f); // 80 m, matches the arena
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            // Standing stones for a sense of scale and motion while the world is a blank arena.
            var stones = new GameObject("Stones");
            for (int stoneIndex = 0; stoneIndex < 16; stoneIndex++)
            {
                float angle = stoneIndex * Mathf.PI * 2f / 16f;
                GameObject stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stone.name = $"Stone {stoneIndex}";
                stone.transform.SetParent(stones.transform, false);
                stone.transform.position = new Vector3(Mathf.Sin(angle) * 20f, 1.5f, Mathf.Cos(angle) * 20f);
                stone.transform.localScale = new Vector3(1.2f, 3f + (stoneIndex % 3), 0.8f);
                stone.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            }
        }

        /// A point-filtered checker texture: the chunky retro floor until real tiles exist.
        private static Material CreateGroundMaterial()
        {
            System.IO.Directory.CreateDirectory(ART_FOLDER);
            const int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var dark = new Color32(46, 44, 40, 255);
            var light = new Color32(64, 60, 52, 255);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool checker = (x / 8 + y / 8) % 2 == 0;
                    Color32 c = checker ? light : dark;
                    if (x % 8 == 0 || y % 8 == 0)
                    {
                        c = new Color32((byte)(c.r - 10), (byte)(c.g - 10), (byte)(c.b - 10), 255);
                    }
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            string texPath = $"{ART_FOLDER}/GroundChecker.png";
            System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            // Unlit so the checker reads crisply; picks the URP shader when URP is active.
            Shader shader = GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Unlit")
                : Shader.Find("Unlit/Texture");
            var material = new Material(shader) { name = "Ground" };
            Texture2D loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            material.mainTexture = loaded;
            material.mainTextureScale = new Vector2(40f, 40f);
            AssetDatabase.CreateAsset(material, $"{ART_FOLDER}/Ground.mat");
            return material;
        }

        private static void Wire(Object target, params (string field, Object value)[] fields)
        {
            var so = new SerializedObject(target);
            foreach ((string field, Object value) in fields)
            {
                SerializedProperty property = so.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError($"[Underthrone] {target.GetType().Name} has no serialized field '{field}'");
                    continue;
                }
                property.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath))
            {
                return;
            }
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
