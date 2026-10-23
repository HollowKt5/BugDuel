using System;
using UnityEngine;

namespace BeetleArena.Campaign
{
    // Owned by the persistent app, so a page refresh never replays a transaction sound.
    public sealed class CampaignCurrencyAudio : MonoBehaviour
    {
        readonly AudioSource[] voices=new AudioSource[4];
        CampaignApp owner;
        AudioClip coin;
        bool subscribed;
        int nextVoice;
        public int ReceivedCount { get; private set; }
        public int PlayedCount { get; private set; }
        public long LastDelta { get; private set; }
        public long LastSequence { get; private set; }
        public float LastPitch { get; private set; }
        public AudioClip CoinClip => coin;

        public static CampaignCurrencyAudio Ensure(CampaignApp app)
        {
            var audio=app.GetComponent<CampaignCurrencyAudio>()??app.gameObject.AddComponent<CampaignCurrencyAudio>();
            audio.Bind(app);return audio;
        }
        void Bind(CampaignApp app)
        {
            if(owner==app&&subscribed)return;
            Unsubscribe();owner=app;
            if(!coin)
            {
                coin=CreateCoinClip();
                for(int i=0;i<voices.Length;i++)
                {
                    var voice=gameObject.AddComponent<AudioSource>();voices[i]=voice;
                    voice.playOnAwake=false;voice.loop=false;voice.spatialBlend=0;voice.dopplerLevel=0;
                    voice.priority=40;voice.volume=.32f;voice.clip=coin;
                }
            }
            Subscribe();
        }
        void Subscribe()
        {
            if(!owner||subscribed)return;
            owner.CurrencyChanged+=OnCurrency;owner.CurrencyReset+=OnReset;subscribed=true;
        }
        void Unsubscribe()
        {
            if(owner&&subscribed){owner.CurrencyChanged-=OnCurrency;owner.CurrencyReset-=OnReset;}
            subscribed=false;
        }
        void OnCurrency(CampaignCurrencyChange change)
        {
            ReceivedCount++;LastDelta=change.Delta;LastSequence=change.Sequence;
            LastPitch=(change.Delta>0?1.2f:.78f)+(change.Sequence%3-1)*.025f;
            if(owner.Preferences.master<=0||AudioListener.volume<=0||AudioListener.pause)return;
            var voice=voices[nextVoice++%voices.Length];voice.Stop();voice.pitch=LastPitch;voice.Play();PlayedCount++;
        }
        void OnReset(long balance)
        {
            LastDelta=0;
            foreach(var voice in voices)if(voice)voice.Stop();
        }
        static AudioClip CreateCoinClip()
        {
            const int rate=44100;var samples=new float[(int)(rate*.26)];var random=new System.Random(1977);
            for(int i=0;i<samples.Length;i++)
            {
                double time=(double)i/rate,value=0;
                for(int strike=0;strike<2;strike++)
                {
                    double t=time-strike*.045;if(t<0)continue;
                    double attack=Math.Min(1,t*1600),ring=Math.Exp(-t*25);
                    double modes=Math.Sin(t*2*Math.PI*1190)+.48*Math.Sin(t*2*Math.PI*1837)+.22*Math.Sin(t*2*Math.PI*2910);
                    double tap=(random.NextDouble()*2-1)*Math.Exp(-t*400)*.22;
                    value+=(modes*ring*.22+tap)*attack*(strike==0?1:.55);
                }
                samples[i]=(float)(value*Math.Min(1,(samples.Length-1-i)/(rate*.015)));
            }
            var clip=AudioClip.Create("Campaign coin feedback",samples.Length,1,rate,false);clip.SetData(samples,0);return clip;
        }
        void OnEnable()=>Subscribe();
        void OnDisable(){Unsubscribe();foreach(var voice in voices)if(voice)voice.Stop();}
        void OnDestroy(){Unsubscribe();if(coin)Destroy(coin);}
    }
}
