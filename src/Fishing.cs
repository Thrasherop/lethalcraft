using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Fishing, Minecraft's way (#96: cats want fish). A fishing rod (three sticks and two string) casts a bobber with the
    /// use button; landed on water (a moon's lake or river, or placed water), after 5-30 s something bites: the bobber
    /// dips with a splash, and the use button within a second reels it in. Otherwise the use button just reels the line
    /// back. Catches as in Minecraft: mostly cod and salmon, now and then a pufferfish, a tropical fish, junk or a treasure.
    /// The furnace cooks cod and salmon. Every machine draws every bobber and line from the cast; the host times the
    /// bite and hands out the catch.
    /// </summary>
    public static class Fishing
    {
        public const string RodKey = "fishing_rod";
        public const float CastSpeed = 11f, Reach = 32f, BiteWindow = 1.2f;
        static readonly Dictionary<ulong, Bobber> bobbers = new Dictionary<ulong, Bobber>();
        public static int Catches; public static string LastCatch = "-"; // (dev/tests)

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
                if (!(p.currentlyHeldObjectServer is StackItem st) || st.ItemKey != RodKey) return;
                BlockNet.ServerFish(sender, 0, from, vel);
                return;
            }
            // reeling in: a fish on the line? it's theirs
            var b = Of(sender);
            if (b != null && b.Biting && p != null)
            {
                var (key, n) = Catch();
                if (ModItems.ByKey.TryGetValue(key, out var item))
                {
                    var at = Vector3.Lerp(b.transform.position, p.transform.position + Vector3.up, 0.85f);
                    ModItems.ServerSpawnStack(item, n, at);
                    BlockNet.ServerXp(sender, Random.Range(1, 7));
                    Catches++; LastCatch = key;
                    Plugin.Log.LogInfo($"[fishing] {p.playerUsername} caught {key}");
                }
            }
            BlockNet.ServerFish(sender, 2, Vector3.zero, Vector3.zero);
        }

        /// <summary>Host, as a bobber of theirs bites (or its fish gets away): told to everyone.</summary>
        public static void ServerBite(ulong client) => BlockNet.ServerFish(client, 1, Vector3.zero, Vector3.zero);

        /// <summary>Minecraft's catch (no Luck of the Sea): fish 85%, junk 10%, treasure 5%.</summary>
        static (string key, int n) Catch()
        {
            float r = Random.value;
            if (r < 0.85f)
            {
                float f = Random.value;
                return (f < 0.6f ? "raw_cod" : f < 0.85f ? "raw_salmon" : f < 0.98f ? "pufferfish" : "tropical_fish", 1);
            }
            if (r < 0.95f)
            {
                var junk = new[] { "bone", "string", "stick", "leather", "rotten_flesh", "bowl" }.Where(k => ModItems.ByKey.ContainsKey(k)).ToArray();
                return (junk.Length > 0 ? junk[Random.Range(0, junk.Length)] : "raw_cod", 1);
            }
            var treasure = new[] { "saddle", "ender_pearl", "golden_apple", NameTags.Key }.Where(k => ModItems.ByKey.ContainsKey(k)).ToArray();
            return (treasure.Length > 0 ? treasure[Random.Range(0, treasure.Length)] : "raw_salmon", 1);
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
            if (!Floating && !Grounded)
            {
                // a throw: gravity, until it meets water or the ground
                var next = transform.position + vel * dt;
                vel += Vector3.down * 18f * dt;
                if (Fishing.WaterAt(next, out surface)) { Floating = true; next.y = surface; Sounds.Play("fish.splash_small", next, 1f, 0.6f, 16f); if (BlockNet.IsServer) biteAt = Time.time + Random.Range(5f, 30f); }
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
            if (BlockNet.IsServer && Floating && biteAt > 0f && Time.time >= biteAt) { biteAt = Time.time + Random.Range(5f, 30f); Fishing.ServerBite(Client); }
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
                bool rod = p.currentlyHeldObjectServer is StackItem st && st.ItemKey == Fishing.RodKey;
                if ((!rod || p.isPlayerDead || Vector3.Distance(p.transform.position, transform.position) > Fishing.Reach) && !asked) { asked = true; BlockNet.RequestFish(1, Vector3.zero, Vector3.zero); }
            }
        }
        bool asked;

        public void Bite()
        {
            Biting = true; biteUntil = Time.time + Fishing.BiteWindow;
            Sounds.Play("fish.splash", transform.position, 1f, Random.Range(0.9f, 1.1f), 20f);
        }
    }
}
