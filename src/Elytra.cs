using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace LethalMinecraft
{
    /// <summary>
    /// The elytra and firework rockets (#61), Minecraft's: worn in the chestplate slot, opened by pressing jump again in
    /// the air, then gliding with Minecraft's physics (look down to gain speed, up to trade it for height). Firework
    /// rockets ([Right-click] while gliding) boost you along where you look; they're loud. Hitting a wall at speed hurts
    /// (Minecraft's kinetic damage), and so does diving into the ground. It wears out: about seven minutes of gliding.
    /// The store sells both (the elytra costs more than the jetpack); rockets are also crafted (paper and gunpowder).
    /// The physics run in Minecraft's units (blocks per tick) and move the player through the game's own movement, like
    /// creative flight; every client draws everyone's wings from the armor it already knows.
    /// </summary>
    [HarmonyPatch]
    public class Elytra : MonoBehaviour
    {
        public const string Key = "elytra", RocketKey = "firework_rocket";
        /// <summary>Minecraft's elytra durability: 432 seconds of gliding (one use a second).</summary>
        public static int MaxUses => Balance.ToolDurability ? Mathf.Max(1, Mathf.RoundToInt(432 * Balance.DurabilityMultiplier)) : 0;

        public static bool Gliding;
        /// <summary>Velocity in Minecraft's units: blocks per tick.</summary>
        public static Vector3 V;
        public static int BoostTicks;
        public static float LastHit; // (dev/tests) the last kinetic damage
        static float startedAt, tickAcc, wearAcc;
        static int unsyncedWear;
        static InputAction jump;

        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
        /// <summary>Metres per second for one block per tick.</summary>
        static float K => Plugin.S * 20f;

        // ------------------------------------------------------------------ the worn elytra
        public static string WornKey => Armor.Local[1] != null && ItemData.Base(Armor.Local[1]) == Key ? Armor.Local[1] : null;
        public static int UsedOf(string key) => Enchants.UsesOf(ItemData.Of(key));
        public static bool Broken(string key) => MaxUses > 0 && UsedOf(key) >= MaxUses - 1; // (Minecraft: at 1 left it stops working)
        public static bool CanGlide => WornKey != null && !Broken(WornKey);

        void OnDestroy() => Gliding = false;

        void Update()
        {
            var p = Local;
            if (p == null || p.isPlayerDead || !p.isPlayerControlled) { Close(false); return; }
            if (jump == null) { try { jump = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Jump"); } catch { } }
            if (!Gliding)
            {
                if (jump != null && jump.WasPressedThisFrame() && !InMenu(p) && CanOpen(p)) Open(p);
                return;
            }
            // closing: on the ground, in water, on a ladder, no (working) elytra, a jetpack
            bool landed = p.thisController.isGrounded && Time.time - startedAt > 0.25f;
            if (landed || p.isClimbingLadder || p.isUnderwater || WaterSwim.BodyIn || !CanGlide || p.jetpackControls || p.inSpecialInteractAnimation || CreativeFlight.Flying)
            { Close(true); return; }
            // wear: one use a second (synced every few seconds, and when it closes)
            if (MaxUses > 0 && !GameModes.LocalCreative)
            {
                wearAcc += Time.deltaTime;
                while (wearAcc >= 1f) { wearAcc -= 1f; unsyncedWear++; }
                if (unsyncedWear >= 5 || (unsyncedWear > 0 && Broken(WithWear(WornKey, unsyncedWear)))) SyncWear();
            }
        }

        static bool InMenu(PlayerControllerB p) =>
            p.isTypingChat || p.inTerminalMenu || SlotScreen.AnyOpen || p.inSpecialInteractAnimation || (p.quickMenuManager != null && p.quickMenuManager.isMenuOpen);

        static bool CanOpen(PlayerControllerB p) =>
            CanGlide && !p.thisController.isGrounded && !p.isClimbingLadder && !p.isUnderwater && !WaterSwim.BodyIn && !p.jetpackControls && !CreativeFlight.Flying && !p.inSpecialInteractAnimation;

        public static void Open(PlayerControllerB p)
        {
            Gliding = true; startedAt = Time.time; tickAcc = 0f; wearAcc = 0f; BoostTicks = 0;
            V = p.thisController.velocity / K;
            Sounds.Play2D("elytra.open", 0.5f, 1f);
        }

        public static void Close(bool sync)
        {
            if (!Gliding) return;
            Gliding = false; BoostTicks = 0;
            if (sync) SyncWear();
        }

        static string WithWear(string key, int more)
        {
            int data = ItemData.Of(key);
            return ItemData.With(key, Enchants.Data(Mathf.Min(Enchants.UsesOf(data) + more, Mathf.Max(MaxUses, 1)), Enchants.EnchOf(data)));
        }

        static void SyncWear()
        {
            var k = WornKey;
            if (k == null || unsyncedWear <= 0) { unsyncedWear = 0; return; }
            var nk = WithWear(k, unsyncedWear);
            unsyncedWear = 0;
            Armor.SetLocal(1, nk);
            if (Broken(nk)) { McHud.Toast("Your elytra is worn out."); Sounds.Play2D("tool.break", 0.6f, 1f); }
        }

        // ------------------------------------------------------------------ Minecraft's glide (LivingEntity.travel, fall flying)
        static void PhysicsTick(PlayerControllerB p)
        {
            Vector3 look = p.gameplayCamera.transform.forward;
            float pitch = Mathf.Asin(Mathf.Clamp(-look.y, -1f, 1f)); // (Minecraft's xRot: looking down is positive)
            float horiz = Mathf.Sqrt(look.x * look.x + look.z * look.z);
            float hSpeed = Mathf.Sqrt(V.x * V.x + V.z * V.z);
            float c = Mathf.Cos(pitch); c *= c;
            V.y += 0.08f * (-1f + c * 0.75f);
            if (V.y < 0f && horiz > 0f) { float k = V.y * -0.1f * c; V.x += look.x * k / horiz; V.y += k; V.z += look.z * k / horiz; }
            if (pitch < 0f && horiz > 0f) { float k = hSpeed * -Mathf.Sin(pitch) * 0.04f; V.x -= look.x * k / horiz; V.y += k * 3.2f; V.z -= look.z * k / horiz; }
            if (horiz > 0f) { V.x += (look.x / horiz * hSpeed - V.x) * 0.1f; V.z += (look.z / horiz * hSpeed - V.z) * 0.1f; }
            V.x *= 0.99f; V.y *= 0.98f; V.z *= 0.99f;
            // a firework: pulls you along where you look (FireworkRocketEntity, attached to a gliding player)
            if (BoostTicks > 0)
            {
                BoostTicks--;
                V += look * 0.1f + (look * 1.5f - V) * 0.5f;
            }
        }

        static Vector3 before, asked;
        static float lastHurtAt = -10f;
        static bool moved;

        /// <summary>Before the game moves the player: our velocity through its own movement (its gravity runs after the move).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPrefix]
        static void Glide(PlayerControllerB __instance)
        {
            moved = false;
            if (!Gliding || __instance != Local) return;
            var p = __instance;
            tickAcc += Time.deltaTime;
            int n = 0;
            while (tickAcc >= 0.05f && n++ < 8) { tickAcc -= 0.05f; PhysicsTick(p); }
            if (n >= 8) tickAcc = 0f;
            float vy = V.y * K;
            p.fallValue = vy;
            // (diving into the ground hurts like the fall it is; gliding down gently doesn't, Minecraft's -0.5 blocks/tick)
            p.fallValueUncapped = V.y > -0.5f ? 0f : vy;
            p.isFallingNoJump = true; p.isFallingFromJump = false; p.isJumping = false;
            var h = new Vector3(V.x, 0f, V.z) * K;
            p.externalForces += h;
            before = p.transform.position; asked = h * Time.deltaTime; moved = true;
        }

        /// <summary>After the move: a wall hit at speed stops you and hurts (Minecraft: (speed lost) x 10 - 3 half hearts).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPostfix]
        static void AfterMove(PlayerControllerB __instance)
        {
            if (!moved || !Gliding || __instance != Local) return;
            moved = false;
            var p = __instance;
            var d = p.transform.position - before; d.y = 0f;
            float want = asked.magnitude;
            if (want < 1e-4f || Time.deltaTime <= 0f) return;
            if (d.magnitude > want * 0.6f || (d - asked).magnitude > 5f) return; // (moved as asked, or a teleport)
            float hBefore = new Vector2(V.x, V.z).magnitude;
            var now = d / Time.deltaTime / K;
            V.x = now.x; V.z = now.z;
            // only a wall hurts: what stopped you is steep (gliding down onto a slope or a bump is a landing, Minecraft's
            // onGround, not its horizontal collision)
            if (!WallAhead(p, asked)) return;
            float hurt = (hBefore - now.magnitude) * 10f - 3f;
            // (Minecraft's half second of invulnerability after a hit: a rocket still pushing you into the wall hurts again
            // only after that)
            if (hurt > 0f && !GameModes.LocalCreative && Time.time - lastHurtAt >= 0.5f)
            {
                lastHurtAt = Time.time;
                BoostTicks = 0; // (a rocket stops pushing you into the wall: one hit, not Minecraft's repeated ones)
                int dmg = Mathf.RoundToInt(hurt * 5f); // (Minecraft's 20 health is the game's 100)
                LastHit = dmg;
                p.DamagePlayer(dmg, true, true, CauseOfDeath.Gravity, 0, false, default);
                Sounds.Play2D("hurt", 0.8f, 1f);
            }
        }

        static int WallMask => StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << 8) | (1 << 11) | (1 << 0) | (1 << 25) | (1 << 26) | (1 << 28);

        /// <summary>Something steep in the way of that move (a wall, a cliff, a block), from the body's middle.</summary>
        static bool WallAhead(PlayerControllerB p, Vector3 move)
        {
            var cc = p.thisController;
            var from = before + p.transform.TransformVector(cc.center);
            var dir = new Vector3(move.x, 0f, move.z);
            if (dir.sqrMagnitude < 1e-8f) return false;
            foreach (var hit in Physics.SphereCastAll(from, cc.radius * 0.8f, dir.normalized, dir.magnitude + cc.radius + 0.3f, WallMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(p.transform) || hit.distance <= 0f) continue;
                if (Mathf.Abs(hit.normal.y) < 0.6f) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ fireworks
        /// <summary>Owner client: a rocket from the hand while gliding.</summary>
        public static bool UseRocket(PlayerControllerB p)
        {
            if (!Gliding) { McHud.Toast("Firework rockets boost you while you glide with an elytra."); return false; }
            if (Inventory.Take(p, p.currentItemSlot, 1) == null) return false;
            BoostTicks = 20 + Random.Range(0, 12); // (Minecraft's flight duration 1: 20-31 ticks)
            BlockNet.RequestRocket(p.transform.position);
            return true;
        }

        /// <summary>Server: the launch, heard by everyone (and the monsters).</summary>
        public static void ServerRocket(Vector3 at)
        {
            BlockNet.ServerSound(at, "firework.launch", 1f, 1f);
            ServerLogic.Noise(at, 30f, 0.9f);
        }

        /// <summary>(dev) what's ahead of the player for the wall check: every hit, its layer and normal.</summary>
        public static string DevCast(float dist)
        {
            var p = Local;
            if (p == null) return "-";
            var cc = p.thisController;
            var from = p.transform.position + p.transform.TransformVector(cc.center);
            var dir = p.transform.forward; dir.y = 0f;
            var sb = new System.Text.StringBuilder($"mask={WallMask} ");
            foreach (var hit in Physics.SphereCastAll(from, cc.radius * 0.8f, dir.normalized, dist, ~0, QueryTriggerInteraction.Ignore))
                sb.Append($"{hit.collider.name}[L{hit.collider.gameObject.layer}{((WallMask >> hit.collider.gameObject.layer) & 1) switch { 1 => "+", _ => "-" }}] d={hit.distance:F2} n={hit.normal:F2} self={hit.collider.transform.IsChildOf(p.transform)} ; ");
            return sb.ToString();
        }

        public static string Describe()
        {
            var k = WornKey;
            return $"gliding={Gliding} v={V.x:F2},{V.y:F2},{V.z:F2} b/t ({V.magnitude * K:F1} m/s) boost={BoostTicks} worn={k ?? "-"} used={(k != null ? UsedOf(k) + unsyncedWear : 0)}/{MaxUses} canGlide={CanGlide} lastHit={LastHit}";
        }
    }

    /// <summary>
    /// The elytra on a player's back (Minecraft's ElytraModel): two wings from the shoulders, 10x20x2 texture pixels each,
    /// folded in a V when walking, out sideways while gliding. A player is gliding when it's ours and open, or when
    /// someone else is in the air and fast.
    /// </summary>
    public class ElytraWings : MonoBehaviour
    {
        PlayerControllerB p;
        Transform bone, left, right;
        BoxCollider col;
        bool isLocal;
        float spread;
        Vector3 lastPos;
        static Material mat;

        public static void Build(PlayerControllerB p, Transform bone, bool isLocal, List<GameObject> into)
        {
            var m = Mat();
            var col = bone.GetComponent<BoxCollider>();
            if (m == null || col == null) return;
            var go = new GameObject(ArmorModels.PartName);
            go.transform.SetParent(bone, false);
            int layer = p.thisPlayerModel != null ? p.thisPlayerModel.gameObject.layer : p.gameObject.layer;
            go.layer = layer;
            var w = go.AddComponent<ElytraWings>();
            w.p = p; w.bone = bone; w.col = col; w.isLocal = isLocal; w.lastPos = p.transform.position;
            float px = w.Px();
            w.left = Wing(go.transform, "left", m, px, false, layer, p, isLocal);
            w.right = Wing(go.transform, "right", m, px, true, layer, p, isLocal);
            into.Add(go);
        }

        static Material Mat()
        {
            if (mat != null) return mat;
            var tex = McAssets.LoadTexture("entity/equipment/wings/elytra") ?? McAssets.LoadTexture("entity/elytra");
            if (tex == null) return null;
            tex.filterMode = FilterMode.Point;
            mat = Atlas.MakeLit("LMC_Elytra", cutout: true, doubleSided: true, emissive: false);
            mat.SetTexture("_BaseColorMap", tex);
            mat.mainTexture = tex;
            mat.SetFloat("_Smoothness", 0.15f);
            mat.SetFloat("_AlbedoAffectEmissive", 1f);
            UnityEngine.Rendering.HighDefinition.HDMaterial.SetUseEmissiveIntensity(mat, true);
            UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveColor(mat, Color.white);
            UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveIntensity(mat, ArmorModels.AmbientEV, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
            return mat;
        }

        /// <summary>A texture pixel in metres: the body's width over Minecraft's 8.</summary>
        float Px()
        {
            var (_, _, w) = Extents();
            return Mathf.Clamp(w / 8f, 0.03f, 0.08f);
        }

        /// <summary>The upper body's box in the player's frame: its top centre at the back, and its width.</summary>
        (Vector3 topBack, float halfDepth, float width) Extents()
        {
            var t = p.transform;
            float minF = float.MaxValue, maxF = float.MinValue, maxU = float.MinValue, minR = float.MaxValue, maxR = float.MinValue;
            Vector3 c = Vector3.zero;
            var h = col.size * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                var corner = col.center + new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y, (i & 4) != 0 ? h.z : -h.z);
                var wpos = bone.TransformPoint(corner);
                c += wpos / 8f;
                var l = t.InverseTransformPoint(wpos);
                minF = Mathf.Min(minF, l.z); maxF = Mathf.Max(maxF, l.z); maxU = Mathf.Max(maxU, l.y); minR = Mathf.Min(minR, l.x); maxR = Mathf.Max(maxR, l.x);
            }
            var lc = t.InverseTransformPoint(c);
            return (new Vector3(lc.x, maxU, minF), (maxF - minF) * 0.5f, (maxR - minR) * t.lossyScale.x);
        }

        static Transform Wing(Transform parent, string name, Material m, float px, bool mirror, int layer, PlayerControllerB p, bool isLocal)
        {
            var go = new GameObject("LMC_ElytraWing_" + name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = WingMesh(px, mirror, m.mainTexture);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = isLocal && !ArmorModels.DevShowLocal ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            if (p.thisPlayerModel != null) r.renderingLayerMask = p.thisPlayerModel.renderingLayerMask;
            Atlas.NoDecals(r);
            return go.transform;
        }

        /// <summary>
        /// One wing, its pivot at the shoulder: 10 px across the back (towards the other side), 20 down, 2 thick behind.
        /// Minecraft's box at (22, 0) on the 64x32 texture; the right wing is the left one mirrored.
        /// </summary>
        static Mesh WingMesh(float px, bool mirror, Texture tex)
        {
            float tw = tex != null ? tex.width : 64, th = tex != null ? tex.height : 32;
            float sx = mirror ? -1f : 1f;
            int u = 22, v = 0, w = 10, h = 20, d = 2;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            // a face from its four corners (top left, top right, bottom left, bottom right as seen from outside) and its texture rect
            void Face(Vector3 tl, Vector3 tr, Vector3 bl, Vector3 br, float x, float y, float fw, float fh)
            {
                if (mirror) { x += fw; fw = -fw; }
                int i = verts.Count;
                verts.Add(tl * px); verts.Add(tr * px); verts.Add(bl * px); verts.Add(br * px);
                uvs.Add(new Vector2(x / tw, 1f - y / th)); uvs.Add(new Vector2((x + fw) / tw, 1f - y / th));
                uvs.Add(new Vector2(x / tw, 1f - (y + fh) / th)); uvs.Add(new Vector2((x + fw) / tw, 1f - (y + fh) / th));
                var n = Vector3.Cross(tr - tl, bl - tl).normalized;
                var center = (tl + tr + bl + br) * 0.25f;
                var box = new Vector3(5f * sx, -10f, -1f);
                if (Vector3.Dot(n, center - box) < 0f) n = -n;
                for (int k = 0; k < 4; k++) norms.Add(n);
                if (Vector3.Dot(Vector3.Cross(verts[i + 1] - verts[i], verts[i + 2] - verts[i]), n) > 0f) tris.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
                else tris.AddRange(new[] { i, i + 2, i + 1, i + 2, i + 3, i + 1 });
            }
            // corners: x from 0 (the shoulder) to 10 (across the back), y 0 to -20, z 0 (against the back) to -2 (out)
            Vector3 C(float x, float y, float z) => new Vector3(x * sx, y, z);
            float X0 = 0, X1 = w, Y0 = 0, Y1 = -h, Z0 = 0, Z1 = -d;
            Face(C(X1, Y0, Z1), C(X0, Y0, Z1), C(X1, Y1, Z1), C(X0, Y1, Z1), u + d + w + d, v + d, w, h); // the outside (seen from behind)
            Face(C(X0, Y0, Z0), C(X1, Y0, Z0), C(X0, Y1, Z0), C(X1, Y1, Z0), u + d, v + d, w, h);         // against the back
            Face(C(X0, Y0, Z1), C(X0, Y0, Z0), C(X0, Y1, Z1), C(X0, Y1, Z0), u, v + d, d, h);             // the shoulder edge
            Face(C(X1, Y0, Z0), C(X1, Y0, Z1), C(X1, Y1, Z0), C(X1, Y1, Z1), u + d + w, v + d, d, h);     // the far edge
            Face(C(X0, Y0, Z1), C(X1, Y0, Z1), C(X0, Y0, Z0), C(X1, Y0, Z0), u + d, v, w, d);             // top
            Face(C(X0, Y1, Z0), C(X1, Y1, Z0), C(X0, Y1, Z1), C(X1, Y1, Z1), u + d + w, v, w, d);         // bottom
            var mesh = new Mesh { name = "LMC_ElytraWing" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static string DevDescribe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var w in FindObjectsOfType<ElytraWings>())
            {
                if (w.p == null || w.left == null) continue;
                var lr = w.left.GetComponent<MeshRenderer>(); var rr = w.right.GetComponent<MeshRenderer>();
                var pl = w.p.transform.InverseTransformPoint(lr.bounds.center); var pr = w.p.transform.InverseTransformPoint(rr.bounds.center);
                sb.Append($"{w.p.playerUsername}: spread={w.spread:F2} left@{pl:F2} size={lr.bounds.size:F2} on={lr.enabled}/{lr.gameObject.activeInHierarchy} right@{pr:F2} size={rr.bounds.size:F2} rotL={w.left.localEulerAngles:F0} ; ");
            }
            return sb.ToString();
        }

        /// <summary>(dev) everyone's wings spread, to photograph them standing still.</summary>
        public static bool DevSpread;

        bool GlidingNow()
        {
            if (DevSpread) return true;
            if (isLocal) return Elytra.Gliding;
            // someone else: in the air and fast (their wings aren't synced; their speed is)
            float dt = Mathf.Max(Time.deltaTime, 1e-3f);
            var v = (p.transform.position - lastPos) / dt;
            return !p.isClimbingLadder && v.magnitude > 9f && !Physics.Raycast(p.transform.position + Vector3.up * 0.2f, Vector3.down, 1.2f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore);
        }

        void LateUpdate()
        {
            if (p == null || bone == null || left == null || right == null) return;
            bool g = GlidingNow();
            lastPos = p.transform.position;
            spread = Mathf.MoveTowards(spread, g ? 1f : 0f, Time.deltaTime * 4f);
            var t = p.transform;
            var (topBack, _, _) = Extents();
            float px = Px();
            // Minecraft's pose: x 15 deg (the bottom out behind), z 15 deg (splayed); gliding x 20, z 90 (straight out); crouching x 40, z 45
            float xr, zr;
            if (p.isCrouching && !g) { xr = 40f; zr = 45f; }
            else { xr = Mathf.Lerp(15f, 20f, spread); zr = Mathf.Lerp(15f, 90f, spread); }
            transform.position = t.TransformPoint(topBack);
            transform.rotation = t.rotation;
            left.localPosition = new Vector3(-5f * px, 0f, 0f);
            right.localPosition = new Vector3(5f * px, 0f, 0f);
            left.localRotation = Quaternion.Euler(0f, 0f, -zr) * Quaternion.Euler(xr, 0f, 0f);
            right.localRotation = Quaternion.Euler(0f, 0f, zr) * Quaternion.Euler(xr, 0f, 0f);
        }
    }
}
