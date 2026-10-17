using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's parrot (#97): out on the moons by day ([Mobs] ParrotRarity), tamed with wheat seeds (one try in
    /// three). [E] on your own tamed parrot puts it on your shoulder; jumping sends it off. Now and then a parrot imitates
    /// a monster that's near (pitched up, as Minecraft's do): a warning worth listening to. With the host's
    /// [Mobs] ParrotsRepeatVoices on (the default), when no monster is near it may instead repeat a snippet of what a player
    /// said over voice chat, unless that player has [Privacy] ParrotsMayCopyMyVoice off.
    /// </summary>
    [HarmonyPatch]
    public class ParrotAI : PetAI
    {
        protected override bool StandsWhenHurt => true; // (Minecraft's Parrot.hurtServer)
        public const string Name = "Parrot";
        public static EnemyType Type;
        protected override string Sound => "parrot";
        protected override bool Passive => true;
        protected override string[] Food => new[] { "wheat_seeds", "pumpkin_seeds" };
        protected override float RoamSpeed => 1.5f;
        protected override float PanicSpeed => 5f;
        protected override float FollowSpeed => 5.5f;
        protected override void ServerDrops(Vector3 at) // (Minecraft's: 1-2 feathers)
        {
            if (ModItems.ByKey.TryGetValue("feather", out var f)) ModItems.ServerSpawnStack(f, Random.Range(1, 3), at);
        }
        /// <summary>On its owner's shoulder (a parrot's "sitting").</summary>
        public bool Perched => Tamed && Sitting;
        float nextMimic;
        AudioSource voice;
        public static int Mimics; // (dev/tests)
        public static string LastMimic = "-";

        public override void Start()
        {
            base.Start();
            nextMimic = Time.time + Random.Range(8f, 20f);
            var go = new GameObject("ParrotVoice"); go.transform.SetParent(transform, false); go.transform.localPosition = Vector3.up * 0.8f;
            voice = go.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.spatialBlend = 1f; voice.rolloffMode = AudioRolloffMode.Linear; voice.maxDistance = 30f; voice.pitch = 1.15f;
        }

        protected override bool BusyAI()
        {
            if (Perched) { if (agent.enabled && agent.isOnNavMesh && agent.hasPath) agent.ResetPath(); return true; }
            return base.BusyAI();
        }

        public bool Dancing { get; private set; }
        float danceCheck;

        public override void Update()
        {
            base.Update();
            // (#151: Minecraft's parrots dance near a playing jukebox: they bob and sway)
            if ((danceCheck -= Time.deltaTime) <= 0f) { danceCheck = 0.5f; Dancing = !isEnemyDead && Jukebox.PlayingNear(transform.position, 3.5f * BlockWorld.S); }
            var dm = transform.Find("Model");
            if (dm != null) dm.localRotation = Dancing ? Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 9f) * 18f) * Quaternion.Euler(Mathf.Abs(Mathf.Sin(Time.time * 9f)) * -12f, 0f, 0f) : Quaternion.identity;
            if (agent != null && IsOwner)
            {
                // (on a shoulder: no walking; off it, back on the ground beside its owner)
                bool want = !Perched;
                if (agent.enabled != want)
                {
                    agent.enabled = want;
                    if (want && IsOwner && NavMesh.SamplePosition(transform.position, out var h, 4f, NavMesh.AllAreas)) agent.Warp(h.position);
                }
            }
            if (IsServer && !isEnemyDead && Time.time >= nextMimic) { nextMimic = Time.time + Random.Range(15f, 40f); ServerMimic(); }
        }

        /// <summary>Every machine, after the game moved things: a perched parrot rides on its owner's right shoulder.</summary>
        void LateUpdate() => OnShoulder();

        /// <summary>On its owner's shoulder, wherever they are (also while riding the ship with its AI paused, #210).</summary>
        public void OnShoulder()
        {
            if (!Perched || isEnemyDead) return;
            var o = OwnerPlayer;
            if (o == null || o.isPlayerDead) return;
            transform.position = o.transform.TransformPoint(new Vector3(0.42f, 1.95f, -0.05f));
            transform.rotation = Quaternion.Euler(0f, o.transform.eulerAngles.y, 0f);
        }

        // ------------------------------------------------------------------ imitating (host decides, everyone hears)
        void ServerMimic()
        {
            // a monster within 25 m: that (Minecraft's parrots imitate nearby hostile mobs)
            var near = RoundManager.Instance.SpawnedEnemies.Where(e => e != null && !e.isEnemyDead && !(e is PetAI) && !(e is MinecraftMobAI m && m.IsPassive) && e.enemyType != null
                && Vector3.Distance(e.transform.position, transform.position) < 25f).OrderBy(e => Vector3.Distance(e.transform.position, transform.position)).FirstOrDefault();
            if (near != null) { BlockNet.ServerParrotMimic(this, 0, near.NetworkObjectId); return; }
            if (ParrotVoices.HostAllows && Random.value < 0.5f) { BlockNet.ServerParrotMimic(this, 1, 0); return; }
            if (Random.value < 0.35f) BlockNet.ServerParrotMimic(this, 2, 0);
        }

        /// <summary>Every machine: kind 0 a monster (its id), 1 a player's voice, 2 its own chirp.</summary>
        public void Mimic(byte kind, ulong id)
        {
            Mimics++;
            var at = transform.position + Vector3.up * 0.8f;
            if (kind == 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(id, out var no) && no.GetComponent<EnemyAI>() is EnemyAI e)
            {
                LastMimic = "monster " + e.enemyType.enemyName;
                if (e is MinecraftMobAI mm) { Sounds.Play(mm.SoundKey + ".idle", at, 0.8f, 1.25f, 24f); return; }
                var clip = ClipOf(e);
                if (clip != null) { voice.pitch = 1.25f; voice.PlayOneShot(clip, 0.8f); return; }
            }
            if (kind == 1)
            {
                var snip = ParrotVoices.Snippet();
                if (snip != null) { LastMimic = "voice " + snip.name; voice.pitch = 1.1f; voice.PlayOneShot(snip, 1f); return; }
            }
            LastMimic = "chirp";
            Sounds.Play("parrot.idle", at, 0.7f, Random.Range(0.9f, 1.15f), 20f);
        }

        /// <summary>One of a Lethal Company monster's own sounds.</summary>
        static AudioClip ClipOf(EnemyAI e)
        {
            var list = new List<AudioClip>();
            if (e.enemyType.audioClips != null) list.AddRange(e.enemyType.audioClips.Where(c => c != null && c.length < 4f));
            if (e.creatureVoice != null && e.creatureVoice.clip != null) list.Add(e.creatureVoice.clip);
            if (e.creatureSFX != null && e.creatureSFX.clip != null) list.Add(e.creatureSFX.clip);
            if (list.Count == 0 && e.enemyType.hitBodySFX != null) list.Add(e.enemyType.hitBodySFX);
            return list.Count > 0 ? list[Random.Range(0, list.Count)] : null;
        }

        // ------------------------------------------------------------------ off the shoulder: a jump
        [HarmonyPatch(typeof(PlayerControllerB), "Jump_performed"), HarmonyPostfix]
        static void JumpOff(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || RoundManager.Instance == null) return;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
                if (e is ParrotAI par && par.Perched && par.Owner == p.actualClientId) BlockNet.RequestPet(par);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (Plugin.ParrotRarity.Value <= 0) return;
            Type = Register<ParrotAI>("Parrot", Name, "Tame it with seeds. It imitates monsters nearby", Plugin.ParrotRarity.Value, 1, 0.9f, 3, 1.5f, BuildModel, daytime: true, length: 0.6f);
        }

        // ------------------------------------------------------------------ the model: Minecraft's parrot (32x32)
        static readonly string[] Feathers = { "red_blue", "blue", "green", "yellow_blue", "grey" };

        public override void Receive(bool tamed, ulong owner, bool sitting) => base.Receive(tamed, owner, sitting);

        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S * 0.8f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Parrot", "entity/parrot/parrot_red_blue", new Color(0.85f, 0.15f, 0.1f), doubleSided: true);
            var m = sharedMat;
            Transform Pivot(Transform p, string n, Vector3 at, float xRot)
            {
                var t = new GameObject(n).transform; t.SetParent(p, false); t.localPosition = at * k; t.localRotation = Quaternion.Euler(xRot, 0f, 0f); return t;
            }
            var body = Pivot(parent, "BodyP", new Vector3(0, 7.5f, 3), 28.3f);
            EntityModels.Part(body, "Body", new Vector3(0, -3, 0), new Vector3(3, 6, 3), 2, 8, k, m);
            var tail = Pivot(parent, "TailP", new Vector3(0, 2.93f, -1.16f), 58f);
            EntityModels.Part(tail, "Tail", new Vector3(0, -1, 0.5f), new Vector3(3, 4, 1), 22, 1, k, m);
            foreach (var (n, x) in new[] { ("WingL", 1.5f), ("WingR", -1.5f) })
            {
                var w = Pivot(parent, n, new Vector3(x, 7.06f, 2.76f), 40f);
                EntityModels.Part(w, "Wing", new Vector3(0, -2.5f, 0), new Vector3(1, 5, 3), 19, 8, k, m);
            }
            var head = Pivot(parent, "HeadP", new Vector3(0, 8.31f, 2.76f), 0f);
            EntityModels.Part(head, "Head", Vector3.zero, new Vector3(2, 3, 2), 2, 2, k, m);
            EntityModels.Part(head, "Head2", new Vector3(0, 2, 1), new Vector3(2, 1, 4), 10, 0, k, m);
            EntityModels.Part(head, "Beak1", new Vector3(0, 0.5f, 1.5f), new Vector3(1, 2, 1), 11, 7, k, m);
            EntityModels.Part(head, "Beak2", new Vector3(0, 0.75f, 2.45f), new Vector3(1, 2, 1), 16, 7, k, m);
            var fe = Pivot(head, "FeatherP", new Vector3(0, 2.15f, -0.15f), -12.7f);
            EntityModels.Part(fe, "Feather", new Vector3(0, 1.5f, 0), new Vector3(0, 5, 4), 2, 18, k, m);
            foreach (var (n, x) in new[] { ("LegL", 1f), ("LegR", -1f) })
            {
                var l = Pivot(parent, n, new Vector3(x, 2f, 1.05f), 0f);
                EntityModels.Part(l, "Leg", new Vector3(0, -1, 0), new Vector3(1, 2, 1), 14, 18, k, m);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyLook();
        }

        protected override void ApplyLook()
        {
            // (each its own feathers, the same on every machine)
            var f = Feathers[(int)(LookId % (ulong)Feathers.Length)];
            if (!mats.TryGetValue(f, out var mat)) mats[f] = mat = EntityModels.Material("LMC_Parrot_" + f, "entity/parrot/parrot_" + f, new Color(0.85f, 0.15f, 0.1f), doubleSided: true);
            var model = transform.Find("Model");
            if (model != null) foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = mat;
        }
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    }

    /// <summary>
    /// (#97) What players said over voice chat, for parrots to repeat: only with the host's [Mobs] ParrotsRepeatVoices on, and
    /// never for a player who has [Privacy] ParrotsMayCopyMyVoice off (everyone is told). Each machine keeps the last
    /// half minute of speech of the others as it heard them (memory only, nothing saved); your own voice it never has.
    /// </summary>
    [HarmonyPatch]
    public static class ParrotVoices
    {
        public static bool HostAllows; // (the host's ParrotVoices, with the rules)
        static readonly HashSet<ulong> optedOut = new HashSet<ulong>();
        static readonly Dictionary<ulong, VoiceTap> taps = new Dictionary<ulong, VoiceTap>();
        static float nextLook;

        public static bool Allowed(ulong client) => HostAllows && !optedOut.Contains(client);
        public static void ReceiveOptOuts(IEnumerable<ulong> ids) { optedOut.Clear(); foreach (var i in ids) optedOut.Add(i); }
        public static IEnumerable<ulong> OptOuts => optedOut;
        public static void Reset() { optedOut.Clear(); }

        /// <summary>Every machine: taps on the other players' voice playback (while it's allowed).</summary>
        public static void Tick()
        {
            if (Time.time < nextLook || StartOfRound.Instance == null) return;
            nextLook = Time.time + 1f;
            var me = GameNetworkManager.Instance?.localPlayerController;
            foreach (var p in StartOfRound.Instance.allPlayerScripts)
            {
                if (p == null || p == me || !p.isPlayerControlled) continue;
                var src = p.currentVoiceChatAudioSource;
                bool want = Allowed(p.actualClientId) && src != null;
                taps.TryGetValue(p.actualClientId, out var tap);
                if (want && (tap == null || tap.gameObject != src.gameObject))
                {
                    if (tap != null) Object.Destroy(tap);
                    tap = src.gameObject.AddComponent<VoiceTap>(); tap.Who = p.playerUsername; taps[p.actualClientId] = tap;
                }
                else if (!want && tap != null) { Object.Destroy(tap); taps.Remove(p.actualClientId); }
            }
        }

        /// <summary>A second or two of someone's speech, or null.</summary>
        public static AudioClip Snippet()
        {
            var have = taps.Where(kv => kv.Value != null && Allowed(kv.Key) && kv.Value.HasSpeech).Select(kv => kv.Value).ToList();
            if (have.Count == 0) return null;
            return have[Random.Range(0, have.Count)].Cut();
        }

        /// <summary>(dev) Two seconds of a warbling tone into every tap, as if someone had spoken.</summary>
        public static int DevFeed()
        {
            int n = 0, rate = AudioSettings.outputSampleRate;
            foreach (var t in taps.Values)
            {
                if (t == null) continue;
                for (int b = 0; b < 2 * rate / 1024; b++)
                {
                    var buf = new float[1024 * 2];
                    for (int i = 0; i < 1024; i++) { float tt = (b * 1024 + i) / (float)rate; buf[2 * i] = buf[2 * i + 1] = 0.2f * Mathf.Sin(2f * Mathf.PI * (300f + 80f * Mathf.Sin(tt * 6f)) * tt); }
                    t.Feed(buf, 2);
                }
                n++;
            }
            return n;
        }

        public static string Describe() => $"hostAllows={HostAllows} optedOut=[{string.Join(",", optedOut)}] taps={string.Join(",", taps.Where(t => t.Value != null).Select(t => t.Value.Who + ":" + t.Value.Seconds.ToString("F1") + "s/" + t.Value.Frames + "f/peak" + t.Value.Peak.ToString("F3")))}";
    }

    /// <summary>On a remote player's voice playback: keeps their recent speech (quiet stretches left out).</summary>
    public class VoiceTap : MonoBehaviour
    {
        public string Who;
        const int Keep = 30; // seconds
        float[] ring; int head, filled, channels = 1, rate = 48000;
        readonly object gate = new object();
        public float Seconds { get { lock (gate) return ring == null ? 0f : filled / (float)(rate * channels); } }
        public bool HasSpeech => Seconds > 1.2f;

        void Awake() { rate = AudioSettings.outputSampleRate; }

        public int Frames; // (dev: buffers seen, speech or not)
        public float Peak;

        void OnAudioFilterRead(float[] data, int ch) => Feed(data, ch);

        /// <summary>A buffer of what was heard (the audio thread; dev: a made-up one).</summary>
        public void Feed(float[] data, int ch)
        {
            // (speech only, a frame at a time)
            Frames++;
            float sum = 0f;
            for (int i = 0; i < data.Length; i++) sum += data[i] * data[i];
            float rms = Mathf.Sqrt(sum / Mathf.Max(1, data.Length));
            if (rms > Peak) Peak = rms;
            if (rms < 0.008f) return;
            lock (gate)
            {
                if (ring == null || channels != ch) { channels = ch; ring = new float[Keep * rate * ch]; head = 0; filled = 0; }
                for (int i = 0; i < data.Length; i++) { ring[head] = data[i]; head = (head + 1) % ring.Length; }
                filled = Mathf.Min(ring.Length, filled + data.Length);
            }
        }

        /// <summary>A random 1-2.5 s stretch of what's kept, as a clip.</summary>
        public AudioClip Cut()
        {
            lock (gate)
            {
                if (ring == null || filled < rate * channels) return null;
                int len = Mathf.Min(filled, (int)(Random.Range(1f, 2.5f) * rate)) / channels * channels;
                int start = (head - filled + ring.Length + Random.Range(0, Mathf.Max(1, filled - len)) / channels * channels) % ring.Length;
                var buf = new float[len];
                for (int i = 0; i < len; i++) buf[i] = ring[(start + i) % ring.Length];
                // (fade in and out: no clicks)
                int f = Mathf.Min(len / 8, rate / 50 * channels);
                for (int i = 0; i < f; i++) { float g = i / (float)f; buf[i] *= g; buf[len - 1 - i] *= g; }
                var clip = AudioClip.Create(Who, len / channels, channels, rate, false);
                clip.SetData(buf, 0);
                return clip;
            }
        }
    }
}
