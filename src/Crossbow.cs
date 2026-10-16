using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// A crossbow (#223, the pillager's; Minecraft's CrossbowItem). Hold the use button to load it (1.25 s, Quick Charge a
    /// quarter second faster a level), Minecraft's loading sounds at a fifth and half of the way and as it locks; it then
    /// stays loaded, through the inventory, chests and a save, until the use button is pressed again: it fires (Multishot:
    /// three arrows, 10 degrees apart, for one; Piercing: through that many monsters). Its arrows fly a little faster
    /// than a fully drawn bow's (3.15 against 3) and hit as hard. 465 uses, worn a use an arrow. Crafted from sticks, an
    /// iron ingot, string and a tripwire hook; [Bows] PlayerBows off, there are none: no recipe, no drops, and one you
    /// have won't load.
    /// </summary>
    public class CrossbowItem : ToolItem
    {
        public CrossbowData.Load Loaded;
        /// <summary>Minecraft's 465 uses (times [Balance] DurabilityMultiplier; at most 2047, the bits it has).</summary>
        public override int MaxUses => Balance.ToolDurability ? Mathf.Clamp(Mathf.RoundToInt(465 * Balance.DurabilityMultiplier), 1, CrossbowData.MaxWear) : 0;
        public override int GetItemDataToSave() => CrossbowData.Data(Used, Loaded, Ench);
        public override void LoadItemSaveData(int saveData)
        {
            Used = CrossbowData.WearOf(saveData); Loaded = CrossbowData.LoadOf(saveData); Ench = CrossbowData.EnchOf(saveData);
            Glint.ApplyModel(this);
            Crossbow.ShowFrame(this, -1);
        }
        // (left click: no swing; the Builder breaks blocks with it at hand speed, as Minecraft's)
        public override void ItemActivate(bool used, bool buttonDown = true) { }
        public override void SetControlTipsForItem()
        {
            string use = Plugin.PlaceWithLeftClick.Value ? "[LMB]" : "[Right-click]";
            var tips = new[] { Loaded != CrossbowData.Load.None ? $"Shoot : {use}" : $"Load : hold {use}", Plugin.PlaceWithLeftClick.Value ? "" : "Break : hold [LMB]", itemProperties.itemName };
            HUDManager.Instance.ChangeControlTipMultiple(tips, holdingItem: true, itemProperties);
        }
    }

    public static class Crossbow
    {
        public const string Key = "crossbow";
        /// <summary>A crossbow arrow's speed: the bow's full draw times Minecraft's 3.15 / 3.</summary>
        public static float Speed => Bow.FullSpeed * 3.15f / 3f;
        /// <summary>A pillager's shot (Minecraft's MOB_ARROW_POWER, 1.6: the skeleton's).</summary>
        public const float MobSpeed = Arrow.Speed;
        public static float ChargeTime; // (the local player's: seconds the use button's been held while loading)
        public static int Shots, Loads; // (dev/tests: this machine's)
        static bool startPlayed, midPlayed, waitRelease;
        static int frame = -2;

        public static bool On => Bow.On;
        public static int QuickCharge(ToolItem t) => Enchants.Level(t.Ench, EnchKind.QuickCharge);

        /// <summary>Every frame on the holder's machine while a crossbow's in hand.</summary>
        public static void Hold(PlayerControllerB p, CrossbowItem cb, bool useHeld, bool useDown)
        {
            if (cb.Loaded != CrossbowData.Load.None)
            {
                // loaded: a fresh press fires (holding on from the load doesn't, as Minecraft's)
                ChargeTime = 0f;
                if (!useHeld) waitRelease = false;
                else if (useDown && !waitRelease) Fire(p, cb);
                ShowFrame(cb, -1);
                return;
            }
            if (!useHeld) { Stop(cb); waitRelease = false; return; }
            if (waitRelease) return;
            if (ChargeTime <= 0f)
            {
                // (Minecraft's use fails with nothing to load)
                if (!On) { if (useDown) Tip("Crossbows are turned off on this server."); return; }
                if (FindArrow(p) < 0 && !GameModes.IsCreative(p)) { if (useDown) Tip("No arrows."); return; }
                startPlayed = midPlayed = false;
            }
            ChargeTime += Time.deltaTime;
            int qc = QuickCharge(cb);
            float need = Mathf.Max(0.05f, CrossbowData.ChargeTicks(qc) / 20f), pull = ChargeTime / need;
            // Minecraft's sounds: the start at a fifth (Quick Charge's own), the middle at half, the lock when it's loaded
            if (pull >= 0.2f && !startPlayed) { startPlayed = true; Sounds.Play(qc > 0 ? "crossbow.quick" + Mathf.Min(qc, 3) : "crossbow.load_start", p.transform.position + Vector3.up, 0.5f, 1f, 12f); }
            if (pull >= 0.5f && !midPlayed && qc == 0) { midPlayed = true; Sounds.Play("crossbow.load_mid", p.transform.position + Vector3.up, 0.5f, 1f, 12f); }
            // (its pull frames: Minecraft's item model, 0 under 0.58, 1 under 1, then 2)
            ShowFrame(cb, pull < 0.58f ? 0 : pull < 1f ? 1 : 2);
            if (pull >= 1f) { Load(p, cb); waitRelease = true; ChargeTime = 0f; }
        }

        /// <summary>The crossbow left the hand (or was never there): any half-done load is lost.</summary>
        public static void Release() { ChargeTime = 0f; waitRelease = false; frame = -2; }

        static void Stop(CrossbowItem cb)
        {
            ChargeTime = 0f;
            ShowFrame(cb, -1);
        }

        /// <summary>The first arrow in the player's slots (the bow's order), or -1.</summary>
        static int FindArrow(PlayerControllerB p)
        {
            for (int i = 0; i < p.ItemSlots.Length; i++)
                if (p.ItemSlots[i] is StackItem s && System.Array.IndexOf(Bow.ArrowKeys, s.ItemKey) >= 0 && Inventory.CountIn(p, i) > 0) return i;
            return -1;
        }

        static void Load(PlayerControllerB p, CrossbowItem cb)
        {
            var load = CrossbowData.Load.Arrow;
            if (!GameModes.IsCreative(p))
            {
                int slot = FindArrow(p);
                if (slot < 0) { Tip("No arrows."); return; }
                load = CrossbowData.LoadFor(((StackItem)p.ItemSlots[slot]).ItemKey);
                Inventory.Take(p, slot, 1);
            }
            cb.Loaded = load; // (shown at once here; the host tells everyone)
            ShowFrame(cb, -1);
            cb.SetControlTipsForItem();
            BlockNet.RequestCrossbowLoad(cb.NetworkObjectId, (byte)load);
            Loads++;
        }

        static void Fire(PlayerControllerB p, CrossbowItem cb)
        {
            if (!On) { Tip("Crossbows are turned off on this server."); return; }
            var cam = p.gameplayCamera.transform;
            var from = cam.position + cam.forward * 0.5f + cam.right * 0.1f - cam.up * 0.1f;
            BlockNet.RequestCrossbowShot(cb.NetworkObjectId, from, cam.forward);
            cb.Loaded = CrossbowData.Load.None;
            ShowFrame(cb, -1);
            cb.SetControlTipsForItem();
            Shots++;
        }

        /// <summary>Its model: loaded (the arrow on it), a pull frame (0-2), or at rest (frame -1).</summary>
        public static void ShowFrame(CrossbowItem cb, int pull)
        {
            if (cb == null) return;
            string tile = cb.Loaded != CrossbowData.Load.None ? "item_crossbow_arrow" : pull >= 0 ? "item_crossbow_pulling_" + pull : "item_crossbow_standby";
            int f = cb.Loaded != CrossbowData.Load.None ? 3 : pull;
            if (cb.isHeld && cb.playerHeldBy == GameNetworkManager.Instance?.localPlayerController) { if (f == frame) return; frame = f; }
            var mf = cb.transform.Find("model")?.GetComponent<MeshFilter>();
            if (mf != null && Atlas.Tiles.ContainsKey(tile)) mf.sharedMesh = MeshBuilder.ExtrudedSprite(tile);
        }

        /// <summary>A loaded crossbow's icon (its key says so), or null: the item's own.</summary>
        public static Sprite IconFor(string key)
        {
            if (key == null || ItemData.Base(key) != Key) return null;
            return CrossbowData.LoadOf(ItemData.Of(key)) != CrossbowData.Load.None ? Atlas.IconFor("item_crossbow_arrow") : null;
        }

        static void Tip(string s) { try { HUDManager.Instance.DisplayTip("Crossbow", s, false, false, "LMC_Crossbow"); } catch { } }

        // ------------------------------------------------------------------ host
        /// <summary>Host: someone's crossbow locked an arrow in (their machine took it from their inventory).</summary>
        public static void ServerLoad(ulong sender, ulong itemId, byte load)
        {
            var cb = Held(sender, itemId);
            if (cb == null || !On || cb.Loaded != CrossbowData.Load.None || load == 0 || load > 3) return;
            cb.Loaded = (CrossbowData.Load)load;
            BlockNet.ServerItemData(cb);
            BlockNet.ServerSound(cb.transform.position, "crossbow.load_end", 1f / (Random.value * 0.5f + 1f) + 0.2f, 1f);
        }

        /// <summary>Host: someone fired their loaded crossbow along `dir` (their eyes'): its arrows, Minecraft's.</summary>
        public static void ServerShot(ulong sender, ulong itemId, Vector3 from, Vector3 dir)
        {
            var cb = Held(sender, itemId);
            if (cb == null || !On || cb.Loaded == CrossbowData.Load.None) return;
            var p = cb.playerHeldBy;
            if (Vector3.Distance(from, p.gameplayCamera.transform.position) > 2f) from = p.gameplayCamera.transform.position;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : p.gameplayCamera.transform.forward;
            var load = cb.Loaded;
            cb.Loaded = CrossbowData.Load.None;
            BlockNet.ServerItemData(cb);
            bool multi = Enchants.Level(cb.Ench, EnchKind.Multishot) > 0;
            int pierce = Enchants.Level(cb.Ench, EnchKind.Piercing);
            byte effect = (byte)(load == CrossbowData.Load.Slowness ? Potions.Effect.Slowness : load == CrossbowData.Load.Poison ? Potions.Effect.Poison : Potions.Effect.None);
            var angles = CrossbowData.ShotAngles(multi);
            // (turned about the shooter's own up, Minecraft's getUpVector: square to where they look)
            var right = Vector3.Cross(Vector3.up, dir); if (right.sqrMagnitude < 1e-6f) right = p.transform.right;
            var up = Vector3.Cross(dir, right.normalized);
            for (int i = 0; i < angles.Length; i++)
            {
                var d = Quaternion.AngleAxis(angles[i], up) * dir;
                d = (d + new Vector3(Gauss(), Gauss(), Gauss()) * Bow.Spread).normalized;
                // (Multishot's two extra arrows can't be picked up: Minecraft's are creative-only pickups)
                BlockNet.ServerArrow(from, d * Speed, sender, effect, Arrow.Flags(pierce, i > 0, true));
                BlockNet.ServerSound(from, "crossbow.shoot", i == 0 ? 1f : 1f / (Random.value * 0.5f + 1.8f) + ((i & 1) == 1 ? 0.63f : 0.43f), 1f);
            }
            if (!GameModes.IsCreative(sender)) cb.ServerUse(angles.Length);
        }

        static CrossbowItem Held(ulong sender, ulong itemId)
        {
            var pl = ServerLogic.PlayerFor(sender);
            if (pl == null || Unity.Netcode.NetworkManager.Singleton == null) return null;
            if (!Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(itemId, out var no)) return null;
            var cb = no.GetComponent<CrossbowItem>();
            return cb != null && cb.playerHeldBy == pl ? cb : null;
        }

        static float Gauss()
        {
            float u1 = 1f - Random.value, u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }
    }
}
