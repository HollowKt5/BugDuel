using System;
using System.Collections.Generic;
using System.Linq;
using BugDuel.Campaign;
using BugDuel.Domain;
using TMPro;
using UnityEngine;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    // An outer driver. No changes to combat decisions, physics, win conditions, rigs or camera code.
    public sealed class CampaignLeagueBridge : MonoBehaviour
    {
        public BeetleMatchController controller;
        [NonSerialized] public TMP_Text playerLabel,opponentLabel;
        [NonSerialized] public UI.Image playerBar,opponentBar,playerStructure,opponentStructure;
        public event Action Finished;
        public bool IsFinished { get; private set; }
        public string ResultTitle { get; private set; }
        public int Speed { get; private set; } = 1;
        CampaignApp app;
        PendingBattle reservation;
        BeetleMatchResult result;
        float accumulator,hudTime,retryTime,finishTime;
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        readonly float[] structure={1,1};
        readonly int[] previousContacts={0,0};
        void Awake() { if(controller)controller.enabled=false; }
        void Start()
        {
            app=CampaignApp.Ensure();reservation=app.Session?.State.battle;
            if(reservation==null){app.Go(CampaignPage.League);return;}
            try
            {
                Speed=app.Preferences.battleSpeed;
                controller.settings=Instantiate(controller.settings);owned.Add(controller.settings);controller.settings.Validate();
                controller.defeatSlowMotion=false;controller.hud.Bind(controller);
                var oldCanvas=controller.hud.GetComponentInParent<Canvas>();if(oldCanvas)oldCanvas.gameObject.SetActive(false);
                controller.SelectArena(0);
                var first=Entrant(app.Session.Beetle(reservation.beetleId));var second=Entrant(reservation.opponent);
                var sim=new BeetleSimulation(controller.settings,reservation.seed,first,second,reservation.id);
                controller.MatchCompleted+=OnCompleted;controller.StartMatch(sim);app.ApplyPreferences();
                for(int i=0;i<2;i++)
                {
                    var b=i==0?app.Session.Beetle(reservation.beetleId):reservation.opponent;
                    var rig=controller.actors[i].GetComponentInChildren<BeetleProceduralAnimator>();
                    CampaignSpecimen.Tint(rig,Color.HSVToRGB((float)app.Session.Catalog.Species(b.speciesId).hue+(i==0?0:.08f),.65f,i==0?.75f:.45f));
                }
            }
            catch(Exception e){ResultTitle="赛场载入失败";IsFinished=true;Debug.LogException(e);Finished?.Invoke();}
        }
        BeetleMatchEntrant Entrant(CampaignBeetle b)
        {
            var species=ScriptableObject.CreateInstance<BeetleSpeciesAsset>();owned.Add(species);species.speciesId=b.speciesId;species.displayName=app.Session.Catalog.Species(b.speciesId).name;
            species.adultLengthMm=(float)b.length;species.adultMassGrams=Mathf.Max(1,(float)(8*Math.Pow(b.length/65,3)));
            var profile=ScriptableObject.CreateInstance<BeetleCombatProfile>();owned.Add(profile);profile.species=species;profile.supportFootOffsets=(Vector3[])controller.settings.supportFootOffsets.Clone();profile.visualScale=controller.settings.visualScale;
            // Keep the calibrated rig dimensions; express within-tier size advantage through normalized physical traits.
            profile.normalizedMass=Mathf.Clamp((float)(b.length/reservation.opponent.length),.8f,1.25f);profile.strength=Mathf.Sqrt(profile.normalizedMass);
            float Potential(string id)=>Mathf.Clamp01(.5f+b.genes.Where(g=>g.id==id).Sum(g=>g.stars)*.08f);
            var individual=BeetleIndividual.Create(species.ToDefinition(),b.id,b.name,b.female?BeetleSex.Female:BeetleSex.Male,LifeStage.Adult,
                new BugDuel.Domain.BeetleTraits(Potential("力量"),Potential("甲壳"),Potential("敏捷"),Potential("耐力")));
            var data=BeetleSaveMapper.Export(individual);data.Health=b.hearts/b.maxHearts*100;data.Nutrition=b.nutrition;
            var restored=BeetleSaveMapper.Restore(data,new SpeciesCatalog(new[]{species.ToDefinition()}));
            if(!restored.Success)throw new InvalidOperationException(restored.ErrorMessage);
            profile.minimumCombatHealth=0;return BeetleMatchEntrant.FromIndividual(restored.Individual,profile);
        }
        public void CycleSpeed(){Speed=Speed==1?2:Speed==2?4:1;}
        public void CycleCamera(){if(controller&&controller.Simulation!=null)controller.CycleView();}
        void Update()
        {
            if(!controller||controller.Simulation==null||app.Modal||app.Busy)return;
            if(result!=null&&!IsFinished){retryTime-=Time.unscaledDeltaTime;if(retryTime<=0){retryTime=3;TrySettle();}}
            if(IsFinished){finishTime+=Time.unscaledDeltaTime;if(finishTime>3)return;}
            accumulator=Mathf.Min(.24f,accumulator+Mathf.Min(.05f,Time.unscaledDeltaTime)*Speed);
            int steps=0;
            while(accumulator>=BeetleSimulation.FixedStep && steps++<12)
            {
                controller.AdvanceRoundClock(BeetleSimulation.FixedStep);controller.AdvanceFixedStep();accumulator-=BeetleSimulation.FixedStep;
                for(int i=0;i<2;i++)
                {
                    var f=controller.Simulation.Fighters[i];int contacts=Math.Max(0,f.Contacts-previousContacts[i]);previousContacts[i]=f.Contacts;
                    // Presentation-only structural wear telemetry. It does not alter the existing combat outcome.
                    structure[i]=Mathf.Max(.1f,structure[i]-contacts*.006f);
                }
            }
            controller.Present(accumulator/BeetleSimulation.FixedStep,controller.Simulation.MotionTime,Mathf.Min(.05f,Time.unscaledDeltaTime)*Speed);
            hudTime+=Time.unscaledDeltaTime;if(hudTime<.1f)return;hudTime=0;
            var a=controller.Simulation.Fighters[0];var b=controller.Simulation.Fighters[1];
            if(playerLabel)playerLabel.text=$"角斗  {a.Traits.lengthMm:0.0} mm    {controller.Simulation.FighterName(0)}";
            if(opponentLabel)opponentLabel.text=$"角斗  {b.Traits.lengthMm:0.0} mm    {controller.Simulation.FighterName(1)}";
            if(playerBar)playerBar.fillAmount=a.Stamina01*(.7f+.3f*a.Footing);if(opponentBar)opponentBar.fillAmount=b.Stamina01*(.7f+.3f*b.Footing);
            if(playerStructure)playerStructure.fillAmount=structure[0];if(opponentStructure)opponentStructure.fillAmount=structure[1];
        }
        void OnCompleted(BeetleMatchResult completed){result=completed;TrySettle();}
        void TrySettle()
        {
            if(result==null||IsFinished||app.Busy)return;
            app.Execute(s=>{if(result.Winner<0)s.DrawBattle(result.MatchId);else s.SettleBattle(result.MatchId,result.Winner==0);},
                ()=>{IsFinished=true;ResultTitle=result.Winner<0?"平局 · 棋逢对手":result.Winner==0?"赢啦！漂亮的一战":"这一场，先歇一歇";Finished?.Invoke();});
        }
        void OnDestroy(){if(controller)controller.MatchCompleted-=OnCompleted;foreach(var item in owned)if(item)Destroy(item);}
    }
}
