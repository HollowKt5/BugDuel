using System;
using System.Collections.Generic;
using BugDuel.Domain;
using TMPro;
using UnityEngine;
using L=BeetleArena.Campaign.CampaignLocalization;

namespace BeetleArena
{
    /// <summary>uGUI displays session snapshots and dispatches care commands only; recipes, environmental eligibility, and health handled by domain services.</summary>
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
        Color normal => Campaign.CampaignRetroTheme.Button;
        Color selected => Campaign.CampaignRetroTheme.Selection;
        int contentVersion = -1, rosterCount = -1;
        bool bound;

        void Start() => BeetleCombatDisplay.Install(GetComponent<Canvas>().worldCamera);
        void OnEnable()
        {
            Campaign.CampaignRetroTheme.Changed += RefreshTheme;
            Bind();
        }
        void RefreshTheme()
        {
            if(!bound)return;
            Campaign.CampaignRetroTheme.ApplyHierarchy(transform);
            Refresh();
        }
        public void Bind()
        {
            if (bound || !controller) return;
            Campaign.CampaignRetroTheme.ApplyHierarchy(transform);
            RetroUiAudio.BindHierarchy(transform);
            controller.Changed += Refresh;
            helpButton.onClick.AddListener(Help);
            pauseButton.onClick.AddListener(Pause);
            saveButton.onClick.AddListener(Save);
            saveButton.gameObject.SetActive(false);
            reloadButton.onClick.AddListener(Reload);
            bound = true;
            Refresh();
        }
        void OnDisable()
        {
            Campaign.CampaignRetroTheme.Changed -= RefreshTheme;
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
            L.Bind(button.GetComponentInChildren<TMP_Text>(),label);
            button.onClick.RemoveAllListeners();
            RetroUiAudio.Bind(button);
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
            L.Bind(selectedName,selectedIndividual.DisplayName + "  <size=17>" + species.DisplayName + "</size>");
            L.Bind(conditionText,$"健康  {selectedIndividual.Condition.Health:0} / 100\n营养  {selectedIndividual.Condition.Nutrition:0} / 100\n水分  {selectedIndividual.Condition.Hydration:0} / 100\n压力  {selectedIndividual.Condition.Stress:0} / 100");
            statusText.color = dead || selectedIndividual.IsOverturned ? Campaign.CampaignRetroTheme.Price : Campaign.CampaignRetroTheme.Ink;
            L.Bind(statusText,dead ? "已死亡 · 停止活动" : selectedIndividual.IsOverturned ? "持续挣扎 · 需要扶正" : "姿态正常 · 可以照料");
            string detail;
            if (dead) detail = "健康已经耗尽，喂食或扶正不能复活。";
            else if (selectedIndividual.IsOverturned)
            {
                double grace = Math.Max(0, species.Care.OverturnedGraceDays - selectedIndividual.OverturnedDays) * controller.secondsPerGameDay;
                detail = grace > 0 ? $"救助宽限剩余 {Math.Ceiling(grace):0} 秒\n请先帮它翻身，再喂食。" : "挣扎正在消耗健康！\n立即扶正可停止翻倒损伤。";
            }
            else detail = "红薯、水果和果冻补充不同营养。\n翻倒时先扶正，再安排进食。";
            L.Bind(detailsText,detail);
            L.Bind(messageText,controller.Message);
            L.Bind(pauseText,controller.IsPaused ? "继续养殖" : "暂停养殖");
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
                bool isSelected = session.GetSelectedHabitatId(selectedIndividual.Id) == habitat.Id;
                button.image.color = isSelected ? selected : normal;
                button.GetComponentInChildren<TMP_Text>().color = isSelected ? Campaign.CampaignRetroTheme.SelectionInk : Campaign.CampaignRetroTheme.Ink;
            }
            foreach (BeetleIndividual individual in roster)
            {
                var button = individuals[individual.Id];
                bool isSelected = individual.Id == selectedIndividual.Id;
                button.image.color = isSelected ? selected : normal;
                button.GetComponentInChildren<TMP_Text>().color = isSelected ? Campaign.CampaignRetroTheme.SelectionInk : Campaign.CampaignRetroTheme.Ink;
                L.Bind(button.GetComponentInChildren<TMP_Text>(),individual.DisplayName + "\n<size=15>" +
                    (individual.Stage == LifeStage.Dead ? "已死亡" : individual.IsOverturned ? "需要救助！" : "正常") + "</size>");
            }
        }
    }
}
