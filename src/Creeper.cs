using System.Collections.Generic;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's creeper (#59), a Lethal Company monster: it roams outside, walks up to the nearest player it sees, hisses
    /// and swells for 1.5 s once it's close, then blows up like TNT (blocks, a crater, the game's own blast damage). Walk
    /// away in time and it calms down. Its model is a box model textured from your own Minecraft install
    /// (entity/creeper/creeper.png), like the blocks; green boxes without one.
    /// Built at startup without asset bundles: the prefab is made in code like the mod's items, and registered with
    /// LethalLib's enemy spawning ([Mobs] in the config).
    /// </summary>
    public class CreeperAI : EnemyAI
    {
        public const int Roam = 0, Chase = 1, Fuse = 2;
        public const string Name = "Creeper";
        public static EnemyType Type;

        public static float FuseSeconds = 1.5f;     // Minecraft: 30 ticks
        public static float StartFuseAt = 3.2f;     // m: Minecraft's 3 blocks, in the game's own sizes (players are ~2 m)
        public static float CancelFuseAt = 7f;      // m: Minecraft's 7 blocks
        public static float SightRange = 20f;
        public static float ChaseSpeed = 3.6f, RoamSpeed = 2.2f;

        Transform model;
        readonly Transform[] legs = new Transform[4];
        float walkPhase;
        Material mat;
        float fuseVisual; // (every client: 0..1 while swelling)
        float fuseStartedAt = -1f; // (server)
        bool hissed;
        float hurtFlash;
        Vector3 lastPos;

        public override void Start()
        {
            base.Start();
            model = transform.Find("Model");
            if (model != null) { legs[0] = model.Find("LegFL"); legs[1] = model.Find("LegFR"); legs[2] = model.Find("LegBL"); legs[3] = model.Find("LegBR"); }
            var r = model != null ? model.GetComponentInChildren<MeshRenderer>() : null;
            if (r != null) { mat = new Material(r.sharedMaterial); foreach (var mr in model.GetComponentsInChildren<MeshRenderer>()) mr.sharedMaterial = mat; }
            if (IsServer)
            {
                agent.speed = RoamSpeed;
                StartSearch(transform.position);
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) return;
            switch (currentBehaviourStateIndex)
            {
                case Roam:
                    {
                        var seen = CheckLineOfSightForClosestPlayer(70f, (int)SightRange, 6);
                        if (seen != null && PlayerIsTargetable(seen))
                        {
                            if (currentSearch.inProgress) StopSearch(currentSearch);
                            targetPlayer = seen;
                            agent.speed = ChaseSpeed;
                            SwitchToBehaviourState(Chase);
                        }
                        else if (!currentSearch.inProgress) StartSearch(transform.position);
                        break;
                    }
                case Chase:
                    {
                        if (targetPlayer == null || !PlayerIsTargetable(targetPlayer) || Vector3.Distance(transform.position, targetPlayer.transform.position) > SightRange * 1.5f)
                        {
                            targetPlayer = null; movingTowardsTargetPlayer = false;
                            agent.speed = RoamSpeed;
                            SwitchToBehaviourState(Roam);
                            StartSearch(transform.position);
                            break;
                        }
                        SetMovingTowardsTargetPlayer(targetPlayer);
                        if (Vector3.Distance(transform.position, targetPlayer.transform.position) < StartFuseAt)
                        {
                            fuseStartedAt = Time.time;
                            agent.speed = 0f;
                            SwitchToBehaviourState(Fuse);
                        }
                        break;
                    }
                case Fuse:
                    {
                        bool away = targetPlayer == null || !PlayerIsTargetable(targetPlayer) || Vector3.Distance(transform.position, targetPlayer.transform.position) > CancelFuseAt;
                        if (away)
                        {
                            fuseStartedAt = -1f;
                            agent.speed = ChaseSpeed;
                            SwitchToBehaviourState(targetPlayer != null ? Chase : Roam);
                            break;
                        }
                        if (Time.time - fuseStartedAt >= FuseSeconds) Explode();
                        break;
                    }
            }
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            // the fuse: it swells and flashes white (every client, from the state the host sends)
            if (currentBehaviourStateIndex == Fuse)
            {
                if (!hissed) { hissed = true; Sounds.Play("fuse", transform.position + Vector3.up, 1f, 1f, 24f); }
                fuseVisual = Mathf.Min(1f, fuseVisual + Time.deltaTime / FuseSeconds);
                // (the host checks the fuse between AI intervals: it blows up on the next frame it's due)
                if (IsServer && fuseStartedAt >= 0f && Time.time - fuseStartedAt >= FuseSeconds) Explode();
            }
            else { hissed = false; fuseVisual = Mathf.Max(0f, fuseVisual - Time.deltaTime * 2f); }
            if (model != null)
            {
                float s = 1f + 0.18f * fuseVisual * (1f + 0.15f * Mathf.Sin(Time.time * 40f));
                model.localScale = new Vector3(s, 1f + 0.08f * fuseVisual, s);
            }
            // walking: Minecraft's leg swing (diagonal pairs together), as fast as it moves
            float speed = Vector3.Distance(transform.position, lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
            lastPos = transform.position;
            walkPhase += Time.deltaTime * Mathf.Clamp(speed, 0f, 6f) * 3.2f;
            float swing = Mathf.Clamp01(speed / 2f) * 40f * Mathf.Sin(walkPhase);
            for (int i = 0; i < 4; i++) if (legs[i] != null) legs[i].localRotation = Quaternion.Euler((i == 0 || i == 3) ? swing : -swing, 0f, 0f);
            hurtFlash = Mathf.Max(0f, hurtFlash - Time.deltaTime * 3f);
            if (mat != null)
            {
                // Minecraft's white flashing while it swells (every few frames), red when hurt
                bool white = fuseVisual > 0f && Mathf.Repeat(Time.time * (4f + 8f * fuseVisual), 1f) < 0.5f;
                var c = hurtFlash > 0f ? Color.Lerp(Color.white, Color.red, hurtFlash) : Color.white;
                mat.SetColor("_BaseColor", c);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveIntensity(mat, white ? ArmorModels.AmbientEV + 6f : ArmorModels.AmbientEV, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
            }
        }

        bool exploded;

        /// <summary>Host: the blast (blocks, a crater, the game's explosion damage on every client), and it's gone.</summary>
        void Explode()
        {
            if (exploded || !IsServer) return;
            exploded = true;
            var pos = transform.position + Vector3.up * 0.9f;
            float r = 3f * BlockWorld.S; // (Minecraft: power 3, TNT's 4)
            try { ServerLogic.ExplodeBlocks(pos, r, -1, carveGround: Plugin.CreeperCraters.Value); }
            catch (System.Exception e) { Plugin.Log.LogWarning("creeper blocks: " + e.Message); }
            BlockNet.ServerExplosion(pos, r);
            ServerLogic.Noise(pos, 40f, 1f);
            KillEnemyOnOwnerClient(overrideDestroy: true);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) return;
            hurtFlash = 1f;
            Sounds.Play("creeper.hurt", transform.position + Vector3.up, 0.8f, Random.Range(0.8f, 1.2f), 20f);
            enemyHP -= force;
            // hit while swelling: like Minecraft, it keeps going (no stun)
            if (enemyHP <= 0 && IsOwner)
            {
                Sounds.Play("creeper.death", transform.position + Vector3.up, 0.9f, 1f, 20f);
                KillEnemyOnOwnerClient(overrideDestroy: true);
            }
            else if (IsOwner && playerWhoHit != null && currentBehaviourStateIndex == Roam)
            {
                targetPlayer = playerWhoHit; agent.speed = ChaseSpeed; SwitchToBehaviourState(Chase);
            }
        }

        // ------------------------------------------------------------------ building it (startup)
        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.Creepers.Value) return;
            var prefab = LethalLib.Modules.NetworkPrefabs.CreateNetworkPrefab("LMC_Creeper");
            prefab.layer = 19; // Enemies
            var no = prefab.GetComponent<NetworkObject>();
            no.AutoObjectParentSync = false;

            var agent = prefab.AddComponent<NavMeshAgent>();
            agent.speed = RoamSpeed; agent.angularSpeed = 400f; agent.acceleration = 12f;
            agent.radius = 0.45f; agent.height = 2f; agent.stoppingDistance = 0.5f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            // the model (a child: it swells on its own) and an Animator for the base class (it has no animations)
            var modelGo = new GameObject("Model");
            modelGo.transform.SetParent(prefab.transform, false);
            modelGo.layer = 19;
            modelGo.AddComponent<Animator>();
            BuildModel(modelGo.transform);

            // what players hit and walk into
            var col = new GameObject("Collision");
            col.transform.SetParent(prefab.transform, false);
            col.layer = 19;
            col.tag = "Enemy";
            var box = col.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.9f, 2f, 0.9f);
            box.center = new Vector3(0f, 1f, 0f);
            var rb = col.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var detect = col.AddComponent<EnemyAICollisionDetect>();

            var eye = new GameObject("Eye").transform;
            eye.SetParent(prefab.transform, false);
            eye.localPosition = new Vector3(0f, 1.8f, 0.2f);

            AudioSource Audio(string name)
            {
                var go = new GameObject(name); go.transform.SetParent(prefab.transform, false); go.transform.localPosition = Vector3.up;
                var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear; a.maxDistance = 25f;
                return a;
            }

            // scan node
            var scan = new GameObject("ScanNode");
            scan.transform.SetParent(prefab.transform, false);
            scan.transform.localPosition = Vector3.up * 1.2f;
            scan.layer = 22;
            var sc = scan.AddComponent<BoxCollider>(); sc.isTrigger = true; sc.size = Vector3.one * 0.6f;
            var sn = scan.AddComponent<ScanNodeProperties>();
            sn.headerText = Name; sn.subText = "Hisses, then explodes"; sn.maxRange = 30; sn.minRange = 1;
            sn.requiresLineOfSight = true; sn.nodeType = 1; sn.creatureScanID = -1;

            var type = ScriptableObject.CreateInstance<EnemyType>();
            type.name = "LMC_CreeperType";
            type.enemyName = Name;
            type.enemyPrefab = prefab;
            type.isOutsideEnemy = true;
            type.isDaytimeEnemy = false;
            type.MaxCount = Plugin.CreeperMaxCount.Value;
            type.PowerLevel = 1f;
            type.probabilityCurve = AnimationCurve.Constant(0f, 1f, 1f);
            type.numberSpawnedFalloff = AnimationCurve.Constant(0f, 1f, 1f);
            type.useNumberSpawnedFalloff = false;
            type.canDie = true;
            type.destroyOnDeath = true;
            type.canBeStunned = true;
            type.stunTimeMultiplier = 1f;
            type.doorSpeedMultiplier = 1f;
            type.canSeeThroughFog = false;
            type.disableAnimatorWhenFar = false;
            type.pushPlayerForce = 0.3f;
            type.pushPlayerDistance = 1.2f;
            type.miscAnimations = new MiscAnimation[0];
            type.audioClips = new AudioClip[0];
            Type = type;

            var ai = prefab.AddComponent<CreeperAI>();
            ai.enemyType = type;
            ai.agent = agent;
            ai.eye = eye;
            ai.creatureSFX = Audio("SFX");
            ai.creatureVoice = Audio("Voice");
            ai.creatureAnimator = modelGo.GetComponent<Animator>();
            ai.enemyHP = 3;
            ai.AIIntervalTime = 0.2f;
            ai.updatePositionThreshold = 0.2f;
            ai.syncMovementSpeed = 0.2f;
            ai.exitVentAnimationTime = 0f;
            ai.ventAnimationFinished = true;
            ai.enemyBehaviourStates = new[]
            {
                new EnemyBehaviourState { name = "Roam", parameterString = "roam" },
                new EnemyBehaviourState { name = "Chase", parameterString = "chase" },
                new EnemyBehaviourState { name = "Fuse", parameterString = "fuse" },
            };
            detect.mainScript = ai;
            detect.canCollideWithEnemies = false;

            LethalLib.Modules.Enemies.RegisterEnemy(type, Plugin.CreeperRarity.Value, LethalLib.Modules.Levels.LevelTypes.All,
                LethalLib.Modules.Enemies.SpawnType.Outside, (TerminalNode)null, (TerminalKeyword)null);
            Plugin.Log.LogInfo($"Creeper registered (outside, rarity {Plugin.CreeperRarity.Value}, at most {type.MaxCount})");
        }

        // ------------------------------------------------------------------ the model: Minecraft's creeper, 64x32 texture
        const float Px = 1f / 16f; // a texture pixel, in blocks

        static void BuildModel(Transform parent)
        {
            float k = Px * BlockWorld.S * 0.8f; // (a block is 1.4 m here: a full-size creeper would stand 2.3 m; 0.8 makes it 1.85)
            var tex = McAssets.LoadTexture("entity/creeper/creeper");
            if (tex == null)
            {
                tex = new Texture2D(1, 1); tex.SetPixel(0, 0, new Color(0.35f, 0.65f, 0.3f)); tex.Apply();
            }
            tex.filterMode = FilterMode.Point;
            if (sharedMat == null)
            {
                sharedMat = Atlas.MakeLit("LMC_Creeper", cutout: true, doubleSided: false, emissive: false);
                sharedMat.SetTexture("_BaseColorMap", tex);
                sharedMat.mainTexture = tex;
                sharedMat.SetFloat("_Smoothness", 0.15f);
                sharedMat.SetFloat("_AlbedoAffectEmissive", 1f);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetUseEmissiveIntensity(sharedMat, true);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveColor(sharedMat, Color.white);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveIntensity(sharedMat, ArmorModels.AmbientEV, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(sharedMat);
            }
            float tw = tex.width > 1 ? tex.width : 64, th = tex.height > 1 ? tex.height : 32;
            // (Minecraft's creeper: head 8x8x8 at 0,0; body 8x12x4 at 16,16; four legs 4x6x4 at 0,16; feet at y 0, facing +z)
            Part(parent, "Head", new Vector3(0, 22, 0), new Vector3(8, 8, 8), 0, 0, k, tw, th);
            Part(parent, "Body", new Vector3(0, 12, 0), new Vector3(8, 12, 4), 16, 16, k, tw, th);
            // (legs hang from a hip pivot, to swing as it walks)
            foreach (var (name, x, z) in new[] { ("LegFL", -2f, 4f), ("LegFR", 2f, 4f), ("LegBL", -2f, -4f), ("LegBR", 2f, -4f) })
            {
                var hip = new GameObject(name).transform;
                hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, 6f, z) * k;
                Part(hip, "Leg", new Vector3(0, -3, 0), new Vector3(4, 6, 4), 0, 16, k, tw, th);
            }
        }

        /// <summary>A box of Minecraft's model (sizes in texture pixels, its texture patch at u, v; its front faces +z).</summary>
        static void Part(Transform parent, string name, Vector3 center, Vector3 size, int u, int v, float k, float tw, float th)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center * k;
            go.layer = 19;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            float hx = size.x * k / 2f, hy = size.y * k / 2f, hz = size.z * k / 2f;
            int w = (int)size.x, h = (int)size.y, d = (int)size.z;
            void Face(Vector3 n, float hn, Vector3 xd, float ex, Vector3 yd, float ey, float px, float py, float pw, float ph)
            {
                var c = n * hn;
                int i = verts.Count;
                verts.Add(c - xd * ex - yd * ey); uvs.Add(new Vector2(px / tw, 1f - py / th));
                verts.Add(c + xd * ex - yd * ey); uvs.Add(new Vector2((px + pw) / tw, 1f - py / th));
                verts.Add(c - xd * ex + yd * ey); uvs.Add(new Vector2(px / tw, 1f - (py + ph) / th));
                verts.Add(c + xd * ex + yd * ey); uvs.Add(new Vector2((px + pw) / tw, 1f - (py + ph) / th));
                for (int q = 0; q < 4; q++) norms.Add(n);
                if (Vector3.Dot(Vector3.Cross(verts[i + 1] - verts[i], verts[i + 2] - verts[i]), n) > 0f) tris.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
                else tris.AddRange(new[] { i, i + 2, i + 1, i + 2, i + 3, i + 1 });
            }
            Vector3 up = Vector3.up, front = Vector3.forward, right = Vector3.right;
            Face(front, hz, -right, hx, -up, hy, u + d, v + d, w, h);               // front (the face)
            Face(-front, hz, right, hx, -up, hy, u + 2 * d + w, v + d, w, h);       // back
            Face(right, hx, -front, hz, -up, hy, u, v + d, d, h);                   // its right side
            Face(-right, hx, front, hz, -up, hy, u + d + w, v + d, d, h);           // its left side
            Face(up, hy, -right, hx, front, hz, u + d, v, w, d);                    // top
            Face(-up, hy, -right, hx, -front, hz, u + d + w, v, w, d);              // bottom
            var mesh = new Mesh { name = "LMC_Creeper_" + name };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = sharedMat;
            Atlas.NoDecals(mr);
        }
    }
}
