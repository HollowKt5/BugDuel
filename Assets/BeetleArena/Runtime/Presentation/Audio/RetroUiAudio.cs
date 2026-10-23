using UnityEngine;

namespace BeetleArena
{
    /// <summary>Shared, synthesized mechanical mouse click. Independent of campaign state and combat audio.</summary>
    public sealed class RetroUiAudio : MonoBehaviour
    {
        static RetroUiAudio instance;
        readonly AudioSource[] voices = new AudioSource[4];
        [SerializeField] RetroUiAudioProfile profile;
        MouseClickSettings appliedSettings;
        AudioClip click;
        int nextVoice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public static void Ensure()
        {
            if (!Application.isPlaying || instance) return;
            new GameObject("Retro UI Audio").AddComponent<RetroUiAudio>();
        }

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (instance && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            // Reuse the owned sources after an editor domain reload instead of leaving a second pool.
            var existing = GetComponents<AudioSource>();
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = i < existing.Length ? existing[i] : gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false; voice.loop = false;
                voice.spatialBlend = 0; voice.dopplerLevel = 0;
                voice.ignoreListenerPause = true; voice.priority = 32;
                voice.volume = 1;
                voices[i] = voice;
            }
            click = voices[0].clip;
            RefreshClip();
        }

        public static void PlayClick()
        {
            Ensure();
            if (!instance || !instance.isActiveAndEnabled) return;
            instance.RefreshClip();
            // Bounded voices allow quick clicks without unbounded PlayOneShot overlap.
            var voice = instance.voices[instance.nextVoice++ % instance.voices.Length];
            voice.Stop(); voice.Play();
            instance.nextVoice %= instance.voices.Length;
        }

        public static void Bind(UnityEngine.UI.Button button)
        {
            if (!button) return;
            button.onClick.RemoveListener(PlayClick);
            button.onClick.AddListener(PlayClick);
        }

        public static void BindHierarchy(Transform root)
        {
            Ensure();
            foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true)) Bind(button);
        }

        // No new samples during ordinary clicks. Inspector edits take effect on the next click.
        void RefreshClip()
        {
            if (!profile) profile = Resources.Load<RetroUiAudioProfile>(RetroUiAudioProfile.ResourceName);
            var settings = profile ? profile.Settings : MouseClickSettings.Default;
            if (click && settings.Equals(appliedSettings)) return;
            var previous = click;
            click = MouseClickSynthesizer.CreateClip(settings);
            appliedSettings = settings;
            foreach (var voice in voices)
            {
                if (!voice) continue;
                voice.Stop(); voice.clip = click;
            }
            if (previous) Destroy(previous);
        }

        void OnDisable() { foreach (var voice in voices) if (voice) voice.Stop(); }
        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (click) Destroy(click);
        }
    }
}
