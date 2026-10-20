using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244, owner) Mobs that spawn wearing armor, Minecraft 26.3's Mob.populateDefaultEquipmentSlots: zombies, husks and
    /// the skeletons now and then (15% at the full local difficulty, which this game's moons are taken to be), of one
    /// material (leather, copper, gold, chainmail, iron or diamond: Minecraft's odds), the helmet first and each piece after
    /// it three times in four (Normal); adult piglins each gold piece one time in ten. Drawn over their model as Minecraft
    /// draws armor on a humanoid. Killed by a player, each piece drops 8.5% of the time (more with Looting), worn.
    /// </summary>
    public static class MobArmor
    {
        public const byte StateBase = 220; // (MobState codes 220-223: the slot; b: the material + 1, 0 none)
        public static readonly string[] Materials = { "leather", "copper", "golden", "chainmail", "iron", "diamond" };
        /// <summary>Minecraft's durability of each piece (helmet, chestplate, leggings, boots) by material.</summary>
        static readonly int[][] Uses =
        {
            new[] { 55, 80, 75, 65 }, new[] { 121, 176, 165, 143 }, new[] { 77, 112, 105, 91 },
            new[] { 165, 240, 225, 195 }, new[] { 165, 240, 225, 195 }, new[] { 363, 528, 495, 429 },
        };
        public static int Rolled, Dropped; // (dev/tests)

        /// <summary>Host, as it spawns: what it wears, told to everyone.</summary>
        public static void ServerRoll(MinecraftMobAI m)
        {
            int rule = m.ArmorRule;
            if (rule == 0) return;
            var worn = new int[4];
            for (int i = 0; i < 4; i++) worn[i] = -1;
            if (rule == 1)
            {
                if (Random.value >= 0.15f * DevChanceScale) return;
                int type = Random.Range(0, 3);
                for (int i = 0; i < 3; i++) if (Random.value < 0.1087f) type++;
                bool first = true;
                for (int slot = 0; slot < 4; slot++) // (Minecraft's order: head, chest, legs, feet)
                {
                    if (!first && Random.value < 0.25f) break;
                    first = false;
                    worn[slot] = type;
                }
            }
            else if (rule == 2)
                for (int slot = 0; slot < 4; slot++) if (Random.value < 0.1f * DevChanceScale) worn[slot] = 2; // (gold)
            if (worn.All(x => x < 0)) return;
            for (int slot = 0; slot < 4; slot++) m.WornArmor[slot] = worn[slot];
            Rolled++;
            for (int slot = 0; slot < 4; slot++) BlockNet.ServerMobState(m.NetworkObjectId, (byte)(StateBase + slot), (byte)(worn[slot] + 1));
        }
        /// <summary>(dev) The chances times this (10: nearly always).</summary>
        public static float DevChanceScale = 1f;

        /// <summary>Host: someone joined: each mob's armor.</summary>
        public static void ServerSyncTo(ulong client)
        {
            if (RoundManager.Instance == null) return;
            foreach (var m in RoundManager.Instance.SpawnedEnemies.OfType<MinecraftMobAI>())
            {
                if (m == null || m.isEnemyDead || m.WornArmor.All(x => x < 0)) continue;
                for (int slot = 0; slot < 4; slot++) BlockNet.ServerMobState(m.NetworkObjectId, (byte)(StateBase + slot), (byte)(m.WornArmor[slot] + 1), client);
            }
        }

        /// <summary>Every machine: one piece (b: the material + 1, 0 none).</summary>
        public static void Receive(MinecraftMobAI m, int slot, byte b)
        {
            if (slot < 0 || slot > 3) return;
            m.WornArmor[slot] = b == 0 || b > Materials.Length ? -1 : b - 1;
            Show(m);
        }

        public static string KeyOf(int material, int slot) => material < 0 ? null : Materials[material] + "_" + LethalMinecraft.Armor.PieceKeys[slot];

        /// <summary>Host, as it dies (killed by a player): Minecraft's 8.5% a piece (Looting more), worn.</summary>
        public static void ServerDrops(MinecraftMobAI m, Vector3 at)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                if (m.WornArmor[slot] < 0) continue;
                int before = MinecraftMobAI.EquipmentDropped;
                m.ServerDropEquipmentPublic(KeyOf(m.WornArmor[slot], slot), Uses[m.WornArmor[slot]][slot], at);
                if (MinecraftMobAI.EquipmentDropped > before) Dropped++;
            }
        }

        /// <summary>Its armor drawn over its model: Minecraft's humanoid armor, a pixel bigger all round (the leggings half
        /// a pixel), from the armor textures (the leggings' own), on the head, body, arms and legs it has.</summary>
        public static void Show(MinecraftMobAI m)
        {
            var model = m.transform.Find("Model");
            if (model == null) return;
            foreach (var old in model.GetComponentsInChildren<Transform>(true).Where(t => t.name == "LMC_MobArmor").ToList()) Object.Destroy(old.gameObject);
            var head = model.Find("Head");
            var body = model.Find("Body");
            if (head == null || body == null) return;
            var bm = body.GetComponent<MeshFilter>()?.sharedMesh;
            if (bm == null) return;
            float k = bm.bounds.size.y / 12f; // (a model pixel: the body is 12 tall)
            Transform Limb(string pivot, string part) { var p = model.Find(pivot); return p != null ? p.Find(part) : null; }
            void Box(Transform on, Vector3 size, float grow, int u, int v, Material mat)
            {
                if (on == null || mat == null) return;
                var go = EntityModels.Part(on, "LMC_MobArmor", Vector3.zero, size, u, v, k, mat, on.gameObject.layer);
                go.transform.localScale = new Vector3((size.x + 2 * grow) / size.x, (size.y + 2 * grow) / size.y, (size.z + 2 * grow) / size.z);
                var r = go.GetComponent<MeshRenderer>(); if (r != null) Atlas.NoDecals(r);
            }
            for (int slot = 0; slot < 4; slot++)
            {
                int mtl = m.WornArmor[slot];
                if (mtl < 0) continue;
                var mat = ArmorModels.MaterialFor(Materials[mtl], slot == 2 ? 2 : 1, color: Materials[mtl] == "leather" ? LeatherDye.ShownColor(KeyOf(mtl, slot)) : -1);
                switch (slot)
                {
                    case 0: Box(head, new Vector3(8, 8, 8), 1f, 0, 0, mat); break;
                    case 1:
                        Box(body, new Vector3(8, 12, 4), 1f, 16, 16, mat);
                        Box(Limb("ArmR", "Arm"), new Vector3(4, 12, 4), 1f, 40, 16, mat);
                        Box(Limb("ArmL", "Arm"), new Vector3(4, 12, 4), 1f, 40, 16, mat);
                        break;
                    case 2:
                        Box(body, new Vector3(8, 12, 4), 0.5f, 16, 16, mat);
                        Box(Limb("LegR", "Leg"), new Vector3(4, 12, 4), 0.5f, 0, 16, mat);
                        Box(Limb("LegL", "Leg"), new Vector3(4, 12, 4), 0.5f, 0, 16, mat);
                        break;
                    case 3:
                        Box(Limb("LegR", "Leg"), new Vector3(4, 12, 4), 1f, 0, 16, mat);
                        Box(Limb("LegL", "Leg"), new Vector3(4, 12, 4), 1f, 0, 16, mat);
                        break;
                }
            }
        }
    }
}
