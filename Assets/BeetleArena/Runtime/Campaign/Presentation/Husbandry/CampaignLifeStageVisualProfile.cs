using System;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    [Serializable]
    public sealed class CampaignStageVisual
    {
        public CampaignLifeStage stage;
        [Tooltip("Optional visual-only replacement. Pivot at substrate level, +Y up, +Z forward. No gameplay scripts or colliders required.")]
        public GameObject specimenPrefab;
        [Tooltip("Optional replacement enclosure. Pivot at substrate level; the front (-Z) should remain open for observation.")]
        public GameObject environmentPrefab;
        [Min(.1f)] public float bodyLength = 3;
        [Range(.2f, 1)] public float startScale = .78f;
        public Vector3 bodyOffset;
        public Vector3 bodyEuler;
        public Color bodyColor = new Color(.91f, .84f, .64f);
        public Vector3 enclosureSize = new Vector3(6, 2.5f, 5);
        public Vector3 environmentOffset;
    }

    /// <summary>Art direction only. Lifetimes and care requirements belong to CampaignLifeRules.</summary>
    [CreateAssetMenu(menuName = "Beetle Arena/Campaign Life Stage Visuals", fileName = "LifeStageVisuals")]
    public sealed class CampaignLifeStageVisualProfile : ScriptableObject
    {
        public const string ResourceName = "LifeStageVisuals";
        [Header("Enclosures")]
        public Color soilColor = new Color(.28f, .18f, .11f);
        public Color humusColor = new Color(.39f, .27f, .16f);
        public Color woodColor = new Color(.68f, .50f, .29f);
        public Color myceliumColor = new Color(.80f, .76f, .61f);
        public Color ceramicColor = new Color(.30f, .45f, .46f);
        public Color rimColor = new Color(.64f, .78f, .75f);
        [Range(12, 80)] public int substratePieces = 40;
        [Range(4, 20)] public int eggCount = 9;
        [Range(9, 16)] public int larvaSegments = 12;
        [Range(0, .08f)] public float larvaBreathing = .016f;
        [Header("Stage models, positions and enclosure dimensions")]
        public CampaignStageVisual[] stages = Defaults();
        [NonSerialized] public int revision;

        public CampaignStageVisual For(CampaignLifeStage stage)
        {
            foreach (var item in stages) if (item.stage == stage) return item;
            throw new InvalidOperationException("Missing life-stage visual configuration: " + stage);
        }

        void OnValidate() => revision++;

        static CampaignStageVisual[] Defaults() => new[]
        {
            new CampaignStageVisual {stage=CampaignLifeStage.Egg,bodyLength=1.8f,startScale=.65f,
                bodyColor=new Color(.94f,.91f,.76f),enclosureSize=new Vector3(5,.85f,4)},
            new CampaignStageVisual {stage=CampaignLifeStage.Larva1,bodyLength=1.35f,startScale=.72f,
                bodyColor=new Color(.96f,.92f,.76f),enclosureSize=new Vector3(4.4f,2.5f,4.4f)},
            new CampaignStageVisual {stage=CampaignLifeStage.Larva2,bodyLength=2.1f,startScale=.75f,
                bodyColor=new Color(.93f,.88f,.69f),enclosureSize=new Vector3(4.9f,2.8f,4.9f)},
            new CampaignStageVisual {stage=CampaignLifeStage.Larva3,bodyLength=3.2f,startScale=.80f,
                bodyColor=new Color(.91f,.84f,.63f),enclosureSize=new Vector3(5.7f,3.1f,5.7f)},
            new CampaignStageVisual {stage=CampaignLifeStage.Prepupa,bodyLength=2.9f,startScale=.94f,
                bodyColor=new Color(.84f,.76f,.52f),enclosureSize=new Vector3(5.7f,1.75f,5.8f)},
            new CampaignStageVisual {stage=CampaignLifeStage.Pupa,bodyLength=3.4f,startScale=.96f,
                bodyColor=new Color(.77f,.47f,.15f),enclosureSize=new Vector3(5.7f,1.75f,5.8f)},
            new CampaignStageVisual {stage=CampaignLifeStage.DormantAdult,bodyLength=1,startScale=1,
                enclosureSize=new Vector3(7,2.1f,7)},
            new CampaignStageVisual {stage=CampaignLifeStage.ActiveAdult,bodyLength=1,startScale=1}
        };
    }
}
