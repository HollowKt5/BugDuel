using System;
using System.Collections.Generic;
using BugDuel.Domain;
using TMPro;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>uGUI 只显示会话快照和发送照料命令；食谱、环境资格、伤害由领域服务处理。</summary>
    public sealed class BeetleHusbandryHud : MonoBehaviour
    {
        public BeetleHusbandryController controller;
        public TMP_Text selectedName, statusText, conditionText, detailsText, messageText, pauseText;
        public UnityEngine.UI.Button helpButton, pauseButton, saveButton, reloadButton;
        public Transform individualList, foodList, habitatList;
        public UnityEngine.UI.Button choiceTemplate;
        readonly Dictionary<string, UnityEngine.UI.Button> individuals = new Dictionary<string, UnityEngine.UI.Button>();
        readonly Dictionary<string, UnityEngine.UI.Button> foods = new Dictionary<string, UnityEngine.UI.Button>();
        readonly Dictionary<string, UnityEngine.UI.Button> habitats = new Dictionary<string, UnityEngine.UI.Button>();
        readonly Color normal = new Color(.16f, .23f, .19f), selected = new Color(.30f, .43f, .30f);
        int contentVersion = -1, rosterCount = -1;
        bool bound;

        void OnEnable() => Bind();
        public void Bind()
        {
            if (bound || !controller) return;
            controller.Changed += Refresh;
            helpButton.onClick.AddListener(Help);
            pauseButton.onClick.AddListener(Pause);
            saveButton.onClick.AddListener(Save);
            reloadButton.onClick.AddListener(Reload);
            bound = true;
            Refresh();
        }
        void OnDisable()
        {
            if (!bound) return;
            if (controller) controller.Changed -= Refresh;
            helpButton.onClick.RemoveListener(Help); pauseButton.onClick.RemoveListener(Pause);
            saveButton.onClick.RemoveListener(Save); reloadButton.onClick.RemoveListener(Reload);
            bound = false;
        }
        void Help() => controller.HelpRight();
        void Pause() => controller.TogglePause();
        void Save() => controller.Save();
        void Reload() => controller.ReloadContent();

        UnityEngine.UI.Button Choice(Transform parent, string label, Action action)
        {
            var button = Instantiate(choiceTemplate, parent);
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<TMP_Text>().text = label;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            return button;
        }
        void Clear(Dictionary<string, UnityEngine.UI.Button> buttons)
        {
            foreach (var button in buttons.Values)
                if (Application.isPlaying) Destroy(button.gameObject); else DestroyImmediate(button.gameObject);
            buttons.Clear();
        }

        public void Refresh()
        {
            if (!controller || controller.Session == null || !choiceTemplate) return;
            var session = controller.Session;
            var roster = session.Snapshot();
            bool rosterChanged = rosterCount != session.Count;
            foreach (BeetleIndividual individual in roster) rosterChanged |= !individuals.ContainsKey(individual.Id);
            if (rosterChanged)
            {
                Clear(individuals);
                foreach (BeetleIndividual individual in roster)
                {
                    string id = individual.Id;
                    individuals.Add(id, Choice(individualList, individual.DisplayName, () => controller.SelectIndividual(id)));
                }
                rosterCount = session.Count;
            }
            if (contentVersion != session.Content.Version)
            {
                Clear(foods); Clear(habitats);
                foreach (FoodDefinition food in session.Foods.Definitions)
                {
                    string id = food.Id;
                    foods.Add(id, Choice(foodList, food.DisplayName + "\n<size=14>营养 +" + food.Portion.Nutrition + " · 水分 +" + food.Portion.Hydration + "</size>", () => controller.Feed(id)));
                }
                foreach (HabitatDefinition habitat in session.Content.Habitats)
                {
                    string id = habitat.Id;
                    habitats.Add(id, Choice(habitatList, habitat.DisplayName, () => controller.SelectHabitat(id)));
                }
                contentVersion = session.Content.Version;
            }
            BeetleIndividual selectedIndividual = controller.Selected;
            if (selectedIndividual == null) return;
            session.Content.Species.TryGet(selectedIndividual.SpeciesId, out SpeciesDefinition species);
            bool dead = selectedIndividual.Stage == LifeStage.Dead;
            bool occupied = session.IsOccupied(selectedIndividual.Id);
            selectedName.text = selectedIndividual.DisplayName + "  <size=17>" + species.DisplayName + "</size>";
            conditionText.text = $"健康  {selectedIndividual.Condition.Health:0} / 100\n营养  {selectedIndividual.Condition.Nutrition:0} / 100\n水分  {selectedIndividual.Condition.Hydration:0} / 100\n压力  {selectedIndividual.Condition.Stress:0} / 100";
            statusText.color = dead || selectedIndividual.IsOverturned ? new Color(1, .64f, .40f) : new Color(.76f, .89f, .68f);
            statusText.text = dead ? "已死亡 · 停止活动" : selectedIndividual.IsOverturned ? "持续挣扎 · 需要扶正" : "姿态正常 · 可以照料";
            string detail;
            if (dead) detail = "健康已经耗尽，喂食或扶正不能复活。";
            else if (selectedIndividual.IsOverturned)
            {
                double grace = Math.Max(0, species.Care.OverturnedGraceDays - selectedIndividual.OverturnedDays) * controller.secondsPerGameDay;
                detail = grace > 0 ? $"救助宽限剩余 {Math.Ceiling(grace):0} 秒\n请先帮它翻身，再喂食。" : "挣扎正在消耗健康！\n立即扶正可停止翻倒损伤。";
            }
            else detail = "红薯、水果和果冻补充不同营养。\n翻倒时先扶正，再安排进食。";
            detailsText.text = detail;
            messageText.text = controller.Message;
            pauseText.text = controller.IsPaused ? "继续养殖" : "暂停养殖";
            helpButton.interactable = !dead && selectedIndividual.IsOverturned && !occupied;
            saveButton.interactable = controller.CanSave;
            FoodKind diet = selectedIndividual.Stage == LifeStage.Adult ? species.Care.AdultFoods :
                selectedIndividual.Stage == LifeStage.Larva ? species.Care.LarvalFoods : FoodKind.None;
            foreach (FoodDefinition food in session.Foods.Definitions)
                foods[food.Id].interactable = !dead && !selectedIndividual.IsOverturned && !occupied && (diet & food.Portion.Kind) != 0;
            foreach (HabitatDefinition habitat in session.Content.Habitats)
            {
                var button = habitats[habitat.Id];
                button.interactable = !dead && !occupied && habitat.Allows(selectedIndividual.Stage);
                button.image.color = session.GetSelectedHabitatId(selectedIndividual.Id) == habitat.Id ? selected : normal;
            }
            foreach (BeetleIndividual individual in roster)
            {
                var button = individuals[individual.Id];
                button.image.color = individual.Id == selectedIndividual.Id ? selected : normal;
                button.GetComponentInChildren<TMP_Text>().text = individual.DisplayName + "\n<size=15>" +
                    (individual.Stage == LifeStage.Dead ? "已死亡" : individual.IsOverturned ? "需要救助！" : "正常") + "</size>";
            }
        }
    }
}
