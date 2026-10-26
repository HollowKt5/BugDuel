using System;
using TMPro;
using UnityEngine;
using L=BeetleArena.Campaign.CampaignLocalization;

namespace BeetleArena
{
    [Serializable]
    // UI reference container for an individual contestant; assigned by scene builder, holds no independent match state.
    public sealed class FighterHud
    {
        // Text components for identity/size, action, stamina, grip, and encouragement button.
        public TMP_Text name, measurements, action, staminaText, footingText, cheerText;
        // Fill bars represent stamina, balance * support, and encouragement cooldown progress in sequence.
        public UnityEngine.UI.Image staminaFill, footingFill, cheerFill;
        public UnityEngine.UI.Button cheerButton;
    }

    // Presentation layer: reads MatchController/Simulation snapshots to update UI; delegates button actions to controller.
    public sealed class BeetleArenaHud : MonoBehaviour
    {
        // Array ordering mirrors Fighters/actors: 0 for Amber, 1 for Obsidian.
        [SerializeField] public FighterHud[] fighters;
        [SerializeField] public TMP_Text clock, eventText, phaseText, roundText, resultText, pauseText;
        [SerializeField] public UnityEngine.UI.Button pauseButton, nextButton, rematchButton;
        [SerializeField] public GameObject resultPanel;
        // Arena swap button is pinned to HUD, persisting across result screens; copy explicitly indicates swapping restarts match.
        public UnityEngine.UI.Button arenaButton;
        public TMP_Text arenaText;
        public UnityEngine.UI.Button viewButton, slowMotionButton;
        public TMP_Text viewText, slowMotionText;
        public TMP_Text[] traitTexts;
        BeetleMatchController match;
        BeetlePanelMotion resultMotion;

        // Unbinds previous controller listeners prior to rebinding, preventing duplicate invocations per click across restarts.
        public void Bind(BeetleMatchController controller)
        {
            Unbind(); match = controller;
            resultMotion=BeetlePanelMotion.Attach(resultPanel);resultMotion.SetVisible(false,false);
            Campaign.CampaignRetroTheme.ApplyHierarchy(transform);
            RetroUiAudio.BindHierarchy(transform);
            SimplifyFighterCards();
            fighters[0].cheerButton.onClick.AddListener(CheerAmber);
            fighters[1].cheerButton.onClick.AddListener(CheerInk);
            pauseButton.onClick.AddListener(Pause);
            nextButton.onClick.AddListener(Next);
            rematchButton.onClick.AddListener(Rematch);
            if (arenaButton) arenaButton.onClick.AddListener(SwitchArena);
            if (viewButton) viewButton.onClick.AddListener(CycleView);
            if (slowMotionButton) slowMotionButton.onClick.AddListener(ToggleSlowMotion);
        }
        // Keep existing serialized references valid while reducing each fighter card to name + stamina.
        void SimplifyFighterCards()
        {
            foreach (var ui in fighters)
            {
                var card = ui.name.transform.parent;
                if (card.TryGetComponent<UnityEngine.UI.Image>(out var background)) background.enabled = false;
                if (card.TryGetComponent<Campaign.RetroBevel>(out var bevel)) bevel.enabled = false;
                ui.name.color = Color.white;
                ui.measurements.gameObject.SetActive(false);
                ui.action.gameObject.SetActive(false);
                ui.staminaText.gameObject.SetActive(false);
                ui.footingText.gameObject.SetActive(false);
                ui.footingFill.transform.parent.gameObject.SetActive(false);
                var track = (RectTransform)ui.staminaFill.transform.parent;
                track.anchoredPosition = new Vector2(20, -60);
                track.sizeDelta = new Vector2(310, 7);
                ui.staminaFill.rectTransform.sizeDelta = track.sizeDelta;
                if (track.TryGetComponent<UnityEngine.UI.Image>(out var trackImage)) trackImage.color = new Color(.32f, .36f, .34f);
                if (track.TryGetComponent<Campaign.RetroBevel>(out var trackBevel)) trackBevel.enabled = false;
            }
            if (traitTexts != null) foreach (var text in traitTexts) if (text) text.gameObject.SetActive(false);
        }
        // Button handler delegates using fixed contestant indices; forwards requests while controller and simulation evaluate eligibility.
        void CheerAmber() => match.Cheer(0);
        // Encouragement button request for Obsidian (index 1).
        void CheerInk() => match.Cheer(1);
        // Pause/Resume button routed uniformly through controller entry.
        void Pause() => match.TogglePause();
        // Next Match button requests a new seed pairing; accepted by controller only post-match.
        void Next() => match.NextMatch();
        // Rematch button recreates initial roster with current seed, without replaying historical inputs.
        void Rematch() => match.Rematch();
        void SwitchArena() => match.SwitchArena();
        void CycleView() => match.CycleView();
        void ToggleSlowMotion() => match.ToggleDefeatSlowMotion();
        // Unregisters named callbacks registered by this component, preserving third-party button listeners.
        void Unbind()
        {
            if (!match) return;
            fighters[0].cheerButton.onClick.RemoveListener(CheerAmber);
            fighters[1].cheerButton.onClick.RemoveListener(CheerInk);
            pauseButton.onClick.RemoveListener(Pause);
            nextButton.onClick.RemoveListener(Next);
            rematchButton.onClick.RemoveListener(Rematch);
            if (arenaButton) arenaButton.onClick.RemoveListener(SwitchArena);
            if (viewButton) viewButton.onClick.RemoveListener(CycleView);
            if (slowMotionButton) slowMotionButton.onClick.RemoveListener(ToggleSlowMotion);
        }
        // Cleans up listeners when HUD is destroyed, preventing holding stale controller references.
        void OnDestroy() => Unbind();

        // Invoked by controller at 10Hz and following key button clicks. Values reflect current simulation with no secondary gameplay checks.
        public void Refresh()
        {
            if (!match || match.Simulation == null) return;
            var sim = match.Simulation;
            if (arenaText) L.Bind(arenaText,"擂台：" + match.ArenaName + "  [T]\n切换并重开");
            if (viewText) L.Bind(viewText,"视角：" + match.spectatorCamera.ViewName + "  [C]");
            if (slowMotionText) L.Bind(slowMotionText,"击败慢镜头：" + (match.defeatSlowMotion ? "开" : "关") + "  [V]");
            for (int i = 0; i < 2; i++)
            {
                var b = sim.Fighters[i]; var ui = fighters[i];
                L.Bind(ui.name,sim.FighterName(i));
                ui.staminaFill.fillAmount = b.Stamina01;
                // Button interactability accounts for match phase, pause, cooldown, and airborne/ring-out; simulation performs authoritative secondary checks.
                bool canCheer = sim.Phase == MatchPhase.Fighting && !match.IsPaused && b.CheerCooldown <= 0 && !b.Airborne && !b.HasFallen;
                ui.cheerButton.interactable = canCheer;
                string key = i == 0 ? "Q" : "E";
                L.Bind(ui.cheerText,b.CheerCooldown > .05f ? $"{key}   加油恢复中 · {b.CheerCooldown:F1} 秒" : $"{key}   为{sim.FighterName(i)}加油");
                ui.cheerFill.fillAmount = 1 - b.CheerCooldown / match.settings.cheerCooldown;
            }
            clock.text = $"{(int)sim.Elapsed / 60:00}:{(int)sim.Elapsed % 60:00}";
            int remaining = Mathf.CeilToInt(Mathf.Max(0, Mathf.Min(sim.FightTimeLimit - sim.Elapsed,
                BeetleSimulation.MaximumRoundSeconds - match.RoundClock)));
            L.Bind(roundText,sim.Phase == MatchPhase.Finished ? $"第 {match.RoundNumber:00} 场  ·  已结束" :
                $"第 {match.RoundNumber:00} 场  ·  剩余 {remaining / 60:00}:{remaining % 60:00}");
            L.Bind(eventText,match.LatestEvent);
            L.Bind(pauseText,match.IsPaused ? "继续  [空格]" : "暂停  [空格]");
            pauseButton.interactable = sim.Phase != MatchPhase.Finished;
            bool finished = sim.Phase == MatchPhase.Finished;
            // Settlement panel displays strictly on Finished; Resolving phase continues waiting for potential double ring-outs.
            resultMotion.SetVisible(finished && !match.IsDefeatSlowMotion);
            if(finished)L.Bind(resultText,(sim.Winner < 0 ? "本场平局" : $"{sim.FighterName(sim.Winner)} 获胜") + "\n<size=20>" + sim.ResultDescription + "</size>");
            nextButton.interactable = rematchButton.interactable = finished && match.CanRequestNextMatch;
            // Displays outer bout pacing phase; contestant card displays name and stamina only.
            L.Bind(phaseText,match.IsPaused ? "观察暂停" : sim.Phase == MatchPhase.Countdown ? $"准备  {Mathf.CeilToInt(sim.Countdown)}" : sim.Phase == MatchPhase.Resolving ? "落台判定中" : finished ? "本场结束" :
                sim.Exchange == ExchangePhase.Clash ? "激烈交锋" : sim.Exchange == ExchangePhase.Recovery ? "拉开恢复" : "轻触试探");
            phaseText.color = sim.Phase == MatchPhase.Fighting && sim.Exchange == ExchangePhase.Clash && !match.IsPaused
                ? new Color(1f,.7f,.3f) : Color.white;
        }
    }
}
