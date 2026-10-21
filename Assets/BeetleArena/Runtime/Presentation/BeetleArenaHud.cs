using System;
using TMPro;
using UnityEngine;

namespace BeetleArena
{
    [Serializable]
    // 单名选手的 UI 引用集合；由场景生成器赋值，不保存独立战斗数据。
    public sealed class FighterHud
    {
        // 身份/尺寸、动作、体力、抓地和鼓励按钮的文本组件。
        public TMP_Text name, measurements, action, staminaText, footingText, cheerText;
        // 填充条依次表示体力、平衡×支撑和鼓励冷却进度。
        public UnityEngine.UI.Image staminaFill, footingFill, cheerFill;
        public UnityEngine.UI.Button cheerButton;
    }

    // 表现层：读取 MatchController/Simulation 快照更新 UI，按钮统一转交控制器处理。
    public sealed class BeetleArenaHud : MonoBehaviour
    {
        // 数组顺序与 Fighters/actors 保持一致，0 为琥珀，1 为墨甲。
        [SerializeField] public FighterHud[] fighters;
        [SerializeField] public TMP_Text clock, eventText, phaseText, roundText, resultText, pauseText;
        [SerializeField] public UnityEngine.UI.Button pauseButton, nextButton, rematchButton;
        [SerializeField] public GameObject resultPanel;
        // 切换按钮固定在 HUD 上，不随结算面板隐藏；文案明确提示切换会重开比赛。
        public UnityEngine.UI.Button arenaButton;
        public TMP_Text arenaText;
        public UnityEngine.UI.Button viewButton, slowMotionButton;
        public TMP_Text viewText, slowMotionText;
        public TMP_Text[] traitTexts;
        BeetleMatchController match;

        // 重新绑定前先移除上一控制器的监听，避免重开比赛后一次点击调用多次。
        public void Bind(BeetleMatchController controller)
        {
            Unbind(); match = controller;
            Campaign.CampaignRetroTheme.ApplyHierarchy(transform);
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
        // 按钮适配方法使用固定选手索引；这里只传递请求，资格由控制器和模拟检查。
        void CheerAmber() => match.Cheer(0);
        // 墨甲的鼓励按钮请求，索引为 1。
        void CheerInk() => match.Cheer(1);
        // 暂停/继续按钮统一走控制器入口。
        void Pause() => match.TogglePause();
        // 下一场按钮请求新种子配对，只有结束后才会被控制器接受。
        void Next() => match.NextMatch();
        // 重赛按钮复用本局种子生成初始阵容，不回放过去的输入记录。
        void Rematch() => match.Rematch();
        void SwitchArena() => match.SwitchArena();
        void CycleView() => match.CycleView();
        void ToggleSlowMotion() => match.ToggleDefeatSlowMotion();
        // 解除自己注册的各个具名回调，保留按钮上可能存在的其他监听者。
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
        // HUD 被销毁时清理监听，防止持有失效的控制器引用。
        void OnDestroy() => Unbind();

        // 由控制器以 10Hz 及关键按钮操作后调用。显示值来自当前模拟，无额外游戏判定。
        public void Refresh()
        {
            if (!match || match.Simulation == null) return;
            var sim = match.Simulation;
            if (arenaText) arenaText.text = "擂台：" + match.ArenaName + "  [T]\n切换并重开";
            if (viewText) viewText.text = "视角：" + match.spectatorCamera.ViewName + "  [C]";
            if (slowMotionText) slowMotionText.text = "击败慢镜头：" + (match.defeatSlowMotion ? "开" : "关") + "  [V]";
            for (int i = 0; i < 2; i++)
            {
                var b = sim.Fighters[i]; var ui = fighters[i];
                ui.name.text = sim.FighterName(i);
                ui.staminaFill.fillAmount = b.Stamina01;
                // 按钮可点击状态同时考虑比赛阶段、暂停、冷却和腾空/落台；模拟层仍会二次检查。
                bool canCheer = sim.Phase == MatchPhase.Fighting && !match.IsPaused && b.CheerCooldown <= 0 && !b.Airborne && !b.HasFallen;
                ui.cheerButton.interactable = canCheer;
                string key = i == 0 ? "Q" : "E";
                ui.cheerText.text = b.CheerCooldown > .05f ? $"{key}   加油恢复中 · {b.CheerCooldown:F1}s" : $"{key}   为{sim.FighterName(i)}加油";
                ui.cheerFill.fillAmount = 1 - b.CheerCooldown / match.settings.cheerCooldown;
            }
            clock.text = $"{(int)sim.Elapsed / 60:00}:{(int)sim.Elapsed % 60:00}";
            int remaining = Mathf.CeilToInt(Mathf.Max(0, Mathf.Min(sim.FightTimeLimit - sim.Elapsed,
                BeetleSimulation.MaximumRoundSeconds - match.RoundClock)));
            roundText.text = sim.Phase == MatchPhase.Finished ? $"第 {match.RoundNumber:00} 场  ·  已结束" :
                $"第 {match.RoundNumber:00} 场  ·  剩余 {remaining / 60:00}:{remaining % 60:00}";
            eventText.text = match.LatestEvent;
            pauseText.text = match.IsPaused ? "继续  [空格]" : "暂停  [空格]";
            pauseButton.interactable = sim.Phase != MatchPhase.Finished;
            bool finished = sim.Phase == MatchPhase.Finished;
            // 结算面板只在 Finished 展示，Resolving 阶段仍等待可能出现的双落台。
            resultPanel.SetActive(finished && !match.IsDefeatSlowMotion);
            resultText.text = (sim.Winner < 0 ? "本场平局" : $"{sim.FighterName(sim.Winner)} 获胜") + "\n<size=20>" + sim.ResultDescription + "</size>";
            nextButton.interactable = rematchButton.interactable = finished && match.CanRequestNextMatch;
            // 显示外层交锋阶段；选手区只呈现名字与体力。
            phaseText.text = match.IsPaused ? "观察暂停" : sim.Phase == MatchPhase.Countdown ? $"准备  {Mathf.CeilToInt(sim.Countdown)}" : sim.Phase == MatchPhase.Resolving ? "落台判定中" : finished ? "本场结束" :
                sim.Exchange == ExchangePhase.Clash ? "激烈交锋" : sim.Exchange == ExchangePhase.Recovery ? "拉开恢复" : "轻触试探";
            phaseText.color = sim.Phase == MatchPhase.Fighting && sim.Exchange == ExchangePhase.Clash && !match.IsPaused
                ? new Color(1f,.7f,.3f) : Color.white;
        }

        // 动作枚举到中文标签的映射；新增动作时同步维护此处和字体字形资源。
        public static string ActionName(BeetleAction action)
        {
            switch (action)
            {
                case BeetleAction.Assess: return "触角 / 角尖试探";
                case BeetleAction.Probe: return "轻触角尖 / 试探短推";
                case BeetleAction.Disengage: return "收角 / 拉开半步";
                case BeetleAction.ChargeWindup: return "后足撑地 / 蓄力";
                case BeetleAction.Charge: return "猛烈冲顶";
                case BeetleAction.HornLock: return "顶角僵持 / 持续发力";
                case BeetleAction.Approach: return "接近对手";
                case BeetleAction.Circle: return "侧移寻找入角位置";
                case BeetleAction.ShoveWindup: return "压低头部";
                case BeetleAction.Shove: return "短促推顶";
                case BeetleAction.PryWindup: return "入角到腹下";
                case BeetleAction.Pry: return "低头寻找承重点";
                case BeetleAction.Lifting: return "角下托举 / 扭转";
                case BeetleAction.Lifted: return "被撬起 / 失去抓地";
                case BeetleAction.ReleaseLift: return "收角 / 后撤脱离";
                case BeetleAction.Toss: return "猛挑 / 甩离台面";
                case BeetleAction.Launched: return "被挑飞 / 空中挣扎";
                case BeetleAction.Brace: return "六足撑地";
                case BeetleAction.Recover: return "调整呼吸与重心";
                case BeetleAction.Retreat: return "疲劳退让";
                case BeetleAction.Stagger: return "挣扎恢复支撑";
                case BeetleAction.Falling: return "失去台面支撑";
                case BeetleAction.Defeated: return "已落台";
                case BeetleAction.Subdued: return "停止对抗 / 撑地恢复";
                case BeetleAction.Winner: return "守住擂台";
                default: return "观察中";
            }
        }
    }
}
