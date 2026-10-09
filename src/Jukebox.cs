using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace LethalMinecraft
{
    /// <summary>A music disc in hand (found as scrap inside facilities, #31). [E] on a jukebox puts it in.</summary>
    public class DiscItem : GrabbableObject
    {
        public string ItemKey;
        // (its scrap value rides on its key through chests and jukeboxes, like a tool's wear)
        public override int GetItemDataToSave() => scrapValue;
        public override void LoadItemSaveData(int saveData) { if (saveData > 0) SetScrapValue(saveData); }
        void Awake() => SpawnFix.Clear(gameObject);
    }

    /// <summary>
    /// Jukeboxes and music discs (#31), Minecraft's: a disc in a jukebox plays its track where the jukebox stands, heard by
    /// everyone nearby (and by monsters, like the boombox). [E] takes it back out; a broken jukebox drops it. The tracks
    /// come from the player's own Minecraft (records/*.ogg); a disc whose track isn't found plays nothing. The host keeps
    /// what's in each jukebox and when it started, so a player joining mid-song hears it from about the right point.
    /// </summary>
    public static class Jukebox
    {
        public class Disc { public string Track, Artist; public string Key => "music_disc_" + Track; }

        public static readonly List<Disc> Discs = new List<Disc>
        {
            new Disc { Track = "13", Artist = "C418" }, new Disc { Track = "cat", Artist = "C418" }, new Disc { Track = "blocks", Artist = "C418" },
            new Disc { Track = "chirp", Artist = "C418" }, new Disc { Track = "far", Artist = "C418" }, new Disc { Track = "mall", Artist = "C418" },
            new Disc { Track = "mellohi", Artist = "C418" }, new Disc { Track = "stal", Artist = "C418" }, new Disc { Track = "strad", Artist = "C418" },
            new Disc { Track = "ward", Artist = "C418" }, new Disc { Track = "11", Artist = "C418" }, new Disc { Track = "wait", Artist = "C418" },
            new Disc { Track = "pigstep", Artist = "Lena Raine" }, new Disc { Track = "otherside", Artist = "Lena Raine" },
            new Disc { Track = "5", Artist = "Samuel Åberg" }, new Disc { Track = "relic", Artist = "Aaron Cherof" },
            new Disc { Track = "creator", Artist = "Lena Raine" }, new Disc { Track = "creator_music_box", Artist = "Lena Raine" },
            new Disc { Track = "precipice", Artist = "Aaron Cherof" }, new Disc { Track = "tears", Artist = "Amos Roddy" },
            new Disc { Track = "lava_chicken", Artist = "Hyper Potions" },
        };

        public static int IndexOf(string key)
        {
            var b = ItemData.Base(key);
            for (int i = 0; i < Discs.Count; i++) if (Discs[i].Key == b) return i;
            return -1;
        }

        public static string Title(Disc d) => d.Artist + " - " + d.Track.Replace("_", " ");

        // ------------------------------------------------------------------ server
        /// <summary>Server: the disc (its key, with its value) in each jukebox; and when it started (server time).</summary>
        static readonly Dictionary<BlockKey, string> inside = new Dictionary<BlockKey, string>();
        static readonly Dictionary<BlockKey, double> started = new Dictionary<BlockKey, double>();
        static float nextNoise;

        static double Now => Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        /// <summary>Server: a player put a disc in (it's already out of their hand).</summary>
        public static void ServerInsert(ulong sender, BlockKey k, string discKey)
        {
            var w = BlockWorld.Instance;
            var bi = w?.Get(k);
            int idx = IndexOf(discKey);
            if (bi == null || bi.Data.Def != Blocks.Jukebox || idx < 0 || bi.Data.State != 0)
            {
                // (not a jukebox, or one already playing: the disc goes back)
                if (idx >= 0) Inventory.ServerSpawnFor(sender, discKey, 1);
                return;
            }
            inside[k] = discKey;
            started[k] = Now;
            var d = bi.Data; d.State = (byte)(idx + 1);
            BlockNet.ServerBroadcastOp(Op.State(k, d));
            BlockNet.ServerJukebox(k, idx + 1, started[k]);
        }

        /// <summary>Server: [E] on a playing jukebox: the disc pops out on top.</summary>
        public static void ServerEject(BlockKey k)
        {
            var w = BlockWorld.Instance;
            var bi = w?.Get(k);
            if (bi == null || bi.Data.Def != Blocks.Jukebox || bi.Data.State == 0) return;
            DropDisc(k, bi.Data.State, w.WorldCenter(k) + w.FrameDirToWorld(k.Frame, Vector3.up) * (BlockWorld.S * 0.7f));
            var d = bi.Data; d.State = 0;
            BlockNet.ServerBroadcastOp(Op.State(k, d));
            BlockNet.ServerJukebox(k, 0, 0);
            BlockNet.ServerSound(w.WorldCenter(k), "pop", 0.5f, 0.8f);
        }

        /// <summary>Server: the jukebox is gone (broken): its disc drops where it stood.</summary>
        public static void ServerBroken(BlockKey k, byte state, Vector3 at)
        {
            if (state == 0) return;
            DropDisc(k, state, at);
            BlockNet.ServerJukebox(k, 0, 0);
        }

        static void DropDisc(BlockKey k, byte state, Vector3 at)
        {
            string key = inside.TryGetValue(k, out var dk) ? dk : (state - 1 < Discs.Count ? Discs[state - 1].Key : null);
            inside.Remove(k); started.Remove(k);
            if (key == null) return;
            var g = ModItems.ServerSpawnPlainKeyed(key, at);
            if (g != null && g.scrapValue <= 0 && g.itemProperties != null && g.itemProperties.isScrap)
            {
                // (a disc whose value got lost, e.g. one that went in before a reload: a fresh one's)
                int v = Random.Range(g.itemProperties.minValue, g.itemProperties.maxValue);
                g.SetScrapValue(v);
                BlockNet.ServerScrapValue(g.NetworkObjectId, v);
            }
        }

        /// <summary>Server: what's playing, to a player who just joined.</summary>
        public static void ServerSyncTo(ulong client)
        {
            var w = BlockWorld.Instance;
            if (w == null) return;
            foreach (var bi in w.Blocks.Values)
                if (bi.Data.Def == Blocks.Jukebox && bi.Data.State != 0)
                    BlockNet.ServerJukebox(bi.Key, bi.Data.State, started.TryGetValue(bi.Key, out var t) ? t : Now, client);
        }

        /// <summary>A frame's blocks are gone (the moon unloaded): forget its jukeboxes.</summary>
        public static void ResetFrame(int frame)
        {
            foreach (var k in new List<BlockKey>(inside.Keys)) if (k.Frame == frame) inside.Remove(k);
            foreach (var k in new List<BlockKey>(started.Keys)) if (k.Frame == frame) started.Remove(k);
            foreach (var k in new List<BlockKey>(startAt.Keys)) if (k.Frame == frame) startAt.Remove(k);
        }

        /// <summary>Server, now and then: monsters hear the music, like a boombox.</summary>
        public static void ServerTick()
        {
            if (Time.time < nextNoise) return;
            nextNoise = Time.time + 1f;
            var w = BlockWorld.Instance;
            if (w == null) return;
            foreach (var kv in sources)
                if (kv.Value != null && kv.Value.isPlaying) ServerLogic.Noise(kv.Value.transform.position, 22f, 0.6f);
        }

        // ------------------------------------------------------------------ every client: playing it
        static readonly Dictionary<BlockKey, double> startAt = new Dictionary<BlockKey, double>();
        static readonly Dictionary<BlockKey, AudioSource> sources = new Dictionary<BlockKey, AudioSource>();
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static readonly HashSet<string> loading = new HashSet<string>();
        public static float Volume = 0.85f, Range = 40f;

        /// <summary>Every client: a jukebox started (state = disc + 1, when) or stopped (0).</summary>
        public static void Receive(BlockKey k, int state, double when)
        {
            if (state == 0) { startAt.Remove(k); Stop(k); return; }
            startAt[k] = when;
            var bi = BlockWorld.Instance?.Get(k);
            if (bi != null) OnVisual(bi);
        }

        /// <summary>Every client: the jukebox block was (re)drawn: play what's in it, or nothing.</summary>
        public static void OnVisual(BlockInstance bi)
        {
            if (bi == null || bi.Data.Def != Blocks.Jukebox || bi.Go == null) return;
            int state = bi.Data.State;
            if (state == 0 || state - 1 >= Discs.Count) { Stop(bi.Key); return; }
            var disc = Discs[state - 1];
            if (sources.TryGetValue(bi.Key, out var src) && src != null && src.gameObject.name == "LMC_Jukebox_" + disc.Track) return;
            Stop(bi.Key);
            var go = new GameObject("LMC_Jukebox_" + disc.Track);
            go.transform.SetParent(bi.Go.transform, false);
            src = go.AddComponent<AudioSource>();
            src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 4f; src.maxDistance = Range;
            src.volume = Volume; src.loop = false; src.playOnAwake = false; src.dopplerLevel = 0f;
            sources[bi.Key] = src;
            if (clips.TryGetValue(disc.Track, out var clip)) Start(bi.Key, src, clip);
            else if (!loading.Contains(disc.Track)) Runner.StartCoroutine(Load(disc.Track));
        }

        static Sounds.CoroutineHost runner;
        static Sounds.CoroutineHost Runner
        {
            get
            {
                if (runner != null) return runner;
                var go = new GameObject("LMC_Jukebox");
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                return runner = go.AddComponent<Sounds.CoroutineHost>();
            }
        }

        static void Start(BlockKey k, AudioSource src, AudioClip clip)
        {
            if (src == null || clip == null) return;
            src.clip = clip;
            // (from where the song is now: a player who just joined hears it from about the right point)
            double since = startAt.TryGetValue(k, out var t) ? Now - t : 0;
            if (since >= clip.length) return; // (over: the disc stays in, quiet)
            src.time = Mathf.Clamp((float)since, 0f, clip.length - 0.05f);
            src.Play();
        }

        static IEnumerator Load(string track)
        {
            loading.Add(track);
            var file = McAssets.SoundFile("records/" + track);
            if (file != null)
            {
                using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + file.Replace('\\', '/'), AudioType.OGGVORBIS))
                {
                    ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = true; // (minutes long: streamed, not decoded up front)
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        var clip = DownloadHandlerAudioClip.GetContent(req);
                        if (clip != null) { clip.name = "LMC_record_" + track; clips[track] = clip; }
                    }
                    else Plugin.Log.LogWarning($"Music disc '{track}': couldn't load {file}: {req.error}");
                }
            }
            else Plugin.Log.LogInfo($"Music disc '{track}': no track in this Minecraft install (it plays nothing)");
            loading.Remove(track);
            // whoever was waiting for this track
            if (clips.TryGetValue(track, out var c))
                foreach (var kv in sources)
                    if (kv.Value != null && kv.Value.gameObject.name == "LMC_Jukebox_" + track && kv.Value.clip == null) Start(kv.Key, kv.Value, c);
        }

        static void Stop(BlockKey k)
        {
            if (sources.TryGetValue(k, out var src) && src != null) Object.Destroy(src.gameObject);
            sources.Remove(k);
        }

        /// <summary>Every client, now and then: sources whose jukebox is gone (broken, the moon unloaded).</summary>
        public static void Tick()
        {
            if (sources.Count == 0) return;
            var gone = new List<BlockKey>();
            foreach (var kv in sources) if (kv.Value == null || BlockWorld.Instance == null || BlockWorld.Instance.DefAt(kv.Key) != Blocks.Jukebox) gone.Add(kv.Key);
            foreach (var k in gone) Stop(k);
        }

        /// <summary>Dev: what's playing.</summary>
        public static string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in sources)
                if (kv.Value != null) sb.Append($"{kv.Key.Pos} {kv.Value.gameObject.name.Substring(12)} playing={kv.Value.isPlaying} t={kv.Value.time:F1}/{(kv.Value.clip != null ? kv.Value.clip.length : 0):F0}s ; ");
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        public static string HoverTip(BlockInstance bi)
        {
            int s = bi.Data.State;
            return s == 0 || s - 1 >= Discs.Count ? "Jukebox - put a disc in : [E]" : $"Jukebox ({Title(Discs[s - 1])}) - take it out : [E]";
        }
    }
}
