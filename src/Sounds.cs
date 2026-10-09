using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace LethalMinecraft
{
    /// <summary>
    /// Sound bank. Loads OGGs from the local Minecraft install when available (async at startup);
    /// otherwise uses procedurally synthesized fallbacks.
    /// </summary>
    public static class Sounds
    {
        static readonly Dictionary<string, List<AudioClip>> bank = new Dictionary<string, List<AudioClip>>();
        static readonly Dictionary<string, string> mcSources = new Dictionary<string, string>
        {
            // id -> minecraft base path (variants 1..n appended automatically)
            ["dig.stone"] = "dig/stone", ["dig.wood"] = "dig/wood", ["dig.grass"] = "dig/grass", ["dig.gravel"] = "dig/gravel",
            ["dig.sand"] = "dig/sand", ["dig.wool"] = "dig/cloth", ["dig.glass"] = "dig/stone", ["dig.metal"] = "dig/stone",
            ["dig.slime"] = "mob/slime/big", ["dig.snow"] = "dig/snow", ["step.snow"] = "step/snow",
            ["step.stone"] = "step/stone", ["step.wood"] = "step/wood", ["step.grass"] = "step/grass", ["step.gravel"] = "step/gravel",
            ["step.sand"] = "step/sand", ["step.wool"] = "step/cloth", ["step.glass"] = "step/stone", ["step.metal"] = "step/stone",
            ["step.slime"] = "mob/slime/small",
            ["break.glass"] = "random/glass",
            ["piston.out"] = "tile/piston/out", ["piston.in"] = "tile/piston/in",
            ["fuse"] = "random/fuse", ["explode"] = "random/explode",
            ["click"] = "random/click", ["pop"] = "random/pop", ["totem"] = "item/totem/use_totem",
            ["note.harp"] = "note/harp", ["note.bass"] = "note/bass", ["note.pling"] = "note/pling", ["note.bell"] = "note/bell",
            ["eat"] = "random/eat", ["burp"] = "random/burp", ["orb"] = "random/orb", ["levelup"] = "random/levelup",
            ["ignite"] = "fire/ignite", ["fire"] = "fire/fire", ["extinguish"] = "random/fizz", ["hurt"] = "damage/hit", ["bounce"] = "mob/slime/big",
            ["swing"] = "entity/player/attack/sweep", ["attack"] = "entity/player/attack/strong",
            ["pearl.throw"] = "random/bow", ["pearl.land"] = "mob/endermen/portal",
            ["chest.open"] = "random/chestopen", ["chest.close"] = "random/chestclosed",
            ["armor.iron"] = "item/armor/equip_iron", ["armor.golden"] = "item/armor/equip_gold", ["armor.diamond"] = "item/armor/equip_diamond",
        };

        static GameObject root;
        static readonly List<AudioSource> pool = new List<AudioSource>();
        public static bool Loaded;

        public static void Init()
        {
            Synthesize();
            root = new GameObject("LMC_Sounds");
            Object.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.HideAndDontSave;
            var runner = root.AddComponent<CoroutineHost>();
            if (McAssets.Available) runner.StartCoroutine(LoadMinecraftSounds());
            else Loaded = true;
        }

        class CoroutineHost : MonoBehaviour { }

        static IEnumerator LoadMinecraftSounds()
        {
            int count = 0;
            foreach (var kv in mcSources)
            {
                var files = new List<string>(McAssets.SoundVariants(kv.Value));
                if (files.Count == 0) continue;
                var list = new List<AudioClip>();
                foreach (var f in files)
                {
                    using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + f.Replace('\\', '/'), AudioType.OGGVORBIS))
                    {
                        ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = false;
                        yield return req.SendWebRequest();
                        if (req.result != UnityWebRequest.Result.Success) continue;
                        var clip = DownloadHandlerAudioClip.GetContent(req);
                        if (clip == null) continue;
                        clip.name = kv.Key;
                        list.Add(clip);
                        count++;
                    }
                }
                if (list.Count > 0) bank[kv.Key] = list;
            }
            Loaded = true;
            Plugin.Log.LogInfo($"Loaded {count} Minecraft sound clips");
        }

        public static AudioClip Get(string id)
        {
            if (bank.TryGetValue(id, out var l) && l.Count > 0) return l[Random.Range(0, l.Count)];
            return null;
        }

        static AudioSource GetSource()
        {
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null && !pool[i].isPlaying) return pool[i];
            var go = new GameObject("LMC_Sfx");
            Object.DontDestroyOnLoad(go);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1.5f;
            src.dopplerLevel = 0f;
            pool.Add(src);
            try
            {
                // route through the game's SFX mixer if possible so volume settings apply
                var mixerGroup = SoundManager.Instance != null && SoundManager.Instance.tempAudio1 != null ? SoundManager.Instance.tempAudio1.outputAudioMixerGroup : null;
                if (mixerGroup != null) src.outputAudioMixerGroup = mixerGroup;
            }
            catch { }
            return src;
        }

        public static void Play(string id, Vector3 pos, float volume = 1f, float pitch = 1f, float maxDistance = 24f)
        {
            var clip = Get(id);
            if (clip == null) return;
            var src = GetSource();
            src.transform.position = pos;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.pitch = pitch;
            src.maxDistance = maxDistance;
            src.spatialBlend = 1f;
            src.Play();
        }

        public static void Play2D(string id, float volume = 1f, float pitch = 1f)
        {
            var clip = Get(id);
            if (clip == null) return;
            var src = GetSource();
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;
            src.spatialBlend = 0f;
            src.Play();
        }

        public static AudioSource PlayLoop(string id, Transform parent, float volume = 1f)
        {
            var clip = Get(id);
            if (clip == null) return null;
            var go = new GameObject("LMC_Loop");
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.maxDistance = 24f;
            src.volume = volume;
            src.Play();
            return src;
        }

        public static string Family(BlockDef d) => d.Sound ?? "stone";

        // ------------------------------------------------------------------ fallback synthesis
        static void Synthesize()
        {
            var rng = new System.Random(1234);
            void Add(string id, AudioClip c) { if (!bank.ContainsKey(id)) bank[id] = new List<AudioClip>(); bank[id].Add(c); }
            string[] fams = { "stone", "wood", "grass", "gravel", "sand", "wool", "glass", "metal", "slime", "snow" };
            float[] bright = { 0.55f, 0.25f, 0.8f, 0.7f, 0.9f, 0.15f, 0.9f, 0.6f, 0.2f, 0.85f };
            for (int f = 0; f < fams.Length; f++)
                for (int v = 0; v < 3; v++)
                {
                    Add("dig." + fams[f], Noise("dig", 0.22f, bright[f], 70 + v * 15 + f * 9, rng, 1f));
                    Add("step." + fams[f], Noise("step", 0.09f, bright[f], 90 + v * 20, rng, 0.6f));
                }
            Add("break.glass", Noise("glass", 0.35f, 1f, 900, rng, 1f));
            Add("piston.out", Sweep(0.18f, 80f, 220f, 0.6f, rng));
            Add("piston.in", Sweep(0.18f, 220f, 80f, 0.6f, rng));
            Add("fuse", Noise("fuse", 1.6f, 1f, 0, rng, 0.4f));
            Add("explode", Noise("boom", 1.2f, 0.2f, 40, rng, 1f));
            Add("click", Tone(0.05f, 1800f, 0.5f));
            Add("pop", Sweep(0.08f, 600f, 1400f, 0.5f, rng, true));
            Add("pearl.throw", Sweep(0.18f, 900f, 300f, 0.35f, rng, true));
            Add("chest.open", Sweep(0.22f, 260f, 520f, 0.35f, rng, true));
            Add("chest.close", Sweep(0.18f, 420f, 200f, 0.4f, rng, true));
            Add("pearl.land", Sweep(0.45f, 200f, 1200f, 0.5f, rng, false));
            Add("note.harp", Pluck(1.2f, 370f, rng));
            Add("eat", Noise("eat", 0.14f, 0.5f, 200, rng, 0.7f));
            Add("burp", Sweep(0.35f, 180f, 120f, 0.6f, rng));
            Add("orb", Sweep(0.12f, 900f, 1600f, 0.35f, rng, true));
            Add("levelup", Sweep(0.6f, 400f, 1200f, 0.45f, rng, true));
            Add("ignite", Noise("ignite", 0.25f, 1f, 1200, rng, 0.6f));
            Add("fire", Noise("ignite", 0.9f, 1f, 600, rng, 0.25f));
            Add("extinguish", Noise("ignite", 0.35f, 1f, 2400, rng, 0.5f));
            Add("swing", Noise("swing", 0.18f, 0.6f, 0, rng, 0.3f));
        }

        const int Rate = 44100;

        static AudioClip Make(string name, float[] data)
        {
            var c = AudioClip.Create("LMC_" + name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip Noise(string name, float len, float brightness, float thumpHz, System.Random rng, float vol)
        {
            int n = (int)(len * Rate);
            var d = new float[n];
            float lp = 0, lp2 = 0;
            float a = Mathf.Lerp(0.05f, 0.9f, brightness);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * (name == "fuse" ? 0.5f : name == "boom" ? 3.2f : 18f));
                if (name == "fuse") env *= 0.5f + 0.5f * Mathf.PerlinNoise(t * 30f, 0.3f);
                float w = (float)(rng.NextDouble() * 2 - 1);
                lp += (w - lp) * a;
                lp2 += (lp - lp2) * a;
                float s = lp2 * 1.6f;
                if (thumpHz > 0) s += Mathf.Sin(2 * Mathf.PI * thumpHz * t) * Mathf.Exp(-t * 30f) * 0.7f;
                if (name == "swing") env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / len));
                d[i] = Mathf.Clamp(s * env * vol, -1, 1);
            }
            return Make(name, d);
        }

        static AudioClip Sweep(float len, float f0, float f1, float vol, System.Random rng, bool sine = false)
        {
            int n = (int)(len * Rate);
            var d = new float[n];
            float phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2 * Mathf.PI * f / Rate;
                float s = sine ? Mathf.Sin(phase) : (Mathf.Sin(phase) * 0.6f + (float)(rng.NextDouble() * 2 - 1) * 0.4f);
                d[i] = s * vol * Mathf.Sin(Mathf.PI * t);
            }
            return Make("sweep", d);
        }

        static AudioClip Tone(float len, float f, float vol)
        {
            int n = (int)(len * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = Mathf.Sin(2 * Mathf.PI * f * i / Rate) * vol * (1 - i / (float)n);
            return Make("tone", d);
        }

        static AudioClip Pluck(float len, float f, System.Random rng)
        {
            // Karplus-Strong
            int n = (int)(len * Rate);
            int p = Mathf.Max(2, (int)(Rate / f));
            var buf = new float[p];
            for (int i = 0; i < p; i++) buf[i] = (float)(rng.NextDouble() * 2 - 1);
            var d = new float[n];
            int idx = 0;
            for (int i = 0; i < n; i++)
            {
                int nx = (idx + 1) % p;
                float v = 0.497f * (buf[idx] + buf[nx]);
                d[i] = buf[idx] * 0.6f;
                buf[idx] = v;
                idx = nx;
            }
            return Make("pluck", d);
        }
    }
}
