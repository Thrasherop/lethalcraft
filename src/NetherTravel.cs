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
            if (Time.time >= nextCheck) { nextCheck = Time.time + 1f; KeepFortress(); }
            cooldown -= Time.deltaTime;
            var p = Local;
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
                failedThisRound = false; hasCameFrom = false;
                return;
            }
            if (NetherFortress.Root != null || failedThisRound || !AnyPortal()) return;
            if (!NetherFortress.Generate(sor.randomMapSeed + 66)) failedThisRound = true; // (once a round: it says why in the log)
        }

        static bool AnyPortal() => BlockWorld.Instance != null && BlockWorld.Instance.Blocks.Values.Any(b => b.Data.Def == Blocks.NetherPortal);

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
            bool there = NetherFortress.Root != null && NetherFortress.InExit(p.transform.position + Vector3.up * 0.9f);
            if (!there)
            {
                if (NetherFortress.Root == null) { McHud.Toast("The portal flickers: nothing on the other side yet."); return; }
                InAPortal(p, out var b);
                cameFrom = b != null ? b.Go.transform.position : p.transform.position; hasCameFrom = true;
                Arrive(p, NetherFortress.StartPoint, NetherFortress.StartForward, true);
            }
            else
            {
                // back out of the portal you came in by, or any other
                var w = BlockWorld.Instance;
                var portals = w != null ? w.Blocks.Values.Where(x => x.Data.Def == Blocks.NetherPortal && x.Go != null).ToList() : null;
                if (portals == null || portals.Count == 0) { McHud.Toast("The portal you came through is gone."); return; }
                var target = hasCameFrom ? portals.OrderBy(x => Vector3.Distance(x.Go.transform.position, cameFrom)).First() : portals[0];
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
            foreach (var it in p.ItemSlots) if (it != null) it.isInFactory = inside;
            Sounds.Play2D("portal.travel", 0.6f, 1f);
            Plugin.Log.LogInfo($"[portal] {p.playerUsername} went {(NetherFortress.InExit(at + Vector3.up * 0.9f) || at.y < NetherFortress.Depth + 100f ? "to the Nether" : "back")} at {at}");
        }

        public static string Describe() => $"inPortal={InPortalFor:F1} trips={Trips} fortress={NetherFortress.Describe()} failed={failedThisRound}";
    }
}
