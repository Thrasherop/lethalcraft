using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244) Minecraft's experience orbs: XP comes out as orbs where it was earned (the mob, the ore, the furnace, the
    /// breeding pair, a thrown bottle) and flies to the player once they're within 8 blocks; Mending takes it first, to
    /// repair what's held. Dying drops 7 a level (100 at most) as orbs anyone can pick up, the rest is lost (not with
    /// keepInventory). Every machine sees the orbs; only the one that picks one up gets its XP.
    /// </summary>
    [HarmonyPatch]
    public class XpOrb : MonoBehaviour
    {
        /// <summary>Minecraft's orb sizes (ExperienceOrb.getExperienceValue) and the icon each shows.</summary>
        static readonly int[] Sizes = { 2477, 1237, 617, 307, 149, 73, 37, 17, 7, 3, 1 };
        public const ulong Anyone = ulong.MaxValue;
        public static int Spawned, Collected, Mended, Dropped; // (dev/tests)
        static readonly List<Mesh> meshes = new List<Mesh>();
        static Material mat;
        static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        ulong owner; int value; float age, life = 300f;
        Vector3 vel; bool landed, given;
        MeshRenderer mr;

        /// <summary>Every machine: XP earned at a place, as orbs (owner Anyone: whoever gets close).</summary>
        public static void Spawn(ulong owner, int amount, Vector3 at)
        {
            while (amount > 0)
            {
                int v = 1;
                foreach (var s in Sizes) if (amount >= s) { v = s; break; }
                amount -= v;
                Make(owner, v, at);
            }
        }

        static int IconOf(int v)
        {
            for (int i = 0; i < Sizes.Length; i++) if (v >= Sizes[i]) return Sizes.Length - 1 - i;
            return 0;
        }

        static void Make(ulong owner, int v, Vector3 at)
        {
            if (mat == null)
            {
                var t = McAssets.Available ? McAssets.LoadTexture("entity/experience_orb") : null;
                mat = Atlas.MakeLit("LMC_XpOrb", true, true, true);
                if (t != null) { t.filterMode = FilterMode.Point; mat.SetTexture("_BaseColorMap", t); }
                for (int i = 0; i < 16; i++)
                {
                    // (the sheet: 4 by 4 icons of 16 pixels, the first at the top left)
                    float u0 = (i % 4) / 4f, v1 = 1f - (i / 4) / 4f, u1 = u0 + 0.25f, v0 = v1 - 0.25f;
                    var m = new Mesh { name = "LMC_XpOrb" + i };
                    m.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
                    m.SetUVs(0, new List<Vector2> { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) });
                    m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                    m.RecalculateBounds(); m.RecalculateNormals();
                    meshes.Add(m);
                }
            }
            var go = new GameObject("LMC_XpOrb");
            go.transform.position = at + Vector3.up * 0.3f;
            go.AddComponent<MeshFilter>().sharedMesh = meshes[IconOf(v)];
            var o = go.AddComponent<XpOrb>();
            o.mr = go.AddComponent<MeshRenderer>(); o.mr.sharedMaterial = mat; o.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            o.owner = owner; o.value = v; o.age = Random.Range(0f, 10f);
            // (Minecraft's: out at a little speed each way, up a bit)
            o.vel = new Vector3(Random.Range(-1f, 1f), Random.Range(1.5f, 3f), Random.Range(-1f, 1f)) * BlockWorld.S;
            go.transform.localScale = Vector3.one * 0.3f * BlockWorld.S * (0.6f + 0.1f * IconOf(v));
            Spawned++;
        }

        PlayerControllerB Target()
        {
            var sor = StartOfRound.Instance;
            if (sor == null) return null;
            PlayerControllerB best = null; float bd = 8f * BlockWorld.S; // (Minecraft's: within 8 blocks)
            foreach (var p in sor.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled || p.isPlayerDead) continue;
                if (owner != Anyone && p.actualClientId != owner) continue;
                float d = Vector3.Distance(p.transform.position + Vector3.up, transform.position);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (age > life) { Destroy(gameObject); return; }
            var pos = transform.position;
            var target = age > 0.4f ? Target() : null;
            if (target != null)
            {
                // flying in (Minecraft's: faster the closer it gets)
                var to = target.transform.position + Vector3.up * 0.9f - pos;
                float d = to.magnitude;
                if (d < 0.5f * BlockWorld.S) { Collect(target); return; }
                float pull = 1f - d / (8f * BlockWorld.S);
                vel += to / Mathf.Max(0.01f, d) * pull * pull * 60f * BlockWorld.S * dt;
                vel *= Mathf.Pow(0.3f, dt);
                landed = false;
            }
            else if (!landed) vel += Vector3.down * 16f * BlockWorld.S * dt;
            var step = vel * dt; float len = step.magnitude;
            if (target == null && len > 1e-5f && Physics.Raycast(pos, step / len, out var hit, len + 0.1f, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point + hit.normal * 0.1f;
                if (hit.normal.y > 0.5f) { landed = true; vel = Vector3.zero; }
                else vel = Vector3.Reflect(vel, hit.normal) * 0.3f;
            }
            else if (!landed || target != null) transform.position = pos + step;
            // (fell out of the world: it goes to its owner)
            if (transform.position.y < -500f) { Destroy(gameObject); return; }
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
            // Minecraft's colour: green, its red and blue pulsing (ExperienceOrbRenderer)
            float h = age * 10f;
            block.SetColor("_BaseColor", new Color((Mathf.Sin(h) + 1f) * 0.5f, 1f, (Mathf.Sin(h + 4.1887903f) + 1f) * 0.1f));
            block.SetColor("_EmissiveColor", new Color((Mathf.Sin(h) + 1f) * 0.5f, 1f, (Mathf.Sin(h + 4.1887903f) + 1f) * 0.1f) * 2f);
            mr.SetPropertyBlock(block);
        }

        void Collect(PlayerControllerB p)
        {
            given = true;
            if (p == GameNetworkManager.Instance?.localPlayerController) { Collected += value; Survival.AddXp(Mend(p, value)); }
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            // (one of mine never picked up, the round over: still mine; a death's orbs are lost, Minecraft's)
            if (!given && owner != Anyone && GameNetworkManager.Instance?.localPlayerController is PlayerControllerB me && me.actualClientId == owner) Survival.AddXp(value);
        }

        /// <summary>Minecraft's Mending: an orb's XP repairs a worn Mending item, held or worn (one at random, then another
        /// with what's left), 2 durability a point; what's left is XP.</summary>
        public static int Mend(PlayerControllerB p, int xp)
        {
            if (p == null || xp <= 0) return xp;
            var tried = new HashSet<int>();
            while (xp > 0)
            {
                // (-1: the held tool; 0-3: the armor slots)
                var can = new List<int>();
                if (!tried.Contains(-1) && p.currentlyHeldObjectServer is ToolItem t && t.Used > 0 && Enchants.Level(t.Ench, EnchKind.Mending) > 0) can.Add(-1);
                var worn = Armor.Local;
                for (int s = 0; s < worn.Length; s++)
                    if (!tried.Contains(s) && worn[s] != null && Enchants.Level(Enchants.EnchOf(ItemData.Of(worn[s])), EnchKind.Mending) > 0 && Durability.Of(worn[s]).used > 0) can.Add(s);
                if (can.Count == 0) break;
                int pick = can[Random.Range(0, can.Count)];
                tried.Add(pick);
                int repair;
                if (pick < 0)
                {
                    var tool = (ToolItem)p.currentlyHeldObjectServer;
                    repair = Mathf.Min(xp * 2, tool.Used);
                    BlockNet.RequestMend(tool, repair);
                }
                else
                {
                    var k = worn[pick];
                    int used = Durability.Of(k).used;
                    repair = Mathf.Min(xp * 2, used);
                    string nk = ItemData.Is(k, Elytra.Key)
                        ? ItemData.With(k, Enchants.Data(used - repair, Enchants.EnchOf(ItemData.Of(k))))
                        : ArmorWear.WithDamage(k, used - repair, chance: true);
                    Armor.SetLocal(pick, nk);
                }
                Mended += repair;
                xp -= (repair + 1) / 2;
            }
            return Mathf.Max(0, xp);
        }

        /// <summary>Owner client, dying: 7 XP a level (100 at most) drops as orbs, the rest is lost (keepInventory: kept).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.KillPlayer)), HarmonyPostfix]
        static void DropOnDeath(PlayerControllerB __instance)
        {
            if (__instance == null || !__instance.IsOwner || !__instance.isPlayerDead || Commands.KeepInventory || Survival.Instance == null) return;
            int lvl = Survival.Instance.XpLevel, drop = Mathf.Min(100, 7 * lvl);
            if (Survival.XpTotal <= 0) return;
            Survival.LoseAllXp();
            if (drop > 0) { BlockNet.RequestXpDrop(drop, __instance.positionOfDeath); Dropped += drop; }
        }
    }
}
