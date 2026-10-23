using System.Collections.Generic;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    /// <summary>Switches observation scenery and anatomy without changing an individual's data or its simulation.</summary>
    public sealed class CampaignLifeStageDisplay : MonoBehaviour
    {
        public CampaignLifeStage CurrentStage { get; private set; } = CampaignLifeStage.ActiveAdult;
        public bool HasStageEnvironment => stageRoot && stageRoot.activeSelf;
        public bool UsesAdultModel => !HasStageEnvironment || CurrentStage == CampaignLifeStage.DormantAdult;
        public int Revision { get; private set; }
        public CampaignLifeStageVisualProfile VisualProfile => profile;
        public Transform StageRoot => stageRoot ? stageRoot.transform : null;
        public Transform SpecimenRoot => bodyRoot;
        public float GroundHeight => anchor.y + .04f;
        public Vector3 AdultPosition => restingAdultPosition??anchor + (configuration != null ? configuration.bodyOffset : Vector3.zero) + Vector3.up * .04f;
        public Quaternion AdultRotation => Quaternion.Euler((configuration != null ? configuration.bodyEuler : Vector3.zero)+Vector3.up*livingRotation);
        public Bounds ObservationBounds => Measure(CurrentStage == CampaignLifeStage.ActiveAdult || CurrentStage == CampaignLifeStage.DormantAdult ? adultRenderers : bodyRenderers,
            bodyRoot ? bodyRoot.position : anchor,true);
        public Bounds EnvironmentBounds
        {
            get { var bounds = Measure(stageRenderers, anchor); if (CurrentStage == CampaignLifeStage.DormantAdult) bounds.Encapsulate(ObservationBounds); return bounds; }
        }

        /// <summary>The horizontal substrate surface, excluding walls and the animal. Shared by the construction grid and its ray plane.</summary>
        public bool TryGetConstructionSurface(out Vector3 center, out Vector2 size, out Quaternion rotation)
        {
            center = anchor; size = Vector2.zero; rotation = Quaternion.identity;
            if (!HasStageEnvironment) return false;
            center = stageRoot.transform.TransformPoint(configuration.environmentOffset + Vector3.up * .04f);
            size = new Vector2(configuration.enclosureSize.x, configuration.enclosureSize.z) * .94f;
            if (CurrentStage == CampaignLifeStage.Larva1 || CurrentStage == CampaignLifeStage.Larva2 || CurrentStage == CampaignLifeStage.Larva3)
                size = Vector2.one * Mathf.Min(size.x, size.y);
            size = Vector2.Scale(size, new Vector2(stageRoot.transform.lossyScale.x, stageRoot.transform.lossyScale.z));
            rotation = Quaternion.Euler(0, stageRoot.transform.eulerAngles.y, 0);
            return true;
        }

        CampaignSpecimen owner;
        CampaignLifeStageVisualProfile profile;
        CampaignStageVisual configuration;
        bool ownsProfile;
        GameObject stageRoot;
        Transform bodyRoot;
        Renderer[] adultRenderers, bodyRenderers, stageRenderers;
        readonly Dictionary<Renderer, bool> originalEnvironment = new Dictionary<Renderer, bool>();
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Transform> segments = new List<Transform>();
        readonly List<Vector3> segmentScales = new List<Vector3>();
        Material material;
        MaterialPropertyBlock colorBlock;
        Vector3 anchor;
        string individualId;
        int profileRevision;
        float animationTime;
        bool female;
        bool animalVisible=true;
        Vector3? livingCenter,restingAdultPosition;
        int livingRotation;

        public void SetAnimalVisible(bool visible)
        {
            animalVisible=visible;
            if(bodyRenderers!=null)foreach(var renderer in bodyRenderers)if(renderer)renderer.enabled=visible;
        }
        public void SetLivingPosition(Vector3 center,int rotation)
        {livingCenter=center;livingRotation=rotation;ApplyLivingPosition();}
        void ApplyLivingPosition()
        {
            if(!livingCenter.HasValue)return;
            if(bodyRoot)
            {
                bodyRoot.localRotation=Quaternion.Euler(configuration.bodyEuler+Vector3.up*livingRotation);
                var delta=livingCenter.Value-ObservationBounds.center;delta.y=0;bodyRoot.position+=delta;
            }
            else if(CurrentStage==CampaignLifeStage.DormantAdult)
            {
                owner.transform.rotation=AdultRotation;
                var delta=livingCenter.Value-ObservationBounds.center;delta.y=0;
                var origin=anchor+configuration.bodyOffset+Vector3.up*.04f;
                restingAdultPosition=new Vector3(owner.transform.position.x+delta.x,origin.y,owner.transform.position.z+delta.z);
            }
        }

        /// <summary>Creates a static immature specimen for a frame. Adult stages keep using the original articulated prefab.</summary>
        public static GameObject CreatePreservedBody(CampaignLifeStage stage, Transform parent, CampaignLifeStageVisualProfile visuals = null, bool female = false)
        {
            var go = new GameObject("Preserved " + stage); go.transform.SetParent(parent, false);
            var display = go.AddComponent<CampaignLifeStageDisplay>();
            display.InitializeProfile(visuals); display.CurrentStage = stage; display.configuration = display.profile.For(stage); display.female = female;
            display.bodyRoot = new GameObject("CarePreservedAnatomy").transform;
            display.bodyRoot.SetParent(go.transform, false);
            display.bodyRoot.localRotation = Quaternion.Euler(display.configuration.bodyEuler);
            display.BuildBody(display.bodyRoot);
            display.bodyRenderers = display.bodyRoot.GetComponentsInChildren<Renderer>();
            display.bodyRoot.localScale = Vector3.one * display.configuration.bodyLength;
            return go;
        }

        void InitializeProfile(CampaignLifeStageVisualProfile visuals)
        {
            profile = visuals ? visuals : Resources.Load<CampaignLifeStageVisualProfile>(CampaignLifeStageVisualProfile.ResourceName);
            if (!profile) { profile = ScriptableObject.CreateInstance<CampaignLifeStageVisualProfile>(); ownsProfile = true; }
            material = new Material(Shader.Find("BeetleArena/Toon")) { name = "Life stage observation surfaces" };
            material.SetFloat("_OutlineWidth", .75f); material.SetFloat("_SpecStrength", .18f);
            colorBlock = new MaterialPropertyBlock();
        }

        public void Bind(CampaignSpecimen specimen, CampaignLifeStageVisualProfile visuals)
        {
            owner = specimen;
            InitializeProfile(visuals);
            adultRenderers = specimen.GetComponentsInChildren<Renderer>(true);
            var floor = specimen.enclosureFloor;
            anchor = floor ? new Vector3(floor.bounds.center.x, floor.bounds.max.y, floor.bounds.center.z) : specimen.transform.position;
            if (floor && floor.transform.parent)
                foreach (var renderer in floor.transform.parent.GetComponentsInChildren<Renderer>(true))
                    if (!renderer.transform.IsChildOf(specimen.transform) && renderer.name != "Backdrop" && !renderer.name.StartsWith("Care"))
                        originalEnvironment.Add(renderer, renderer.enabled);
        }

        /// <returns>True when scenery or selected individual changed and the camera/pose should be refit.</returns>
        public bool Present(CampaignBeetle individual, CampaignSession session, float dt)
        {
            if (individual == null || individual.stage == CampaignLifeStage.ActiveAdult)
            {
                bool changed = HasStageEnvironment;
                if (changed) { ClearStage(); RestoreEnvironment(); Revision++; }
                CurrentStage = CampaignLifeStage.ActiveAdult; individualId = individual?.id;
                return changed;
            }
            bool rebuild = !HasStageEnvironment || CurrentStage != individual.stage || individualId != individual.id || profileRevision != profile.revision;
            if (rebuild)
            {
                ClearStage();
                CurrentStage = individual.stage; individualId = individual.id; profileRevision = profile.revision; female = individual.female;
                configuration = profile.For(CurrentStage); animationTime = 0;
                foreach (var pair in originalEnvironment) if (pair.Key) pair.Key.enabled = false;
                BuildStage(); Revision++;
            }
            if (bodyRoot)
            {
                float growth = Mathf.Lerp(configuration.startScale, 1, (float)session.StageProgress(individual));
                bodyRoot.localScale = Vector3.one * configuration.bodyLength * growth;
                if (!individual.dead) animationTime += dt;
                // Small, local breathing only. The visible animal never walks out of its bottle or advances domain time.
                for (int i = 0; i < segments.Count; i++)
                {
                    float breath = individual.dead ? 0 : Mathf.Sin(animationTime * 1.5f - i * .42f) * profile.larvaBreathing;
                    segments[i].localScale = Vector3.Scale(segmentScales[i], new Vector3(1, 1 + breath, 1));
                }
            }
            ApplyLivingPosition();SetAnimalVisible(animalVisible);
            return rebuild;
        }

        void BuildStage()
        {
            stageRoot = new GameObject("CareLifeStageEnvironment");
            stageRoot.transform.SetParent(owner.transform.parent, false);
            stageRoot.transform.position = anchor;
            var environmentRoot = new GameObject("CareStageContainer").transform;
            environmentRoot.SetParent(stageRoot.transform, false); environmentRoot.localPosition = configuration.environmentOffset;
            if (configuration.environmentPrefab) Instantiate(configuration.environmentPrefab, environmentRoot, false);
            else if (CurrentStage == CampaignLifeStage.Egg) BuildIncubator(environmentRoot);
            else if (CurrentStage == CampaignLifeStage.Larva1 || CurrentStage == CampaignLifeStage.Larva2 || CurrentStage == CampaignLifeStage.Larva3) BuildBottle(environmentRoot);
            else BuildChamber(environmentRoot);

            if (CurrentStage != CampaignLifeStage.DormantAdult)
            {
                bodyRoot = new GameObject("CareStageAnimal").transform;
                bodyRoot.SetParent(stageRoot.transform, false);
                bodyRoot.localPosition = configuration.bodyOffset + Vector3.up * .045f;
                bodyRoot.localRotation = Quaternion.Euler(configuration.bodyEuler);
                BuildBody(bodyRoot);
                bodyRenderers = bodyRoot.GetComponentsInChildren<Renderer>();
            }
            stageRenderers = stageRoot.GetComponentsInChildren<Renderer>();
        }

        void BuildBody(Transform root)
        {
            if (configuration.specimenPrefab) Instantiate(configuration.specimenPrefab, root, false);
            else if (CurrentStage == CampaignLifeStage.Egg) BuildEggs(root);
            else if (CurrentStage == CampaignLifeStage.Pupa) BuildPupa(root);
            else BuildLarva(root, CurrentStage == CampaignLifeStage.Prepupa);
        }

        void BuildIncubator(Transform root)
        {
            var size = configuration.enclosureSize;
            Part(root, "IncubationTray", PrimitiveType.Cube, new Vector3(0, -.18f, 0), new Vector3(size.x, .35f, size.z), profile.ceramicColor);
            Part(root, "MoistHumus", PrimitiveType.Cube, Vector3.zero, new Vector3(size.x - .24f, .08f, size.z - .24f), profile.humusColor);
            // An intentionally open front makes the egg cluster visible at every permitted camera elevation.
            Part(root, "TrayBack", PrimitiveType.Cube, new Vector3(0, size.y * .5f, size.z * .5f - .08f),
                new Vector3(size.x, size.y, .16f), profile.rimColor);
            foreach (int side in new[] { -1, 1 })
                Part(root, "TraySide", PrimitiveType.Cube, new Vector3(side * (size.x * .5f - .08f), size.y * .23f, 0),
                    new Vector3(.16f, size.y * .46f, size.z), profile.ceramicColor);
            Part(root, "TrayFrontLip", PrimitiveType.Cube, new Vector3(0, .09f, -size.z * .5f + .07f),
                new Vector3(size.x, .18f, .14f), profile.rimColor);
            ScatterSubstrate(root, size.x * .45f, size.z * .45f, false);
            // Vent holes and a lifted rear lid communicate an incubation box without obscuring the eggs.
            for (int i = 0; i < 9; i++)
                Part(root, "Vent", PrimitiveType.Sphere, new Vector3((i - 4) * size.x / 11, size.y * .72f, size.z * .5f - .17f),
                    new Vector3(.10f, .10f, .035f), profile.soilColor);
        }

        void BuildBottle(Transform root)
        {
            var size = configuration.enclosureSize;
            float radius = Mathf.Min(size.x, size.z) * .5f;
            Part(root, "BottleBase", PrimitiveType.Cylinder, new Vector3(0, -.17f, 0), new Vector3(radius * 2, .19f, radius * 2), profile.ceramicColor);
            Part(root, "SubstrateCutFace", PrimitiveType.Cylinder, new Vector3(0, -.01f, 0), new Vector3(radius * 1.9f, .05f, radius * 1.9f), profile.humusColor);
            // Rear half of the bottle remains; the near half is cut away. No glass face hides the larva.
            ArcWall(root, "BottleRear", radius, size.y, 0, 180, .09f, profile.rimColor);
            ArcWall(root, "RearSubstrate", radius * .92f, size.y * .66f, 8, 172, .13f,
                CurrentStage == CampaignLifeStage.Larva1 ? profile.humusColor : profile.woodColor);
            ArcLine(root, "BottleLip", radius, size.y, 0, 180, .085f, profile.ceramicColor);
            ArcLine(root, "BottleShoulder", radius, size.y - .17f, 0, 180, .055f, profile.ceramicColor);
            foreach (int side in new[] { -1, 1 })
                Rod(root, "BottleCutEdge", new Vector3(side * radius, .04f, 0), new Vector3(side * radius, size.y, 0), .045f, profile.rimColor);
            ScatterSubstrate(root, radius * .85f, radius * .85f, true);
            // Fine pale filaments on the rear substrate distinguish a fungal bottle from the egg humus tray.
            for (int i = 0; CurrentStage == CampaignLifeStage.Larva3 && i < 16; i++)
            {
                float angle = (18 + i * 9.3f) * Mathf.Deg2Rad;
                var from = new Vector3(Mathf.Cos(angle) * radius * .86f, .22f + Fraction(i * .618f) * size.y * .49f, Mathf.Sin(angle) * radius * .86f);
                var to = from + new Vector3(.16f * Mathf.Sin(i), .11f, -.04f);
                Rod(root, "Mycelium", from, to, .014f, profile.myceliumColor);
            }
        }

        void BuildChamber(Transform root)
        {
            var size = configuration.enclosureSize;
            Part(root, "ChamberSoilBlock", PrimitiveType.Cube, new Vector3(0, -.20f, 0), new Vector3(size.x, .45f, size.z), profile.soilColor);
            Part(root, "ChamberFloor", PrimitiveType.Sphere, new Vector3(0, -.045f, 0), new Vector3(size.x * .86f, .17f, size.z * .89f), profile.humusColor);
            // The exposed section retains a compacted earthen half-shell and low cut edges, open toward -Z.
            var shell = new GameObject("CarePupalChamberShell").transform; shell.SetParent(root, false);
            shell.localScale = new Vector3(1, 1, size.z / size.x);
            float radius = size.x * .45f;
            ArcWall(shell, "CompactedEarthWall", radius, size.y, 0, 180, .28f, profile.humusColor);
            ArcLine(shell, "ChamberRim", radius, size.y, 0, 180, .13f, profile.woodColor);
            foreach (int side in new[] { -1, 1 })
                Part(root, "CutawaySection", PrimitiveType.Cube, new Vector3(side * (size.x * .5f - .18f), .30f, -size.z * .20f),
                    new Vector3(.36f, .60f, size.z * .40f), profile.humusColor);
            for (int i = 0; i < 15; i++)
            {
                float angle = (10 + i * 11.5f) * Mathf.Deg2Rad;
                Part(root, "CompactedSoilGrain", PrimitiveType.Sphere,
                    new Vector3(Mathf.Cos(angle) * radius * .94f, .25f + Fraction(i * .37f) * size.y * .66f, Mathf.Sin(angle) * radius * .94f * size.z / size.x),
                    new Vector3(.17f, .12f, .12f), Color.Lerp(profile.soilColor, profile.woodColor, Fraction(i * .73f)));
            }
        }

        void BuildEggs(Transform root)
        {
            for (int i = 0; i < profile.eggCount; i++)
            {
                float radius = i == 0 ? 0 : .12f + Mathf.Sqrt(i) * .12f;
                float angle = i * 2.399963f;
                float size = .18f + Fraction(i * .43f) * .045f;
                var egg = Part(root, "EggPearl", PrimitiveType.Sphere,
                    new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius),
                    Vector3.one, Color.Lerp(configuration.bodyColor, Color.white, Fraction(i * .31f) * .15f));
                // Bake the tilted ellipsoid before seating it: a rotated sphere's conservative AABB is not its contact point.
                var filter = egg.GetComponent<MeshFilter>();
                var mesh = Instantiate(filter.sharedMesh); mesh.name = "Care resting egg";
                var shape = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(i * 11 % 27, i * 47, i * 7 % 19), new Vector3(size, size * 1.10f, size * .90f));
                var normalTransform = shape.inverse.transpose;
                var vertices = mesh.vertices; var normals = mesh.normals;
                for (int v = 0; v < vertices.Length; v++)
                { vertices[v] = shape.MultiplyPoint3x4(vertices[v]); normals[v] = normalTransform.MultiplyVector(normals[v]).normalized; }
                mesh.vertices = vertices; mesh.normals = normals; mesh.RecalculateBounds();
                filter.sharedMesh = mesh; meshes.Add(mesh);
                egg.localPosition += Vector3.up * -mesh.bounds.min.y;
            }
        }

        void BuildLarva(Transform root, bool prepupa)
        {
            int count = profile.larvaSegments;
            var headColor = new Color(.42f, .21f, .075f);
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1), angle = (55 + t * (prepupa ? 215 : 250)) * Mathf.Deg2Rad;
                float radius = prepupa ? .265f : .31f;
                float diameter = .15f + Mathf.Sin(t * Mathf.PI) * .045f;
                var point = new Vector3(Mathf.Cos(angle) * radius, diameter * .44f, Mathf.Sin(angle) * radius);
                var segment = Part(root, "LarvaSegment", PrimitiveType.Sphere, point,
                    new Vector3(diameter, diameter * .86f, diameter * 1.06f), Color.Lerp(configuration.bodyColor, profile.woodColor, (1 - t) * .08f));
                segment.localRotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
                segments.Add(segment); segmentScales.Add(segment.localScale);
                if (i > 0 && i < count - 2)
                {
                    var normal = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    Part(root, "LarvalSpiracle", PrimitiveType.Sphere, point + normal * diameter * .46f + Vector3.up * .015f,
                        new Vector3(.018f, .027f, .018f), headColor);
                }
                if (i == count - 1)
                {
                    var tangent = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle));
                    var head = point + tangent * .10f;
                    Part(root, "LarvaHeadCapsule", PrimitiveType.Sphere, head, new Vector3(.18f, .16f, .18f), headColor);
                    var side = Vector3.Cross(Vector3.up, tangent);
                    foreach (int sign in new[] { -1, 1 })
                        Rod(root, "LarvaMandible", head + tangent * .065f + side * sign * .05f,
                            head + tangent * .13f + side * sign * .018f, .018f, profile.soilColor);
                }
                if (i >= count - 4 && i < count - 1)
                    foreach (int sign in new[] { -1, 1 })
                    {
                        var leg = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * sign;
                        var start = point + leg * .055f;
                        var joint = point + leg * .13f - Vector3.up * .035f;
                        Rod(root, "LarvaThoracicLeg", start, joint, .013f, headColor);
                        Rod(root, "LarvaClaw", joint, joint + leg * .025f - Vector3.up * .035f, .008f, profile.soilColor);
                    }
            }
        }

        void BuildPupa(Transform root)
        {
            Color amber = configuration.bodyColor, fold = Color.Lerp(amber, profile.soilColor, .28f);
            for (int i = 0; i < 7; i++)
            {
                float t = i / 6f, width = Mathf.Lerp(.12f, .36f, Mathf.Sin((t * .8f + .1f) * Mathf.PI * .5f));
                Part(root, "PupalAbdominalRing", PrimitiveType.Sphere, new Vector3(0, .13f, -.41f + i * .069f),
                    new Vector3(width, width * .70f, .125f), Color.Lerp(amber, fold, (i % 2) * .14f));
            }
            Part(root, "PupalThorax", PrimitiveType.Sphere, new Vector3(0, .17f, .13f), new Vector3(.38f, .28f, .30f), amber);
            Part(root, "PupalHead", PrimitiveType.Sphere, new Vector3(0, .18f, .30f), new Vector3(.25f, .24f, .20f), amber);
            foreach (int side in new[] { -1, 1 })
            {
                var wing = Part(root, "FoldedWingCase", PrimitiveType.Sphere, new Vector3(side * .17f, .13f, -.015f),
                    new Vector3(.13f, .14f, .34f), Color.Lerp(amber, profile.woodColor, .20f));
                wing.localRotation = Quaternion.Euler(0, side * -12, side * 18);
                for (int pair = 0; pair < 3; pair++)
                {
                    float z = .21f - pair * .095f;
                    var hip = new Vector3(side * .13f, .12f, z);
                    var knee = new Vector3(side * (.25f + pair * .016f), .085f, z - .08f);
                    Rod(root, "PupalFoldedLeg", hip, knee, .022f, fold);
                    Rod(root, "PupalFoldedShin", knee, new Vector3(side * .12f, .07f, z - .16f), .017f, amber);
                }
                Rod(root, "PupalAntenna", new Vector3(side * .07f, .20f, .35f), new Vector3(side * .14f, .09f, .28f), .014f, fold);
            }
            if (!female)
            {
                Rod(root, "PupalHornBase", new Vector3(0, .21f, .34f), new Vector3(0, .30f, .47f), .045f, amber);
                Rod(root, "PupalHornTip", new Vector3(0, .30f, .47f), new Vector3(0, .28f, .54f), .023f, amber);
            }
        }

        void ScatterSubstrate(Transform root, float width, float depth, bool bottle)
        {
            for (int i = 0; i < profile.substratePieces; i++)
            {
                float angle = i * 2.399963f, radius = .60f + Fraction(i * .618034f) * .35f;
                float x = Mathf.Cos(angle) * width * radius, z = Mathf.Sin(angle) * depth * radius;
                bool fungal = bottle && CurrentStage == CampaignLifeStage.Larva3;
                bool fineHumus = CurrentStage == CampaignLifeStage.Larva1;
                var color = fungal && i % 5 == 0 ? profile.myceliumColor : Color.Lerp(profile.soilColor, fineHumus ? profile.humusColor : profile.woodColor, Fraction(i * .381f));
                var chip = Part(root, bottle ? "FungalWoodChip" : "HumusGrain", PrimitiveType.Cube,
                    new Vector3(x, .075f, z), new Vector3(.10f + Fraction(i * .31f) * .20f, .04f, .07f + Fraction(i * .77f) * .08f), color);
                chip.localRotation = Quaternion.Euler(5 + i % 13, i * 137.5f, i % 9);
                if (fineHumus) chip.localScale *= .65f;
            }
        }

        Transform Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type); go.name = "Care" + name;
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            Paint(go.GetComponent<Renderer>(), color); return go.transform;
        }

        void Paint(Renderer renderer, Color color)
        {
            renderer.sharedMaterial = material;
            colorBlock.Clear(); colorBlock.SetColor("_BaseColor", color); colorBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(colorBlock);
        }

        void Rod(Transform root, string name, Vector3 from, Vector3 to, float radius, Color color)
        {
            var segment = Part(root, name, PrimitiveType.Cylinder, (from + to) * .5f,
                new Vector3(radius * 2, Vector3.Distance(from, to) * .5f, radius * 2), color);
            segment.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
        }

        void ArcLine(Transform root, string name, float radius, float y, float start, float end, float thickness, Color color)
        {
            const int steps = 24;
            for (int i = 0; i < steps; i++)
            {
                float a = Mathf.Lerp(start, end, i / (float)steps) * Mathf.Deg2Rad;
                float b = Mathf.Lerp(start, end, (i + 1) / (float)steps) * Mathf.Deg2Rad;
                Rod(root, name, new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius),
                    new Vector3(Mathf.Cos(b) * radius, y, Mathf.Sin(b) * radius), thickness, color);
            }
        }

        void ArcWall(Transform root, string name, float radius, float height, float start, float end, float thickness, Color color)
        {
            // Closed cross section: inner/outer surfaces plus top, bottom and both cut ends. Visible from both sides.
            const int steps = 32;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2); triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 3);
            }
            for (int i = 0; i < steps; i++)
            {
                float a = Mathf.Lerp(start, end, i / (float)steps) * Mathf.Deg2Rad;
                float b = Mathf.Lerp(start, end, (i + 1) / (float)steps) * Mathf.Deg2Rad;
                var ai = new Vector3(Mathf.Cos(a) * (radius - thickness), .04f, Mathf.Sin(a) * (radius - thickness));
                var bi = new Vector3(Mathf.Cos(b) * (radius - thickness), .04f, Mathf.Sin(b) * (radius - thickness));
                var ao = new Vector3(Mathf.Cos(a) * radius, .04f, Mathf.Sin(a) * radius);
                var bo = new Vector3(Mathf.Cos(b) * radius, .04f, Mathf.Sin(b) * radius);
                var up = Vector3.up * (height - .04f);
                Quad(ai, bi, bi + up, ai + up); Quad(bo, ao, ao + up, bo + up);
                Quad(ai + up, bi + up, bo + up, ao + up); Quad(ao, bo, bi, ai);
                if (i == 0) Quad(ao, ai, ai + up, ao + up);
                if (i == steps - 1) Quad(bi, bo, bo + up, bi + up);
            }
            var mesh = new Mesh { name = "Care " + name }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh);
            var go = new GameObject("Care" + name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; Paint(go.GetComponent<Renderer>(), color);
        }

        static float Fraction(float value) => value - Mathf.Floor(value);
        static Bounds Measure(Renderer[] renderers, Vector3 fallback,bool includeHidden=false)
        {
            var result = new Bounds(fallback, Vector3.one * .1f); bool found = false;
            if (renderers != null) foreach (var renderer in renderers)
                if (renderer && (includeHidden||renderer.enabled) && renderer.gameObject.activeInHierarchy)
                { if (!found) { result = renderer.bounds; found = true; } else result.Encapsulate(renderer.bounds); }
            return result;
        }
        void RestoreEnvironment()
        { foreach (var pair in originalEnvironment) if (pair.Key) pair.Key.enabled = pair.Value; }
        void ClearStage()
        {
            livingCenter=restingAdultPosition=null;livingRotation=0;
            if (stageRoot) { stageRoot.SetActive(false); Destroy(stageRoot); }
            foreach (var mesh in meshes) if (mesh) Destroy(mesh);
            meshes.Clear(); segments.Clear(); segmentScales.Clear(); bodyRoot = null; stageRoot = null; bodyRenderers = stageRenderers = null;
        }
        void OnDestroy()
        {
            RestoreEnvironment(); ClearStage();
            if (material) Destroy(material);
            if (ownsProfile && profile) Destroy(profile);
        }
    }
}
