using System;
using UnityEngine;

namespace BeetleArena.Campaign
{
    // Small synthesized placeholders, allocated once per application. Replace clips without changing page code.
    public sealed class CampaignAudio : MonoBehaviour
    {
        AudioSource music,nature;
        AudioClip musicClip,natureClip;
        void Start()
        {
            music=gameObject.AddComponent<AudioSource>();nature=gameObject.AddComponent<AudioSource>();
            const int rate=22050,seconds=16;var melody=new float[rate*seconds];var forest=new float[rate*seconds];var random=new System.Random(7051);
            int[] notes={60,64,67,69,67,64,62,55};float noise=0;
            for(int i=0;i<melody.Length;i++)
            {
                double t=(double)i/rate;int note=(int)(t/2)%notes.Length;double local=t%2,frequency=440*Math.Pow(2,(notes[note]-69)/12.0);
                double envelope=Math.Min(1,local*12)*Math.Exp(-local*2.7)*Math.Min(1,(2-local)*15);
                melody[i]=(float)((Math.Sin(2*Math.PI*frequency*t)+.25*Math.Sin(2*Math.PI*frequency*2*t))*.08*envelope);
                noise+=(float)((random.NextDouble()*2-1-noise)*.015);float fade=Mathf.Min(1,Mathf.Min(i,melody.Length-1-i)/(float)rate);
                forest[i]=noise*.14f*fade;
            }
            musicClip=AudioClip.Create("Temporary greenhouse melody",melody.Length,1,rate,false);musicClip.SetData(melody,0);
            natureClip=AudioClip.Create("Temporary canopy air",forest.Length,1,rate,false);natureClip.SetData(forest,0);
            music.clip=musicClip;nature.clip=natureClip;music.loop=nature.loop=true;music.Play();nature.Play();
        }
        void Update(){var p=CampaignApp.Instance?.Preferences;if(p==null)return;if(music)music.volume=p.music;if(nature)nature.volume=p.nature;}
        void OnDestroy(){if(musicClip)Destroy(musicClip);if(natureClip)Destroy(natureClip);}
    }
}
