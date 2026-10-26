using System;
using System.Collections.Generic;
using System.IO;
using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Application entry for husbandry scenes: owns timing, selection, and save policies; decoupled from match simulation or bone animation.</summary>
    public sealed class BeetleHusbandryController : MonoBehaviour
    {
        [Min(1)] public float secondsPerGameDay = 600;
        public bool simulateAccidents = true;
        public bool loadOnStart = true;
        public BeetleRosterSession Session { get; private set; }
        public string SelectedId { get; private set; }
        public string Message { get; private set; }
        public bool IsPaused { get; private set; }
        public bool CanSave { get; private set; }
        public string SavePath { get; private set; }
        public string ContentPath { get; private set; }
        public event Action Changed;
        readonly System.Random accidents = new System.Random();
        BeetleRosterJsonStore store;
        double updateSeconds, saveSeconds;

        void Start() { if (Session == null) Initialize(null, loadOnStart); }

        public void Initialize(string absoluteSavePath = null, bool loadSavedRoster = true)
        {
            if (Session != null) return;
            SavePath = absoluteSavePath ?? Path.Combine(BeetleStoragePaths.PersistentData, "husbandry-roster.json");
            store = new BeetleRosterJsonStore(SavePath);
            ContentPath = Path.Combine(Path.GetDirectoryName(SavePath), "beetle-content.json");
            Session = new BeetleRosterSession(BeetleContentExamples.Create(), new Dictionary<string, BeetleCombatProfile>());
            CanSave = true;
            if (File.Exists(ContentPath))
            {
                DomainResult content = new BeetleContentService(Session).TryReloadFile(ContentPath);
                if (!content.Success) Message = "内容更新未通过校验，继续使用原有定义。";
            }
            bool loaded = false;
            if (loadSavedRoster && File.Exists(SavePath))
            {
                try { Session.Restore(store.Load()); loaded = true; Message = "已读取养殖记录；离线期间不推进照料时间。"; }
                catch (Exception error) when (error is IOException || error is ArgumentException || error is InvalidOperationException ||
                    error is NotSupportedException || error is UnauthorizedAccessException)
                {
                    // Save load failures do not overwrite existing player saves; temporary session remains interactive.
                    CanSave = false;
                    Message = "存档读取失败，当前使用临时示例；原存档已保留。";
                    Debug.LogWarning("Husbandry save could not be restored: " + error.Message, this);
                }
            }
            if (!loaded) CreateStarterRoster();
            var roster = Session.Snapshot();
            SelectedId = roster.Count > 0 ? roster[0].Id : null;
            Changed?.Invoke();
        }

        void CreateStarterRoster()
        {
            if (!Session.Content.Species.TryGet("example.horn-beetle", out SpeciesDefinition species))
            {
                foreach (SpeciesDefinition candidate in Session.Content.Species.Definitions) { species = candidate; break; }
            }
            if (species == null) { Message = "当前内容包没有可创建的甲虫种类。"; return; }
            AddStarter(species, "nursery.amber", "琥珀", BeetleSex.Female, 58, 66);
            AddStarter(species, "nursery.ink", "墨甲", BeetleSex.Male, 48, 56);
            Session.MarkOverturned("nursery.amber");
            if (CanSave) Message = "琥珀翻倒了！选中它并点击「帮它翻身」，再准备食物。";
        }

        void AddStarter(SpeciesDefinition species, string id, string name, BeetleSex sex, float nutrition, float hydration)
        {
            var data = BeetleSaveMapper.Export(BeetleIndividual.Create(species, id, name, sex));
            data.Nutrition = nutrition; data.Hydration = hydration;
            RestoreResult restored = BeetleSaveMapper.Restore(data, Session.Content.Species);
            if (!restored.Success) throw new InvalidOperationException(restored.ErrorMessage);
            Session.Add(restored.Individual);
            var habitats = Session.AvailableHabitats(id);
            if (habitats.Count > 0) Session.SelectHabitat(id, habitats[0].Id);
        }

        public BeetleIndividual Selected => Session != null && Session.TryGet(SelectedId, out var individual) ? individual : null;

        public void SelectIndividual(string id)
        {
            if (Session == null || !Session.TryGet(id, out _)) return;
            SelectedId = id;
            Changed?.Invoke();
        }

        public DomainResult Feed(string foodId)
        {
            DomainResult result = Session.Feed(SelectedId, foodId);
            return FinishAction(result, "食物已吃下，营养和水分已补充。");
        }

        public DomainResult HelpRight() => FinishAction(Session.HelpRight(SelectedId), "已轻轻扶正。甲虫停止挣扎，可以继续进食和休养。");

        public DomainResult SelectHabitat(string habitatId) =>
            FinishAction(Session.SelectHabitat(SelectedId, habitatId), "已迁入新的养殖环境。");

        DomainResult FinishAction(DomainResult result, string success)
        {
            Message = result.Success ? success : Explain(result.ErrorCode);
            if (result.Success) TrySave(false);
            Changed?.Invoke();
            return result;
        }

        public void SetPaused(bool paused) { IsPaused = paused; updateSeconds = 0; Changed?.Invoke(); }
        public void TogglePause() => SetPaused(!IsPaused);

        void Update()
        {
            if (Session == null || IsPaused) return;
            updateSeconds += Time.unscaledDeltaTime;
            if (updateSeconds < .2) return;
            double elapsed = updateSeconds; updateSeconds = 0;
            AdvanceSeconds(elapsed);
            saveSeconds += elapsed;
            if (saveSeconds >= 10) { saveSeconds = 0; TrySave(false); }
        }

        /// <summary>Explicit scene clock; pausing/offline incurs no vitality penalties. Incidents generate domain commands without controlling animations.</summary>
        public void AdvanceSeconds(double seconds)
        {
            if (Session == null || IsPaused) return;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || !float.IsFinite(secondsPerGameDay) || secondsPerGameDay < 1)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            double days = seconds / secondsPerGameDay;
            foreach (BeetleIndividual before in Session.Snapshot())
            {
                if (before.Stage == LifeStage.Dead || Session.IsOccupied(before.Id)) continue;
                DomainResult advanced = Session.AdvanceInSelectedHabitat(before.Id, days);
                if (!advanced.Success)
                {
                    if (before.Id == SelectedId) Message = Explain(advanced.ErrorCode);
                    continue;
                }
                Session.TryGet(before.Id, out BeetleIndividual current);
                if (current.Stage == LifeStage.Dead)
                {
                    Message = current.DisplayName + "已死亡。照料和扶正不能使它复活。";
                    continue;
                }
                if (!simulateAccidents || current.Stage != LifeStage.Adult || current.IsOverturned || days == 0) continue;
                Session.Content.TryGetHabitat(Session.GetSelectedHabitatId(current.Id), out HabitatDefinition habitat);
                double rate = .8 + habitat.Environment.Crowding * 4 + (100 - habitat.Environment.CleanlinessPercent) / 50;
                if (accidents.NextDouble() < 1 - Math.Exp(-rate * days))
                {
                    Session.MarkOverturned(current.Id);
                    Message = current.DisplayName + "翻倒后没能站起来，正在挣扎，需要帮助！";
                }
            }
            Changed?.Invoke();
        }

        public bool Save() => TrySave(true);
        bool TrySave(bool announce)
        {
            if (!CanSave || Session == null) return false;
            try
            {
                store.Save(Session.Export());
                if (announce) { Message = "养殖记录已保存。"; Changed?.Invoke(); }
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException)
            {
                Message = "保存失败，已有存档仍然保留。";
                if (announce) Changed?.Invoke();
                Debug.LogWarning("Husbandry save failed: " + error.Message, this);
                return false;
            }
        }

        public DomainResult ReloadContent()
        {
            DomainResult result = new BeetleContentService(Session).TryReloadFile(ContentPath);
            Message = result.Success ? "甲虫种类与养殖环境已更新。" : "没有可用的兼容更新包，当前内容保持不变。";
            Changed?.Invoke();
            return result;
        }

        void OnApplicationPause(bool paused) { if (paused) TrySave(false); }
        void OnDestroy() { TrySave(false); }

        static string Explain(string code)
        {
            switch (code)
            {
                case "IncompatibleFood": return "这个阶段或种类不能吃这种食物。";
                case "NeedsRighting": return "甲虫还在挣扎，请先帮它翻身，再喂食。";
                case "RelocationRequired": return "当前环境不再适合这个阶段，请先更换养殖环境。";
                case "HabitatUnavailable": return "这个环境不适合当前甲虫，请选择可用的环境。";
                case "IndividualInMatch": return "这只甲虫正在参赛，暂时不能照料。";
                default: return "操作未完成，请检查甲虫是否存活、翻倒或正处于不适合的阶段。";
            }
        }
    }
}
