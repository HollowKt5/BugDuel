using System;
using UnityEngine;

namespace BeetleArena
{
    public enum BeetleContactRole { Leg, Body, HeadHorn }
    // Invisible collision proxies for the actual model. shapes are used for physics; surfaces retain visible meshes for diagnostics only.
    // Skinned Renderer.bounds cannot serve as bounding boxes for individual leg proxies; the two arrays do not require 1:1 correspondence.
    public sealed class BeetleContactRig : MonoBehaviour
    {
        /// <summary>
        /// Model contact semantics, independent of GameObject names and array ordering. New species should use explicit bindings; unbound legacy models retain name fallbacks.
        /// legIndex is ordered Left Front/Middle/Hind, Right Front/Middle/Hind; ignored for non-leg parts.
        /// weapon identifies horn feedback; it does not grant unimplemented attack mechanics like mandibles out of nowhere.
        /// </summary>
        [Serializable]
        public sealed class ShapeBinding
        {
            public Collider shape;
            public BeetleContactRole role = BeetleContactRole.Body;
            [Range(-1, 5)] public int legIndex = -1;
            public bool weapon;
        }

        // Capsule and convex mesh proxies bound by the model builder, moving with their respective bones.
        public Collider[] shapes;
        [Tooltip("Optional explicit collision semantics. Unbound shapes retain legacy name mapping; unknown names are body contacts.")]
        public ShapeBinding[] shapeBindings = Array.Empty<ShapeBinding>();
        // Retains visible Renderers from the source model to verify correct migration of the full model.
        public Renderer[] surfaces;
        // Runtime part AABB cache; worldBounds is the unified AABB covering the entire beetle.
        [NonSerialized] public Bounds[] bounds;
        [NonSerialized] public Bounds worldBounds;
        // Cache references only. Read exact world poses for narrow-phase tests so repeated
        // root translations cannot accumulate rounding error in an independent pose cache.
        [NonSerialized] public Transform[] shapeTransforms;
        BeetleContactRole[] roles;
        int[] legIndices;
        bool[] weapons;
        Collider[] classifiedShapes;
        BindingSnapshot[] classifiedBindings;
        struct BindingSnapshot
        {
            public bool present;
            public Collider shape;
            public BeetleContactRole role;
            public int legIndex;
            public bool weapon;
            public BindingSnapshot(ShapeBinding binding)
            {
                present = binding != null;
                shape = binding?.shape;
                role = binding != null ? binding.role : BeetleContactRole.Body;
                legIndex = binding != null ? binding.legIndex : -1;
                weapon = binding != null && binding.weapon;
            }
            public bool Matches(ShapeBinding binding) => present == (binding != null) &&
                (!present || ReferenceEquals(shape, binding.shape) && role == binding.role &&
                    legIndex == binding.legIndex && weapon == binding.weapon);
        }
        // Model builders supply canonical names. Cached once per match on first access to avoid string matching across collision pairs.
        public BeetleContactRole Role(int index) => roles[index];
        public int LegIndex(int index) => legIndices[index];
        // Thoracic horns remain Body for physics resolution, but presentation sampling recognizes them as weapons; preserves enum and serialized values.
        public bool IsHorn(int index) => weapons[index];

        /// <summary>Explicitly invalidates after renaming legacy model parts; proxies and explicit binding updates are auto-detected, avoiding reading name on hot paths.</summary>
        public void InvalidateShapeMetadata() => classifiedShapes = null;
        void OnValidate() => InvalidateShapeMetadata();
        bool ShapeMetadataCurrent()
        {
            if (classifiedShapes == null || classifiedShapes.Length != shapes.Length || roles == null ||
                classifiedBindings == null || classifiedBindings.Length != (shapeBindings?.Length ?? 0)) return false;
            for (int i = 0; i < shapes.Length; i++)
                if (!shapes[i] || !ReferenceEquals(classifiedShapes[i], shapes[i])) return false;
            for (int i = 0; i < classifiedBindings.Length; i++)
                if (!classifiedBindings[i].Matches(shapeBindings[i])) return false;
            return true;
        }
        void EnsureShapeMetadata()
        {
            if (ShapeMetadataCurrent()) return;
            // Keeps cache invalid on validation failure; safely rebuilt on next call once configuration is resolved.
            classifiedShapes = null;
            roles = new BeetleContactRole[shapes.Length];
            legIndices = new int[shapes.Length]; weapons = new bool[shapes.Length];
            int count = shapeBindings?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                var binding = shapeBindings[i];
                // Empty rows added in the Inspector can be staged; bindings with colliders assigned must be explicit and valid.
                if (binding == null || !binding.shape) continue;
                if (binding.role != BeetleContactRole.Leg && binding.role != BeetleContactRole.Body &&
                    binding.role != BeetleContactRole.HeadHorn)
                    throw new InvalidOperationException("Invalid contact role at binding " + i);
                if (binding.role == BeetleContactRole.Leg && (binding.legIndex < 0 || binding.legIndex > 5))
                    throw new InvalidOperationException("Leg contact binding requires a slot from 0 to 5 at binding " + i);
                for (int j = 0; j < i; j++)
                    if (shapeBindings[j] != null && shapeBindings[j].shape == binding.shape)
                        throw new InvalidOperationException("Duplicate collision shape binding at index " + i);
            }
            for (int i = 0; i < shapes.Length; i++)
            {
                if (!shapes[i]) throw new InvalidOperationException("Missing beetle collision shape at index " + i);
                ShapeBinding binding = null;
                for (int j = 0; j < count; j++)
                    if (shapeBindings[j] != null && shapeBindings[j].shape == shapes[i])
                    { binding = shapeBindings[j]; break; }
                if (binding != null)
                {
                    roles[i] = binding.role;
                    legIndices[i] = binding.role == BeetleContactRole.Leg ? binding.legIndex : -1;
                    weapons[i] = binding.weapon;
                    continue;
                }
                string n = shapes[i].name;
                roles[i] = n.StartsWith("Cephalic horn", StringComparison.Ordinal) || n.StartsWith("Horn fork", StringComparison.Ordinal) || n.StartsWith("Horn tip", StringComparison.Ordinal)
                    ? BeetleContactRole.HeadHorn : BeetleContactRole.Body;
                legIndices[i] = LegacyLegIndex(n);
                if (roles[i] != BeetleContactRole.HeadHorn && legIndices[i] >= 0) roles[i] = BeetleContactRole.Leg;
                weapons[i] = roles[i] == BeetleContactRole.HeadHorn || n == "Thoracic horn";
            }
            classifiedShapes = (Collider[])shapes.Clone();
            classifiedBindings = new BindingSnapshot[count];
            for (int i = 0; i < count; i++) classifiedBindings[i] = new BindingSnapshot(shapeBindings[i]);
        }
        static int LegacyLegIndex(string name)
        {
            int offset = name.StartsWith("Left ", StringComparison.Ordinal) ? 0 :
                name.StartsWith("Right ", StringComparison.Ordinal) ? 3 : -1;
            if (offset < 0) return -1;
            // Only accepts segment names from the model builder; e.g. "Left front mandible" remains an unknown organ.
            string part = name.Substring(offset == 0 ? 5 : 6);
            int pair, prefixLength;
            if (part.StartsWith("front ", StringComparison.Ordinal)) { pair = 0; prefixLength = 6; }
            else if (part.StartsWith("middle ", StringComparison.Ordinal)) { pair = 1; prefixLength = 7; }
            else if (part.StartsWith("hind ", StringComparison.Ordinal)) { pair = 2; prefixLength = 5; }
            else return -1;
            string segment = part.Substring(prefixLength);
            return segment == "femur contact" || segment == "tibia contact" || segment == "tarsus contact" ||
                segment == "contact" ? offset + pair : -1;
        }
        // Callers must call Physics.SyncTransforms before reading proxy Collider.bounds; avoids per-frame array allocations.
        public void CacheBounds()
        {
            if (shapes == null || shapes.Length == 0) throw new InvalidOperationException("No collision shapes bound.");
            if (bounds == null || bounds.Length != shapes.Length) bounds = new Bounds[shapes.Length];
            if (shapeTransforms == null || shapeTransforms.Length != shapes.Length)
                shapeTransforms = new Transform[shapes.Length];
            EnsureShapeMetadata();
            for (int i = 0; i < shapes.Length; i++)
            {
                bounds[i] = shapes[i].bounds;
                shapeTransforms[i] = shapes[i].transform;
            }
            worldBounds = bounds[0];
            for (int i = 1; i < bounds.Length; i++) worldBounds.Encapsulate(bounds[i]);
        }
        // For pure root translation, translating the root and cached AABBs by the same delta suffices without querying every Renderer.
        // If local joints, rotations, or scales change, CacheBounds must be called again; this method only translates existing bounds.
        public void Translate(Vector3 movement)
        {
            transform.position += movement;
            for (int i = 0; i < bounds.Length; i++)
                bounds[i].center += movement;
            worldBounds.center += movement;
        }
    }
}
