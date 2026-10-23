using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace BeetleArena.Campaign
{
    /// <summary>Owned geometry and animation for a placed ornament. No colliders, inventory or save mutations.</summary>
    public sealed class CampaignDecorationVisuals : MonoBehaviour
    {
        public Transform Shape { get; private set; }
        public string ItemId { get; private set; }
        public Bounds Bounds
        {
            get { var result = parts[0].GetComponent<Renderer>().bounds; foreach (var part in parts) result.Encapsulate(part.GetComponent<Renderer>().bounds); return result; }
        }
        readonly List<Transform> parts = new List<Transform>();
        readonly List<Vector3> restScales = new List<Vector3>();
        readonly List<Vector3> restPositions = new List<Vector3>();
        Material material;
        MaterialPropertyBlock colors;
        Tween reveal, spring;
        bool preview;
        float orientation;

        public static CampaignDecorationVisuals Create(string itemId, Transform parent, bool ghost = false)
        {
            var go = new GameObject(ghost ? "CareConstructionGhost" : "Construction " + itemId);
            go.transform.SetParent(parent, false);
            var visual = go.AddComponent<CampaignDecorationVisuals>();
            visual.ItemId = itemId; visual.preview = ghost;
            visual.material = new Material(Shader.Find(ghost ? "BeetleArena/ConstructionPreview" : "BeetleArena/Toon")) { name = ghost ? "Construction ghost" : "Construction surfaces" };
            if (!ghost) { visual.material.SetFloat("_OutlineWidth", .8f); visual.material.SetFloat("_SpecStrength", .25f); }
            visual.colors = new MaterialPropertyBlock();
            visual.Shape = new GameObject("Construction shape").transform; visual.Shape.SetParent(go.transform, false);
            visual.Build();
            foreach (var part in visual.parts) { visual.restScales.Add(part.localScale); visual.restPositions.Add(part.localPosition); }
            if (ghost) visual.SetPreviewValid(true);
            return visual;
        }

        public void SetOrientation(int degrees)
        { orientation = degrees; Shape.localRotation = Quaternion.Euler(0, orientation, 0); }

        public void SetPreviewValid(bool valid)
        {
            material.SetColor("_BaseColor", valid ? new Color(.21f, .92f, .66f, .48f) : new Color(1, .23f, .19f, .52f));
        }

        public void CompleteReveal()
        {
            reveal?.Kill(); spring?.Kill();
            reveal = spring = null;
            for (int i = 0; i < parts.Count; i++)
            {
                parts[i].localPosition = restPositions[i];
                parts[i].localScale = restScales[i];
            }
            Shape.localScale = Vector3.one;
        }

        public void Reveal(Action onComplete = null)
        {
            CompleteReveal();
            // Reveal low pieces first, then upper leaves/branches. Each piece rises and grows into place.
            float max = .1f;
            foreach (var position in restPositions) max = Mathf.Max(max, position.y);
            for (int i = 0; i < parts.Count; i++) { parts[i].localScale = Vector3.zero; parts[i].localPosition = restPositions[i] - Vector3.up * .13f; }
            float progress = 0;
            reveal = DOTween.To(() => progress, value =>
            {
                progress = value;
                for (int i = 0; i < parts.Count; i++)
                {
                    float start = restPositions[i].y / max * .52f;
                    float t = Mathf.Clamp01((value - start) / .40f);
                    float eased = 1 - Mathf.Pow(1 - t, 3);
                    parts[i].localScale = restScales[i] * eased;
                    parts[i].localPosition = restPositions[i] - Vector3.up * (.13f * (1 - eased));
                }
            }, 1, .85f).SetEase(Ease.Linear).SetUpdate(true).OnComplete(() => { reveal = null; Bounce(); onComplete?.Invoke(); });
        }

        public void Bounce()
        {
            spring?.Kill();
            float value = 0;
            spring = DOTween.To(() => value, x =>
            {
                value = x; float wave = Mathf.Sin(x * Mathf.PI * 3) * (1 - x) * .10f;
                Shape.localScale = new Vector3(1 + wave * .45f, 1 - wave, 1 + wave * .45f);
            }, 1, .34f).SetEase(Ease.OutQuad).SetUpdate(true).OnComplete(() => { Shape.localScale = Vector3.one; spring = null; });
        }

        public void Follow(Vector3 target, float deltaTime)
        {
            var delta = target - transform.position;
            transform.position = Vector3.Lerp(transform.position, target, 1 - Mathf.Exp(-28 * deltaTime));
            float stretch = Mathf.Min(.06f, delta.magnitude * .09f);
            if (spring == null) Shape.localScale = Vector3.Lerp(Shape.localScale, new Vector3(1 + stretch, 1 - stretch, 1 + stretch), 1 - Mathf.Exp(-18 * deltaTime));
            Shape.localRotation = Quaternion.Euler(0, orientation, 0);
        }

        void Build()
        {
            var stone = new Color(.46f, .50f, .43f); var darkStone = new Color(.25f, .32f, .29f);
            var wood = new Color(.43f, .27f, .12f); var bark = new Color(.25f, .15f, .075f);
            var green = new Color(.27f, .48f, .16f);
            switch (ItemId)
            {
                case "decor-stones":
                    Part("Stone low", PrimitiveType.Sphere, new Vector3(-.19f, .13f, -.10f), new Vector3(.47f, .27f, .40f), stone);
                    Part("Stone tall", PrimitiveType.Sphere, new Vector3(.17f, .22f, .10f), new Vector3(.42f, .44f, .48f), darkStone);
                    Part("Stone small", PrimitiveType.Sphere, new Vector3(.16f, .075f, -.28f), new Vector3(.22f, .15f, .20f), new Color(.62f, .60f, .46f)); break;
                case "decor-fern":
                    Part("Fern soil", PrimitiveType.Sphere, new Vector3(0, .055f, 0), new Vector3(.60f, .11f, .60f), bark);
                    for (int branch = 0; branch < 7; branch++)
                    {
                        float a = branch * Mathf.PI * 2 / 7; var direction = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        var tip = direction * .38f + Vector3.up * (.45f + branch % 3 * .10f);
                        Rod("Fern stem", Vector3.up * .04f, tip, .017f, green);
                        for (int leaf = 1; leaf <= 4; leaf++)
                        {
                            var at = Vector3.Lerp(Vector3.up * .04f, tip, leaf / 5f); var tangent = Vector3.Cross(Vector3.up, direction);
                            foreach (int side in new[] { -1, 1 })
                            {
                                var frond = Part("Fern leaflet", PrimitiveType.Sphere, at + tangent * side * .055f,
                                    new Vector3(.10f, .028f, .22f * (1 - leaf * .12f)), Color.Lerp(green, new Color(.42f, .61f, .22f), leaf * .14f));
                                frond.localRotation = Quaternion.LookRotation(tangent * side + direction * .55f, Vector3.up);
                            }
                        }
                    }
                    break;
                case "decor-driftwood":
                    Rod("Old trunk", new Vector3(-1.32f, .22f, 0), new Vector3(1.32f, .28f, .06f), .24f, wood);
                    Rod("Branch", new Vector3(-.25f, .32f, .03f), new Vector3(.32f, .71f, .31f), .075f, bark);
                    for (int i = 0; i < 5; i++) Rod("Bark ridge", new Vector3(-1.05f + i * .44f, .34f, -.16f), new Vector3(-.85f + i * .44f, .42f, .10f), .027f, bark);
                    break;
                case "decor-moss":
                    Part("Moss earth", PrimitiveType.Sphere, new Vector3(0, .06f, 0), new Vector3(.83f, .12f, .79f), bark);
                    for (int i = 0; i < 11; i++)
                    { float a = i * 2.39996f, r = .10f + i % 4 * .07f; Part("Moss cushion", PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * r, .14f, Mathf.Sin(a) * r), new Vector3(.28f, .19f, .24f), Color.Lerp(green, new Color(.48f, .57f, .19f), (i % 4) * .18f)); }
                    break;
                case "decor-water":
                    Part("Water dish base", PrimitiveType.Cylinder, new Vector3(0, .08f, 0), new Vector3(1.72f, .08f, .83f), stone);
                    Part("Water surface", PrimitiveType.Cylinder, new Vector3(0, .17f, 0), new Vector3(1.39f, .015f, .57f), new Color(.24f, .60f, .62f));
                    for (int i = 0; i < 16; i++)
                    { float a = i * Mathf.PI * 2 / 16; Part("Dish lip", PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * .77f, .16f, Mathf.Sin(a) * .34f), new Vector3(.22f, .17f, .17f), stone); }
                    break;
                case "decor-arch":
                    for (int i = 0; i < 12; i++)
                    {
                        float a = Mathf.PI * i / 12, b = Mathf.PI * (i + 1) / 12;
                        Rod("Arched wood", new Vector3(Mathf.Cos(a) * 1.18f, .14f + Mathf.Sin(a) * 1.3f, 0), new Vector3(Mathf.Cos(b) * 1.18f, .14f + Mathf.Sin(b) * 1.3f, 0), .23f, i % 3 == 0 ? bark : wood);
                    }
                    foreach (int side in new[] { -1, 1 }) Part("Arch footing", PrimitiveType.Sphere, new Vector3(side * 1.12f, .13f, 0), new Vector3(.54f, .26f, 1.48f), bark);
                    break;
                case "decor-leaf":
                    for (int i = 0; i < 5; i++)
                    {
                        var leaf = Part("Dry leaf", PrimitiveType.Sphere, new Vector3((i % 3 - 1) * .13f, .055f + i * .018f, (i / 3 - .5f) * .19f),
                            new Vector3(.30f, .035f, .61f), Color.Lerp(new Color(.63f, .39f, .13f), new Color(.38f, .24f, .10f), i * .17f));
                        leaf.localRotation = Quaternion.Euler(3, i * 53, 4);
                    }
                    break;
                case "decor-lamp":
                    Part("Lamp foot", PrimitiveType.Cylinder, new Vector3(0, .07f, 0), new Vector3(.47f, .07f, .47f), darkStone);
                    Rod("Lamp post", Vector3.up * .10f, Vector3.up * 1.12f, .05f, wood);
                    Part("Lantern base", PrimitiveType.Cylinder, Vector3.up * 1.04f, new Vector3(.48f, .05f, .48f), darkStone);
                    Part("Lantern amber", PrimitiveType.Sphere, Vector3.up * 1.25f, new Vector3(.31f, .38f, .31f), new Color(1, .74f, .22f));
                    Part("Lantern cap", PrimitiveType.Sphere, Vector3.up * 1.47f, new Vector3(.61f, .15f, .61f), wood);
                    foreach (int side in new[] { -1, 1 }) Rod("Lantern frame", new Vector3(side * .18f, 1.05f, 0), new Vector3(side * .18f, 1.44f, 0), .022f, darkStone);
                    break;
            }
        }

        Transform Part(string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type); go.name = (preview ? "CareGhost " : "Construction ") + name;
            go.transform.SetParent(Shape, false); go.transform.localPosition = position; go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            if (!preview) { colors.Clear(); colors.SetColor("_BaseColor", color); colors.SetColor("_Color", color); renderer.SetPropertyBlock(colors); }
            parts.Add(go.transform); return go.transform;
        }
        void Rod(string name, Vector3 from, Vector3 to, float radius, Color color)
        {
            var part = Part(name, PrimitiveType.Cylinder, (from + to) * .5f, new Vector3(radius * 2, Vector3.Distance(from, to) * .5f, radius * 2), color);
            part.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
        }
        void OnDestroy() { reveal?.Kill(); spring?.Kill(); if (material) Destroy(material); }
    }
}
