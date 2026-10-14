using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// A bow (owner's request): hold the use button to draw it (a second for a full draw, Minecraft's), let go to shoot an
    /// arrow from your inventory. A hit does what one shovel hit does, to a monster or a player. Arrows fly with
    /// Minecraft's spread and drop; one that hits a wall can be picked back up where it stuck when [Bows]
    /// ArrowsRecoverable is on. Bows are 200 in the store (or a crafting recipe: three sticks, three string); arrows are
    /// crafted (flint, a stick and a feather: four) or, with [Bows] ArrowsInStore, 8 for 850. [Bows] PlayerBows off:
    /// no bows at all.
    /// </summary>
    [HarmonyPatch]
    public static class Bow
    {
        /// <summary>(#190) Using an item slows you to Minecraft's fifth of a walk, and no sprinting (LocalPlayer.modifyInput:
        /// the move input times UseEffects' 0.2): a bow drawn, eating. A push against the walk, as Slowness's.</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPrefix]
        static void UseSlowdown(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || !(DrawTime > 0f || Survival.EatingNow)) return;
            if (!p.thisController.isGrounded) return;
            var mv = p.moveInputVector;
            if (mv.sqrMagnitude < 0.01f) return;
            var dir = p.transform.right * mv.x + p.transform.forward * mv.y; dir.y = 0f;
            // (Minecraft stops the sprint while an item's in use: the game's sprint factor held at a walk's)
            p.sprintMultiplier = 1f;
            p.externalForces += Vector3.ClampMagnitude(dir, 1f) * p.movementSpeed * (0.2f - 1f);
        }

        public const string Key = "bow", ArrowKey = "arrow";
        /// <summary>What the bow shoots, and what each does (#136: the stray's and bogged's tipped arrows).</summary>
        public static readonly string[] ArrowKeys = { ArrowKey, "arrow_slowness", "arrow_poison" };
        static Potions.Effect EffectOf(string key) => key == "arrow_slowness" ? Potions.Effect.Slowness : key == "arrow_poison" ? Potions.Effect.Poison : Potions.Effect.None;
        public const float FullDraw = 1f, MinPower = 0.1f, FullSpeed = 45f, Gravity = 12f;
        /// <summary>Minecraft's inaccuracy for a player's bow (Projectile.shoot: a Gaussian 0.0172275 on each axis).</summary>
        public const float Spread = 0.0172275f;
        public static float DrawTime; // (the local player's draw: seconds held)
        public static int Shots; // (dev/tests: arrows this machine shot)
        /// <summary>(dev: override the host's [Bows] settings for this session without touching the config file; -1 = as configured)</summary>
        public static int DevOn = -1, DevRecover = -1;
        public static bool On => DevOn >= 0 ? DevOn == 1 : Plugin.PlayerBows.Value;
        public static bool Recover => DevRecover >= 0 ? DevRecover == 1 : Plugin.ArrowsRecoverable.Value;
        static int frame = -1;

        /// <summary>Minecraft's draw curve (BowItem.getPowerForTime): how much of a full shot after t seconds.</summary>
        public static float Power(float t) { float f = t / FullDraw; f = (f * f + f * 2f) / 3f; return Mathf.Min(1f, f); }

        /// <summary>Every frame on the holder's machine while a bow is in hand: drawing while the use button is down,
        /// shooting as it comes up.</summary>
        public static void Hold(PlayerControllerB p, StackItem bow, bool useHeld)
        {
            if (useHeld) DrawTime += Time.deltaTime;
            else if (DrawTime > 0f)
            {
                float power = Power(DrawTime);
                DrawTime = 0f;
                if (power >= MinPower) Shoot(p, power);
            }
            // the bow in hand bends as you draw (item/bow_pulling_0-2)
            int f = DrawTime <= 0f ? -1 : DrawTime < 0.65f ? 0 : DrawTime < 0.9f ? 1 : 2;
            if (f != frame)
            {
                frame = f;
                string tile = f < 0 ? "item_bow" : "item_bow_pulling_" + f;
                var mf = bow.GetComponentInChildren<MeshFilter>();
                if (mf != null && Atlas.Tiles.ContainsKey(tile)) mf.sharedMesh = MeshBuilder.ExtrudedSprite(tile);
            }
        }

        public static void Release() { DrawTime = 0f; frame = -1; }

        static void Shoot(PlayerControllerB p, float power)
        {
            if (!On) { HudTip("Bows are turned off on this server."); return; }
            var effect = Potions.Effect.None;
            if (!GameModes.IsCreative(p))
            {
                int slot = -1;
                for (int i = 0; i < p.ItemSlots.Length; i++)
                    if (p.ItemSlots[i] is StackItem s && System.Array.IndexOf(ArrowKeys, s.ItemKey) >= 0 && Inventory.CountIn(p, i) > 0) { slot = i; break; }
                if (slot < 0) { HudTip("No arrows."); return; }
                effect = EffectOf(((StackItem)p.ItemSlots[slot]).ItemKey);
                Inventory.Take(p, slot, 1);
            }
            var cam = p.gameplayCamera.transform;
            var dir = cam.forward;
            // Minecraft's spread: a little random off the line on each axis
            dir = (dir + new Vector3(Gauss(), Gauss(), Gauss()) * Spread).normalized;
            var from = cam.position + cam.forward * 0.5f + cam.right * 0.1f - cam.up * 0.1f;
            BlockNet.RequestBowShot(from, dir * FullSpeed * power, (byte)effect);
            Shots++;
        }

        static float Gauss()
        {
            float u1 = 1f - Random.value, u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        static void HudTip(string s) { try { HUDManager.Instance.DisplayTip("Bow", s, false, false, "LMC_Bow"); } catch { } }

        /// <summary>Host: someone shot. (Their own machine took the arrow out of their inventory.)</summary>
        public static void ServerShot(ulong sender, Vector3 from, Vector3 vel, byte effect = 0)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || !On) return;
            if (!(p.currentlyHeldObjectServer is StackItem st) || st.ItemKey != Key) return;
            if (vel.magnitude > FullSpeed * 1.05f) vel = vel.normalized * FullSpeed;
            BlockNet.ServerArrow(from, vel, sender, effect);
        }
    }
}
