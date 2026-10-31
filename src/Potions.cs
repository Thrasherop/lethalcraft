using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's potions (#88), the ones whose ingredients this world has: Swiftness (sugar), Strength (blaze powder),
    /// Fire Resistance (magma cream) and Night Vision (golden carrot), each from an awkward potion (nether wart in a
    /// water bottle), made longer with redstone or stronger with glowstone. Brewed in a brewing stand (BrewingStand),
    /// drunk like food (hold [Right-click]), the bottle comes back. Effects are the drinker's own: Swiftness pushes your
    /// walk along, Strength hits harder with Minecraft weapons, Fire Resistance stops burning damage (fire, lava,
    /// fireballs), Night Vision lights up what's around you. Shown at the top right with Minecraft's icons and the time
    /// left.
    /// </summary>
    [HarmonyPatch]
    public class Potions : MonoBehaviour
    {
        public enum Effect : byte { None, Swiftness, Strength, FireResistance, NightVision, Slowness, Poison, Weakness, Regeneration, Hunger, JumpBoost, SlowFalling, BadOmen, Resistance, WaterBreathing, Invisibility, Wither, InstantHealth, InstantDamage } // (JumpBoost to BadOmen: a witch's splash potions, #134; Resistance on: #244)

        public class Def
        {
            public string Key, Name; public Color32 Color; public Effect Effect; public int Level; public float Seconds;
            public bool Splash; // (#244: thrown, not drunk)
        }

        public static readonly ItemData.TypeMap<Def> Defs = new ItemData.TypeMap<Def>(); // (#228: by type)
        /// <summary>Brewing: (the potion in a bottle slot, the ingredient) -> the potion it becomes.</summary>
        public static readonly Dictionary<(string potion, string ingredient), string> Brews = new Dictionary<(string, string), string>();

        static Potions()
        {
            void D(string key, string name, uint rgb, Effect e = Effect.None, int level = 0, float secs = 0f) =>
                Defs[key] = new Def { Key = key, Name = name, Color = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255), Effect = e, Level = level, Seconds = secs };
            D("potion_water", "Water Bottle", 0x385DC6);
            D("potion_awkward", "Awkward Potion", 0x385DC6);
            // Minecraft's colours and times: 3:00, 8:00 with redstone, 1:30 at level II with glowstone
            foreach (var (k, n, c, e, strong) in new[]
            {
                ("swiftness", "Swiftness", 0x33EBFFu, Effect.Swiftness, true),
                ("strength", "Strength", 0xFFC700u, Effect.Strength, true),
                ("fire_resistance", "Fire Resistance", 0xFF9900u, Effect.FireResistance, false),
                ("night_vision", "Night Vision", 0xC2FF66u, Effect.NightVision, false),
            })
            {
                D("potion_" + k, "Potion of " + n, c, e, 1, 180f);
                D("potion_" + k + "_long", "Potion of " + n + " (8:00)", c, e, 1, 480f);
                Brews[("potion_" + k, "redstone_dust")] = "potion_" + k + "_long";
                if (strong) { D("potion_" + k + "_strong", "Potion of " + n + " II", c, e, 2, 90f); Brews[("potion_" + k, "glowstone_dust")] = "potion_" + k + "_strong"; }
            }
            Brews[("potion_water", "nether_wart")] = "potion_awkward";
            Brews[("potion_awkward", "sugar")] = "potion_swiftness";
            Brews[("potion_awkward", "blaze_powder")] = "potion_strength";
            Brews[("potion_awkward", "magma_cream")] = "potion_fire_resistance";
            Brews[("potion_awkward", "golden_carrot")] = "potion_night_vision";
            // (#139: the spider eye's and the ghast tear's; Minecraft's times: 0:45, 1:30 long, 0:21/0:22 at II)
            foreach (var (k, n, c, e) in new[] { ("poison", "Poison", 0x87A363u, Effect.Poison), ("regeneration", "Regeneration", 0xCD5CABu, Effect.Regeneration) })
            {
                D("potion_" + k, "Potion of " + n, c, e, 1, 45f);
                D("potion_" + k + "_long", "Potion of " + n + " (1:30)", c, e, 1, 90f);
                D("potion_" + k + "_strong", "Potion of " + n + " II", c, e, 2, 22f);
                Brews[("potion_" + k, "redstone_dust")] = "potion_" + k + "_long";
                Brews[("potion_" + k, "glowstone_dust")] = "potion_" + k + "_strong";
            }
            Brews[("potion_awkward", "spider_eye")] = "potion_poison";
            // (#149: the rabbit's foot's; Minecraft's 3:00, 8:00 long, 1:30 at II)
            D("potion_leaping", "Potion of Leaping", 0xFDFF84u, Effect.JumpBoost, 1, 180f);
            D("potion_leaping_long", "Potion of Leaping (8:00)", 0xFDFF84u, Effect.JumpBoost, 1, 480f);
            D("potion_leaping_strong", "Potion of Leaping II", 0xFDFF84u, Effect.JumpBoost, 2, 90f);
            Brews[("potion_leaping", "redstone_dust")] = "potion_leaping_long";
            Brews[("potion_leaping", "glowstone_dust")] = "potion_leaping_strong";
            Brews[("potion_awkward", "rabbit_foot")] = "potion_leaping";
            // (#141: the phantom membrane's; Minecraft's 1:30, 4:00 long)
            D("potion_slow_falling", "Potion of Slow Falling", 0xF3CFB9u, Effect.SlowFalling, 1, 90f);
            D("potion_slow_falling_long", "Potion of Slow Falling (4:00)", 0xF3CFB9u, Effect.SlowFalling, 1, 240f);
            Brews[("potion_slow_falling", "redstone_dust")] = "potion_slow_falling_long";
            Brews[("potion_awkward", "phantom_membrane")] = "potion_slow_falling";
            Brews[("potion_awkward", "ghast_tear")] = "potion_regeneration";

            // (#244) Minecraft 26.3's other brews (VanillaBrewingProvider): mundane and thick potions; water breathing (the
            // pufferfish); the fermented spider eye's: weakness, and turned over, slowness, invisibility, harming; healing
            // (the glistering melon). Colours: 26.3's effect colours; times: its potions'
            D("potion_mundane", "Mundane Potion", 0x385DC6);
            D("potion_thick", "Thick Potion", 0x385DC6);
            Brews[("potion_water", "glowstone_dust")] = "potion_thick";
            Brews[("potion_water", "redstone_dust")] = "potion_mundane";
            foreach (var start in new[] { "sugar", "blaze_powder", "magma_cream", "rabbit_foot", "ghast_tear", "spider_eye", "glistering_melon_slice" })
                Brews[("potion_water", start)] = "potion_mundane"; // (Minecraft's buildStartMix: in plain water they make it mundane)
            D("potion_water_breathing", "Potion of Water Breathing", 0x98DAC0u, Effect.WaterBreathing, 1, 180f);
            D("potion_water_breathing_long", "Potion of Water Breathing (8:00)", 0x98DAC0u, Effect.WaterBreathing, 1, 480f);
            Brews[("potion_awkward", "pufferfish")] = "potion_water_breathing";
            Brews[("potion_water_breathing", "redstone_dust")] = "potion_water_breathing_long";
            D("potion_invisibility", "Potion of Invisibility", 0xF6F6F6u, Effect.Invisibility, 1, 180f);
            D("potion_invisibility_long", "Potion of Invisibility (8:00)", 0xF6F6F6u, Effect.Invisibility, 1, 480f);
            Brews[("potion_night_vision", "fermented_spider_eye")] = "potion_invisibility";
            Brews[("potion_night_vision_long", "fermented_spider_eye")] = "potion_invisibility_long";
            Brews[("potion_invisibility", "redstone_dust")] = "potion_invisibility_long";
            D("potion_slowness", "Potion of Slowness", 0x8BAFE0u, Effect.Slowness, 1, 90f);
            D("potion_slowness_long", "Potion of Slowness (4:00)", 0x8BAFE0u, Effect.Slowness, 1, 240f);
            D("potion_slowness_strong", "Potion of Slowness IV", 0x8BAFE0u, Effect.Slowness, 4, 20f);
            foreach (var from in new[] { "potion_leaping", "potion_swiftness" }) { Brews[(from, "fermented_spider_eye")] = "potion_slowness"; Brews[(from + "_long", "fermented_spider_eye")] = "potion_slowness_long"; }
            Brews[("potion_slowness", "redstone_dust")] = "potion_slowness_long";
            Brews[("potion_slowness", "glowstone_dust")] = "potion_slowness_strong";
            D("potion_weakness", "Potion of Weakness", 0x484D48u, Effect.Weakness, 1, 90f);
            D("potion_weakness_long", "Potion of Weakness (4:00)", 0x484D48u, Effect.Weakness, 1, 240f);
            Brews[("potion_water", "fermented_spider_eye")] = "potion_weakness";
            Brews[("potion_weakness", "redstone_dust")] = "potion_weakness_long";
            D("potion_healing", "Potion of Healing", 0xF82423u, Effect.InstantHealth, 1, 0f);
            D("potion_healing_strong", "Potion of Healing II", 0xF82423u, Effect.InstantHealth, 2, 0f);
            D("potion_harming", "Potion of Harming", 0xA9656Au, Effect.InstantDamage, 1, 0f);
            D("potion_harming_strong", "Potion of Harming II", 0xA9656Au, Effect.InstantDamage, 2, 0f);
            Brews[("potion_awkward", "glistering_melon_slice")] = "potion_healing";
            Brews[("potion_healing", "glowstone_dust")] = "potion_healing_strong";
            Brews[("potion_healing", "fermented_spider_eye")] = "potion_harming";
            Brews[("potion_healing_strong", "fermented_spider_eye")] = "potion_harming_strong";
            Brews[("potion_harming", "glowstone_dust")] = "potion_harming_strong";
            Brews[("potion_poison", "fermented_spider_eye")] = "potion_harming";
            Brews[("potion_poison_long", "fermented_spider_eye")] = "potion_harming";
            Brews[("potion_poison_strong", "fermented_spider_eye")] = "potion_harming_strong";

            // (#244) splash potions: every potion has one (gunpowder in the stand), thrown instead of drunk; they brew on as
            // the drinkable ones do (Minecraft's containers: any mix works on a splash potion too)
            foreach (var d in Defs.Values.ToList())
            {
                string sk = "splash_" + d.Key;
                string sn = d.Name.StartsWith("Potion of ") ? "Splash " + d.Name : d.Name == "Water Bottle" ? "Splash Water Bottle" : d.Name.Replace(" Potion", " Splash Potion");
                Defs[sk] = new Def { Key = sk, Name = sn, Color = d.Color, Effect = d.Effect, Level = d.Level, Seconds = d.Seconds, Splash = true };
                Brews[(d.Key, "gunpowder")] = sk;
            }
            foreach (var b in Brews.ToList())
                if (!b.Key.potion.StartsWith("splash_") && b.Key.ingredient != "gunpowder") Brews[("splash_" + b.Key.potion, b.Key.ingredient)] = "splash_" + b.Value;
        }

        /// <summary>(#244) Minecraft 26.3's effect colours (the swirls'), and which effects are harmful (the HUD's second row).</summary>
        public static readonly Dictionary<Effect, uint> EffectColor = new Dictionary<Effect, uint>
        {
            [Effect.Swiftness] = 0x33EBFF, [Effect.Strength] = 0xFFC700, [Effect.FireResistance] = 0xFF9900, [Effect.NightVision] = 0xC2FF66,
            [Effect.Slowness] = 0x8BAFE0, [Effect.Poison] = 0x87A363, [Effect.Weakness] = 0x484D48, [Effect.Regeneration] = 0xCD5CAB,
            [Effect.Hunger] = 0x587653, [Effect.JumpBoost] = 0xFDFF84, [Effect.SlowFalling] = 0xF3CFB9, [Effect.BadOmen] = 0x0B6138,
            [Effect.Resistance] = 0x9146F0, [Effect.WaterBreathing] = 0x98DAC0, [Effect.Invisibility] = 0xF6F6F6, [Effect.Wither] = 0x736156,
        };
        public static bool IsHarmful(Effect e) => e == Effect.Slowness || e == Effect.Poison || e == Effect.Weakness || e == Effect.Hunger || e == Effect.Wither || e == Effect.BadOmen || e == Effect.InstantDamage;

        /// <summary>Minecraft's swirl colour: the effects' colours, each weighted by its level; 0 for none.</summary>
        public static uint SwirlColor(IEnumerable<(Effect e, int level)> effects)
        {
            float r = 0, g = 0, b = 0, n = 0;
            foreach (var (e, lv) in effects)
            {
                if (!EffectColor.TryGetValue(e, out var c) || lv <= 0) continue;
                r += ((c >> 16) & 255) * lv; g += ((c >> 8) & 255) * lv; b += (c & 255) * lv; n += lv;
            }
            if (n <= 0) return 0;
            return ((uint)(r / n) << 16) | ((uint)(g / n) << 8) | (uint)(b / n) | 0x01000000u; // (a high bit: never 0 for black)
        }

        public static bool IsSplash(string key) { var t = ItemData.TypeOf(key); return t != null && t.StartsWith("splash_") && Defs.ContainsKey(t); }

        /// <summary>(#244) A potion's effect on the local player, a share of it (a splash from further off: Minecraft's 1 -
        /// distance / 4 blocks). Healing and Harming at once (Minecraft's 4 and 6 health a level, doubled at II); the rest
        /// for that share of their time.</summary>
        public static void Apply(Def d, float share = 1f)
        {
            var p = Local;
            if (d == null || p == null || share <= 0f) return;
            if (d.Effect == Effect.InstantHealth) { Survival.Heal(p, Mathf.RoundToInt(20 * (1 << (d.Level - 1)) * share)); return; }
            if (d.Effect == Effect.InstantDamage)
            {
                int dmg = Mathf.RoundToInt(30 * (1 << (d.Level - 1)) * share);
                if (dmg > 0) p.DamagePlayer(dmg, true, true, CauseOfDeath.Unknown);
                return;
            }
            if (share >= 1f) Give(d); else GiveEffect(d.Effect, d.Level, d.Seconds * share);
        }

        public static bool IsPotion(string key) => Defs.ContainsKey(key); // (#228: by type: named, still a potion)
        public static bool IsIngredient(string key) { var k = ItemData.TypeOf(key); return k != null && Brews.Keys.Any(b => b.ingredient == k); }
        public static string BrewOf(string potion, string ingredient) => potion != null && ingredient != null && Brews.TryGetValue((ItemData.TypeOf(potion), ItemData.TypeOf(ingredient)), out var r) ? r : null;

        // ------------------------------------------------------------------ the local player's effects
        static readonly Dictionary<Effect, (int level, float until)> active = new Dictionary<Effect, (int, float)>();
        static readonly Dictionary<Effect, float> started = new Dictionary<Effect, float>();
        static readonly Dictionary<Effect, (int level, float until)> hidden = new Dictionary<Effect, (int, float)>(); // (#244)
        public static int Level(Effect e) => active.TryGetValue(e, out var a) && a.until > Time.time ? a.level : 0;
        public static float SecondsLeft(Effect e) => active.TryGetValue(e, out var a) ? Mathf.Max(0f, a.until - Time.time) : 0f;

        public static void Give(Def d)
        {
            if (d == null || d.Effect == Effect.None) return;
            float until = Time.time + d.Seconds;
            // (a stronger one replaces a weaker; the same level takes the longer time. #244: a weaker one that lasts longer
            // waits underneath and comes back when the stronger one ends, Minecraft's)
            if (active.TryGetValue(d.Effect, out var cur) && cur.until > Time.time && cur.level > d.Level)
            {
                if (until > cur.until && (!hidden.TryGetValue(d.Effect, out var h) || h.until < until)) hidden[d.Effect] = (d.Level, until);
                return;
            }
            if (active.TryGetValue(d.Effect, out cur) && cur.until > Time.time && cur.level < d.Level && cur.until > until) hidden[d.Effect] = cur;
            if (active.TryGetValue(d.Effect, out cur) && cur.level == d.Level && cur.until > until) until = cur.until;
            active[d.Effect] = (d.Level, until);
            started[d.Effect] = Time.time;
        }

        /// <summary>An effect from outside a bottle (a witch's splash potion, #134): that level for that long.</summary>
        public static void GiveEffect(Effect e, int level, float seconds)
        {
            if (seconds <= 0.5f) return;
            Give(new Def { Key = "splash", Name = e.ToString(), Effect = e, Level = level, Seconds = seconds });
        }

        /// <summary>(#244) One effect gone (honey's cure for poison).</summary>
        public static void Remove(Effect e) { active.Remove(e); hidden.Remove(e); }

        public static void Clear() { active.Clear(); hidden.Clear(); }

        /// <summary>The harmful effects end (a totem of undying, #204): slowness, poison, weakness, hunger.</summary>
        public static void ClearHarmful()
        {
            foreach (var e in new[] { Effect.Slowness, Effect.Poison, Effect.Weakness, Effect.Hunger }) { active.Remove(e); hidden.Remove(e); }
        }
        /// <summary>The effects on now, newest first (the HUD's list).</summary>
        public static IEnumerable<(Effect effect, int level, float left)> Active() =>
            active.Where(a => a.Value.until > Time.time).OrderByDescending(a => started.TryGetValue(a.Key, out var t) ? t : 0f).Select(a => (a.Key, a.Value.level, a.Value.until - Time.time));


        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        // ------------------------------------------------------------------ drinking (like eating: hold the use button)
        public const float DrinkSeconds = 1.6f;
        public static float DrinkProgress;
        public static bool Drinking => DrinkProgress > 0f; // (#244: slowed while drinking, as eating)
        static StackItem drinking;
        float sipTimer;

        void Update()
        {
            var p = Local;
            if (p == null) return;
            if (p.isPlayerDead) { Clear(); DrinkProgress = 0f; drinking = null; return; }
            var held = p.isHoldingObject ? p.currentlyHeldObjectServer as StackItem : null;
            bool use = (Plugin.PlaceWithLeftClick.Value ? DevServer.LmbHeld : Builder.RmbHeld) && Builder.CanAct(p);
            if (held == null || !(IsPotion(held.ItemKey) || OminousBottle.LevelOf(held.ItemKey) > 0) || IsSplash(held.ItemKey) || !use || held.Count <= 0)
            {
                DrinkProgress = 0f; drinking = null;
            }
            else
            {
                if (drinking != held) { drinking = held; DrinkProgress = 0f; sipTimer = 0f; }
                DrinkProgress += Time.deltaTime / DrinkSeconds;
                sipTimer -= Time.deltaTime;
                if (sipTimer <= 0f) { sipTimer = 0.4f; Sounds.Play("drink", p.gameplayCamera.transform.position, 0.5f, Random.Range(0.9f, 1.1f), 12f); }
                if (DrinkProgress >= 1f)
                {
                    DrinkProgress = 0f; drinking = null;
                    bool ominous = OminousBottle.LevelOf(held.ItemKey) > 0;
                    if (ominous) OminousBottle.Drunk(held.ItemKey); else Apply(Defs[held.ItemKey]);
                    if (!GameModes.LocalCreative) // (#244: in creative the potion stays, Minecraft's)
                    {
                        BlockNet.RequestEat(held);           // (one used up, as eating does)
                        if (!ominous) Inventory.Give("glass_bottle", 1); // the bottle comes back (not an ominous bottle's, #223)
                    }
                }
            }
            ApplyNightVision(p);
            // leaping (#149): jump higher (Minecraft's: a half block more a level)
            if (baseJump < 0f) baseJump = p.jumpForce;
            p.jumpForce = baseJump * (1f + 0.25f * Level(Effect.JumpBoost));
            // poison (#134): Minecraft's 1 of 20 health every 1.25 s, never the last half heart
            if (Level(Effect.Poison) > 0)
            {
                poisonTick -= Time.deltaTime;
                if (poisonTick <= 0f)
                {
                    poisonTick = 1.25f / Level(Effect.Poison);
                    if (p.health > 5) p.DamagePlayer(Mathf.Min(5, p.health - 5), true, true, CauseOfDeath.Unknown);
                }
            }
            else poisonTick = 1.25f;
            // hunger (#136, a husk's hit): Minecraft's exhaustion, 0.1 a second a level
            if (Level(Effect.Hunger) > 0) Survival.AddExhaustion(0.1f * Level(Effect.Hunger) * Time.deltaTime);
            // regeneration (#139): Minecraft's half a heart every 2.5 s (every 1.25 s at II)
            if (Level(Effect.Regeneration) > 0)
            {
                regenTick -= Time.deltaTime;
                if (regenTick <= 0f) { regenTick = 2.5f / Level(Effect.Regeneration); Survival.Heal(p, 5); }
            }
            else regenTick = 2.5f;
            // (#244) water breathing: no drowning (the game's breath timer kept full); wither: Minecraft's half a heart every
            // 2 s (faster a level), and unlike poison it can kill
            if (Level(Effect.WaterBreathing) > 0 && StartOfRound.Instance != null) StartOfRound.Instance.drowningTimer = 1f;
            if (Level(Effect.Wither) > 0)
            {
                witherTick -= Time.deltaTime;
                if (witherTick <= 0f) { witherTick = 2f / Level(Effect.Wither); p.DamagePlayer(5, true, true, CauseOfDeath.Unknown); }
            }
            else witherTick = 2f;
            foreach (var h in hidden.ToList())
            {
                if (h.Value.until <= Time.time) { hidden.Remove(h.Key); continue; }
                if (Level(h.Key) == 0) { active[h.Key] = h.Value; started[h.Key] = Time.time; hidden.Remove(h.Key); }
            }
            bool invisible = Level(Effect.Invisibility) > 0;
            uint swirl = SwirlColor(Active().Select(a => (a.effect, a.level)));
            if (invisible != toldInvisible || swirl != toldSwirl || ((invisible || swirl != 0) && Time.time > toldAt + 5f))
            { toldInvisible = invisible; toldSwirl = swirl; toldAt = Time.time; BlockNet.RequestShown(invisible, swirl); }
            Swirls.Ensure();
            // (#244) Minecraft's: the view widens with speed, narrows with slowness (its walking speed over the usual, halved)
            float speed = (1f + 0.2f * Level(Effect.Swiftness)) * Mathf.Max(0f, 1f - 0.15f * Level(Effect.Slowness));
            float fovTarget = (speed + 1f) / 2f;
            FovMod = fovTarget + (FovMod - fovTarget) * Mathf.Pow(0.5f, Time.deltaTime * 8f);
            if (Mathf.Abs(FovMod - fovTarget) < 0.001f) FovMod = fovTarget;
        }
        public static float FovMod = 1f;
        uint toldSwirl;
        float witherTick = 2f, toldAt; bool toldInvisible;
        void LateUpdate() => Invisibility.UpdateBodies(); // (#244)
        float regenTick = 2.5f, baseJump = -1f;
        float poisonTick = 1.25f;

        // ------------------------------------------------------------------ the effects themselves
        /// <summary>Swiftness: before the game moves you, a push along your walk (Minecraft's +20% a level).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPrefix]
        static void Swift(PlayerControllerB __instance)
        {
            // (slow falling, #141: you drift down, and never hard enough to hurt)
            if (__instance == Local && Level(Effect.SlowFalling) > 0 && __instance.fallValue < -3f) { __instance.fallValue = -3f; __instance.fallValueUncapped = Mathf.Max(__instance.fallValueUncapped, -3f); }
            // (slowness, #134: Minecraft's -15% a level, against the walk)
            float lvl = Level(Effect.Swiftness) - Level(Effect.Slowness) * 0.75f;
            if (lvl == 0f || __instance != Local || !__instance.thisController.isGrounded) return;
            var p = __instance;
            // (a share of the walk the game is about to make: never more than all of it, #213)
            p.externalForces += Bow.WalkOf(p) * Mathf.Max(-1f, 0.2f * lvl);
        }

        /// <summary>Fire Resistance: burning doesn't hurt.</summary>
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer)), HarmonyPrefix, HarmonyPriority(Priority.High)]
        static bool FireProof(PlayerControllerB __instance, CauseOfDeath causeOfDeath)
        {
            if (__instance != Local || causeOfDeath != CauseOfDeath.Burning || Level(Effect.FireResistance) <= 0) return true;
            return false;
        }

        /// <summary>(#244) Jump Boost: Minecraft takes a block a level off the fall, so a health point (5 here) off its damage.</summary>
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer)), HarmonyPrefix, HarmonyPriority(Priority.LowerThanNormal)]
        static bool JumpBoostFall(PlayerControllerB __instance, ref int damageNumber, bool fallDamage, CauseOfDeath causeOfDeath)
        {
            int lvl = __instance == Local && (fallDamage || causeOfDeath == CauseOfDeath.Gravity) ? Level(Effect.JumpBoost) : 0; // (a real fall: Gravity)
            if (lvl <= 0) return true;
            damageNumber -= 5 * lvl;
            return damageNumber > 0;
        }

        /// <summary>(#244) Resistance: Minecraft's 20% less damage a level (the enchanted golden apple's).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer)), HarmonyPrefix, HarmonyPriority(Priority.High)]
        static void Resist(PlayerControllerB __instance, ref int damageNumber)
        {
            int lvl = __instance == Local ? Level(Effect.Resistance) : 0;
            if (lvl > 0 && damageNumber > 0) damageNumber = Mathf.Max(0, Mathf.RoundToInt(damageNumber * Mathf.Max(0f, 1f - 0.2f * lvl)));
        }

        /// <summary>Strength: extra hit force for Minecraft weapons (Minecraft's +3 damage a level, a fifth of that here).</summary>
        public static float StrengthBonus => Level(Effect.Strength) * 0.6f - Level(Effect.Weakness) * 0.8f; // (weakness, #134: Minecraft's -4)

        static float nvIntensity = -1f, nvRange;
        static void ApplyNightVision(PlayerControllerB p)
        {
            var l = p.nightVision;
            if (l == null) return;
            bool on = Level(Effect.NightVision) > 0;
            if (on)
            {
                if (nvIntensity < 0f) { nvIntensity = l.intensity; nvRange = l.range; }
                l.enabled = true;
                float left = SecondsLeft(Effect.NightVision);
                float scale = left > 10f ? 1f : 0.7f + 0.3f * Mathf.Sin(left * 20f * Mathf.PI * 0.2f); // (#244: GameRenderer.getNightVisionScale)
                l.intensity = Mathf.Lerp(nvIntensity, nvIntensity * 6f, scale);
                l.range = Mathf.Max(nvRange, 40f);
            }
            else if (nvIntensity >= 0f)
            {
                l.intensity = nvIntensity; l.range = nvRange; nvIntensity = -1f;
                l.enabled = p.isInsideFactory; // (the game's own rule)
            }
        }

        public static string Describe() =>
            string.Join(" ", active.Where(a => a.Value.until > Time.time).Select(a => $"{a.Key}{a.Value.level}:{a.Value.until - Time.time:F0}s")) is string s && s.Length > 0 ? s : "none";
    }
}
