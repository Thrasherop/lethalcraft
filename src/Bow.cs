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
        /// the move input times UseEffects' 0.2): a bow drawn, eating. A push against the walk, as Slowness's: four fifths
        /// of the walk the game is about to make (#213: it was four fifths of a full-speed walk, more than a walk carrying
        /// anything, sideways or just starting, so you went the other way).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPrefix]
        static void UseSlowdown(PlayerControllerB __instance)
        {
            var p = __instance;
            var h = p.isHoldingObject ? p.currentlyHeldObjectServer : null;
            bool drawing = DrawTime > 0f && h is StackItem hs && hs.ItemKey == Key, loading = Crossbow.ChargeTime > 0f && h is CrossbowItem;
            if (p != GameNetworkManager.Instance?.localPlayerController || !(drawing || loading || Survival.EatingNow || Potions.Drinking || Spyglass.Scoping)) return; // (#229: a spyglass up too; #244: drinking)
            if (!p.thisController.isGrounded) return;
            // (Minecraft stops the sprint while an item's in use: the game's sprint factor held at a walk's)
            p.sprintMultiplier = 1f;
            p.externalForces += WalkOf(p) * (0.2f - 1f);
        }

        /// <summary>The walk the game moves the player by this frame (PlayerControllerB.Update: walkForce, eased towards the
        /// input, times movementSpeed / carryWeight, times the sprint factor), flat.</summary>
        public static Vector3 WalkOf(PlayerControllerB p)
        {
            var w = p.walkForce * (p.movementSpeed / Mathf.Max(1f, p.carryWeight)) * p.sprintMultiplier;
            w.y = 0f;
            return w;
        }

        public const string Key = "bow", ArrowKey = "arrow";
        /// <summary>What the bow shoots, and what each does (#136: the stray's and bogged's tipped arrows).</summary>
        public static readonly string[] ArrowKeys = { ArrowKey, "arrow_slowness", "arrow_poison" };
        static Potions.Effect EffectOf(string key) => (key = ItemData.TypeOf(key)) == "arrow_slowness" ? Potions.Effect.Slowness : key == "arrow_poison" ? Potions.Effect.Poison : Potions.Effect.None;
        public const float FullDraw = 1f, MinPower = 0.1f, FullSpeed = 45f, Gravity = 12f;
        /// <summary>Minecraft's inaccuracy for a player's bow (Projectile.shoot: a Gaussian 0.0172275 on each axis).</summary>
        public const float Spread = 0.0172275f;
        public static float DrawTime; // (the local player's draw: seconds held)
        /// <summary>The drawn bow's place in the hand (the item's own space: x to the right, y up). Dev-tunable.</summary>
        public static Vector3 DrawPos = new Vector3(-0.2f, 0.08f, 0f), DrawRot = new Vector3(0f, -35f, 0f);
        public static int Shots; // (dev/tests: arrows this machine shot)
        /// <summary>(dev: override the host's [Bows] settings for this session without touching the config file; -1 = as configured)</summary>
        public static int DevOn = -1, DevRecover = -1;
        public static bool On => DevOn >= 0 ? DevOn == 1 : HostOn ?? Plugin.PlayerBows.Value;
        /// <summary>(#226) The host's [Bows] PlayerBows, on a client (null on the host, and before the host says): the
        /// host decides for everyone.</summary>
        public static bool? HostOn;
        static bool storeRemoved;

        /// <summary>(#226) Bows on or off for this game: off, the bow leaves this machine's store (it's in every machine's
        /// list, so the lists stay the same; LethalLib takes only its terminal words away). Host: its own setting, at
        /// start; client: the host's, as it joins.</summary>
        public static void ApplyHost(bool on, bool fromHost)
        {
            if (fromHost) HostOn = on;
            if (on || storeRemoved || !ModItems.ByKey.TryGetValue(Key, out var bow)) return;
            try { LethalLib.Modules.Items.RemoveShopItem(bow); storeRemoved = true; Plugin.Log.LogInfo("[bows] off on this game: the bow isn't sold"); }
            catch (System.Exception e) { Plugin.Log.LogWarning("[bows] store: " + e.Message); }
        }

        public static void ResetSession() { HostOn = null; storeRemoved = false; }
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
            // drawn, the bow comes in toward the middle and turns, as Minecraft's (ItemInHandRenderer, a bow in use: in by
            // 0.28 and up 0.18, turned 35 degrees; quickly, as the arm comes up)
            var mt = bow.GetComponentInChildren<MeshFilter>()?.transform;
            if (mt != null)
            {
                float t = DrawTime > 0f ? Mathf.Clamp01(DrawTime / 0.15f) : 0f;
                mt.localPosition = DrawPos * t;
                mt.localRotation = Quaternion.Euler(DrawRot * t);
                posed = mt;
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

        static Transform posed; // (the bow last held: put back as it was when it leaves the hand)
        public static void Release()
        {
            DrawTime = 0f; frame = -1;
            if (posed != null) { posed.localPosition = Vector3.zero; posed.localRotation = Quaternion.identity; posed = null; }
        }

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
