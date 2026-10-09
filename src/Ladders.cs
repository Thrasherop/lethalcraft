using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Ladders (#30): a ladder block on a wall is climbed like the game's own ladders: [E] to get on (a hand free: not with
    /// a two-handed item), W/S to climb, off at the bottom or over the top. Each ladder block carries the game's ladder
    /// trigger, set up for the whole column of ladders it's part of (re-measured as the column grows or shrinks).
    /// </summary>
    public class LadderBlock : MonoBehaviour
    {
        public BlockKey Key;
        InteractTrigger trig;
        Transform top, bottom, horizontal, node;
        float nextMeasure;

        public static void Attach(BlockInstance bi)
        {
            if (bi.Go == null || bi.Go.GetComponent<LadderBlock>() != null) return;
            var lb = bi.Go.AddComponent<LadderBlock>();
            lb.Key = bi.Key;
            lb.Setup();
        }

        Transform Child(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        void Setup()
        {
            gameObject.tag = "InteractTrigger";
            gameObject.layer = BlockWorld.NonSolidLayer;
            top = Child("ladderTop"); bottom = Child("ladderBottom"); horizontal = Child("ladderHorizontal"); node = Child("ladderNode");
            trig = gameObject.AddComponent<InteractTrigger>();
            trig.hoverIcon = HudAssets.HandIcon;
            trig.hoverTip = "Climb : [E]";
            trig.disabledHoverTip = "";
            trig.interactable = true;
            trig.oneHandedItemAllowed = true;
            trig.twoHandedItemAllowed = false; // (a hand free, like the game's ladders)
            trig.holdInteraction = false;
            trig.interactCooldown = true;
            trig.cooldownTime = 0.3f;
            trig.isLadder = true;
            trig.disableTriggerMesh = false; // (it would hide the rungs: the trigger sits on the block itself)
            trig.useRaycastToGetTopPosition = true;
            trig.hidePlayerItem = true;
            trig.animationWaitTime = 0.3f;
            trig.topOfLadderPosition = top;
            trig.bottomOfLadderPosition = bottom;
            trig.ladderHorizontalPosition = horizontal;
            trig.ladderPlayerPositionNode = node;
            trig.onInteract = new InteractEvent();
            trig.onInteractEarly = new InteractEvent();
            trig.onStopInteract = new InteractEvent();
            trig.onCancelAnimation = new InteractEvent();
            trig.onInteractEarlyOtherClients = new InteractEvent();
            trig.holdingInteractEvent = new InteractEventFloat();
            Measure();
        }

        void Update()
        {
            // (the column may have grown or lost a rung: re-measure now and then, only when someone could be using it)
            if (Time.time < nextMeasure) return;
            nextMeasure = Time.time + 0.5f;
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (p == null || Vector3.Distance(p.transform.position, transform.position) > 12f) return;
            if (trig != null && trig.usingLadder) return; // (not while climbing)
            Measure();
        }

        /// <summary>The column this rung is part of (same wall, straight up and down), and the game's ladder points for it.</summary>
        public void Measure()
        {
            var world = BlockWorld.Instance;
            var me = world?.Get(Key);
            if (me == null) return;
            byte f = me.Data.Facing;
            bool Rung(BlockKey k) { var b = world.Get(k); return b != null && b.Data.Def.Shape == BlockShape.Ladder && b.Data.Facing == f; }
            var lo = Key; while (Rung(lo.Offset((int)Face.Down))) lo = lo.Offset((int)Face.Down);
            var hi = Key; while (Rung(hi.Offset((int)Face.Up))) hi = hi.Offset((int)Face.Up);
            float S = BlockWorld.S;
            var up = world.FrameDirToWorld(Key.Frame, Vector3.up);
            var front = world.FrameDirToWorld(Key.Frame, Faces.Dir[f]); // away from the wall
            var toWall = Quaternion.LookRotation(-front, up);
            // where you stand on it: in front of the rungs
            var mid = world.WorldCenter(Key) + front * (S * 0.5f - 0.28f);
            horizontal.position = mid;
            node.rotation = toWall;
            // off at the bottom: on the floor in front of the lowest rung
            bottom.position = world.WorldCenter(lo) - up * (S * 0.5f) + front * (S * 0.5f + 0.3f);
            bottom.rotation = toWall;
            // over the top: onto whatever the ladder leans on (or in front of the top rung if that's closed off); the
            // climb ends when your feet are 2 m below this point
            var behindTop = hi.Offset(Faces.Opposite(f)).Offset((int)Face.Up);
            bool openAbove = !world.IsSolidAt(behindTop) && !world.IsSolidAt(behindTop.Offset((int)Face.Up));
            top.position = (openAbove ? world.WorldCenter(behindTop) - up * (S * 0.5f) : world.WorldCenter(hi) + up * (S * 0.5f) + front * 0.3f) + up * 0.05f;
            top.rotation = toWall;
            // the climb ends when your feet are 2 m below the top point: with short columns, make sure that's above the lowest rung
            float minTop = (world.WorldCenter(hi) + up * (S * 0.5f)).y + 0.2f;
            if (top.position.y < minTop) top.position = new Vector3(top.position.x, minTop, top.position.z);
        }
    }
}
