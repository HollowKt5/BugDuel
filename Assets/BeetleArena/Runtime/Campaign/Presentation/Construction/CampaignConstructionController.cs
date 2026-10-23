using System;
using System.Collections.Generic;
using System.Linq;
using BugDuel.Campaign;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BeetleArena.Campaign
{
    /// <summary>Grid picking and previews. Durable ownership remains exclusively in CampaignSession transactions.</summary>
    [DefaultExecutionOrder(250)]
    public sealed class CampaignConstructionController : MonoBehaviour
    {
        public event Action Changed;
        public bool Editing { get; private set; }
        public bool HasPreview => ghost;
        public bool PreviewValid { get; private set; }
        public string PreviewReason { get; private set; } = "";
        public int PreviewRotation { get; private set; }
        public Vector2Int PreviewCell => new Vector2Int(previewX, previewZ);
        public string PreviewItemId => previewItem;
        public string SelectedId { get; private set; }
        public string CurrentBeetleId => beetleId;
        public CampaignDecorationPlacement Selected => app?.Session?.State.decorations.Find(p => p.id == SelectedId && p.beetleId == beetleId);
        public Vector2Int GridDimensions => new Vector2Int(CampaignDecorations.Columns, CampaignDecorations.Rows);
        public IReadOnlyDictionary<string, CampaignDecorationVisuals> Bindings => visuals;
        public Vector3 SurfaceCenter => surfaceCenter;
        public Vector2 SurfaceSize => surfaceSize;
        public Quaternion SurfaceRotation => surfaceRotation;

        readonly Dictionary<string, CampaignDecorationVisuals> visuals = new Dictionary<string, CampaignDecorationVisuals>();
        static readonly HashSet<string> templateScenery = new HashSet<string>
        { "ClimbingLog", "Leaf0", "Leaf1", "Leaf2", "Leaf3", "Leaf4", "Leaf5", "Leaf6" };
        readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        CampaignApp app;
        CampaignBackdrop backdrop;
        CampaignObservationCamera observation;
        CampaignSpecimen specimen;
        CampaignConstructionHabitat habitat;
        Transform placedRoot;
        GameObject grid, footprint;
        Mesh gridMesh, footprintMesh;
        Material gridMaterial, footprintMaterial;
        CampaignDecorationVisuals ghost;
        Vector3 surfaceCenter, ghostTarget;
        Vector2 surfaceSize;
        Quaternion surfaceRotation;
        string beetleId, previewItem, movingId;
        int previewX, previewZ, stageRevision = -1;
        bool initialized, dirty, awaitingCommit, pointerOnSurface;
        CampaignState observedState;
        CampaignState previewState;
        string checkedItem,checkedMove,checkedProblem;
        int checkedX,checkedZ,checkedRotation;
        PointerEventData pointer;
        EventSystem pointerEvents;

        public static CampaignConstructionController Ensure(CampaignBackdrop world)
        {
            var controller = world.GetComponent<CampaignConstructionController>();
            if (!controller) controller = world.gameObject.AddComponent<CampaignConstructionController>();
            controller.Bind(world); return controller;
        }

        public void Bind(CampaignBackdrop world)
        {
            if (initialized) return;
            backdrop = world; app = CampaignApp.Instance; observation = CampaignObservationCamera.Ensure(world);
            specimen = FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None).First(s => s.selected && s.gameObject.scene == world.gameObject.scene);
            app.Changed += OnStateChanged; initialized = true; dirty = true;
            gridMaterial = new Material(Shader.Find("BeetleArena/ConstructionPreview")) { name = "Construction grid" };
            gridMaterial.SetColor("_BaseColor", new Color(.48f, .91f, .78f, .32f));
            footprintMaterial = new Material(Shader.Find("BeetleArena/ConstructionPreview")) { name = "Construction footprint" };
            Synchronize();
        }

        public void SetEditing(bool value)
        {
            if (Editing == value) return;
            if (!value) Cancel();
            Editing = value; app.Constructing = value;
            if(value)specimen.SetConstructionHidden(true);
            else RestoreAnimal();
            if (grid) grid.SetActive(value);
            if (!value) { SelectedId = null; if (footprint) footprint.SetActive(false); }
            observation.SetConstructionView(value); Changed?.Invoke();
        }

        void RestoreAnimal()
        {
            foreach(var visual in visuals.Values)if(visual)visual.CompleteReveal();
            PlaceRestingAnimal();
            specimen.SetConstructionHidden(false);
        }
        void PlaceRestingAnimal()
        {
            var beetle=app.Session.State.beetles.Find(b=>b.id==beetleId);
            if(beetle==null||beetle.stage==CampaignLifeStage.ActiveAdult||habitat==null)return;
            if(habitat.TryRestingPosition(app.Session.DecorationSpace(beetleId),beetle.stage,out var cell,out int rotation))
            {
                var position=surfaceCenter+surfaceRotation*new Vector3((cell.x/CampaignDecorations.Columns-.5f)*surfaceSize.x,0,(cell.y/CampaignDecorations.Rows-.5f)*surfaceSize.y);
                specimen.LifeStageDisplay.SetLivingPosition(position,rotation);
            }
        }

        public void BeginPlace(string itemId)
        {
            if (!initialized || app.Busy || app.Modal) return;
            Synchronize();
            if (beetleId == null || surfaceSize == Vector2.zero) return;
            CampaignDecorations.Get(itemId);
            Cancel(); SetEditing(true); SelectedId = null;
            previewItem = itemId; PreviewRotation = 0; movingId = null;
            ghost = CampaignDecorationVisuals.Create(itemId, placedRoot, true);
            ConfigureVisual(ghost, 0, 0, 0); ghost.gameObject.SetActive(false);
            SetPreviewStatus(false, "拖入养殖箱，绿色位置可摆放");
            if (Mouse.current != null) PreviewAtScreenPoint(Mouse.current.position.ReadValue());
            Changed?.Invoke();
        }

        public void BeginMove(string placementId)
        {
            if (!initialized || app.Busy || app.Modal) return;
            Synchronize();
            var placement = app.Session.State.decorations.Find(p => p.id == placementId && p.beetleId == beetleId);
            if (placement == null) return;
            Cancel(); SetEditing(true); SelectedId = placement.id;
            movingId = placement.id; previewItem = placement.itemId; PreviewRotation = placement.rotation;
            ghost = CampaignDecorationVisuals.Create(previewItem, placedRoot, true);
            if (visuals.TryGetValue(movingId, out var source))
            {
                source.CompleteReveal();
                source.gameObject.SetActive(false);
            }
            PreviewAtCell(placement.x, placement.z); ghost.transform.position = ghostTarget;
            Changed?.Invoke();
        }

        public void Select(string placementId)
        {
            if (app.Busy || app.Modal) return;
            Cancel();
            var placement = app.Session.State.decorations.Find(p => p.id == placementId && p.beetleId == beetleId);
            if (placement != null) SetEditing(true);
            SelectedId = placement?.id;
            RefreshFootprint(); Changed?.Invoke();
        }

        public void RotatePreview(int deltaDegrees)
        {
            if (!HasPreview || awaitingCommit) return;
            PreviewRotation = (PreviewRotation + deltaDegrees % 360 + 360) % 360;
            ghost.SetOrientation(PreviewRotation); ghost.Bounce();
            PreviewAtCell(previewX, previewZ); Changed?.Invoke();
        }

        public void Cancel()
        {
            bool hadPreview = HasPreview;
            if (movingId != null && visuals.TryGetValue(movingId, out var source) && source)
            {
                source.gameObject.SetActive(true);
                specimen.RefreshConstructionArea();
            }
            if (ghost) { ghost.gameObject.SetActive(false); Destroy(ghost.gameObject); ghost = null; }
            previewItem = movingId = null; PreviewValid = false; PreviewReason = ""; pointerOnSurface = false;
            previewState=null;
            RefreshFootprint();
            if (hadPreview) Changed?.Invoke();
        }

        public bool Confirm()
        {
            if (!HasPreview || !PreviewValid || !pointerOnSurface || awaitingCommit || app.Busy || app.Modal) return false;
            string ownerId = beetleId, item = previewItem, move = movingId, newId = null;
            int x = previewX, z = previewZ, rotation = PreviewRotation;
            bool accepted = app.Execute(session =>
            {
                string problem=habitat?.Problem(session.DecorationSpace(ownerId,item,x,z,rotation,move));
                if(problem!=null)throw new CampaignRuleException(problem);
                if (move == null) newId = session.PlaceDecoration(ownerId, item, x, z, rotation);
                else { session.MoveDecoration(ownerId, move, x, z, rotation); newId = move; }
            }, () =>
            {
                if (!this) return;
                awaitingCommit = false; Cancel(); SelectedId = newId;
                Synchronize();
                if (visuals.TryGetValue(newId, out var built))
                {
                    if (move == null) built.Reveal(() =>
                    {
                        if (this && beetleId == ownerId && visuals.TryGetValue(newId, out var current) && current == built)
                            specimen.RefreshConstructionArea();
                    });
                    else built.Bounce();
                }
                RefreshFootprint(); Changed?.Invoke();
            });
            if (accepted) { awaitingCommit = true; SetPreviewStatus(false, "正在保存摆放位置"); }
            else SetPreviewStatus(false, app.Message);
            return accepted;
        }

        public bool ReturnSelected() => RemoveSelected(false);
        public bool SellSelected() => RemoveSelected(true);
        bool RemoveSelected(bool sell)
        {
            var placed = Selected;
            if (placed == null || app.Busy || app.Modal) return false;
            string ownerId = beetleId, id = placed.id; Cancel();
            return app.Execute(session =>
            { if (sell) session.SellDecoration(ownerId, id); else session.ReturnDecoration(ownerId, id); }, () =>
            {
                if (!this) return;
                SelectedId = null; Synchronize(); RefreshFootprint(); Changed?.Invoke();
            });
        }

        /// <summary>Uses the displayed RawImage rectangle; correct under sidebar collapse, resized windows and render textures.</summary>
        public bool PreviewAtScreenPoint(Vector2 screenPosition)
        {
            if (!HasPreview || awaitingCommit) return false;
            if (!TryPointerRay(screenPosition, out var ray) || PointerOverUi(screenPosition) ||
                !new Plane(Vector3.up, surfaceCenter).Raycast(ray, out float distance))
            {
                pointerOnSurface = false; ghost.gameObject.SetActive(false); if (footprint) footprint.SetActive(false);
                SetPreviewStatus(false, "拖入养殖箱，绿色位置可摆放"); return false;
            }
            var local = Quaternion.Inverse(surfaceRotation) * (ray.GetPoint(distance) - surfaceCenter);
            int width = CampaignDecorations.Width(previewItem, PreviewRotation), depth = CampaignDecorations.Depth(previewItem, PreviewRotation);
            int x = Mathf.RoundToInt((local.x / surfaceSize.x + .5f) * CampaignDecorations.Columns - width * .5f);
            int z = Mathf.RoundToInt((local.z / surfaceSize.y + .5f) * CampaignDecorations.Rows - depth * .5f);
            return PreviewAtCell(x, z);
        }

        /// <summary>Exact cell placement for replay and validation; coordinates are the rotated footprint's lower-left cell.</summary>
        public bool PreviewAtCell(int x, int z)
        {
            if (!HasPreview || awaitingCommit) return false;
            previewX = x; previewZ = z; pointerOnSurface = true;
            bool firstVisible = !ghost.gameObject.activeSelf;
            ghost.gameObject.SetActive(true); ConfigureVisual(ghost, x, z, PreviewRotation, false);
            if (firstVisible) ghost.transform.position = ghostTarget;
            if(previewState!=app.Session.State||checkedItem!=previewItem||checkedMove!=movingId||checkedX!=x||checkedZ!=z||checkedRotation!=PreviewRotation)
            {
                checkedProblem=app.Session.DecorationPlacementProblem(beetleId,previewItem,x,z,PreviewRotation,movingId);
                if(checkedProblem==null&&habitat!=null)checkedProblem=habitat.Problem(app.Session.DecorationSpace(beetleId,previewItem,x,z,PreviewRotation,movingId));
                if(checkedProblem==null&&movingId==null&&app.Session.InventoryCount(previewItem)<1)checkedProblem="仓库中没有这件景观，请先购买";
                previewState=app.Session.State;checkedItem=previewItem;checkedMove=movingId;checkedX=x;checkedZ=z;checkedRotation=PreviewRotation;
            }
            string problem=checkedProblem;
            SetPreviewStatus(problem == null, problem ?? "点击摆放 · Q / E 旋转 · Esc 取消");
            ghost.SetPreviewValid(PreviewValid); RefreshFootprint(); return PreviewValid;
        }

        public bool TryPointerRay(Vector2 screenPosition, out Ray ray)
        {
            ray = default;
            var rect = backdrop.output.rectTransform; var canvas = backdrop.output.canvas;
            var eventCamera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, eventCamera) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPosition, eventCamera, out var local)) return false;
            ray = backdrop.sceneCamera.ViewportPointToRay(new Vector3((local.x - rect.rect.xMin) / rect.rect.width, (local.y - rect.rect.yMin) / rect.rect.height, 0));
            return true;
        }

        void SetPreviewStatus(bool valid, string reason)
        {
            if (PreviewValid == valid && PreviewReason == reason) return;
            PreviewValid = valid; PreviewReason = reason; Changed?.Invoke();
        }

        void OnStateChanged() => dirty = true;

        void LateUpdate()
        {
            if (!initialized || !app || app.Session == null) return;
            if (awaitingCommit && !app.Busy)
            { awaitingCommit = false; SetPreviewStatus(false, app.Message); }
            Synchronize();
            if (ghost && ghost.gameObject.activeSelf) ghost.Follow(ghostTarget, Mathf.Min(Time.unscaledDeltaTime, .05f));
            if (!Editing || app.Busy || app.Modal || !Application.isFocused || backdrop.UiFocused) return;
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            if (HasPreview && keyboard != null)
            {
                if (keyboard.qKey.wasPressedThisFrame) RotatePreview(-90);
                if (keyboard.eKey.wasPressedThisFrame) RotatePreview(90);
            }
            if (mouse == null) return;
            var position = mouse.position.ReadValue();
            if (HasPreview)
            {
                PreviewAtScreenPoint(position);
                if (mouse.leftButton.wasPressedThisFrame && PreviewValid) Confirm();
            }
            else if (mouse.leftButton.wasPressedThisFrame && TryPointerRay(position, out var ray) && !PointerOverUi(position))
            {
                string hit = null; float nearest = float.PositiveInfinity;
                foreach (var pair in visuals)
                    if (pair.Value.Bounds.IntersectRay(ray, out float distance) && distance < nearest) { hit = pair.Key; nearest = distance; }
                Select(hit);
            }
        }

        void Synchronize()
        {
            if (app.Session == null || !specimen || !specimen.enclosureFloor) return;
            var profile=specimen.LifeStageDisplay?specimen.LifeStageDisplay.VisualProfile:null;
            if(profile&&(habitat==null||habitat.Profile!=profile||habitat.ProfileRevision!=profile.revision))
            {habitat=new CampaignConstructionHabitat(specimen);previewState=null;dirty=true;}
            string current = app.Session.State.selectedId;
            int revision = specimen.LifeStageDisplay ? specimen.LifeStageDisplay.Revision : 0;
            bool switched = current != beetleId || revision != stageRevision;
            if (switched)
            {
                bool changedBeetle=current!=beetleId;
                Cancel(); SelectedId = null; beetleId = current; stageRevision = revision;
                if (placedRoot) { placedRoot.gameObject.SetActive(false); Destroy(placedRoot.gameObject); }
                visuals.Clear(); placedRoot = null;
                BuildSurface(); dirty = true;
                if(changedBeetle&&Editing)SetEditing(false);
            }
            if (app.Busy || awaitingCommit || (!dirty && observedState == app.Session.State)) return;
            observedState = app.Session.State; dirty = false;
            var records = app.Session.State.decorations.Where(p => p.beetleId == beetleId).ToArray();
            foreach (var id in visuals.Keys.Where(id => records.All(p => p.id != id)).ToArray())
            { var visual = visuals[id]; if (visual) { visual.gameObject.SetActive(false); Destroy(visual.gameObject); } visuals.Remove(id); }
            foreach (var record in records)
            {
                if (!visuals.TryGetValue(record.id, out var visual))
                { visual = CampaignDecorationVisuals.Create(record.itemId, placedRoot); visuals.Add(record.id, visual); }
                ConfigureVisual(visual, record.x, record.z, record.rotation);
                visual.gameObject.SetActive(record.id != movingId);
            }
            if (SelectedId != null && records.All(p => p.id != SelectedId)) SelectedId = null;
            if (specimen.LifeStageDisplay == null || !specimen.LifeStageDisplay.HasStageEnvironment) specimen.RefreshConstructionArea();
            PlaceRestingAnimal();
            observation.RefreshEnvironment(); RefreshFootprint(); Changed?.Invoke();
        }

        void BuildSurface()
        {
            // Retire the nursery demo's fixed ornaments; only saved construction objects occupy the buildable floor.
            foreach (var renderer in specimen.enclosureFloor.transform.parent.GetComponentsInChildren<Renderer>(true))
                if (templateScenery.Contains(renderer.name)) renderer.gameObject.SetActive(false);
            var life = specimen.LifeStageDisplay;
            bool staged = life && life.TryGetConstructionSurface(out surfaceCenter, out surfaceSize, out surfaceRotation);
            if (!staged)
            {
                var floor = specimen.enclosureFloor;
                surfaceCenter = floor.transform.TransformPoint(floor.localBounds.center); surfaceCenter.y = floor.bounds.max.y;
                surfaceSize = new Vector2(floor.localBounds.size.x * Mathf.Abs(floor.transform.lossyScale.x), floor.localBounds.size.z * Mathf.Abs(floor.transform.lossyScale.z)) * .94f;
                surfaceRotation = Quaternion.Euler(0, floor.transform.eulerAngles.y, 0);
            }
            var root = new GameObject("Construction placed scenery");
            placedRoot = root.transform; placedRoot.SetParent(staged ? life.StageRoot : specimen.enclosureFloor.transform.parent, true);
            placedRoot.SetPositionAndRotation(surfaceCenter, surfaceRotation);
            BuildGrid();
        }

        Vector3 Position(int x, int z, int width, int depth) => surfaceCenter + surfaceRotation * new Vector3(
            ((x + width * .5f) / CampaignDecorations.Columns - .5f) * surfaceSize.x, .012f,
            ((z + depth * .5f) / CampaignDecorations.Rows - .5f) * surfaceSize.y);

        void ConfigureVisual(CampaignDecorationVisuals visual, int x, int z, int rotation, bool snap = true)
        {
            var scale = new Vector3(surfaceSize.x / CampaignDecorations.Columns, Mathf.Min(surfaceSize.x / CampaignDecorations.Columns, surfaceSize.y / CampaignDecorations.Rows), surfaceSize.y / CampaignDecorations.Rows);
            var position = Position(x, z, CampaignDecorations.Width(visual.ItemId, rotation), CampaignDecorations.Depth(visual.ItemId, rotation));
            visual.transform.rotation = surfaceRotation; visual.transform.localScale = scale; visual.SetOrientation(rotation);
            if (snap) visual.transform.position = position;
            if (visual == ghost) ghostTarget = position;
        }

        void BuildGrid()
        {
            if (grid) Destroy(grid); if (gridMesh) Destroy(gridMesh);
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            float dx = surfaceSize.x / CampaignDecorations.Columns, dz = surfaceSize.y / CampaignDecorations.Rows;
            for (int z = 0; z < CampaignDecorations.Rows; z++)
                for (int x = 0; x < CampaignDecorations.Columns; x++)
                {
                    if (CampaignDecorations.IsReservedCell(x, z)) continue;
                    float left = x * dx - surfaceSize.x * .5f, bottom = z * dz - surfaceSize.y * .5f;
                    AddQuad(vertices, triangles, left, bottom, dx, .014f);
                    AddQuad(vertices, triangles, left, bottom + dz - .014f, dx, .014f);
                    AddQuad(vertices, triangles, left, bottom, .014f, dz);
                    AddQuad(vertices, triangles, left + dx - .014f, bottom, .014f, dz);
                }
            gridMesh = new Mesh { name = "Construction legal cells" }; gridMesh.SetVertices(vertices); gridMesh.SetTriangles(triangles, 0); gridMesh.RecalculateNormals();
            grid = new GameObject("CareConstructionGrid", typeof(MeshFilter), typeof(MeshRenderer)); grid.transform.SetParent(placedRoot, false); grid.transform.localPosition = Vector3.up * .023f;
            grid.GetComponent<MeshFilter>().sharedMesh = gridMesh; grid.GetComponent<Renderer>().sharedMaterial = gridMaterial; grid.SetActive(Editing);
            if (footprint) Destroy(footprint); if (footprintMesh) Destroy(footprintMesh);
            footprintMesh = new Mesh { name = "Construction footprint" }; var corners = new List<Vector3>(); var indices = new List<int>();
            AddQuad(corners, indices, -.5f, -.5f, 1, 1); footprintMesh.SetVertices(corners); footprintMesh.SetTriangles(indices, 0); footprintMesh.RecalculateNormals();
            footprint = new GameObject("CareConstructionFootprint", typeof(MeshFilter), typeof(MeshRenderer)); footprint.transform.SetParent(placedRoot, false);
            footprint.GetComponent<MeshFilter>().sharedMesh = footprintMesh; footprint.GetComponent<Renderer>().sharedMaterial = footprintMaterial; footprint.SetActive(false);
        }

        void RefreshFootprint()
        {
            if (!footprint) return;
            var record = Selected; bool preview = HasPreview && pointerOnSurface;
            if (!Editing || (!preview && record == null)) { footprint.SetActive(false); return; }
            string item = preview ? previewItem : record.itemId;
            int rotation = preview ? PreviewRotation : record.rotation, x = preview ? previewX : record.x, z = preview ? previewZ : record.z;
            int width = CampaignDecorations.Width(item, rotation), depth = CampaignDecorations.Depth(item, rotation);
            footprint.SetActive(true); footprint.transform.SetPositionAndRotation(Position(x, z, width, depth) + Vector3.up * .018f, surfaceRotation);
            footprint.transform.localScale = new Vector3(width * surfaceSize.x / CampaignDecorations.Columns, 1, depth * surfaceSize.y / CampaignDecorations.Rows);
            footprintMaterial.SetColor("_BaseColor", preview ? (PreviewValid ? new Color(.16f, .91f, .58f, .25f) : new Color(1, .18f, .12f, .3f)) : new Color(1, .76f, .22f, .3f));
        }

        static void AddQuad(List<Vector3> vertices, List<int> triangles, float x, float z, float width, float depth)
        {
            int n = vertices.Count; vertices.Add(new Vector3(x, 0, z)); vertices.Add(new Vector3(x, 0, z + depth));
            vertices.Add(new Vector3(x + width, 0, z + depth)); vertices.Add(new Vector3(x + width, 0, z));
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2); triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 3);
        }

        bool PointerOverUi(Vector2 position)
        {
            var events = EventSystem.current; if (!events) return false;
            if (pointer == null || pointerEvents != events) { pointer = new PointerEventData(events); pointerEvents = events; }
            pointer.Reset(); pointer.position = position; uiHits.Clear(); events.RaycastAll(pointer, uiHits);
            foreach (var hit in uiHits) if (hit.gameObject && hit.gameObject != backdrop.output.gameObject) return true;
            return false;
        }

        void OnDestroy()
        {
            if(specimen&&specimen.LifeStageDisplay&&specimen.ConstructionHidden)specimen.SetConstructionHidden(false);
            if (app) { app.Changed -= OnStateChanged; if (Editing) app.Constructing = false; }
            if (placedRoot) Destroy(placedRoot.gameObject);
            if (gridMesh) Destroy(gridMesh); if (footprintMesh) Destroy(footprintMesh);
            if (gridMaterial) Destroy(gridMaterial); if (footprintMaterial) Destroy(footprintMaterial);
        }
        void OnDisable()
        {if(initialized&&Editing)SetEditing(false);}
    }
}
