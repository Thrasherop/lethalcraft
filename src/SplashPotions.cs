using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244) Minecraft's splash potions, thrown by players: a potion brewed with gunpowder, thrown with the use button
    /// (Minecraft's 20 degrees up from where you look), breaking where it lands. Everyone within 4 blocks gets its effect,
    /// a share of it by how far off (Minecraft's 1 - distance / 4), the thrower too. Each machine flies its own copy and
    /// applies it to its own player (as a witch's potion is: WitchPotion).
    /// </summary>
    public class SplashPotion : MonoBehaviour
    {
        public const float Speed = 10f, Gravity = 14f, Life = 6f;
        public static int Thrown, Broken, Applied; // (dev/tests)
        public static float LastShare; // (dev/tests)
        Vector3 vel; float age; string key; ulong thrower;

        /// <summary>The local player's use button with one in hand.</summary>
        public static void Throw(PlayerControllerB p, string key)
        {
            if (!GameModes.LocalCreative && Inventory.Take(p, p.currentItemSlot, 1) == null) return;
            var cam = p.gameplayCamera.transform;
            var dir = Quaternion.AngleAxis(-20f, cam.right) * cam.forward;
            BlockNet.RequestSplash(cam.position + cam.forward * 0.5f, dir * Speed, ItemData.TypeOf(key));
            Thrown++;
        }

        public static void Launch(Vector3 from, Vector3 vel, string key, ulong thrower)
        {
            var go = new GameObject("LMC_SplashPotion"); go.transform.position = from;
            var s = go.AddComponent<SplashPotion>(); s.vel = vel; s.key = key; s.thrower = thrower;
            var model = new GameObject("model"); model.transform.SetParent(go.transform, false); model.transform.localScale = Vector3.one * 0.4f;
            string tile = "item_" + key;
            model.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey(tile) ? tile : "item_glass_bottle");
            var mr = model.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Cutout; Atlas.NoDecals(mr);
            Sounds.Play("snowball.throw", from, 0.6f, Random.Range(0.9f, 1.1f), 16f);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > Life) { Destroy(gameObject); return; }
            vel += Vector3.down * Gravity * Time.deltaTime;
            var p = transform.position; var step = vel * Time.deltaTime; float len = step.magnitude;
            transform.Rotate(Vector3.right, 600f * Time.deltaTime, Space.Self);
            // (a player in the way breaks it on them; not its thrower while it leaves their hand)
            foreach (var pl in StartOfRound.Instance.allPlayerScripts)
            {
                if (pl == null || !pl.isPlayerControlled || pl.isPlayerDead || (pl.actualClientId == thrower && age < 0.3f)) continue;
                if (Vector3.Distance(pl.transform.position + Vector3.up * 0.9f, p) < 0.6f) { Break(p, pl == GameNetworkManager.Instance?.localPlayerController); return; }
            }
            if (len > 1e-4f && Physics.Raycast(p, step / len, out var hit, len + 0.05f, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore))
            {
                Break(hit.point + hit.normal * 0.1f);
                return;
            }
            transform.position = p + step;
        }

        void Break(Vector3 at, bool direct = false)
        {
            Broken++;
            Sounds.Play("break.glass", at, 0.9f, Random.Range(0.9f, 1.1f), 20f);
            Potions.Defs.TryGetValue(key, out var d);
            SplashBits.Play(at, d != null ? (Color)d.Color : new Color(0.22f, 0.36f, 0.78f));
            var me = GameNetworkManager.Instance?.localPlayerController;
            if (d != null && me != null && !me.isPlayerDead)
            {
                // (Minecraft's: within 4 blocks of where it broke, measured to your feet; all of it when it broke on you)
                float dist = Vector3.Distance(me.transform.position, at) / BlockWorld.S;
                float share = direct ? 1f : 1f - dist / 4f;
                if (share > 0f) { Potions.Apply(d, share); Applied++; LastShare = share; }
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// (#244) Invisibility, the owner's rules for who it fools (2026-10-18): the giants (Forest Keepers) can't see you at
    /// all; the ones that hunt by sight see you only close by (bunker spiders, masked, the creeper, and Minecraft's mobs:
    /// Minecraft's 7% of their range, more with armor on); the rest (coil-heads, eyeless dogs, the clay surgeon, the
    /// jester...) find you as before. Other players see only what you hold and wear.
    /// </summary>
    [HarmonyPatch]
    public static class Invisibility
    {
        static readonly HashSet<ulong> invisible = new HashSet<ulong>();
        public static int Fooled; // (dev/tests)

        public static bool Of(PlayerControllerB p) => p != null && invisible.Contains(p.actualClientId);

        /// <summary>Every machine: a player's invisibility, from the host.</summary>
        public static void Set(ulong client, bool on)
        {
            if (on) invisible.Add(client); else invisible.Remove(client);
        }

        public static void Reset() => invisible.Clear();

        /// <summary>How close a sight-hunting monster has to be to see an invisible player: Minecraft's visibility, 7% of
        /// its range bare, up to 70% in a full set of armor (a quarter more a piece), out of a 50 m sight.</summary>
        public static float SeenWithin(PlayerControllerB p)
        {
            int pieces = Armor.Of(p.actualClientId)?.Count(k => k != null) ?? 0;
            return 50f * 0.7f * Mathf.Max(pieces / 4f, 0.1f);
        }

        enum Kind { Sees, Partly, Fooled }
        static Kind KindOf(EnemyAI e) =>
            e is ForestGiantAI ? Kind.Fooled
            : e is SandSpiderAI || e is MaskedPlayerEnemy || e is CreeperAI || e is MinecraftMobAI ? Kind.Partly
            : Kind.Sees;

        /// <summary>Is this player hidden from this monster right now?</summary>
        public static bool HiddenFrom(EnemyAI e, PlayerControllerB p)
        {
            if (e == null || !Of(p)) return false;
            switch (KindOf(e))
            {
                case Kind.Fooled: return true;
                case Kind.Partly: return Vector3.Distance(e.transform.position, p.transform.position) > SeenWithin(p);
                default: return false;
            }
        }

        [HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.PlayerIsTargetable)), HarmonyPrefix]
        static bool Targetable(EnemyAI __instance, PlayerControllerB playerScript, ref bool __result)
        {
            if (!HiddenFrom(__instance, playerScript)) return true;
            __result = false; Fooled++;
            return false;
        }

        /// <summary>Every frame: the invisible players' bodies (not the local one's own view) aren't drawn; what they hold
        /// and wear still is.</summary>
        public static void UpdateBodies()
        {
            var sor = StartOfRound.Instance;
            var me = GameNetworkManager.Instance?.localPlayerController;
            if (sor == null) return;
            foreach (var p in sor.allPlayerScripts)
            {
                if (p == null || p == me) continue;
                bool hide = Of(p) && !p.isPlayerDead;
                foreach (var r in new Renderer[] { p.thisPlayerModel, p.thisPlayerModelLOD1, p.thisPlayerModelLOD2 })
                    if (r != null && r.enabled == hide) r.enabled = !hide;
                if (p.usernameCanvas != null && p.usernameCanvas.gameObject.activeSelf == hide) p.usernameCanvas.gameObject.SetActive(!hide);
            }
        }
    }
}
