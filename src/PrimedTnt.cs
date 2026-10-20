using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244) Minecraft's PrimedTnt: lit TNT is a thing of its own, not a block: it hops up a little as it's lit, falls,
    /// drifts on a water current, is flung by other explosions, and goes off when its fuse is out (4 s, or half to one and
    /// a half seconds lit by another blast). Under water it breaks no blocks (what's near still gets hurt).
    /// </summary>
    public class PrimedTntAI : MinecraftMobAI
    {
        public static EnemyType Type;
        protected override string Sound => "tnt";
        protected override bool Passive => true;
        protected override bool Swims => true; // (it moves itself: no navmesh)
        protected override int Xp => 0;
        public override bool GnawsBlocks => false;
        protected override bool HatesLight => false;
        protected override void SwimAI() { }
        protected override void ServerDrops(Vector3 at) { }
        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1) { } // (a swing doesn't touch it)

        /// <summary>Its speed, blocks a tick (Minecraft's units).</summary>
        public Vector3 Vel;
        public int Fuse = 80;
        public int Frame;
        float tickAcc, shownFuseAt; int shownFuse = 80; bool done;
        public static int Spawned, Exploded, Flung, Wet; // (dev/tests)
        static int Mask => StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer);

        /// <summary>Host: TNT lit at k: the block goes, the lit TNT comes (Minecraft's TntBlock.prime).</summary>
        public static bool ServerPrime(BlockKey k, int fuse)
        {
            var w = BlockWorld.Instance;
            if (Type == null || w == null || RoundManager.Instance == null) return false;
            var at = w.WorldCenter(k) - w.FrameDirToWorld(k.Frame, Vector3.up) * (0.5f * BlockWorld.S);
            BlockNet.ServerBroadcastOp(Op.Remove(k, false));
            MobSpawns.DevSpawnUntil = Time.time + 1f; // (not a natural spawn)
            var r = RoundManager.Instance.SpawnEnemyGameObject(at, 0f, -1, Type);
            if (!r.TryGet(out Unity.Netcode.NetworkObject no) || !(no.GetComponent<PrimedTntAI>() is PrimedTntAI t)) return false;
            float rot = Random.value * Mathf.PI * 2f;
            t.Vel = new Vector3(-Mathf.Sin(rot) * 0.02f, 0.2f, -Mathf.Cos(rot) * 0.02f);
            t.Fuse = fuse; t.Frame = k.Frame;
            BlockNet.ServerMobState(t.NetworkObjectId, 1, (byte)Mathf.Clamp(fuse, 0, 255));
            BlockNet.ServerSound(at, "fuse", 1f, 1f);
            ServerLogic.Noise(at, 12f, 0.6f);
            Spawned++;
            return true;
        }

        public override void ReceiveState(byte a, byte b)
        {
            if (a == 1) { shownFuse = b; shownFuseAt = Time.time; }
            else base.ReceiveState(a, b);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            Look();
            if (!IsServer || done) return;
            tickAcc += Time.deltaTime;
            int n = 0;
            while (tickAcc >= 0.05f && n++ < 4) { tickAcc -= 0.05f; Tick(); if (done) return; }
            SyncPositionToClients();
        }

        /// <summary>Host: Minecraft's PrimedTnt.tick: gravity 0.04, the air's drag 0.98, the ground's 0.7.</summary>
        void Tick()
        {
            float S = BlockWorld.S;
            Vel.y -= 0.04f;
            Vel += WaterFlow.FlowAt(transform.position + Vector3.up * 0.3f * S) * 0.014f;
            var pos = transform.position;
            var step = Vel * S;
            bool ground = false;
            // (down or up first, then across: a box half a block wide)
            if (step.y < 0f)
            {
                if (Physics.Raycast(pos + Vector3.up * 0.1f * S, Vector3.down, out var h, -step.y + 0.1f * S, Mask, QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<EnemyAI>() == null) { pos.y = h.point.y; ground = true; }
                else pos.y += step.y;
            }
            else if (step.y > 0f)
            {
                if (Physics.Raycast(pos + Vector3.up * 0.9f * S, Vector3.up, out var h, step.y + 0.05f * S, Mask, QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<EnemyAI>() == null) Vel.y = 0f;
                else pos.y += step.y;
            }
            var hor = new Vector3(step.x, 0f, step.z);
            if (hor.sqrMagnitude > 1e-8f)
            {
                if (Physics.Raycast(pos + Vector3.up * 0.5f * S, hor.normalized, out var h, hor.magnitude + 0.49f * S, Mask, QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<EnemyAI>() == null) { Vel.x = 0f; Vel.z = 0f; }
                else pos += hor;
            }
            transform.position = pos;
            Vel *= 0.98f;
            if (ground) Vel = new Vector3(Vel.x * 0.7f, Vel.y * -0.5f, Vel.z * 0.7f);
            if (pos.y < -1000f || --Fuse <= 0) Explode();
        }

        void Explode()
        {
            done = true;
            float S = BlockWorld.S;
            var at = transform.position + Vector3.up * 0.0625f * S;
            float r = 3.6f * S;
            // (Minecraft's: in water the blast breaks no blocks)
            bool wet = MoonWater.Depth(at + Vector3.up * 0.3f * S) > 0.05f || WaterSwim.WaterCellAt(at + Vector3.up * 0.3f * S, out _);
            if (wet) { Wet++; ServerFling(at, 4f); } else ServerLogic.ExplodeBlocks(at, r, Frame, carveGround: true);
            BlockNet.ServerExplosion(at, r);
            ServerLogic.Noise(at, 40f, 1f);
            Exploded++;
            KillEnemyOnOwnerClient(overrideDestroy: true);
        }

        /// <summary>Host: Minecraft's explosion knockback on lit TNT near it (within twice its power): flung away, harder the
        /// nearer.</summary>
        public static void ServerFling(Vector3 at, float power)
        {
            if (RoundManager.Instance == null) return;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
            {
                if (!(e is PrimedTntAI t) || t.isEnemyDead || t.done) continue;
                var d = t.transform.position + Vector3.up * 0.5f * BlockWorld.S - at;
                float dist = d.magnitude / BlockWorld.S;
                if (dist > power * 2f || dist < 0.01f) continue;
                float push = 1f - dist / (power * 2f);
                t.Vel += d.normalized * push;
                Flung++;
            }
        }

        /// <summary>Every machine: Minecraft's TntRenderer: it swells in its last half second, and flashes white.</summary>
        void Look()
        {
            var m = transform.Find("Model");
            if (m == null) return;
            float left = shownFuse - (Time.time - shownFuseAt) * 20f;
            float s = 1f;
            if (left < 10f) { float g = 1f - Mathf.Max(0f, left) / 10f; g *= g; g *= g; s = 1f + g * 0.3f; }
            m.localScale = Vector3.one * s;
            var flash = m.Find("Flash");
            if (flash != null) flash.gameObject.SetActive(((int)left / 5) % 2 == 0);
        }

        static Material flashMat;
        public static void Register()
        {
            Type = Register<PrimedTntAI>("PrimedTnt", "Primed TNT", "It's about to go off", 0, 100, 1.0f, 40, 0f, b =>
            {
                float S = BlockWorld.S;
                var go = new GameObject("Tnt"); go.transform.SetParent(b, false);
                go.transform.localPosition = new Vector3(0f, 0.5f * S, 0f);
                go.layer = 19;
                go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.For(Blocks.TNT, 0, 0);
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { Atlas.Opaque, Atlas.Emissive }; Atlas.NoDecals(mr);
                // (its white flash: a white box just over it, half seen)
                var fl = GameObject.CreatePrimitive(PrimitiveType.Cube); fl.name = "Flash";
                Object.Destroy(fl.GetComponent<Collider>());
                fl.transform.SetParent(b, false); fl.transform.localPosition = new Vector3(0f, 0.5f * S, 0f); fl.transform.localScale = Vector3.one * 1.01f * S;
                if (flashMat == null) { flashMat = new Material(Shader.Find("HDRP/Unlit")); flashMat.SetColor("_UnlitColor", new Color(1f, 1f, 1f, 0.5f)); flashMat.SetFloat("_SurfaceType", 1f); }
                fl.GetComponent<MeshRenderer>().sharedMaterial = flashMat;
                fl.SetActive(false);
            }, daytime: true, length: 1.0f);
        }
    }
}
