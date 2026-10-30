using System;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// Explicit seasonal bindings for an imported tree. Assign leaf-only MeshRenderers or
    /// SkinnedMeshRenderers; the trunk stays outside these groups. No names or hierarchy are assumed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HusbandryTreeModelAdapter : MonoBehaviour
    {
        [Tooltip("Leaf-only renderers. Separate groups allow ordinary materials to shed foliage gradually.")]
        public Renderer[] foliage = Array.Empty<Renderer>();
        [Tooltip("Optional snow meshes. An empty group is valid.")]
        public Renderer[] snowCaps = Array.Empty<Renderer>();
        [Tooltip("Optional spring buds or blossoms. An empty group is valid.")]
        public Renderer[] buds = Array.Empty<Renderer>();

        public bool HasBindings => HasRenderer(foliage) || HasRenderer(snowCaps) || HasRenderer(buds);

        /// <summary>
        /// Replaces only seasonal renderer bindings. Preserves model transforms, shared materials,
        /// skinning and the existing daylight/weather system. Call again after changing material slots.
        /// </summary>
        public void Bind(HusbandrySeasons seasons)
        {
            if (!seasons) throw new ArgumentNullException(nameof(seasons));
            seasons.foliage = foliage ?? Array.Empty<Renderer>();
            seasons.snowCaps = snowCaps ?? Array.Empty<Renderer>();
            seasons.buds = buds ?? Array.Empty<Renderer>();
            seasons.RefreshModelBindings();
        }

        static bool HasRenderer(Renderer[] renderers)
        {
            if (renderers == null) return false;
            for (int i = 0; i < renderers.Length; i++) if (renderers[i]) return true;
            return false;
        }
    }
}
