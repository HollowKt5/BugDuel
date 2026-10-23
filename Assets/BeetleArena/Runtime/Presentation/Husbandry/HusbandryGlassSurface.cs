using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BeetleArena
{
    /// <summary>Shared glass materials; player quality is independent of geometry and screen filters.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class HusbandryGlassSurface : MonoBehaviour
    {
        [SerializeField] BeetleGlassQuality editorPreview = BeetleGlassQuality.Realistic;
        [SerializeField] Material realisticMaterial;
        [SerializeField] Material lowMaterial;
        [SerializeField, HideInInspector] MeshRenderer[] panes = new MeshRenderer[0];
        public BeetleGlassQuality ActiveQuality => Application.isPlaying ? BeetleGlassPreferences.Quality : editorPreview;
        public Material RealisticMaterial => realisticMaterial;
        public Material LowMaterial => lowMaterial;

        public void Configure(Material realistic, Material low, MeshRenderer[] glassPanes)
        {
            realisticMaterial = realistic; lowMaterial = low; panes = glassPanes;
            ApplyQuality();
        }
        void OnEnable()
        {
            ApplyQuality();
            BeetleGlassPreferences.Changed += ApplyQuality;
            if (Application.isPlaying)
            {
                // URP caches this component before beginCameraRendering. Install it before
                // the first frame, including when this prefab is used in a new scene.
                foreach (var root in gameObject.scene.GetRootGameObjects())
                    foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                        if (camera.cullingMask != 0) camera.GetUniversalAdditionalCameraData();
            }
            GlassCameraTextures.Register(this);
        }
        void OnDisable()
        {
            BeetleGlassPreferences.Changed -= ApplyQuality;
            GlassCameraTextures.Unregister(this);
        }
        void OnValidate() => ApplyQuality();
        void ApplyQuality()
        {
            var material = ActiveQuality == BeetleGlassQuality.Realistic ? realisticMaterial : lowMaterial;
            if (!material) return; // Newly added authoring component has no bindings yet.
            foreach (var pane in panes) pane.sharedMaterial = material;
        }
        internal bool VisibleTo(Camera camera)
        {
            if (gameObject.scene != camera.gameObject.scene || !isActiveAndEnabled) return false;
            foreach (var pane in panes)
                if (pane.enabled && pane.gameObject.activeInHierarchy && (camera.cullingMask & (1 << pane.gameObject.layer)) != 0) return true;
            return false;
        }
    }

    /// <summary>Only the rendering camera borrows scene textures; never change the shared pipeline asset.</summary>
    static class GlassCameraTextures
    {
        struct OriginalOptions
        {
            public UniversalAdditionalCameraData data;
            public CameraOverrideOption color, depth;
        }
        static readonly List<HusbandryGlassSurface> surfaces = new List<HusbandryGlassSurface>();
        static readonly Dictionary<Camera, OriginalOptions> borrowed = new Dictionary<Camera, OriginalOptions>();

        public static void Register(HusbandryGlassSurface surface)
        {
            if (surfaces.Contains(surface)) return;
            if (surfaces.Count == 0)
            {
                RenderPipelineManager.beginCameraRendering += Begin;
                RenderPipelineManager.endCameraRendering += End;
            }
            surfaces.Add(surface);
        }
        public static void Unregister(HusbandryGlassSurface surface)
        {
            surfaces.Remove(surface);
            if (surfaces.Count != 0) return;
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            foreach (var options in borrowed.Values) Restore(options);
            borrowed.Clear();
        }
        static void Begin(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game) return;
            bool visible = false, realistic = false;
            foreach (var surface in surfaces)
                if (surface.VisibleTo(camera)) { visible = true; realistic |= surface.ActiveQuality == BeetleGlassQuality.Realistic; }
            if (!visible || !camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)) return;
            borrowed.Add(camera, new OriginalOptions { data = data, color = data.requiresColorOption, depth = data.requiresDepthOption });
            // Low quality explicitly skips the otherwise globally enabled opaque copy.
            // Depth remains available to unrelated features such as SSAO and depth of field.
            data.requiresColorOption = realistic ? CameraOverrideOption.On : CameraOverrideOption.Off;
            if (realistic) data.requiresDepthOption = CameraOverrideOption.On;
        }
        static void End(ScriptableRenderContext context, Camera camera)
        {
            if (!borrowed.TryGetValue(camera, out var options)) return;
            Restore(options); borrowed.Remove(camera);
        }
        static void Restore(OriginalOptions options)
        {
            if (!options.data) return; // A scene can be unloaded while a render scope is being disposed.
            options.data.requiresColorOption = options.color;
            options.data.requiresDepthOption = options.depth;
        }
    }
}
