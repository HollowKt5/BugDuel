using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace BeetleArena
{
    // Standalone arena and nursery have no CampaignBackdrop. Redirect their scene render to a
    // texture and present it behind the existing HUD; never read pixels back to the CPU.
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class BeetleCombatDisplay : MonoBehaviour
    {
        public BeetleCrtProfile profile;
        Camera source, screenCamera;
        RenderTexture frame;
        GameObject canvasObject;
        UnityEngine.UI.RawImage image;
        BeetleCombatCrt effect;
        readonly WorldToonLayer toon=new WorldToonLayer();
        int width, height;
        string sceneName;
        readonly List<CanvasBinding> hudCanvases = new List<CanvasBinding>();

        struct CanvasBinding
        {
            public Canvas canvas;
            public RenderMode mode;
            public Camera camera;
            public float planeDistance;
            public int sortingLayer, sortingOrder, targetDisplay;
            public bool overrideSorting;
        }

        public static void Install(Camera camera)
        {
            if (camera && !camera.GetComponent<BeetleCombatDisplay>())
                camera.gameObject.AddComponent<BeetleCombatDisplay>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallConfiguredScenes()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) InstallScene(SceneManager.GetSceneAt(i));
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InstallScene(scene);

        public static void InstallScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            if (!WorldToonLayer.Enabled && (!BeetleDisplayPreferences.Profile ||
                BeetleDisplayPreferences.Profile.Find(scene.name) == null)) return;
            var roots = scene.GetRootGameObjects();
            // Campaign pages already present the world through CampaignBackdrop.
            foreach (var root in roots)
                if (root.GetComponentInChildren<Campaign.CampaignBackdrop>(true)) return;
            foreach (var root in roots)
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                    if (camera.CompareTag("MainCamera")) Install(camera);
        }

        void Awake() { source = GetComponent<Camera>();sceneName=gameObject.scene.name; }
        void OnEnable()=>RenderPipelineManager.endCameraRendering+=Rendered;

        void Rendered(ScriptableRenderContext context,Camera camera)
        {
            if(camera==source&&frame&&source.targetTexture==frame&&image)
                image.texture=toon.Render(frame);
        }

        public static bool RequiresSurface(string scene,BeetleCrtProfile crt)
        {
            if(WorldToonLayer.Enabled)return true;
            var style=BeetleDisplayPreferences.GetStyle(scene);
            if(style==BeetleDisplayStyle.Clean)return false;
            if(style==BeetleDisplayStyle.Custom)return BeetleDisplayPreferences.GetCustomMaterial(scene);
            if(style==BeetleDisplayStyle.Crt)return crt&&crt.effectEnabled&&crt.intensity>0;
            return BeetleDisplayPreferences.IsDither(style);
        }

        void LateUpdate()
        {
            if (!source) return;
            // All Start methods have completed by LateUpdate. A campaign camera already
            // owns its target; CampaignView attaches the selected treatment to that display.
            if (!frame && source.targetTexture) return;
            if (!profile) profile = Resources.Load<BeetleCrtProfile>("CombatCrt");
            if (effect) effect.profile = profile;
            if (!RequiresSurface(sceneName,profile))
            {
                Release();
                return;
            }
            if (!source.enabled || !source.gameObject.activeInHierarchy)
            {
                Release();
                return;
            }
            if (frame && source.targetTexture != frame)
            {
                // Yield ownership if another presentation system takes over this camera.
                Release();
                return;
            }
            int desiredWidth = Mathf.Max(1, Mathf.Min(profile ? Mathf.Clamp(profile.maximumWidth, 640, 2560) : 1600, Screen.width));
            int desiredHeight = Mathf.Max(1, Mathf.RoundToInt(desiredWidth * (float)Screen.height / Mathf.Max(1, Screen.width)));
            if (!frame || desiredWidth != width || desiredHeight != height) Resize(desiredWidth, desiredHeight);
            if (!image) CreateDisplay();
        }

        void Resize(int w, int h)
        {
            ReleaseFrame();
            width = w; height = h;
            frame = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "World frame", antiAliasing = 1, filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            frame.Create();
            var previous = RenderTexture.active;
            try { RenderTexture.active = frame; GL.Clear(true, true, Color.black); }
            finally { RenderTexture.active = previous; }
            source.targetTexture = frame;
            if (image) image.texture = frame;
        }

        void CreateDisplay()
        {
            LiftHudCanvases();
            canvasObject = new GameObject("WorldDisplay", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -32000;
            canvas.targetDisplay = source.targetDisplay;
            var surface = new GameObject("World", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
            surface.transform.SetParent(canvasObject.transform, false);
            var rect = (RectTransform)surface.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            image = surface.GetComponent<UnityEngine.UI.RawImage>();
            image.texture = frame; image.raycastTarget = false;
            effect = BeetleCombatCrt.Attach(image);
            effect.profile = profile;

            // URP needs one camera targeting the screen for overlay canvases. This camera
            // draws no scene layers, so the world is rendered exactly once per frame.
            var go = new GameObject("WorldUICamera", typeof(Camera));
            go.transform.SetParent(transform, false);
            screenCamera = go.GetComponent<Camera>();
            screenCamera.cullingMask = 0;
            screenCamera.clearFlags = CameraClearFlags.SolidColor;
            screenCamera.backgroundColor = Color.black;
            screenCamera.depth = source.depth + 1;
            screenCamera.targetDisplay = source.targetDisplay;
            screenCamera.allowHDR = false;
            screenCamera.allowMSAA = false;
            screenCamera.useOcclusionCulling = false;
        }

        void LiftHudCanvases()
        {
            // The original standalone HUD is ScreenSpaceCamera. Leaving it attached to
            // the source would bake its text and panels into the CRT world texture.
            // Snapshot root canvases once, including inactive result panels. Nested
            // canvases inherit their root's mode and need no independent mutation.
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != source)
                    continue;
                hudCanvases.Add(new CanvasBinding
                {
                    canvas = canvas, mode = canvas.renderMode, camera = canvas.worldCamera,
                    planeDistance = canvas.planeDistance, sortingLayer = canvas.sortingLayerID,
                    sortingOrder = canvas.sortingOrder, targetDisplay = canvas.targetDisplay,
                    overrideSorting = canvas.overrideSorting
                });
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.targetDisplay = source.targetDisplay;
            }
        }

        void RestoreHudCanvases()
        {
            foreach (var binding in hudCanvases)
            {
                var canvas = binding.canvas;
                if (!canvas) continue;
                canvas.renderMode = binding.mode;
                canvas.worldCamera = binding.camera;
                canvas.planeDistance = binding.planeDistance;
                canvas.sortingLayerID = binding.sortingLayer;
                canvas.sortingOrder = binding.sortingOrder;
                canvas.overrideSorting = binding.overrideSorting;
                canvas.targetDisplay = binding.targetDisplay;
            }
            hudCanvases.Clear();
        }

        void ReleaseFrame()
        {
            if (image) image.texture = null;
            toon.Dispose();
            if (source && source.targetTexture == frame) source.targetTexture = null;
            if (frame) { frame.Release(); Destroy(frame); }
            frame = null;
        }

        void Release()
        {
            RestoreHudCanvases();
            ReleaseFrame();
            if (screenCamera) { screenCamera.enabled = false; Destroy(screenCamera.gameObject); }
            if (canvasObject) { canvasObject.SetActive(false); Destroy(canvasObject); }
            screenCamera = null; canvasObject = null; image = null; effect = null;
        }

        void OnDisable()
        {
            RenderPipelineManager.endCameraRendering-=Rendered;
            Release();
        }
    }
}
