using System;
using System.Collections.Generic;
using BeetleArena.Campaign;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BeetleArena
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class ArenaCardHud : MonoBehaviour
    {
        [Header("Card fan")]
        [SerializeField, Range(1, 100), Tooltip("Total spread in degrees.")] float fanAngle = 50;
        [SerializeField, Min(1)] float fanRadius = 1050;
        [SerializeField, Min(0), Tooltip("Vertical drop from the center to the edges, in Canvas units.")] float fanDepth = 51;
        [SerializeField, Range(0, 1), Tooltip("How far each card rotates to follow the fan.")] float cardTilt = .72f;
        [SerializeField] Vector2 cardSize = new Vector2(136, 152);
        [SerializeField, Min(0)] float bottomOffset = 141;
        [SerializeField, Min(0)] float eventGap = 14;
        [SerializeField, Min(0)] float hoverLift = 48;
        [SerializeField, Range(1, 1.4f)] float hoverScale = 1.09f;

        static readonly Vector2 Bottom = new Vector2(.5f, 0);
        static readonly Vector2 Center = new Vector2(.5f, .5f);
        static readonly Color Ink = new Color(.93f, .94f, .87f);
        static readonly Color Muted = new Color(.66f, .74f, .69f);
        static readonly Color Gold = new Color(.96f, .77f, .39f);
        static readonly Color Dark = new Color(.055f, .10f, .092f);
        static readonly string[] Keys = { "Q", "W", "E", "R", "1", "2", "3", "SPACE" };
        static readonly string[] PadKeys = { "X", "Y", "A", "B", "LB", "RB", "RT", "LT" };

        readonly List<ArenaCard> selected = new List<ArenaCard>(ArenaCardCatalog.SpecialSlots);
        readonly CardView[] slots = new CardView[ArenaCardCatalog.SlotCount];
        readonly DraftCard[] choices = new DraftCard[(int)ArenaCard.Stridulate - (int)ArenaCard.Rosin + 1];
        BeetleMatchController match;
        BeetleSimulation observedMatch;
        Canvas canvas;
        Func<bool> blocked;
        RectTransform root, hand, draft, tooltip, jellyMarker;
        TMP_Text draftTitle, draftPrompt, draftSummary, beginText, statusText, tooltipText;
        UnityEngine.UI.Button begin;
        UnityEngine.UI.Image lockFill, flash;
        CardView played;
        ArenaCard playedCard;
        EventSystem reservedNavigation;
        int hovered = -1;
        float reveal;
        bool padMode, english;

        sealed class CardView
        {
            public RectTransform Rect, Hitbox;
            public CanvasGroup Group;
            public UnityEngine.UI.Image Border;
            public TMP_Text Header, Index, Name, Footer;
            public ArenaCard PressedCard;
            public float Lift;
        }

        sealed class DraftCard
        {
            public ArenaCard Card;
            public UnityEngine.UI.Button Button;
            public UnityEngine.UI.Image Border;
            public TMP_Text Order, Title, Detail;
        }

        public bool AcceptsShortcuts => isActiveAndEnabled && root && match && match.UsesCards &&
            match.Simulation?.Phase == MatchPhase.Fighting && !Blocked;
        bool Blocked => !match || match.IsPaused || match.ArenaSettingsOpen || (blocked?.Invoke() ?? false);

        public void Bind(BeetleMatchController controller, Canvas target = null, Func<bool> interactionBlocked = null)
        {
            if (root)
                return;
            match = controller;
            blocked = interactionBlocked;
            canvas = target ? target : MakeOverlay();
            english = CampaignLocalization.English;
            root = Stretch("ArenaTactics", canvas.transform);
            BuildDraft();
            BuildHand();
            flash = Fill(Stretch("CardFlash", root), Color.clear);
            played = MakeCard(root, "PlayedCard", Center, Vector2.zero, new Vector2(170, 190));
            played.Rect.gameObject.SetActive(false);
            jellyMarker = Box("CrowdJelly", root, Center, Vector2.zero, new Vector2(100, 32));
            Fill(jellyMarker, Dark);
            Label("JellyLabel", jellyMarker, Center, Vector2.zero, jellyMarker.sizeDelta, 16, Gold);
            RefreshLanguage();
            root.gameObject.SetActive(false);
            CampaignLocalization.Changed += RefreshLanguage;
        }

        Canvas MakeOverlay()
        {
            var host = new GameObject("ArenaTacticsCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            host.transform.SetParent(transform, false);
            var overlay = host.GetComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.sortingOrder = 12;
            var scaler = host.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            return overlay;
        }

        void BuildDraft()
        {
            draft = Stretch("CardLoadout", root);
            Fill(draft, new Color(.025f, .052f, .048f, .86f), true);
            var panel = Box("LoadoutPanel", draft, Center, Vector2.zero, new Vector2(1116, 696));
            Fill(panel, Dark);
            Fill(Box("TopAccent", panel, new Vector2(.5f, 1), new Vector2(0, -2), new Vector2(1116, 4)), Gold);
            draftTitle = Label("DraftTitle", panel, Center, new Vector2(0, 292), new Vector2(1020, 46), 32, Ink);
            draftPrompt = Label("DraftPrompt", panel, Center, new Vector2(0, 247), new Vector2(1010, 34), 18, Muted);
            for (int i = 0; i < choices.Length; i++)
            {
                ArenaCard card = (ArenaCard)((int)ArenaCard.Rosin + i);
                var tile = Box(card.ToString(), panel, Center, new Vector2((i % 4 - 1.5f) * 265, 153 - i / 4 * 144),
                    new Vector2(252, 132));
                var border = Fill(tile, Tint(card));
                var face = Fill(Inset("Face", tile, 2), new Color(.09f, .16f, .137f), true);
                var button = MakeButton(tile, face, () => ToggleSpecial(card));
                var order = Label("SlotOrder", tile, Center, new Vector2(-97, 41), new Vector2(46, 30), 16, Tint(card));
                var title = Label("CardName", tile, Center, new Vector2(18, 41), new Vector2(195, 34), 21, Ink);
                var detail = Label("CardEffect", tile, Center, new Vector2(0, -13), new Vector2(225, 69), 16, Muted);
                detail.textWrappingMode = TextWrappingModes.Normal;
                choices[i] = new DraftCard { Card = card, Button = button, Border = border, Order = order, Title = title, Detail = detail };
            }

            draftSummary = Label("SelectedCards", panel, Center, new Vector2(-134, -294), new Vector2(744, 42), 19, Gold);
            draftSummary.alignment = TextAlignmentOptions.MidlineLeft;
            var confirm = Box("确认出战", panel, Center, new Vector2(396, -294), new Vector2(240, 48));
            var confirmFace = Fill(confirm, Gold, true);
            begin = MakeButton(confirm, confirmFace, ConfirmDraft);
            beginText = Label("ConfirmLabel", confirm, Center, Vector2.zero, confirm.sizeDelta, 23, Dark);
        }

        void BuildHand()
        {
            hand = Stretch("TacticalHand", root);
            var visuals = Stretch("Cards", hand);
            var hitboxes = Stretch("CardTargets", hand);
            for (int i = 0; i < slots.Length; i++)
            {
                int slot = i;
                CardView view = MakeCard(visuals, "Card" + (i + 1), Bottom, Vector2.zero, cardSize);
                view.Hitbox = Box("PlaySlot" + (i + 1), hitboxes, Bottom, Vector2.zero, cardSize);
                var target = Fill(view.Hitbox, Color.clear, true);
                target.canvasRenderer.cullTransparentMesh = false;
                var button = MakeButton(view.Hitbox, target, () => Play(slot, view.PressedCard));
                button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                button.transition = UnityEngine.UI.Selectable.Transition.None;
                var events = view.Hitbox.gameObject.AddComponent<EventTrigger>();
                AddTrigger(events, EventTriggerType.PointerEnter, _ => hovered = slot);
                AddTrigger(events, EventTriggerType.PointerExit, _ => { if (hovered == slot) hovered = -1; });
                AddTrigger(events, EventTriggerType.PointerDown, _ => view.PressedCard = match.Simulation.CardAt(slot));
                slots[i] = view;
            }
            var status = Box("SharedCooldown", hand, Bottom, Vector2.zero, new Vector2(310, 32));
            Fill(status, Dark);
            var progress = Box("CooldownFill", status, new Vector2(0, 0), Vector2.zero, new Vector2(310, 3));
            progress.pivot = Vector2.zero;
            lockFill = Fill(progress, Gold);
            statusText = Label("CooldownStatus", status, Center, Vector2.zero, status.sizeDelta, 17, Ink);
            tooltip = Box("HoveredCardDetail", hand, Bottom, Vector2.zero, new Vector2(616, 62));
            Fill(tooltip, Dark);
            tooltipText = Label("HoveredCardEffect", tooltip, Center, Vector2.zero, new Vector2(588, 56), 19, Ink);
            tooltipText.textWrappingMode = TextWrappingModes.Normal;
        }

        static CardView MakeCard(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var card = Box(name, parent, anchor, position, size);
            var border = Fill(card, Gold);
            var face = Inset("CardFace", card, 3);
            Fill(face, Dark);
            Fill(Box("KeyBand", face, new Vector2(.5f, 1), new Vector2(0, -14), new Vector2(84, 20)), new Color(.17f, .24f, .19f));
            return new CardView
            {
                Rect = card,
                Group = card.gameObject.AddComponent<CanvasGroup>(),
                Border = border,
                Header = Label("Key", face, new Vector2(.5f, 1), new Vector2(0, -14), new Vector2(94, 20), 14, Ink),
                Index = Label("CardClass", face, Center, new Vector2(0, 13), new Vector2(96, 26), 17, Gold),
                Name = Label("CardName", face, Center, new Vector2(0, -16), new Vector2(118, 33), 21, Ink),
                Footer = Label("CardCooldown", face, Bottom, new Vector2(0, 16), new Vector2(104, 25), 15, Muted)
            };
        }

        void ToggleSpecial(ArenaCard card)
        {
            if (Blocked || !match.AwaitingCardLoadout)
                return;
            if (!selected.Remove(card) && selected.Count < ArenaCardCatalog.SpecialSlots)
                selected.Add(card);
            RefreshDraft();
        }

        void ConfirmDraft()
        {
            if (Blocked || !match.AwaitingCardLoadout || !match.Simulation.EquipCards(selected))
                return;
            draft.gameObject.SetActive(false);
            ClearDraftSelection();
        }

        void ClearDraftSelection()
        {
            var events = EventSystem.current;
            if (events && events.currentSelectedGameObject && events.currentSelectedGameObject.transform.IsChildOf(draft))
                events.SetSelectedGameObject(null);
        }

        void RefreshDraft()
        {
            draftTitle.text = Local("战术卡组", "TACTICAL LOADOUT");
            draftPrompt.text = Local("选择 3 张特殊卡。进攻、防守各 2 张基础牌自动携带，临场机变由战况触发。",
                "Choose 3 specials. Two attack and two defense cards are carried automatically. Events unlock from combat.");
            beginText.text = Local("确认出战", "BEGIN BOUT");
            string summary = Local("已选", "SELECTED") + $"  {selected.Count}/3";
            foreach (ArenaCard card in selected)
                summary += "   /   " + Name(card);
            draftSummary.text = summary;
            begin.interactable = selected.Count == ArenaCardCatalog.SpecialSlots;
            foreach (var choice in choices)
            {
                int order = selected.IndexOf(choice.Card);
                var info = ArenaCardCatalog.Get(choice.Card);
                choice.Border.color = order < 0 ? Color.Lerp(Dark, Tint(choice.Card), .48f) : Gold;
                choice.Order.text = order < 0 ? $"{info.Cooldown:0}s" : $"{order + 1:00}";
                choice.Title.text = Name(choice.Card);
                choice.Detail.text = Description(choice.Card);
                choice.Button.interactable = order >= 0 || selected.Count < ArenaCardCatalog.SpecialSlots;
            }
        }

        void Update()
        {
            RestoreNavigation();
            if (root && draft.gameObject.activeInHierarchy && !Blocked && !RetroUiNavigation.PointerMode)
            {
                var events = EventSystem.current;
                if (events && (!events.currentSelectedGameObject || !events.currentSelectedGameObject.transform.IsChildOf(draft)))
                    events.SetSelectedGameObject(choices[0].Button.gameObject);
            }
            if (!AcceptsShortcuts)
                return;
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            if (pad != null && pad.buttonSouth.wasPressedThisFrame && EventSystem.current && EventSystem.current.sendNavigationEvents)
            {
                reservedNavigation = EventSystem.current;
                reservedNavigation.sendNavigationEvents = false;
            }
            int slot = -1;
            if (keyboard != null)
            {
                if (keyboard.qKey.wasPressedThisFrame) slot = 0;
                else if (keyboard.wKey.wasPressedThisFrame) slot = 1;
                else if (keyboard.eKey.wasPressedThisFrame) slot = 2;
                else if (keyboard.rKey.wasPressedThisFrame) slot = 3;
                else if (keyboard.digit1Key.wasPressedThisFrame) slot = 4;
                else if (keyboard.digit2Key.wasPressedThisFrame) slot = 5;
                else if (keyboard.digit3Key.wasPressedThisFrame) slot = 6;
                else if (keyboard.spaceKey.wasPressedThisFrame) slot = 7;
            }
            if (slot >= 0)
                padMode = false;
            else if (pad != null)
            {
                if (pad.buttonWest.wasPressedThisFrame) slot = 0;
                else if (pad.buttonNorth.wasPressedThisFrame) slot = 1;
                else if (pad.buttonSouth.wasPressedThisFrame) slot = 2;
                else if (pad.buttonEast.wasPressedThisFrame) slot = 3;
                else if (pad.leftShoulder.wasPressedThisFrame) slot = 4;
                else if (pad.rightShoulder.wasPressedThisFrame) slot = 5;
                else if (pad.rightTrigger.wasPressedThisFrame) slot = 6;
                else if (pad.leftTrigger.wasPressedThisFrame) slot = 7;
                if (slot >= 0)
                    padMode = true;
            }
            if (slot >= 0)
                Play(slot, match.Simulation.CardAt(slot));
        }

        void LateUpdate()
        {
            RestoreNavigation();
            if (!root || !match)
                return;
            var sim = match.Simulation;
            if (!ReferenceEquals(observedMatch, sim))
            {
                observedMatch = sim;
                selected.Clear();
                hovered = -1;
                reveal = 0;
                played.Rect.gameObject.SetActive(false);
                flash.color = Color.clear;
                RefreshDraft();
            }

            bool visible = sim != null && match.UsesCards && !(blocked?.Invoke() ?? false) && !match.ArenaSettingsOpen;
            root.gameObject.SetActive(visible);
            if (!visible)
                return;
            draft.gameObject.SetActive(match.AwaitingCardLoadout);
            bool showHand = sim.HasCardLoadout && (sim.Phase == MatchPhase.Countdown || sim.Phase == MatchPhase.Fighting);
            hand.gameObject.SetActive(showHand);
            if (showHand)
                UpdateHand(sim);
            UpdateReveal(sim);
            UpdateJelly(sim);
        }

        void UpdateHand(BeetleSimulation sim)
        {
            float halfAngle = Mathf.Max(1, fanAngle) * .5f;
            float arc = 1 - Mathf.Cos(halfAngle * Mathf.Deg2Rad);
            float remaining = sim.CardCooldownRemaining;
            Vector2 size = new Vector2(Mathf.Max(70, cardSize.x), Mathf.Max(94, cardSize.y));
            for (int i = 0; i < slots.Length; i++)
            {
                var view = slots[i];
                var card = sim.CardAt(i);
                float angle = Mathf.Lerp(-halfAngle, halfAngle, i / (float)(slots.Length - 1));
                float radians = angle * Mathf.Deg2Rad;
                float y = bottomOffset - fanDepth * (1 - Mathf.Cos(radians)) / arc;
                float x = Mathf.Sin(radians) * fanRadius + (i == ArenaCardCatalog.EventSlot ? eventGap : 0);
                bool hover = hovered == i && !Blocked;
                view.Lift = Mathf.MoveTowards(view.Lift, hover ? 1 : 0, Time.unscaledDeltaTime * 10);
                Quaternion rotation = Quaternion.Euler(0, 0, -angle * cardTilt);
                float lift = hoverLift * view.Lift;
                view.Hitbox.anchoredPosition = new Vector2(x, y + lift * .5f);
                view.Hitbox.sizeDelta = new Vector2(size.x, size.y + lift);
                view.Hitbox.localRotation = rotation;
                view.Rect.anchoredPosition = new Vector2(x, y + lift);
                view.Rect.sizeDelta = size;
                view.Rect.localRotation = Quaternion.Slerp(rotation, Quaternion.identity, view.Lift);
                view.Rect.localScale = Vector3.one * Mathf.Lerp(1, hoverScale, view.Lift);
                if (hover)
                    view.Rect.SetAsLastSibling();
                SetCard(view, card, (padMode ? PadKeys : Keys)[i], sim.CanPlayCard(i) && !Blocked, hover);
            }

            var status = (RectTransform)statusText.transform.parent;
            status.anchoredPosition = new Vector2(0, bottomOffset + size.y * .5f + 29);
            statusText.text = sim.Phase == MatchPhase.Countdown ? Local($"准备  {Mathf.CeilToInt(sim.Countdown)}", $"READY  {Mathf.CeilToInt(sim.Countdown)}") :
                match.IsPaused ? Local("已暂停", "PAUSED") : remaining > 0
                ? Local($"全栏冷却   {remaining:0.0}s", $"HAND LOCKED   {remaining:0.0}s") : Local("战术卡就绪", "TACTICS READY");
            lockFill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                remaining > 0 ? 310 * remaining / sim.CardCooldownDuration : 310);
            bool showDetail = hovered >= 0 && !Blocked;
            tooltip.gameObject.SetActive(showDetail);
            if (showDetail)
            {
                tooltip.anchoredPosition = new Vector2(0, status.anchoredPosition.y + 20 + hoverLift + 31);
                ArenaCard card = sim.CardAt(hovered);
                tooltipText.text = card == ArenaCard.None ? Local("等待局势触发临场机变卡。", "Waiting for a combat event.") : Description(card);
            }
        }

        void Play(int slot, ArenaCard pressed)
        {
            if (!AcceptsShortcuts || pressed == ArenaCard.None || match.Simulation.CardAt(slot) != pressed ||
                !match.Simulation.TryPlayCard(slot))
                return;
            playedCard = pressed;
            reveal = .85f;
            SetCard(played, pressed, Local("战术生效", "TACTIC PLAYED"), true, true);
            played.Rect.gameObject.SetActive(true);
        }

        void UpdateReveal(BeetleSimulation sim)
        {
            if (reveal <= 0 || sim.Phase == MatchPhase.Finished)
            {
                played.Rect.gameObject.SetActive(false);
                flash.color = Color.clear;
                return;
            }
            reveal = Mathf.Max(0, reveal - Time.unscaledDeltaTime);
            float t = 1 - reveal / .85f;
            float grow = 1 - Mathf.Pow(1 - Mathf.Clamp01(t / .38f), 3);
            played.Rect.localScale = Vector3.one * Mathf.Lerp(.65f, 1.75f, grow);
            played.Rect.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-9, 0, grow));
            played.Group.alpha = 1 - Mathf.InverseLerp(.5f, 1, t);
            Color tint = Tint(playedCard);
            tint.a = .2f * (1 - Mathf.Clamp01(t / .42f));
            flash.color = tint;
        }

        void UpdateJelly(BeetleSimulation sim)
        {
            jellyMarker.gameObject.SetActive(sim.JellyOnField);
            if (!sim.JellyOnField)
                return;
            var camera = match.spectatorCamera.GetComponent<Camera>();
            sim.SampleGround(Vector2.zero, out var ground);
            Vector3 point = camera.WorldToScreenPoint(new Vector3(0, ground.height + .3f, 0));
            var eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            bool projected = RectTransformUtility.ScreenPointToLocalPointInRectangle(root, point, eventCamera, out var local);
            bool onScreen = point.z > 0 && projected;
            jellyMarker.gameObject.SetActive(onScreen);
            if (onScreen)
                jellyMarker.anchoredPosition = local;
        }

        void SetCard(CardView view, ArenaCard card, string key, bool ready, bool highlight)
        {
            var color = Tint(card);
            view.Border.color = highlight ? Gold : color;
            view.Group.alpha = ready ? 1 : highlight ? .8f : .58f;
            view.Header.text = key;
            view.Index.color = color;
            view.Index.text = card switch
            {
                ArenaCard.Shove or ArenaCard.Probe => Local("进攻卡", "ATTACK"),
                ArenaCard.Brace => Local("防守卡", "DEFEND"),
                ArenaCard.Sidestep => Local("身法卡", "FOOTWORK"),
                ArenaCard.None => "?",
                >= ArenaCard.LastStand => Local("机变卡", "EVENT"),
                _ => Local("特殊卡", "SPECIAL")
            };
            view.Name.text = card == ArenaCard.None ? Local("临场机变", "EVENT SLOT") : Name(card);
            view.Footer.text = card == ArenaCard.None ? Local("等待触发", "WAITING") : $"{ArenaCardCatalog.Get(card).Cooldown:0}s CD";
        }

        static Color Tint(ArenaCard card) => card switch
        {
            ArenaCard.None => Muted,
            ArenaCard.Shove => new Color(.97f, .52f, .37f),
            ArenaCard.Probe => new Color(.93f, .63f, .46f),
            ArenaCard.Brace => new Color(.43f, .77f, .91f),
            ArenaCard.Sidestep => new Color(.55f, .84f, .95f),
            >= ArenaCard.LastStand => Gold,
            >= ArenaCard.PlayDead => new Color(.76f, .64f, .96f),
            >= ArenaCard.Honeydew => new Color(.54f, .86f, .55f),
            >= ArenaCard.Pheromone => new Color(.85f, .62f, .85f),
            _ => new Color(.43f, .81f, .71f)
        };

        // Card text lives in the GameText table; the authored English text only fills gaps when a
        // translation is missing, so English never falls back to Chinese card names.
        string Local(string zh, string en) => english ? CampaignLocalization.Text(zh, en) : zh;
        string Name(ArenaCard card) => Local(ArenaCardCatalog.Get(card).Name, ArenaCardCatalog.Get(card).EnglishName);
        string Description(ArenaCard card) => Local(ArenaCardCatalog.Get(card).Description, ArenaCardCatalog.Get(card).EnglishDescription);

        void RefreshLanguage()
        {
            if (!root)
                return;
            english = CampaignLocalization.English;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                text.font = CampaignLocalization.Font;
            jellyMarker.GetComponentInChildren<TMP_Text>(true).text = Local("观众果冻", "CROWD JELLY");
            RefreshDraft();
        }

        void RestoreNavigation()
        {
            if (reservedNavigation)
                reservedNavigation.sendNavigationEvents = true;
            reservedNavigation = null;
        }

        void OnDisable()
        {
            RestoreNavigation();
            if (root)
                root.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            CampaignLocalization.Changed -= RefreshLanguage;
            if (root)
                Destroy(root.gameObject);
        }

        static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action<BaseEventData> handler)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => handler(data));
            trigger.triggers.Add(entry);
        }

        static RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static RectTransform Stretch(string name, Transform parent) => Inset(name, parent, 0);

        static RectTransform Inset(string name, Transform parent, float inset)
        {
            var rect = Box(name, parent, Center, Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = -Vector2.one * inset;
            return rect;
        }

        static UnityEngine.UI.Image Fill(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        static UnityEngine.UI.Button MakeButton(RectTransform rect, UnityEngine.UI.Image face, Action clicked)
        {
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = face;
            button.onClick.AddListener(() => clicked());
            return button;
        }

        static TMP_Text Label(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size,
            float fontSize, Color color)
        {
            var text = Box(name, parent, anchor, position, size).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = CampaignLocalization.Font;
            text.fontSize = fontSize;
            text.fontSizeMin = fontSize * .62f;
            text.fontSizeMax = fontSize;
            text.enableAutoSizing = true;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.color = color;
            return text;
        }
    }
}
