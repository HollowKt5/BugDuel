using BugDuel.Campaign;
using TMPro;
using UnityEngine;

namespace BeetleArena.Campaign
{
    /// <summary>A scene-anchored rescue action, independent of the collapsible greenhouse sidebar.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(300)]
    public sealed class CampaignRescuePrompt : MonoBehaviour
    {
        RectTransform canvas, rect;
        CampaignBackdrop backdrop;
        CampaignSpecimen specimen;
        CanvasGroup group;
        UnityEngine.UI.Button button;
        TMP_Text label;
        readonly Vector3[] corners = new Vector3[4];

        public bool Visible => group && group.alpha > 0;
        public UnityEngine.UI.Button Button => button;

        public static CampaignRescuePrompt Attach(RectTransform canvas, CampaignBackdrop backdrop, TMP_FontAsset font)
        {
            var prompt = canvas.GetComponentInChildren<CampaignRescuePrompt>(true);
            if (!prompt)
            {
                var go = new GameObject("RescuePrompt", typeof(RectTransform));
                go.transform.SetParent(canvas, false);
                prompt = go.AddComponent<CampaignRescuePrompt>();
                prompt.Build(canvas, backdrop);
            }
            prompt.label.font = font;
            prompt.label.color = CampaignRetroTheme.Ink;
            prompt.GetComponent<UnityEngine.UI.Image>().color = CampaignRetroTheme.Notice;
            prompt.GetComponent<RetroBevel>().Configure(2, CampaignRetroTheme.LightEdge, CampaignRetroTheme.DarkEdge);
            var colors = prompt.button.colors; colors.normalColor = Color.white; colors.selectedColor = CampaignRetroTheme.HoverTint;
            colors.highlightedColor = CampaignRetroTheme.HoverTint; colors.pressedColor = CampaignRetroTheme.PressedTint;
            colors.disabledColor = CampaignRetroTheme.DisabledTint; prompt.button.colors = colors;
            CampaignLocalization.Bind(prompt.label, "帮助翻身 H");
            prompt.transform.SetAsLastSibling();
            return prompt;
        }

        void Build(RectTransform parent, CampaignBackdrop world)
        {
            canvas = parent; backdrop = world; rect = GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(190, 42);
            group = gameObject.AddComponent<CanvasGroup>();
            var image = gameObject.AddComponent<UnityEngine.UI.Image>(); image.raycastTarget = true;
            gameObject.AddComponent<RetroBevel>();
            button = gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            button.onClick.AddListener(Rescue);
            var text = new GameObject("Label", typeof(RectTransform)); text.transform.SetParent(transform, false);
            label = text.AddComponent<TextMeshProUGUI>(); label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(10, 3); label.rectTransform.offsetMax = new Vector2(-10, -3);
            label.fontSize = label.fontSizeMax = 19; label.fontSizeMin = 14; label.enableAutoSizing = true;
            label.alignment = TextAlignmentOptions.Center; label.richText = false; label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            foreach (var candidate in FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None))
                if (candidate.selected && candidate.gameObject.scene == backdrop.gameObject.scene) { specimen = candidate; break; }
            SetVisible(false);
        }

        static CampaignBeetle Rescuable(CampaignApp app)
        {
            if (!app || app.Session == null || app.Modal || app.Constructing) return null;
            var state = app.Session.State;
            var beetle = state.beetles.Find(b => b.id == state.selectedId);
            return beetle != null && beetle.overturned && !beetle.dead && !beetle.listed &&
                state.battle?.beetleId != beetle.id && state.auction?.beetleId != beetle.id ? beetle : null;
        }

        void Rescue()
        {
            var app = CampaignApp.Instance; var beetle = Rescuable(app);
            if (beetle == null || app.Busy) return;
            string id = beetle.id;
            RetroUiAudio.PlayClick(); app.Execute(session => session.Rescue(id));
        }

        void LateUpdate()
        {
            var app = CampaignApp.Instance;
            if (Rescuable(app) == null || !specimen || !specimen.LifeStageDisplay || !backdrop.output)
            { SetVisible(false); return; }
            var bounds = specimen.LifeStageDisplay.ObservationBounds;
            if (backdrop.sceneCamera.WorldToViewportPoint(bounds.center).z <= 0) { SetVisible(false); return; }
            Vector2 low = Vector2.positiveInfinity, high = Vector2.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var point = Project(corner); low = Vector2.Min(low, point); high = Vector2.Max(high, point);
            }
            var visible = CanvasRect(backdrop.output.rectTransform);
            var subject = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
            rect.localPosition = ChoosePosition(subject, visible);
            SetVisible(true); button.interactable = !app.Busy;
        }

        // The render texture's size and Camera.pixelRect do not describe where the image appears in the UI.
        Vector2 Project(Vector3 world)
        {
            var viewport = backdrop.sceneCamera.WorldToViewportPoint(world);
            var output = backdrop.output.rectTransform; var area = output.rect;
            var point = output.TransformPoint(new Vector3(Mathf.LerpUnclamped(area.xMin, area.xMax, viewport.x), Mathf.LerpUnclamped(area.yMin, area.yMax, viewport.y), 0));
            return canvas.InverseTransformPoint(point);
        }

        Rect CanvasRect(RectTransform target)
        {
            target.GetWorldCorners(corners);
            Vector2 low = Vector2.positiveInfinity, high = Vector2.negativeInfinity;
            foreach (var corner in corners)
            { var point = (Vector2)canvas.InverseTransformPoint(corner); low = Vector2.Min(low, point); high = Vector2.Max(high, point); }
            return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        }

        Vector2 ChoosePosition(Rect subject, Rect visible)
        {
            const float gap = 12;
            Vector2 half = rect.sizeDelta * .5f;
            var preferred = new Vector2(subject.center.x, subject.yMax + gap + half.y);
            Vector2 best = preferred; float bestScore = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                var point = i == 0 ? preferred : i == 1 ? new Vector2(subject.xMax + gap + half.x, subject.center.y) :
                    i == 2 ? new Vector2(subject.xMin - gap - half.x, subject.center.y) : new Vector2(subject.center.x, subject.yMin - gap - half.y);
                point.x = Mathf.Clamp(point.x, visible.xMin + gap + half.x, visible.xMax - gap - half.x);
                point.y = Mathf.Clamp(point.y, visible.yMin + gap + half.y, visible.yMax - gap - half.y);
                var candidate = new Rect(point - half, rect.sizeDelta);
                float score = Overlap(candidate, subject) * 20 + (point - preferred).sqrMagnitude * .001f;
                foreach (Transform child in canvas)
                    if (child.gameObject.activeInHierarchy && (child.name == "Game calendar" || child.name == "Player currency" || child.name == "Sidebar toggle"))
                        score += Overlap(candidate, CanvasRect((RectTransform)child)) * 50;
                if (score < bestScore) { bestScore = score; best = point; }
            }
            return best;
        }

        static float Overlap(Rect a, Rect b) => Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin)) *
            Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));

        void SetVisible(bool value)
        { group.alpha = value ? 1 : 0; group.interactable = value; group.blocksRaycasts = value; }
    }
}
