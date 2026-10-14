using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>The Totem of Undying in hand (one item, doesn't stack).</summary>
    public class TotemItem : GrabbableObject
    {
        void Awake() => SpawnFix.Clear(gameObject);
        public override void EquipItem() { base.EquipItem(); QKey.InHand(this, true); }
        public override void PocketItem() { base.PocketItem(); QKey.InHand(this, false); }
        public override void DiscardItem() { QKey.InHand(this, false); base.DiscardItem(); }
        public override void ItemInteractLeftRight(bool right)
        {
            base.ItemInteractLeftRight(right);
            if (!right && QKey.CanThrow(this)) playerHeldBy.DiscardHeldObject();
        }
    }

    /// <summary>
    /// Totem of Undying (#53): a store item. Anywhere in your hotbar when you would die, it's used up instead, with
    /// Minecraft's totem showing over your screen and its green and yellow particles around you for everyone to see.
    /// You stay where you are (#204, the owner: missing a jump and being saved but still stuck in the hole is very Lethal
    /// Company; being sent back to the ship isn't): full health and stamina, whatever had hold of you lets go (a bracken,
    /// a giant, a snare flea), poison and the like gone, and for 2 seconds nothing can hurt you and sprinting doesn't
    /// tire you, so the next tick's bite (a worm's) doesn't finish you. The host's TotemTeleportsToShip brings you back to
    /// the ship as well, as it used to.
    /// </summary>
    [HarmonyPatch]
    public static class Totem
    {
        public const string Key = "totem_of_undying";
        public static bool DevNoTotem; // (dev: "totem 0" makes deaths ignore the totem, to compare)
        /// <summary>The host's TotemTeleportsToShip (the rules message brings it to every client).</summary>
        public static bool HostToShip;
        /// <summary>How long after a save nothing can hurt you (and sprinting is free).</summary>
        public const float Invulnerable = 2f;
        public static bool Shielded => Time.time - savedAt < Invulnerable;
        /// <summary>
        /// When the last totem saved the local player. A kill can come several times in one moment (the Earth Leviathan's
        /// bite fires on every touching collider in a physics step, #76): the totem is gone after the first, so the rest
        /// killed the player anyway. Kills within this long after a save are the same death: absorbed.
        /// </summary>
        static float savedAt = -10f;

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.KillPlayer)), HarmonyPrefix, HarmonyPriority(Priority.High)]
        static bool Save(PlayerControllerB __instance, CauseOfDeath causeOfDeath)
        {
            var p = __instance;
            if (DevNoTotem || p == null || !p.IsOwner || p.isPlayerDead || !p.isPlayerControlled || p != GameNetworkManager.Instance?.localPlayerController) return true;
            if (GameModes.IsCreative(p)) return true;
            if (Shielded && !(thrown && causeOfDeath == CauseOfDeath.Gravity))
            {
                Plugin.Log.LogInfo($"Totem: absorbed a second kill of {p.playerUsername} ({causeOfDeath}) right after a save");
                return false;
            }
            int slot = -1;
            for (int i = 0; i < p.ItemSlots.Length; i++)
                if (Crafting.KeyOf(p.ItemSlots[i]) == Key) { slot = i; break; }
            if (slot < 0) return true;
            Plugin.Log.LogInfo($"Totem of Undying saved {p.playerUsername} ({causeOfDeath})");
            // used up
            p.DestroyItemInSlotAndSync(slot);
            savedAt = Time.time;
            Advancements.Grant("postmortal");
            // whatever had hold of you lets go (the monster too: it would keep you in its grab), full health, not bleeding
            try { p.CancelSpecialTriggerAnimations(); } catch { }
            try { if (p.inAnimationWithEnemy != null) p.inAnimationWithEnemy.CancelSpecialAnimationWithPlayer(); } catch { }
            p.inSpecialInteractAnimation = false;
            p.inAnimationWithEnemy = null;
            p.health = 100;
            if (p.criticallyInjured || p.bleedingHeavily) p.MakeCriticallyInjured(false);
            HUDManager.Instance.UpdateHealthUI(100, false);
            if (Survival.Instance != null) Survival.Instance.LastHealth = 100;
            Cleanse(p);
            var died = p.transform.position;
            thrown = false;
            // the worm's bite (#204, the owner): the totem saves you, but its momentum still throws you up and away; the
            // landing can still kill you
            if (Time.time - wormAt.t < 0.25f)
            {
                var away = Vector3.ProjectOnPlane(died - wormAt.pos, Vector3.up);
                if (away.sqrMagnitude < 0.01f) away = Vector3.ProjectOnPlane(-p.transform.forward, Vector3.up);
                // (out of its body first: it fills the space over where it bit you, and held you down)
                float moved = 0f;
                foreach (float d in new[] { 6f, 8f, 10f })
                {
                    var clear = died + away.normalized * d + Vector3.up * 1.5f;
                    if (Physics.CheckCapsule(clear, clear + Vector3.up * 1.8f, 0.4f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) continue;
                    p.TeleportPlayer(clear); moved = d; break;
                }
                Plugin.Log.LogInfo($"Totem: out of the worm's way {moved} m");
                p.externalForceAutoFade += Vector3.up * LaunchUp + away.normalized * LaunchOut;
                thrown = true;
                p.fallValue = 0f; p.fallValueUncapped = 0f;
                Plugin.Log.LogInfo($"Totem: thrown by the worm ({LaunchUp} up, {LaunchOut} out)");
            }
            var arrive = died;
            if (BlockNet.IsServer ? Plugin.TotemToShip.Value : HostToShip)
            {
                // back to the ship (the burst goes off where you were and where you arrive, for everyone)
                arrive = StartOfRound.Instance.GetPlayerSpawnPosition((int)p.playerClientId);
                Commands.TeleportLocal(arrive, false, true, true);
            }
            Overlay.Show();
            BlockNet.RequestTotemPop(died, arrive);
            return false;
        }

        /// <summary>What was hurting you stops (#204): a snare flea on you dies, the game's poison, drunkenness and quicksand
        /// go, and ours (slowness, poison, weakness, hunger); stamina full.</summary>
        static void Cleanse(PlayerControllerB p)
        {
            try
            {
                // (a snare flea on your face dies, as it does when you carry it out of the facility; the owner's ask)
                foreach (var c in Object.FindObjectsOfType<CentipedeAI>())
                    if (c != null && !c.isEnemyDead && c.clingingToPlayer == p) { if (c.IsOwner) c.KillEnemyOnOwnerClient(); else c.KillEnemyServerRpc(false); }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Totem: snare flea: " + e.Message); }
            p.drunkness = 0f; p.drunknessInertia = 1f;
            p.poison = 0f;
            p.sinkingValue = 0f;
            Potions.ClearHarmful();
            p.sprintMeter = 1f; p.isExhausted = false;
        }

        // ------------------------------------------------------------------ the worm
        /// <summary>The worm's throw (the game's external force, as an explosion's: up, and away from the worm).</summary>
        public static float LaunchUp = 130f, LaunchOut = 20f;
        static (Vector3 pos, float t) wormAt = (Vector3.zero, -10f);

        [HarmonyPatch(typeof(SandWormAI), nameof(SandWormAI.EatPlayer)), HarmonyPrefix]
        static void WormBites(SandWormAI __instance, PlayerControllerB playerScript)
        {
            if (playerScript == GameNetworkManager.Instance?.localPlayerController) wormAt = (__instance.transform.position, Time.time);
        }

        /// <summary>Dev: as if a worm under you (a little behind) bit you now.</summary>
        public static void DevWorm()
        {
            var p = GameNetworkManager.Instance.localPlayerController;
            wormAt = (p.transform.position - p.transform.forward * 2f - Vector3.up * 2f, Time.time);
            p.KillPlayer(Vector3.zero, spawnBody: false);
        }

        // ------------------------------------------------------------------ a snare flea's grab
        // (A snare flea makes you drop everything as it grabs you (CentipedeAI.ClingToPlayer), so a totem was on the floor
        // by the time the flea killed you and never saved you from one. It stays where it was, in hand or pocketed; the
        // rest drops as the game has it. Every client runs the grab, so every client keeps it.)
        static PlayerControllerB fleaGrab;
        static GrabbableObject keptTotem; static int keptSlot; static bool keptHeld;

        [HarmonyPatch(typeof(CentipedeAI), "ClingToPlayer"), HarmonyPrefix]
        static void FleaGrabs(PlayerControllerB playerScript) => fleaGrab = playerScript;
        [HarmonyPatch(typeof(CentipedeAI), "ClingToPlayer"), HarmonyPostfix]
        static void FleaGrabbed() => fleaGrab = null;

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DropAllHeldItems)), HarmonyPrefix]
        static void KeepTotem(PlayerControllerB __instance)
        {
            keptTotem = null;
            if (fleaGrab == null || __instance != fleaGrab) return;
            for (int i = 0; i < __instance.ItemSlots.Length; i++)
                if (Crafting.KeyOf(__instance.ItemSlots[i]) == Key)
                {
                    keptTotem = __instance.ItemSlots[i]; keptSlot = i; keptHeld = __instance.currentlyHeldObjectServer == keptTotem;
                    __instance.ItemSlots[i] = null; // (out of the game's way while it drops the rest)
                    if (keptHeld) __instance.currentlyHeldObjectServer = null;
                    return;
                }
        }

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DropAllHeldItems)), HarmonyPostfix]
        static void KeptTotem(PlayerControllerB __instance)
        {
            if (keptTotem == null || __instance != fleaGrab) return;
            __instance.ItemSlots[keptSlot] = keptTotem;
            if (keptHeld) { __instance.currentlyHeldObjectServer = keptTotem; __instance.isHoldingObject = true; }
            __instance.carryWeight = Mathf.Clamp(__instance.carryWeight + (keptTotem.itemProperties.weight - 1f), 1f, 10f);
            if (__instance.IsOwner && keptTotem.itemProperties.itemIcon != null)
            {
                HUDManager.Instance.itemSlotIcons[keptSlot].sprite = keptTotem.itemProperties.itemIcon;
                HUDManager.Instance.itemSlotIcons[keptSlot].enabled = true;
            }
            keptTotem = null;
        }

        /// <summary>Nothing hurts you for a moment after a save (the game's damage, falls, our fire and lava, mobs).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer)), HarmonyPrefix, HarmonyPriority(Priority.High)]
        static bool NoDamage(PlayerControllerB __instance, CauseOfDeath causeOfDeath) =>
            !(Shielded && __instance.IsOwner && __instance == GameNetworkManager.Instance?.localPlayerController && !(thrown && causeOfDeath == CauseOfDeath.Gravity));

        /// <summary>The worm threw you: the landing still counts (the shield keeps off the worm, not the ground).</summary>
        static bool thrown;

        /// <summary>...and running doesn't tire you.</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix]
        static void FreeSprint(PlayerControllerB __instance)
        {
            if (!Shielded || __instance != GameNetworkManager.Instance?.localPlayerController) return;
            __instance.sprintMeter = 1f; __instance.isExhausted = false;
        }

        /// <summary>Dev: the effect alone (overlay and burst), to look at.</summary>
        public static string DevFx()
        {
            var p = GameNetworkManager.Instance.localPlayerController;
            Overlay.Show();
            Pop((int)p.playerClientId, p.transform.position + p.transform.forward * 4f, p.transform.position);
            return "icon=" + (Atlas.IconFor("item_" + Key) != null);
        }

        /// <summary>
        /// Every client: the green and yellow burst and Minecraft's totem sound, both where the player was about to die and
        /// where they arrive in the ship (positions from their own client, so nobody sees it in the wrong place mid-teleport).
        /// </summary>
        public static void Pop(int playerIdx, Vector3 died, Vector3 arrive)
        {
            var sor = StartOfRound.Instance;
            if (sor == null || playerIdx < 0 || playerIdx >= sor.allPlayerScripts.Length) return;
            var p = sor.allPlayerScripts[playerIdx];
            bool self = p != null && p == GameNetworkManager.Instance?.localPlayerController;
            LastPops.Add((died, arrive));
            if (LastPops.Count > 8) LastPops.RemoveAt(0);
            // (one burst when you stayed where you were: around you)
            var where = Vector3.Distance(died, arrive) < 0.5f ? new[] { (died + Vector3.up * 1.1f, self) } : new[] { (died + Vector3.up * 1.1f, false), (arrive + Vector3.up * 1.1f, self) };
            foreach (var (at, mine) in where)
            {
                Sounds.Play("totem", at, 1f, 1f);
                // (your own, where you arrive: around you, not in your face)
                Particles(at, mine);
            }
        }

        /// <summary>(dev/tests) where the last bursts went off.</summary>
        public static readonly List<(Vector3 died, Vector3 arrive)> LastPops = new List<(Vector3, Vector3)>();

        static Material green, yellow;

        static void Particles(Vector3 at, bool self)
        {
            if (green == null)
            {
                green = Glow("LMC_TotemGreen", new Color(0.35f, 1f, 0.2f));
                yellow = Glow("LMC_TotemYellow", new Color(1f, 0.85f, 0.15f));
            }
            var mesh = Quad();
            for (int i = 0; i < 70; i++)
            {
                var go = new GameObject("LMC_TotemSpark");
                var off = Random.onUnitSphere; if (self) off.y = Mathf.Abs(off.y) * 0.3f - 0.4f;
                go.transform.position = at + off * (self ? Random.Range(0.9f, 1.4f) : Random.Range(0f, 0.4f));
                go.transform.localScale = Vector3.one * (self ? Random.Range(0.03f, 0.07f) : Random.Range(0.06f, 0.14f));
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Random.value < 0.6f ? green : yellow;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var s = go.AddComponent<Spark>();
                var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 1.4f + 0.3f;
                s.Velocity = dir.normalized * Random.Range(2.5f, 6f);
                s.Life = Random.Range(1.2f, 2.4f);
            }
        }

        static Material Glow(string name, Color c)
        {
            var m = new Material(Shader.Find("HDRP/Unlit")) { name = name };
            m.SetColor("_UnlitColor", c);
            m.SetColor("_BaseColor", c);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m);
            return m;
        }

        static Mesh quad;
        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "LMC_TotemQuad" };
            quad.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
            quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }, 0); // (both sides)
            quad.RecalculateBounds();
            return quad;
        }

        /// <summary>A spark: drifts out, slows, falls a little, faces the camera, shrinks away.</summary>
        class Spark : MonoBehaviour
        {
            public Vector3 Velocity;
            public float Life;
            float age;
            Vector3 size;
            void Start() => size = transform.localScale;
            void Update()
            {
                age += Time.deltaTime;
                if (age >= Life) { Destroy(gameObject); return; }
                Velocity *= 1f - 2.2f * Time.deltaTime;
                Velocity += Vector3.down * 0.6f * Time.deltaTime;
                transform.position += Velocity * Time.deltaTime;
                var cam = Camera.main != null ? Camera.main.transform : null;
                if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
                transform.localScale = size * (1f - age / Life);
            }
        }

        /// <summary>The totem over your screen: it springs up big in the middle, turns, and falls away (local player).</summary>
        class Overlay : MonoBehaviour
        {
            RectTransform img;
            float t;

            public static void Show()
            {
                var sprite = Atlas.IconFor("item_" + Key);
                if (sprite == null) return;
                var go = new GameObject("LMC_TotemOverlay");
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 500;
                var ig = new GameObject("totem");
                ig.transform.SetParent(go.transform, false);
                var image = ig.AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                var o = go.AddComponent<Overlay>();
                o.img = ig.GetComponent<RectTransform>();
                o.img.sizeDelta = new Vector2(Screen.height * 0.5f, Screen.height * 0.5f);
            }

            void Update()
            {
                t += Time.deltaTime;
                const float total = 1.8f;
                if (t >= total) { Destroy(gameObject); return; }
                // a quick overshoot up to full size, a wobble, then it drops and shrinks away
                float up = Mathf.Clamp01(t / 0.35f), s = up < 1f ? Mathf.SmoothStep(0.2f, 1.15f, up) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((t - 0.35f) / 0.2f));
                float away = Mathf.Clamp01((t - 1.1f) / 0.7f);
                img.localScale = Vector3.one * s * (1f - away * 0.6f);
                img.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 9f) * 12f * (1f - up * 0.5f));
                img.anchoredPosition = new Vector2(0, -away * away * Screen.height * 0.9f);
            }
        }
    }
}
