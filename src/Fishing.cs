using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Fishing, Minecraft's way (#96: cats want fish). A fishing rod (three sticks and two string) casts a bobber with the
    /// use button; landed on water (a moon's lake or river, or placed water), after 5-30 s something bites: the bobber
    /// dips with a splash, and the use button within a second reels it in. Otherwise the use button just reels the line
    /// back. Catches as in Minecraft: mostly cod and salmon, now and then a pufferfish, a tropical fish, junk or a treasure.
    /// (#244) Minecraft 26.3's tables and weights (gameplay/fishing); the bobber hooks a mob or an item it flies into, and
    /// reeling pulls the mob toward you or brings the item to your feet; the rod wears as Minecraft's (a catch 1, the
    /// ground 2, an item 3, a mob 5).
    /// The furnace cooks cod and salmon. Every machine draws every bobber and line from the cast; the host times the
    /// bite and hands out the catch.
    /// </summary>
    public static class Fishing
    {
        public const string RodKey = "fishing_rod";
        public const float CastSpeed = 11f, Reach = 32f, BiteWindow = 1.2f;
        static readonly Dictionary<ulong, Bobber> bobbers = new Dictionary<ulong, Bobber>();
        public static int Catches, Hooked, Pulled; public static string LastCatch = "-"; // (dev/tests)

        public static bool Out(ulong client) => bobbers.TryGetValue(client, out var b) && b != null;
        public static Bobber Of(ulong client) => bobbers.TryGetValue(client, out var b) ? b : null;

        /// <summary>The rod's use button, on the holder's machine: cast, or reel in.</summary>
        public static void Use(PlayerControllerB p)
        {
            if (Out(p.actualClientId)) { BlockNet.RequestFish(1, Vector3.zero, Vector3.zero); return; }
            var cam = p.gameplayCamera.transform;
            var from = cam.position + cam.forward * 0.6f + Vector3.down * 0.2f;
            // (an arc out over the water, as Minecraft's: even looking down, the bobber goes out a few metres first)
            var dir = cam.forward; dir.y = Mathf.Max(dir.y, -0.15f);
            BlockNet.RequestFish(0, from, dir.normalized * CastSpeed + Vector3.up * 3.5f);
        }

        // ------------------------------------------------------------------ every machine
        public static void Receive(ulong client, byte kind, Vector3 from, Vector3 vel)
        {
            if (kind == 0)
            {
                Remove(client);
                var go = new GameObject("LMC_Bobber");
                var b = go.AddComponent<Bobber>();
                b.Begin(client, from, vel);
                bobbers[client] = b;
                Sounds.Play("fish.cast", from, 1f, Random.Range(0.8f, 1.1f), 16f);
            }
            else if (kind == 1) { var b = Of(client); if (b != null) b.Bite(); }
            else Remove(client);
        }

        /// <summary>(#244) Every machine: the host's word that a bobber caught on a mob or an item (its network id).</summary>
        public static void ReceiveHook(ulong client, ulong target)
        {
            var b = Of(client);
            if (b == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(target, out var no)) return;
            b.HookOn(no.GetComponent<EnemyAI>(), no.GetComponent<GrabbableObject>());
        }

        static void Remove(ulong client)
        {
            if (bobbers.TryGetValue(client, out var b) && b != null) Object.Destroy(b.gameObject);
            bobbers.Remove(client);
        }

        public static void ResetAll() { foreach (var b in bobbers.Values) if (b != null) Object.Destroy(b.gameObject); bobbers.Clear(); }

        // ------------------------------------------------------------------ the host
        public static void ServerRequest(ulong sender, byte kind, Vector3 from, Vector3 vel)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null) return;
            if (kind == 0)
            {
                if (!(p.currentlyHeldObjectServer is RodItem rod)) return;
                // (#244) the rod's Lure and Luck of the Sea, for this cast
                Lure[sender] = Enchants.Level(rod.Ench, EnchKind.Lure); Luck[sender] = Enchants.Level(rod.Ench, EnchKind.LuckOfTheSea);
                BlockNet.ServerFish(sender, 0, from, vel);
                return;
            }
            // reeling in: a fish on the line? it's theirs
            var b = Of(sender);
            int wear = 0;
            if (b != null && b.HookedMob != null && !b.HookedMob.isEnemyDead)
            {
                // (#244) Minecraft's pullEntity: a tenth of the way to you a tick, as a push (about a fifth of the distance)
                var d = p.transform.position - b.HookedMob.transform.position;
                MobEffects.Pull(b.HookedMob, d, d.magnitude / BlockWorld.S * 0.22f);
                wear = 5; Pulled++;
            }
            else if (b != null && b.HookedItem != null && !b.HookedItem.isHeld && !b.HookedItem.isPocketed)
            {
                // (#244) an item: Minecraft's pull throws it to about your feet
                var to = p.transform.position + p.transform.forward * 0.6f + Vector3.up * 0.5f;
                BlockNet.ServerItemMoved(b.HookedItem, to);
                wear = 3; Pulled++;
            }
            else if (b != null && b.Biting)
            {
                var (key, n) = Catch(Luck.TryGetValue(sender, out int lk) ? lk : 0);
                wear = 1;
                var at = Vector3.Lerp(b.transform.position, p.transform.position + Vector3.up, 0.85f);
                if (ItemData.Of(key) > 0) for (int i = 0; i < n; i++) ModItems.ServerSpawnPlainKeyedOrStack(key, at); // (worn, enchanted: one each)
                else if (ModItems.ByKey.TryGetValue(key, out var item)) ModItems.ServerSpawnStack(item, n, at);
                BlockNet.ServerXp(sender, Random.Range(1, 7), p.transform.position + Vector3.up * 0.5f);
                Catches++; LastCatch = key;
                Plugin.Log.LogInfo($"[fishing] {p.playerUsername} caught {key}");
            }
            else if (b != null && b.Grounded) wear = 2;
            if (wear > 0 && p.currentlyHeldObjectServer is RodItem worn && !GameModes.IsCreative(sender)) worn.ServerUse(wear); // (#244: Minecraft's wear)
            BlockNet.ServerFish(sender, 2, Vector3.zero, Vector3.zero);
        }

        /// <summary>Host, as a bobber of theirs bites (or its fish gets away): told to everyone.</summary>
        public static void ServerBite(ulong client) => BlockNet.ServerFish(client, 1, Vector3.zero, Vector3.zero);

        /// <summary>(#244) The rod's enchantments, a caster's: Lure (bites 5 s sooner a level), Luck of the Sea.</summary>
        public static readonly Dictionary<ulong, int> Lure = new Dictionary<ulong, int>(), Luck = new Dictionary<ulong, int>();
        /// <summary>Host: seconds until a bite (Minecraft's 5-30, less 5 a Lure level).</summary>
        public static float WaitFor(ulong client) => Mathf.Max(1f, Random.Range(5f, 30f) - 5f * (Lure.TryGetValue(client, out int l) ? l : 0));

        // (#244) Minecraft 26.3's tables (loot_table/gameplay/fishing): item, weight. What the mod doesn't have is left out
        // (the lily pad, the nautilus shell; bamboo, a jungle's only), the rest keep their weights.
        static readonly (string key, int w)[] FishTable = { ("raw_cod", 60), ("raw_salmon", 25), ("tropical_fish", 2), ("pufferfish", 13) };
        static readonly (string key, int w)[] JunkTable = { ("lily_pad", 17), ("leather_boots", 10), ("leather", 10), ("bone", 10), ("potion_water", 10), ("string", 5), (RodKey, 2), ("bowl", 10), ("stick", 5), ("ink_sac", 1), ("tripwire_hook", 10), ("rotten_flesh", 10) };
        static readonly (string key, int w)[] TreasureTable = { (NameTags.Key, 1), ("saddle", 1), (Bow.Key, 1), (RodKey, 1), ("book", 1), ("nautilus_shell", 1) };

        static string Pick((string key, int w)[] table)
        {
            var here = table.Where(e => ModItems.ByKey.ContainsKey(e.key)).ToArray();
            int total = here.Sum(e => e.w), r = Random.Range(0, Mathf.Max(1, total));
            foreach (var e in here) { r -= e.w; if (r < 0) return e.key; }
            return here.Length > 0 ? here[here.Length - 1].key : "raw_cod";
        }

        /// <summary>Minecraft's catch: fish 85%, junk 10%, treasure 5%; Luck of the Sea (its quality) takes one off the
        /// fish's weight and two off the junk's and puts two on the treasure's, a level. Junk boots and rods come worn (up to
        /// 90%), treasure bows and rods worn a little (up to 25%) and enchanted as at a level-30 table, treasure enchantments
        /// too (enchant_with_levels 30, on_random_loot), and a book enchanted that way.</summary>
        public static (string key, int n) Catch(int luck = 0)
        {
            float wf = 85 - luck, wj = Mathf.Max(0, 10 - 2 * luck), wt = 5 + 2 * luck;
            float r = Random.value * (wf + wj + wt);
            if (r < wf) return (Pick(FishTable), 1);
            if (r < wf + wj)
            {
                string j = Pick(JunkTable);
                if (j == "ink_sac") return (j, 10);
                if (j == "leather_boots") return (ArmorWear.WithDamage(j, Mathf.RoundToInt(Random.value * 0.9f * ArmorWear.Max(j))), 1);
                if (j == RodKey) return (Worn(j, 0.9f, false), 1);
                return (j, 1);
            }
            string t = Pick(TreasureTable);
            if (t == Bow.Key || t == RodKey) return (Worn(t, 0.25f, true), 1);
            if (t == "book")
            {
                var list = Enchants.Select(new System.Random(Random.Range(0, int.MaxValue)), EnchTarget.Book, 1, 30, treasure: true);
                int e = list.Count > 0 ? Enchants.Pack(list) : 0;
                return (e > 0 ? ItemData.With(AnvilLogic.Book, Enchants.Data(0, e)) : "book", 1);
            }
            return (t, 1);
        }

        /// <summary>A bow or rod, worn up to that much of its uses, enchanted at level 30 (treasure ones too) if asked.</summary>
        static string Worn(string key, float upTo, bool enchant)
        {
            int max = key == Bow.Key ? 384 : 64;
            if (ModItems.ByKey.TryGetValue(key, out var it) && it.spawnPrefab != null && it.spawnPrefab.GetComponent<ToolItem>() is ToolItem ti && ti.MaxUses > 0) max = ti.MaxUses;
            int used = Mathf.RoundToInt(Random.value * upTo * max);
            int e = 0;
            if (enchant)
            {
                var list = Enchants.Select(new System.Random(Random.Range(0, int.MaxValue)), key == Bow.Key ? EnchTarget.Bow : EnchTarget.FishingRod, 1, 30, treasure: true);
                e = list.Count > 0 ? Mathf.Max(0, Enchants.Pack(list)) : 0;
            }
            return used > 0 || e > 0 ? ItemData.With(key, Enchants.Data(used, e)) : key;
        }

        /// <summary>The moon's water (the game's water volumes) or placed water at a point; the surface's height.</summary>
        public static bool WaterAt(Vector3 p, out float surface)
        {
            surface = p.y;
            foreach (var h in Physics.OverlapSphere(p, 0.12f, ~0, QueryTriggerInteraction.Collide))
            {
                var q = h.GetComponent<QuicksandTrigger>();
                if (q != null && q.isWater) { surface = h.bounds.max.y; return true; }
            }
            if (WaterSwim.FluidCellAt(p, out var fb) && fb.Data.Def == Blocks.Water && fb.Go != null)
            {
                surface = fb.Go.transform.position.y + BlockWorld.S * 0.4f;
                return true;
            }
            return false;
        }
    }

    /// <summary>A bobber and its line (every machine): flies from the cast, floats on water, dips when something bites.</summary>
    public class Bobber : MonoBehaviour
    {
        public ulong Client;
        Vector3 vel;
        public bool Floating, Grounded, Biting;
        public EnemyAI HookedMob; public GrabbableObject HookedItem; // (#244: caught on a mob or an item)
        float surface, biteAt = -1f, biteUntil, dip;
        LineRenderer line;
        static Material lineMat, bobMat;

        public void Begin(ulong client, Vector3 from, Vector3 v)
        {
            Client = client; vel = v; transform.position = from;
            // the bobber: a little red and white float
            var top = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(top.GetComponent<Collider>());
            top.transform.SetParent(transform, false); top.transform.localScale = new Vector3(0.12f, 0.08f, 0.12f); top.transform.localPosition = Vector3.up * 0.05f;
            var bot = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(bot.GetComponent<Collider>());
            bot.transform.SetParent(transform, false); bot.transform.localScale = new Vector3(0.12f, 0.08f, 0.12f); bot.transform.localPosition = Vector3.down * 0.03f;
            if (bobMat == null) { bobMat = Atlas.MakeLit("LMC_BobberRed", false, false, false); bobMat.SetColor("_BaseColor", new Color(0.8f, 0.1f, 0.1f)); }
            top.GetComponent<MeshRenderer>().sharedMaterial = bobMat;
            bot.GetComponent<MeshRenderer>().sharedMaterial = Atlas.MakeLit("LMC_BobberWhite", false, false, false);
            line = gameObject.AddComponent<LineRenderer>();
            if (lineMat == null) { lineMat = new Material(Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color")); lineMat.SetColor("_UnlitColor", new Color(0.1f, 0.1f, 0.1f)); lineMat.color = new Color(0.1f, 0.1f, 0.1f); }
            line.sharedMaterial = lineMat; line.widthMultiplier = 0.012f; line.positionCount = 2; line.numCapVertices = 0;
        }

        PlayerControllerB Holder => StartOfRound.Instance.allPlayerScripts.FirstOrDefault(x => x != null && x.actualClientId == Client);

        void Update()
        {
            var p = Holder;
            float dt = Time.deltaTime;
            if (HookedMob != null || HookedItem != null)
            {
                // (#244) on what it hooked: with it, wherever it goes (gone: it drops)
                if (HookedMob != null && !HookedMob.isEnemyDead) transform.position = HookedMob.transform.position + Vector3.up * 0.8f * BlockWorld.S;
                else if (HookedItem != null && !HookedItem.isHeld && !HookedItem.isPocketed) transform.position = HookedItem.transform.position + Vector3.up * 0.1f;
                else { HookedMob = null; HookedItem = null; vel = Vector3.zero; }
            }
            else if (!Floating && !Grounded)
            {
                // a throw: gravity, until it meets water or the ground
                var next = transform.position + vel * dt;
                vel += Vector3.down * 18f * dt;
                // (#244) the host: a mob or an item in its way catches it (Minecraft's FishingHook.onHitEntity)
                if (BlockNet.IsServer && !hookAsked)
                    foreach (var c in Physics.OverlapSphere(next, 0.3f, ~0, QueryTriggerInteraction.Collide))
                    {
                        var col = c.GetComponentInParent<EnemyAICollisionDetect>();
                        var g = c.GetComponentInParent<GrabbableObject>();
                        if (col != null && col.mainScript != null && !col.mainScript.isEnemyDead) { hookAsked = true; BlockNet.ServerFishHook(Client, col.mainScript.NetworkObjectId); Fishing.Hooked++; break; }
                        if (g != null && !g.isHeld && !g.isPocketed && !g.isHeldByEnemy && g.hasHitGround && g.NetworkObject != null && !(g is RodItem)) { hookAsked = true; BlockNet.ServerFishHook(Client, g.NetworkObjectId); Fishing.Hooked++; break; }
                    }
                if (Fishing.WaterAt(next, out surface)) { Floating = true; next.y = surface; Sounds.Play("fish.splash_small", next, 1f, 0.6f, 16f); if (BlockNet.IsServer) biteAt = Time.time + Fishing.WaitFor(Client); }
                else if (Physics.Linecast(transform.position, next, out var h, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) { Grounded = true; next = h.point + h.normal * 0.05f; }
                transform.position = next;
            }
            else if (Floating)
            {
                dip = Mathf.MoveTowards(dip, Biting ? 1f : 0f, dt * 6f);
                var q = transform.position; q.y = surface - 0.02f + Mathf.Sin(Time.time * 2f) * 0.015f - dip * 0.12f;
                transform.position = q;
            }
            if (Biting && Time.time > biteUntil) Biting = false;
            // host: the bite (and if it isn't reeled in in time, the fish swims off and another comes later)
            if (BlockNet.IsServer && Floating && biteAt > 0f && Time.time >= biteAt) { biteAt = Time.time + Fishing.WaitFor(Client); Fishing.ServerBite(Client); }
            // the line, from the rod's tip
            if (p != null && line != null)
            {
                // (the rod's tip: first person, where the camera draws the rod, bottom right; seen by others, above their hand)
                var cam = p.gameplayCamera.transform;
                Vector3 tip;
                if (p == GameNetworkManager.Instance?.localPlayerController) tip = cam.position + cam.forward * 0.6f + cam.right * 0.28f - cam.up * 0.17f;
                else tip = p.serverItemHolder != null ? p.serverItemHolder.position + Vector3.up * 0.5f + p.transform.forward * 0.3f : p.transform.position + Vector3.up * 1.6f;
                line.SetPosition(0, tip); line.SetPosition(1, transform.position + Vector3.up * 0.08f);
            }
            // too far, or the rod put away: the line comes back (its holder's machine asks)
            if (p != null && p == GameNetworkManager.Instance?.localPlayerController)
            {
                bool rod = p.currentlyHeldObjectServer is RodItem;
                if ((!rod || p.isPlayerDead || Vector3.Distance(p.transform.position, transform.position) > Fishing.Reach) && !asked) { asked = true; BlockNet.RequestFish(1, Vector3.zero, Vector3.zero); }
            }
        }
        bool asked, hookAsked;

        /// <summary>(#244) Every machine: caught on this mob or item.</summary>
        public void HookOn(EnemyAI mob, GrabbableObject item)
        {
            HookedMob = mob; HookedItem = mob == null ? item : null;
            Floating = false; Grounded = false; Biting = false; biteAt = -1f;
        }

        public void Bite()
        {
            Biting = true; biteUntil = Time.time + Fishing.BiteWindow;
            Sounds.Play("fish.splash", transform.position, 1f, Random.Range(0.9f, 1.1f), 20f);
        }
    }
}
