using System;
using System.Collections.Generic;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    // Compare committed mail identities, not unread counts: reading, restoring and loading
    // mail are silent, and a failed save never sounds like a completed delivery/deletion.
    public sealed class CampaignMailAudio : MonoBehaviour
    {
        readonly Dictionary<string,bool> previousMail=new Dictionary<string,bool>();
        CampaignApp owner;
        AudioSource notification,disposal;
        public int NotificationCount { get; private set; }
        public int DeletionCount { get; private set; }
        public int NotificationPlayedCount { get; private set; }
        public int DeletionPlayedCount { get; private set; }
        public AudioClip NotificationClip=>notification.clip;
        public AudioClip DeletionClip=>disposal.clip;

        void Awake()
        {
            owner=GetComponent<CampaignApp>();
            notification=CreateVoice(CreateNotification(),.4f);
            disposal=CreateVoice(CreateDisposal(),.5f);
        }
        AudioSource CreateVoice(AudioClip clip,float volume)
        {
            var voice=gameObject.AddComponent<AudioSource>();voice.playOnAwake=false;voice.loop=false;
            voice.spatialBlend=0;voice.dopplerLevel=0;voice.priority=35;voice.ignoreListenerPause=true;
            voice.volume=volume;voice.clip=clip;return voice;
        }
        void OnEnable()=>owner.SessionCommitted+=OnCommitted;
        void OnDisable()
        {
            owner.SessionCommitted-=OnCommitted;notification.Stop();disposal.Stop();
        }
        void OnCommitted(CampaignState before,CampaignState after,bool reset)
        {
            if(reset||before==null)return;
            previousMail.Clear();
            foreach(var mail in before.mail)previousMail.Add(mail.id,mail.deleted);
            bool arrived=false,deleted=false;
            foreach(var mail in after.mail)
            {
                if(previousMail.TryGetValue(mail.id,out bool wasDeleted))
                { deleted|=mail.deleted&&!wasDeleted;previousMail.Remove(mail.id); }
                else arrived|=!mail.deleted;
            }
            deleted|=previousMail.Count>0;
            if(arrived)
            {
                NotificationCount++;
                if(owner.Preferences.master>0&&AudioListener.volume>0)
                { notification.Stop();notification.Play();NotificationPlayedCount++; }
            }
            if(deleted)
            {
                DeletionCount++;
                if(owner.Preferences.master>0&&AudioListener.volume>0)
                { disposal.Stop();disposal.Play();DeletionPlayedCount++; }
            }
        }
        const int Rate=44100;
        static AudioClip Clip(string name,float[] samples)
        {
            var clip=AudioClip.Create(name,samples.Length,1,Rate,false);clip.hideFlags=HideFlags.DontSave;
            clip.SetData(samples,0);return clip;
        }
        static AudioClip CreateNotification()
        {
            var samples=new float[(int)(Rate*.58)];
            for(int i=0;i<samples.Length;i++)
            {
                double time=(double)i/Rate,value=0;
                for(int note=0;note<2;note++)
                {
                    double t=time-note*.13;if(t<0)continue;
                    double frequency=note==0?784:1046.5;
                    double envelope=(1-Math.Exp(-t*450))*Math.Exp(-t*12);
                    value+=(Math.Sin(t*2*Math.PI*frequency)+.18*Math.Sin(t*2*Math.PI*frequency*2.01))*envelope*.35;
                }
                samples[i]=(float)(value*Math.Min(1,(samples.Length-1-i)/(Rate*.025)));
            }
            return Clip("New mail - two soft chimes",samples);
        }
        static AudioClip CreateDisposal()
        {
            var samples=new float[(int)(Rate*.48)];var random=new System.Random(6203);double low=0;
            for(int i=0;i<samples.Length;i++)
            {
                double time=(double)i/Rate,noise=random.NextDouble()*2-1;low+=(noise-low)*.14;
                // A short paper swish, then a hollow bin impact and small rebound.
                double swish=time<.2?Math.Sin(Math.PI*time/.2):0;
                double value=(noise-low)*swish*(.5+.5*Math.Sin(time*117))*.16;
                for(int hit=0;hit<2;hit++)
                {
                    double t=time-.16-hit*.065;if(t<0)continue;
                    double envelope=(1-Math.Exp(-t*1400))*Math.Exp(-t*24);
                    value+=(Math.Sin(2*Math.PI*t*178)+.32*Math.Sin(2*Math.PI*t*587)+low*.6)*envelope*(hit==0?.4:.14);
                }
                samples[i]=(float)(value*Math.Min(1,(samples.Length-1-i)/(Rate*.025)));
            }
            return Clip("Mail disposal - paper into bin",samples);
        }
        void OnDestroy()
        {
            if(notification&&notification.clip)Destroy(notification.clip);
            if(disposal&&disposal.clip)Destroy(disposal.clip);
        }
    }
}
