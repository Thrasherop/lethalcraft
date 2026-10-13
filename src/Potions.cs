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
        public enum Effect : byte { None, Swiftness, Strength, FireResistance, NightVision, Slowness, Poison, Weakness, Regeneration, Hunger, JumpBoost } // (the last three: a witch's splash potions, #134)

        public class Def
        {
            public string Key, Name; public Color32 Color; public Effect Effect; public int Level; public float Seconds;
        }

        public static readonly Dictionary<string, Def> Defs = new Dictionary<string, Def>();
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
                if (strong) { D("potion_" + k + "_strong", "Potion of " + n + " II", c, e, 2, 90f); Brews[("potion_" + k, "glowstone")] = "potion_" + k + "_strong"; }
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
                Brews[("potion_" + k, "glowstone")] = "potion_" + k + "_strong";
            }
            Brews[("potion_awkward", "spider_eye")] = "potion_poison";
            // (#149: the rabbit's foot's; Minecraft's 3:00, 8:00 long, 1:30 at II)
            D("potion_leaping", "Potion of Leaping", 0x22FF4Cu, Effect.JumpBoost, 1, 180f);
            D("potion_leaping_long", "Potion of Leaping (8:00)", 0x22FF4Cu, Effect.JumpBoost, 1, 480f);
            D("potion_leaping_strong", "Potion of Leaping II", 0x22FF4Cu, Effect.JumpBoost, 2, 90f);
            Brews[("potion_leaping", "redstone_dust")] = "potion_leaping_long";
            Brews[("potion_leaping", "glowstone")] = "potion_leaping_strong";
            Brews[("potion_awkward", "rabbit_foot")] = "potion_leaping";
            Brews[("potion_awkward", "ghast_tear")] = "potion_regeneration";
        }

        public static bool IsPotion(string key) => key != null && Defs.ContainsKey(key);
        public static bool IsIngredient(string key) => key != null && Brews.Keys.Any(b => b.ingredient == key);
        public static string BrewOf(string potion, string ingredient) => potion != null && ingredient != null && Brews.TryGetValue((potion, ingredient), out var r) ? r : null;

        // ------------------------------------------------------------------ the local player's effects
        static readonly Dictionary<Effect, (int level, float until)> active = new Dictionary<Effect, (int, float)>();
        static readonly Dictionary<Effect, float> started = new Dictionary<Effect, float>();
        public static int Level(Effect e) => active.TryGetValue(e, out var a) && a.until > Time.time ? a.level : 0;
        public static float SecondsLeft(Effect e) => active.TryGetValue(e, out var a) ? Mathf.Max(0f, a.until - Time.time) : 0f;

        public static void Give(Def d)
        {
            if (d == null || d.Effect == Effect.None) return;
            // (a stronger one replaces a weaker; the same level takes the longer time)
            if (active.TryGetValue(d.Effect, out var cur) && cur.until > Time.time && cur.level > d.Level) return;
            float until = Time.time + d.Seconds;
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

        public static void Clear() => active.Clear();
        /// <summary>The effects on now, newest first (the HUD's list).</summary>
        public static IEnumerable<(Effect effect, int level, float left)> Active() =>
            active.Where(a => a.Value.until > Time.time).OrderByDescending(a => started.TryGetValue(a.Key, out var t) ? t : 0f).Select(a => (a.Key, a.Value.level, a.Value.until - Time.time));


        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        // ------------------------------------------------------------------ drinking (like eating: hold the use button)
        public const float DrinkSeconds = 1.6f;
        public static float DrinkProgress;
        static StackItem drinking;
        float sipTimer;

        void Update()
        {
            var p = Local;
            if (p == null) return;
            if (p.isPlayerDead) { Clear(); DrinkProgress = 0f; drinking = null; return; }
            var held = p.isHoldingObject ? p.currentlyHeldObjectServer as StackItem : null;
            bool use = (Plugin.PlaceWithLeftClick.Value ? DevServer.LmbHeld : Builder.RmbHeld) && Builder.CanAct(p);
            if (held == null || !IsPotion(held.ItemKey) || !use || held.Count <= 0)
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
                    Give(Defs[held.ItemKey]);
                    BlockNet.RequestEat(held);           // (one used up, as eating does)
                    Inventory.Give("glass_bottle", 1);    // the bottle comes back
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
        }
        float regenTick = 2.5f, baseJump = -1f;
        float poisonTick = 1.25f;

        // ------------------------------------------------------------------ the effects themselves
        /// <summary>Swiftness: before the game moves you, a push along your walk (Minecraft's +20% a level).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPrefix]
        static void Swift(PlayerControllerB __instance)
        {
            // (slowness, #134: Minecraft's -15% a level, against the walk)
            float lvl = Level(Effect.Swiftness) - Level(Effect.Slowness) * 0.75f;
            if (lvl == 0f || __instance != Local || !__instance.thisController.isGrounded) return;
            var p = __instance;
            var mv = p.moveInputVector;
            if (mv.sqrMagnitude < 0.01f) return;
            var dir = p.transform.right * mv.x + p.transform.forward * mv.y; dir.y = 0f;
            float run = p.isSprinting ? 2.25f : 1f;
            p.externalForces += Vector3.ClampMagnitude(dir, 1f) * p.movementSpeed * run * 0.2f * lvl;
        }

        /// <summary>Fire Resistance: burning doesn't hurt.</summary>
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer)), HarmonyPrefix, HarmonyPriority(Priority.High)]
        static bool FireProof(PlayerControllerB __instance, CauseOfDeath causeOfDeath)
        {
            if (__instance != Local || causeOfDeath != CauseOfDeath.Burning || Level(Effect.FireResistance) <= 0) return true;
            return false;
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
                l.intensity = nvIntensity * 6f;
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
