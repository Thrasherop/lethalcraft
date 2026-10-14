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
        public enum Effect : byte { None, Swiftness, Strength, FireResistance, NightVision }

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
        }

        // ------------------------------------------------------------------ the effects themselves
        /// <summary>Swiftness: before the game moves you, a push along your walk (Minecraft's +20% a level).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPrefix]
        static void Swift(PlayerControllerB __instance)
        {
            int lvl = Level(Effect.Swiftness);
            if (lvl <= 0 || __instance != Local || !__instance.thisController.isGrounded) return;
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
        public static float StrengthBonus => Level(Effect.Strength) * 0.6f;

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
