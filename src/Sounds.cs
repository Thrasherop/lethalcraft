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
            // (#223) the mud family's and the mangrove roots', and an axe stripping a log
            ["dig.mud"] = "block/mud/break", ["step.mud"] = "block/mud/step", ["dig.packed_mud"] = "block/packed_mud/break", ["step.packed_mud"] = "block/packed_mud/step",
            ["dig.mud_bricks"] = "block/mud_bricks/break", ["step.mud_bricks"] = "block/mud_bricks/step", ["dig.mangrove_roots"] = "block/mangrove_roots/break", ["step.mangrove_roots"] = "block/mangrove_roots/step",
            ["dig.muddy_mangrove_roots"] = "block/muddy_mangrove_roots/break", ["step.muddy_mangrove_roots"] = "block/muddy_mangrove_roots/step", ["axe.strip"] = "item/axe/strip", ["honeycomb.wax"] = "item/honeycomb/wax_on", ["boat.paddle_water"] = "entity/boat/paddle_water", ["boat.paddle_land"] = "entity/boat/paddle_land", ["dye.use"] = "item/dye/dye", ["dig.anvil"] = "random/anvil_break", ["step.anvil"] = "step/stone", ["anvil.use"] = "random/anvil_use", ["anvil.land"] = "random/anvil_land", ["anvil.break"] = "random/anvil_break", ["loom.take"] = "ui/loom/take_result", ["loom.select"] = "ui/loom/select_pattern",
            ["dig.resin"] = "block/resin/resin_break", ["step.resin"] = "block/resin/resin_step", ["dig.resin_bricks"] = "block/resin_bricks/resin_brick_break", ["step.resin_bricks"] = "block/resin_bricks/resin_brick_step", // (#230)
            ["shelf.take"] = "block/shelf/single_swap", ["shelf.place"] = "block/shelf/place_item", ["shelf.swap"] = "block/shelf/single_swap", ["shelf.multi_swap"] = "block/shelf/multi_swap",
            ["shelf.activate"] = "block/shelf/activate", ["shelf.deactivate"] = "block/shelf/deactivate",
            // (#229) amethyst's (the buds' are the cluster's files), calcite's, smooth basalt's (basalt's), the spyglass's
            ["dig.amethyst"] = "block/amethyst/break", ["step.amethyst"] = "block/amethyst/step", ["amethyst.chime"] = "block/amethyst/shimmer",
            ["dig.amethyst_cluster"] = "block/amethyst_cluster/break", ["step.amethyst_cluster"] = "block/amethyst/step",
            ["dig.calcite"] = "block/calcite/break", ["step.calcite"] = "block/calcite/step", ["dig.basalt"] = "block/basalt/break", ["step.basalt"] = "block/basalt/step",
            ["spyglass.use"] = "item/spyglass/use", ["spyglass.stop"] = "item/spyglass/stop",
            ["step.stone"] = "step/stone", ["step.wood"] = "step/wood", ["step.grass"] = "step/grass", ["step.gravel"] = "step/gravel",
            ["step.sand"] = "step/sand", ["step.wool"] = "step/cloth", ["step.glass"] = "step/stone", ["step.metal"] = "step/stone",
            ["step.slime"] = "mob/slime/small",
            ["break.glass"] = "random/glass",
            ["pot.insert"] = "block/decorated_pot/insert", ["pot.insert_fail"] = "block/decorated_pot/insert_fail", ["pot.shatter"] = "block/decorated_pot/shatter",
            ["piston.out"] = "tile/piston/out", ["piston.in"] = "tile/piston/in",
            ["fuse"] = "random/fuse", ["explode"] = "random/explode",
            ["creeper.hurt"] = "mob/creeper/say", ["creeper.death"] = "mob/creeper/death",
            ["wskel.hurt"] = "mob/wither_skeleton/hurt", ["wskel.death"] = "mob/wither_skeleton/death", ["wskel.idle"] = "mob/wither_skeleton/idle",
            ["wskel.hit"] = "damage/hit",
            ["piglin.hurt"] = "mob/piglin/hurt", ["piglin.death"] = "mob/piglin/death", ["piglin.idle"] = "mob/piglin/idle", ["piglin.hit"] = "damage/hit",
            ["brute.idle"] = "mob/piglin_brute/idle", ["brute.hurt"] = "mob/piglin_brute/hurt", ["brute.death"] = "mob/piglin_brute/death", ["brute.hit"] = "damage/hit", ["brute.angry"] = "mob/piglin_brute/angry",
            ["piglin.celebrate"] = "mob/piglin/celebrate", ["piglin.admire"] = "mob/piglin/admire",
            ["blaze.idle"] = "mob/blaze/breathe", ["blaze.hurt"] = "mob/blaze/hit", ["blaze.death"] = "mob/blaze/death", ["blaze.hit"] = "damage/hit", ["blaze.shoot"] = "mob/ghast/fireball",
            ["skeleton.idle"] = "mob/skeleton/say", ["skeleton.hurt"] = "mob/skeleton/hurt", ["skeleton.death"] = "mob/skeleton/death", ["skeleton.hit"] = "damage/hit",
            ["spider.idle"] = "mob/spider/say", ["spider.hurt"] = "mob/spider/say", ["spider.death"] = "mob/spider/death", ["spider.hit"] = "damage/hit",
            ["enderman.idle"] = "mob/endermen/idle", ["enderman.hurt"] = "mob/endermen/hit", ["enderman.death"] = "mob/endermen/death", ["enderman.hit"] = "damage/hit",
            ["enderman.scream"] = "mob/endermen/scream", ["enderman.portal"] = "mob/endermen/portal", ["enderman.stare"] = "mob/endermen/stare",
            ["bow"] = "random/bow", ["bowhit"] = "random/bowhit", ["bonemeal"] = "item/bonemeal/bonemeal",
            ["wolf.idle"] = "mob/wolf/bark", ["wolf.hurt"] = "mob/wolf/hurt", ["wolf.death"] = "mob/wolf/death", ["wolf.hit"] = "mob/wolf/growl",
            ["wolf.eat"] = "random/eat", ["wolf.tamed"] = "mob/wolf/panting",
            ["cat.idle"] = "mob/cat/meow", ["cat.hurt"] = "mob/cat/hitt", ["cat.death"] = "mob/cat/hitt", ["cat.eat"] = "random/eat", ["cat.tamed"] = "mob/cat/purreow", ["cat.hiss"] = "mob/cat/hiss",
            ["parrot.idle"] = "mob/parrot/idle", ["parrot.hurt"] = "mob/parrot/hurt", ["parrot.death"] = "mob/parrot/death", ["parrot.eat"] = "mob/parrot/eat",
            ["parrot.tamed"] = "mob/parrot/idle",
            ["ghast.idle"] = "mob/ghast/moan", ["ghast.hurt"] = "mob/ghast/scream", ["ghast.death"] = "mob/ghast/death", ["ghast.charge"] = "mob/ghast/charge",
            ["ghast.shoot"] = "mob/ghast/fireball", ["ghast.hit"] = "damage/hit",
            ["zpiglin.idle"] = "mob/zombified_piglin/zpig", ["zpiglin.hurt"] = "mob/zombified_piglin/zpighurt", ["zpiglin.death"] = "mob/zombified_piglin/zpigdeath", ["zpiglin.hit"] = "damage/hit",
            ["hoglin.idle"] = "mob/hoglin/idle", ["hoglin.hurt"] = "mob/hoglin/hurt", ["hoglin.death"] = "mob/hoglin/death", ["hoglin.hit"] = "mob/hoglin/attack",
            ["endermite.idle"] = "mob/silverfish/say", ["endermite.hurt"] = "mob/silverfish/hit", ["endermite.death"] = "mob/silverfish/kill", ["endermite.hit"] = "damage/hit",
            ["husk.idle"] = "mob/husk/idle", ["husk.hurt"] = "mob/husk/hurt", ["husk.death"] = "mob/husk/death", ["husk.hit"] = "damage/hit",
            ["drowned.idle"] = "mob/drowned/idle", ["drowned.hurt"] = "mob/drowned/hurt", ["drowned.death"] = "mob/drowned/death", ["drowned.hit"] = "damage/hit",
            ["stray.idle"] = "mob/stray/idle", ["stray.hurt"] = "mob/stray/hurt", ["stray.death"] = "mob/stray/death", ["stray.hit"] = "damage/hit",
            ["bogged.idle"] = "mob/bogged/ambient", ["bogged.hurt"] = "mob/bogged/hurt", ["bogged.death"] = "mob/bogged/death", ["bogged.hit"] = "damage/hit",
            ["witch.idle"] = "mob/witch/ambient", ["witch.hurt"] = "mob/witch/hurt", ["witch.death"] = "mob/witch/death", ["witch.hit"] = "damage/hit",
            ["witch.drink"] = "mob/witch/drink", ["witch.throw"] = "mob/witch/throw",
            // (#223) the crossbow's, and the pillager's
            ["crossbow.load_start"] = "item/crossbow/loading_start", ["crossbow.load_mid"] = "item/crossbow/loading_middle", ["crossbow.load_end"] = "item/crossbow/loading_end",
            ["crossbow.shoot"] = "item/crossbow/shoot", ["crossbow.quick1"] = "item/crossbow/quick_charge/quick1_", ["crossbow.quick2"] = "item/crossbow/quick_charge/quick2_", ["crossbow.quick3"] = "item/crossbow/quick_charge/quick3_",
            ["pillager.idle"] = "mob/pillager/idle", ["pillager.hurt"] = "mob/pillager/hurt", ["pillager.death"] = "mob/pillager/death", ["pillager.hit"] = "damage/hit", ["pillager.celebrate"] = "mob/pillager/celebrate", ["ominous.dispose"] = "item/ominous_bottle/dispose",
            ["horn.0"] = "item/goat_horn/call0", ["horn.1"] = "item/goat_horn/call1", ["horn.2"] = "item/goat_horn/call2", ["horn.3"] = "item/goat_horn/call3",
            ["slime.idle"] = "mob/slime/small", ["slime.hurt"] = "mob/slime/small", ["slime.death"] = "mob/slime/big", ["slime.hit"] = "mob/slime/attack",
            ["magma.idle"] = "mob/magmacube/jump", ["magma.hurt"] = "mob/magmacube/small", ["magma.death"] = "mob/magmacube/big", ["magma.hit"] = "mob/magmacube/small",
            ["fish.cast"] = "random/bow", ["fish.splash"] = "random/splash", ["fish.splash_small"] = "liquid/splash",
            ["trader.idle"] = "mob/wandering_trader/idle", ["trader.hurt"] = "mob/wandering_trader/hurt", ["trader.death"] = "mob/wandering_trader/death", // (#244: it was silent)
            ["trader.yes"] = "mob/wandering_trader/yes", ["trader.no"] = "mob/wandering_trader/no", ["trader.haggle"] = "mob/wandering_trader/haggle",
            ["trader.drink_potion"] = "mob/wandering_trader/drink_potion", ["trader.drink_milk"] = "mob/wandering_trader/drink_milk",
            ["trader.disappeared"] = "mob/wandering_trader/disappeared", ["trader.reappeared"] = "mob/wandering_trader/reappeared",
            ["llama.idle"] = "mob/llama/idle", ["llama.hurt"] = "mob/llama/hurt", ["llama.death"] = "mob/llama/death", ["llama.angry"] = "mob/llama/angry", // (#244: it was silent)
            ["llama.spit"] = "mob/llama/spit", ["llama.eat"] = "mob/llama/eat", ["llama.swag"] = "mob/llama/swag", ["llama.chest"] = "mob/chicken/plop",
            ["horse.idle"] = "mob/horse/idle", ["horse.hurt"] = "mob/horse/hit", ["horse.death"] = "mob/horse/death", ["horse.angry"] = "mob/horse/angry",
            ["horse.saddle"] = "mob/horse/leather", ["horse.armor"] = "mob/horse/armor", ["lead.tie"] = "entity/leashknot/place", ["horse.eat"] = "mob/horse/eat", ["horse.jump"] = "mob/horse/jump", ["horse.land"] = "mob/horse/land",
            ["golem.idle"] = "mob/irongolem/walk", ["golem.hurt"] = "mob/irongolem/hit", ["golem.death"] = "mob/irongolem/death", ["golem.hit"] = "mob/irongolem/throw",
            ["golem.attack"] = "mob/irongolem/throw", ["golem.repair"] = "mob/irongolem/repair",
            ["snowgolem.idle"] = "mob/snowgolem/ambient", ["snowgolem.hurt"] = "mob/snowgolem/hurt", ["snowgolem.death"] = "mob/snowgolem/death", ["snowball.throw"] = "random/bow",
            ["phantom.idle"] = "mob/phantom/flap", ["phantom.hurt"] = "mob/phantom/hurt", ["phantom.death"] = "mob/phantom/death", ["phantom.hit"] = "mob/phantom/bite", ["phantom.swoop"] = "mob/phantom/swoop",
            ["squid.idle"] = "mob/squid/ambient", ["squid.hurt"] = "mob/squid/hurt", ["squid.death"] = "mob/squid/death",
            ["sheep.idle"] = "mob/sheep/say", ["sheep.hurt"] = "mob/sheep/say", ["sheep.death"] = "mob/sheep/say", ["sheep.shear"] = "mob/sheep/shear",
            ["rabbit.idle"] = "mob/rabbit/idle", ["rabbit.hurt"] = "mob/rabbit/hurt", ["rabbit.death"] = "mob/rabbit/bunnymurder",
            ["chicken.idle"] = "mob/chicken/say", ["chicken.hurt"] = "mob/chicken/hurt", ["chicken.death"] = "mob/chicken/hurt",
            ["pig.idle"] = "mob/pig/say", ["pig.hurt"] = "mob/pig/say", ["pig.death"] = "mob/pig/death",
            ["cow.milk"] = "entity/cow/milk", ["honey.drink"] = "item/bottle/drink_honey", // (#244)
            ["cow.idle"] = "mob/cow/say", ["cow.hurt"] = "mob/cow/hurt", ["cow.death"] = "mob/cow/hurt",
            ["zombie.idle"] = "mob/zombie/say", ["zombie.hurt"] = "mob/zombie/hurt", ["zombie.death"] = "mob/zombie/death", ["zombie.hit"] = "damage/hit",
            ["till"] = "item/hoe/till",
            ["drink"] = "random/drink", ["bottle.fill"] = "item/bottle/fill", ["brew"] = "block/brewing_stand/brew",
            ["portal.ambient"] = "portal/portal", ["portal.trigger"] = "portal/trigger", ["portal.travel"] = "portal/travel",
            ["armor.elytra"] = "item/armor/equip_leather", ["elytra.open"] = "item/elytra/elytra_loop", ["firework.launch"] = "fireworks/launch",
            ["bucket.fill"] = "item/bucket/fill", ["bucket.empty"] = "item/bucket/empty",
            ["bucket.fill_lava"] = "item/bucket/fill_lava", ["bucket.empty_lava"] = "item/bucket/empty_lava",
            ["click"] = "random/click", ["page"] = "item/book/page_turn1", ["pop"] = "random/pop", ["tool.break"] = "random/break", ["totem"] = "item/totem/use_totem",
            ["note.harp"] = "note/harp", ["note.bass"] = "note/bass", ["note.pling"] = "note/pling", ["note.bell"] = "note/bell",
            ["note.basedrum"] = "note/bd", ["note.snare"] = "note/snare", ["note.hat"] = "note/hat", ["note.flute"] = "note/flute", ["note.guitar"] = "note/guitar", // (#244)
            ["note.chime"] = "note/icechime", ["note.xylophone"] = "note/xylobone", ["note.iron_xylophone"] = "note/iron_xylophone", ["note.cow_bell"] = "note/cow_bell",
            ["note.didgeridoo"] = "note/didgeridoo", ["note.bit"] = "note/bit", ["note.banjo"] = "note/banjo",
            ["eat"] = "random/eat", ["burp"] = "random/burp", ["orb"] = "random/orb", ["levelup"] = "random/levelup",
            ["ignite"] = "fire/ignite", ["fire"] = "fire/fire", ["extinguish"] = "random/fizz", ["hurt"] = "damage/hit", ["bounce"] = "mob/slime/big",
            ["swing"] = "entity/player/attack/sweep", ["attack"] = "entity/player/attack/strong", ["crit"] = "entity/player/attack/crit",
            ["pearl.throw"] = "random/bow", ["pearl.land"] = "mob/endermen/portal",
            ["chest.open"] = "random/chestopen", ["chest.close"] = "random/chestclosed", ["barrel.open"] = "block/barrel/open", ["barrel.close"] = "block/barrel/close", // (#244)
            ["enchant"] = "block/enchantment_table/enchant", ["thorns"] = "enchant/thorns/hit",
            ["door.open"] = "random/door_open", ["door.close"] = "random/door_close",
            ["iron_door.open"] = "block/iron_door/open", ["iron_door.close"] = "block/iron_door/close", ["iron_trapdoor.open"] = "block/iron_trapdoor/open",
            ["iron_trapdoor.close"] = "block/iron_trapdoor/close", ["copper_door.toggle"] = "block/copper_door/toggle", ["copper_trapdoor.toggle"] = "block/copper_trapdoor/toggle", // (#244)
            ["armor.pumpkin"] = "item/armor/equip_generic", ["toast.challenge"] = "ui/toast/in", ["toast.out"] = "ui/toast/out", ["pumpkin.carve"] = "block/pumpkin/carve", ["egg.throw"] = "random/bow", ["firecharge.use"] = "mob/ghast/fireball4", ["firework.blast"] = "fireworks/blast", ["firework.blast_far"] = "fireworks/blast_far", ["firework.largeblast"] = "fireworks/largeblast", ["firework.largeblast_far"] = "fireworks/largeblast_far", ["firework.twinkle"] = "fireworks/twinkle", ["firework.twinkle_far"] = "fireworks/twinkle_far", ["chicken.plop"] = "mob/chicken/plop", ["armor.iron"] = "item/armor/equip_iron", ["armor.leather"] = "item/armor/equip_leather", ["armor.chainmail"] = "item/armor/equip_chain", ["armor.copper"] = "item/armor/equip_copper", ["armor.golden"] = "item/armor/equip_gold", ["armor.diamond"] = "item/armor/equip_diamond", ["armor.netherite"] = "item/armor/equip_netherite", ["smithing"] = "block/smithing_table/smithing_table1",
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

        public class CoroutineHost : MonoBehaviour { }

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
                        FromMinecraft.Add(kv.Key);
                        count++;
                    }
                }
                if (list.Count > 0) bank[kv.Key] = list;
            }
            Loaded = true;
            Plugin.Log.LogInfo($"Loaded {count} Minecraft sound clips");
        }

        /// <summary>(dev) sounds loaded from the player's Minecraft (the rest are synthesized stand-ins), and the last ones played.</summary>
        public static readonly HashSet<string> FromMinecraft = new HashSet<string>();
        public static readonly List<string> Recent = new List<string>();

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

        /// <summary>(dev) The last sounds this machine played, newest last (dev soundlog).</summary>
        public static readonly List<string> DevRecent = new List<string>();
        static void DevNote(string id, string how) { DevRecent.Add(how + id + "@" + Time.time.ToString("F1")); if (DevRecent.Count > 40) DevRecent.RemoveAt(0); }

        public static void Play(string id, Vector3 pos, float volume = 1f, float pitch = 1f, float maxDistance = 24f)
        {
            DevNote(id, "");
            var clip = Get(id);
            if (clip == null) return;
            if (Plugin.DevMode.Value) { Recent.Add($"{id}@{pos.x:F1},{pos.y:F1},{pos.z:F1}"); if (Recent.Count > 20) Recent.RemoveAt(0); }
            var src = GetSource();
            src.transform.position = pos;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume * SoundOptions.Factor(id)); // (#219: Minecraft's sound categories)
            src.pitch = pitch;
            src.maxDistance = maxDistance;
            src.spatialBlend = 1f;
            src.Play();
        }

        public static void Play2D(string id, float volume = 1f, float pitch = 1f)
        {
            DevNote(id, "2d:");
            var clip = Get(id);
            if (clip == null) return;
            var src = GetSource();
            src.clip = clip;
            src.volume = volume * SoundOptions.Factor(id);
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
            src.volume = volume * SoundOptions.Factor(id);
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
            Add("door.open", Sweep(0.18f, 300f, 500f, 0.35f, rng, true));
            Add("door.close", Sweep(0.14f, 420f, 240f, 0.4f, rng, true));
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
