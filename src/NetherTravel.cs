using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// Going to the Nether and back (#66, phase 3), on every client for its own player:
    /// - the fortress exists while there's a portal: once the ship has landed and the game's interior is done, a lit
    ///   portal anywhere (the moon, the ship) makes this machine generate it (the same seed everywhere, so every
    ///   machine builds the same one); in orbit it's gone;
    /// - standing in a portal for 4 seconds (Minecraft's; half a second in creative), the screen going purple, takes
    ///   you there: into the fortress's portal room, or from there back out of the portal you went in by (another one
    ///   if that's gone). It's inside, like the facility: whoever's there when the ship leaves is lost.
    /// </summary>
    public class NetherTravel : MonoBehaviour
    {
        public const float Wait = 4f, CreativeWait = 0.5f;
        public static float InPortalFor;
        public static int Trips; // (dev/tests)
        static Vector3 cameFrom; static bool hasCameFrom;
        static bool failedThisRound;
        float nextCheck, cooldown;
        Image overlay;

        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        void Start()
        {
            var go = new GameObject("LMC_PortalOverlay", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 5;
            var img = new GameObject("tint", typeof(RectTransform));
            img.transform.SetParent(go.transform, false);
            var rt = (RectTransform)img.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            overlay = img.AddComponent<Image>();
            overlay.color = new Color(0.45f, 0.1f, 0.75f, 0f);
            overlay.raycastTarget = false;
        }

        void Update()
        {
            if (Time.time >= nextCheck) { nextCheck = Time.time + 1f; KeepFortress(); NetherLife.ServerTick(); }
            cooldown -= Time.deltaTime;
            var p = Local;
            NetherVoid.Tick(p);
            bool inside = p != null && !p.isPlayerDead && p.isPlayerControlled && cooldown <= 0f && InAPortal(p, out _);
            if (!inside) { InPortalFor = 0f; SetTint(0f); return; }
            if (InPortalFor == 0f) Sounds.Play2D("portal.trigger", 0.5f, 1f);
            InPortalFor += Time.deltaTime;
            float wait = GameModes.LocalCreative ? CreativeWait : Wait;
            SetTint(Mathf.Clamp01(InPortalFor / wait) * 0.7f);
            if (InPortalFor >= wait) { InPortalFor = 0f; SetTint(0f); cooldown = 3f; Travel(p); }
        }

        void SetTint(float a) { if (overlay != null && !Mathf.Approximately(overlay.color.a, a)) overlay.color = new Color(0.45f, 0.1f, 0.75f, a); }

        // ------------------------------------------------------------------ the fortress comes and goes
        static void KeepFortress()
        {
            var sor = StartOfRound.Instance; var rm = RoundManager.Instance;
            bool round = sor != null && rm != null && !sor.inShipPhase && sor.shipHasLanded && rm.dungeonCompletedGenerating;
            if (!round)
            {
                if (NetherFortress.Root != null) NetherFortress.Clear();
                failedThisRound = false; hasCameFrom = false; NetherLife.Reset();
                return;
            }
            if (NetherFortress.Root != null)
            {
                // the exit rooms' portals are lit while there's a portal on the moon for each
                int groups = PortalGroups().Count;
                for (int i = 1; i < NetherFortress.Exits.Count; i++) NetherFortress.SetExitLit(i, i < groups);
                return;
            }
            if (failedThisRound || !AnyPortal()) return;
            if (!NetherFortress.Generate(sor.randomMapSeed + 66)) failedThisRound = true; // (once a round: it says why in the log)
        }

        static bool AnyPortal() => BlockWorld.Instance != null && BlockWorld.Instance.Blocks.Values.Any(b => b.Data.Def == Blocks.NetherPortal);

        /// <summary>
        /// The portals on the moon (and the ship): each lit frame's blocks together, in a fixed order every machine agrees
        /// on (by position). The first goes to the fortress's portal room, the next ones to its exit rooms.
        /// </summary>
        public static List<List<BlockInstance>> PortalGroups()
        {
            var w = BlockWorld.Instance;
            var groups = new List<List<BlockInstance>>();
            if (w == null) return groups;
            // (the moon's and the ship's: not the ones in the Nether itself, #118)
            var left = new HashSet<BlockKey>(w.Blocks.Where(kv => kv.Value.Data.Def == Blocks.NetherPortal && kv.Value.Go != null && !NetherFortress.Contains(kv.Value.Go.transform.position)).Select(kv => kv.Key));
            while (left.Count > 0)
            {
                var first = left.First(); left.Remove(first);
                var g = new List<BlockInstance>(); var todo = new Stack<BlockKey>(); todo.Push(first);
                while (todo.Count > 0)
                {
                    var k = todo.Pop(); g.Add(w.Get(k));
                    for (int f = 0; f < 6; f++) { var n = k.Offset(f); if (left.Remove(n)) todo.Push(n); }
                }
                groups.Add(g);
            }
            return groups.OrderBy(g => g.Min(b => b.Key.Frame)).ThenBy(g => g.Min(b => b.Key.Pos.x)).ThenBy(g => g.Min(b => b.Key.Pos.z)).ThenBy(g => g.Min(b => b.Key.Pos.y)).ToList();
        }

        // ------------------------------------------------------------------ standing in a portal
        /// <summary>The player's body in a portal block (here) or in the fortress's portal (there).</summary>
        static bool InAPortal(PlayerControllerB p, out BlockInstance block)
        {
            block = null;
            var body = p.transform.position + Vector3.up * 0.9f;
            var w = BlockWorld.Instance;
            if (w != null)
                foreach (var b in w.Blocks.Values)
                {
                    if (b.Data.Def != Blocks.NetherPortal || b.Go == null) continue;
                    var l = b.Go.transform.InverseTransformPoint(body);
                    if (Mathf.Abs(l.x) <= 0.55f && Mathf.Abs(l.y) <= 0.55f && Mathf.Abs(l.z) <= 0.55f) { block = b; return true; }
                }
            return NetherFortress.InExit(body);
        }

        static void Travel(PlayerControllerB p)
        {
            int exitIn = NetherFortress.Root != null ? NetherFortress.ExitAt(p.transform.position + Vector3.up * 0.9f) : -1;
            var groups = PortalGroups();
            if (exitIn < 0)
            {
                if (NetherFortress.Root == null) { McHud.Toast("The portal flickers: nothing on the other side yet."); return; }
                InAPortal(p, out var b);
                // (#118) a portal in the Nether (one made where someone arrived, or built there): back out
                if (b != null && NetherFortress.Contains(b.Go.transform.position))
                {
                    var back = NetherLinks.ReturnTarget(b, hasCameFrom, cameFrom);
                    if (back == null) { McHud.Toast("The portal you came through is gone."); return; }
                    NetherLinks.FrontOf(back, out var bat, out var bface);
                    Arrive(p, bat, bface, bat.y < -100f);
                    Trips++;
                    return;
                }
                cameFrom = b != null ? b.Go.transform.position : p.transform.position; hasCameFrom = true;
                // (#118) Minecraft's way: 1/8 of where the portal is, the nearest room with room for a portal (one appears there)
                if (NetherLinks.ArrivalFor(cameFrom, out var nat, out var nface, out var norigin, out var naxis, out bool nbuild))
                {
                    if (nbuild) BlockNet.RequestNetherPortal(norigin, naxis);
                    Arrive(p, nat, nface, true);
                    Trips++;
                    return;
                }
                // portal n on the moon comes out of the fortress's portal n (the portal room's for the first, else an exit room's)
                int n = b != null ? groups.FindIndex(g => g.Contains(b)) : 0;
                var (at, facing) = n >= 0 && n < NetherFortress.Arrivals.Count ? NetherFortress.Arrivals[n] : (NetherFortress.StartPoint, NetherFortress.StartForward);
                Arrive(p, at, facing, true);
            }
            else
            {
                // out of the moon's portal n for the fortress's portal n; the portal room's: the one you came in by, else the first
                var w = BlockWorld.Instance;
                var portals = w != null ? w.Blocks.Values.Where(x => x.Data.Def == Blocks.NetherPortal && x.Go != null).ToList() : null;
                if (portals == null || portals.Count == 0) { McHud.Toast("The portal you came through is gone."); return; }
                var target = exitIn > 0 && exitIn < groups.Count ? groups[exitIn][0]
                           : hasCameFrom ? portals.OrderBy(x => Vector3.Distance(x.Go.transform.position, cameFrom)).First() : groups[0][0];
                // its bottom block, then a step out of the frame on the open side
                var bottom = portals.Where(x => Vector3.Distance(Vector3.ProjectOnPlane(x.Go.transform.position - target.Go.transform.position, Vector3.up), Vector3.zero) < 0.1f)
                                    .OrderBy(x => x.Go.transform.position.y).First();
                bool alongX = (bottom.Data.State & 1) == 0;
                var n = alongX ? bottom.Go.transform.forward : bottom.Go.transform.right;
                float s = BlockWorld.S;
                var feet = bottom.Go.transform.position - bottom.Go.transform.up * (0.5f * s);
                var outA = feet + n * s; var outB = feet - n * s;
                var to = Physics.CheckCapsule(outA + Vector3.up * 0.4f, outA + Vector3.up * 1.8f, 0.3f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore) ? outB : outA;
                Arrive(p, to + Vector3.up * 0.05f, (to - feet).normalized, feet.y < -100f);
            }
            Trips++;
        }

        static void Arrive(PlayerControllerB p, Vector3 at, Vector3 facing, bool inside)
        {
            p.TeleportPlayer(at);
            p.isInElevator = false;
            p.isInHangarShipRoom = false;
            if (facing.sqrMagnitude > 0.01f) p.thisPlayerBody.eulerAngles = new Vector3(p.thisPlayerBody.eulerAngles.x, Quaternion.LookRotation(Vector3.ProjectOnPlane(facing, Vector3.up)).eulerAngles.y, p.thisPlayerBody.eulerAngles.z);
            p.isInsideFactory = inside;
            BlockNet.RequestInside(inside); // (everyone else hears and sees you as inside, or out)
            foreach (var it in p.ItemSlots) if (it != null) it.isInFactory = inside;
            Sounds.Play2D("portal.travel", 0.6f, 1f);
            Plugin.Log.LogInfo($"[portal] {p.playerUsername} went {(NetherFortress.InExit(at + Vector3.up * 0.9f) || at.y < NetherFortress.Depth + 100f ? "to the Nether" : "back")} at {at}");
        }

        public static string Describe() => $"inPortal={InPortalFor:F1} trips={Trips} fortress={NetherFortress.Describe()} failed={failedThisRound} moonPortals={PortalGroups().Count}";
    }
}
