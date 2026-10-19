using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>(#244) What an item is to the enchantments (the table's and the anvil's): its kind, as Minecraft's tags put
    /// it (a pickaxe or shovel, an axe, a hoe, shears, a sword; a helmet, chestplate, leggings, boots; a bow, crossbow,
    /// fishing rod, elytra, flint and steel; a book or an enchanted book).</summary>
    public static class EnchantTargets
    {
        public static EnchTarget Of(string key)
        {
            if (key == null) return EnchTarget.None;
            string type = ItemData.TypeOf(key);
            switch (type)
            {
                case "book": case AnvilLogic.Book: return EnchTarget.Book;
                case "bow": return EnchTarget.Bow;
                case "fishing_rod": return EnchTarget.FishingRod;
                case "flint_and_steel": return EnchTarget.FlintAndSteel;
            }
            if (!ModItems.ByKey.TryGetValue(key, out var it) || it.spawnPrefab == null) return EnchTarget.None;
            var t = it.spawnPrefab.GetComponent<ToolItem>();
            if (t is CrossbowItem) return EnchTarget.Crossbow;
            if (t != null)
                switch (t.Kind)
                {
                    case ToolKind.Sword: return EnchTarget.Sword;
                    case ToolKind.Axe: return EnchTarget.Axe;
                    case ToolKind.Hoe: return EnchTarget.Hoe;
                    case ToolKind.Shears: return EnchTarget.Shears;
                    case ToolKind.None: return EnchTarget.None;
                    default: return EnchTarget.Tool;
                }
            var a = Armor.Get(key);
            if (a == null || a.Material == "pumpkin") return EnchTarget.None;
            if (a.Key == Elytra.Key) return EnchTarget.Elytra;
            return a.Slot == 0 ? EnchTarget.Helmet : a.Slot == 1 ? EnchTarget.Armor : a.Slot == 2 ? EnchTarget.Leggings : EnchTarget.Boots;
        }

        public static int Enchantability(string key) => Enchants.Enchantability(Of(key), ItemData.TypeOf(key));
    }

    /// <summary>
    /// (#244) Minecraft's enchanting table glyphs: now and then a letter of the Galactic alphabet floats from each bookshelf
    /// that powers it (air between) to the table (EnchantingTableBlock.animateTick: a 1 in 16 chance a tick a shelf, while a
    /// player is near).
    /// </summary>
    public class TableGlyphs : MonoBehaviour
    {
        static TableGlyphs instance;
        static readonly List<Material> mats = new List<Material>();
        static Mesh quad;
        public static int Floated; // (dev/tests)
        float tick;

        public static void Ensure() { if (instance == null) instance = new GameObject("LMC_TableGlyphs").AddComponent<TableGlyphs>(); }

        void Update()
        {
            tick += Time.deltaTime;
            if (tick < 0.05f) return;
            tick = 0f;
            var w = BlockWorld.Instance;
            var me = GameNetworkManager.Instance?.localPlayerController;
            if (w == null || me == null) return;
            foreach (var b in w.Blocks.Values)
            {
                if (b.Data.Def != Blocks.EnchantingTable || b.Go == null) continue;
                var c = w.WorldCenter(b.Key);
                if (Vector3.Distance(c, me.transform.position) > 16f) continue;
                foreach (var shelf in EnchantUI.PoweringShelves(b.Key))
                    if (Random.value < 1f / 16f) Float(w.WorldCenter(shelf) + Vector3.up * BlockWorld.S * 0.5f, c + Vector3.up * BlockWorld.S * 0.6f);
            }
        }

        static void Float(Vector3 from, Vector3 to)
        {
            if (mats.Count == 0)
            {
                for (char ch = 'a'; ch <= 'z'; ch++)
                {
                    var t = McAssets.Available ? McAssets.LoadTexture("particle/sga_" + ch) : null;
                    if (t == null) continue;
                    t.filterMode = FilterMode.Point;
                    var m = Atlas.MakeLit("LMC_Glyph", true, true, true);
                    m.SetTexture("_BaseColorMap", t);
                    m.SetColor("_BaseColor", new Color(0.85f, 0.85f, 1f));
                    mats.Add(m);
                }
                if (mats.Count == 0) mats.Add(null);
            }
            if (mats[0] == null) return;
            if (quad == null)
            {
                quad = new Mesh { name = "LMC_GlyphQuad" };
                quad.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
                quad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.RecalculateBounds(); quad.RecalculateNormals();
            }
            var go = new GameObject("LMC_Glyph");
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats[Random.Range(0, mats.Count)]; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var g = go.AddComponent<Glyph>(); g.from = from + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0f, 0.6f), Random.Range(-0.3f, 0.3f)); g.to = to; g.life = Random.Range(1.5f, 2.0f);
            go.transform.localScale = Vector3.one * 0.12f;
            Floated++;
        }

        class Glyph : MonoBehaviour
        {
            public Vector3 from, to; public float life; float age;
            void Update()
            {
                age += Time.deltaTime;
                float t = age / life;
                if (t >= 1f) { Destroy(gameObject); return; }
                // (Minecraft's: it rises out, then falls in to the table: 1 - (1-t)^2 along the way, lifted at first)
                float e = 1f - (1f - t) * (1f - t);
                transform.position = Vector3.Lerp(from, to, e) + Vector3.up * (1f - t) * 0.6f * (1f - e);
                var cam = Camera.main != null ? Camera.main.transform : null;
                if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
            }
        }
    }
}
