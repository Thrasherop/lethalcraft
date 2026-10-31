using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244) Minecraft's fireworks. A firework star (gunpowder, one to eight dyes, and a fire charge for a large ball, a gold
    /// nugget for a star, a head for a creeper, a feather for a burst; a diamond for a trail, glowstone dust for a twinkle;
    /// a star and dyes again: the colours it fades to) holds an explosion. A rocket (paper, one to three gunpowder: its
    /// flight, and up to seven stars) flies up from the ground where you use it, or pulls you along gliding, and goes off
    /// at the end of its flight in its stars' shapes and colours (FireworkParticles.Starter, reimplemented), hurting what's
    /// within five blocks of it, 5 and 2 a star (FireworkRocketEntity.dealExplosionDamage). A rocket's or star's data is a
    /// text kept by number (EnchSets' raw texts), and that number is on the stack (StackData) and in its key.
    /// </summary>
    public static class Fireworks
    {
        public const string RocketKey = Elytra.RocketKey, StarKey = "firework_star";
        public const ulong NotAttached = ulong.MaxValue;
        /// <summary>(#244) In place of a glider's id: shot from a crossbow (Minecraft's shotAtAngle: straight, no speeding up,
        /// and it goes off on the first thing it hits).</summary>
        public const ulong ShotAngled = ulong.MaxValue - 1;
        public static int CrossbowShots; // (dev/tests)
        public enum Shape : byte { SmallBall, LargeBall, Star, Creeper, Burst }
        static readonly string[] ShapeNames = { "Small Ball", "Large Ball", "Star-shaped", "Creeper-shaped", "Burst" };

        public class Explosion
        {
            public Shape Shape; public List<int> Colors = new List<int>(), Fades = new List<int>(); public bool Trail, Twinkle;
        }
        public class Rocket { public int Flight = 1; public List<Explosion> Explosions = new List<Explosion>(); }

        public static int Launched, Exploded, HurtMobs, HurtMe; public static string LastBurst = "-"; // (dev/tests)
        public static float DevGlow = 2f, DevSize = 1f; // (dev "fwlook": the sparks' brightness and size)

        // ------------------------------------------------------------------ the data, as a text by number
        static string Hex(IEnumerable<int> cs) => string.Join(".", cs.Select(c => (c & 0xFFFFFF).ToString("X6")));
        static List<int> Cols(string s) => string.IsNullOrEmpty(s) ? new List<int>() : s.Split('.').Select(x => int.TryParse(x, NumberStyles.HexNumber, null, out int c) ? c : 0xFFFFFF).ToList();
        static string Text(Explosion e) => $"{(int)e.Shape},{Hex(e.Colors)},{Hex(e.Fades)},{(e.Trail ? 1 : 0)},{(e.Twinkle ? 1 : 0)}";
        static Explosion ParseExp(string s)
        {
            var p = s.Split(',');
            if (p.Length < 5 || !int.TryParse(p[0], out int sh)) return null;
            return new Explosion { Shape = (Shape)Mathf.Clamp(sh, 0, 4), Colors = Cols(p[1]), Fades = Cols(p[2]), Trail = p[3] == "1", Twinkle = p[4] == "1" };
        }
        public static string RocketText(Rocket r) => "F:r:" + r.Flight + string.Concat(r.Explosions.Select(e => "|" + Text(e)));
        public static string StarText(Explosion e) => "F:s:" + Text(e);

        public static Rocket RocketOfId(int id)
        {
            var r = new Rocket();
            var t = id > 0 ? EnchSets.RawOf(id) : null;
            if (t == null || !t.StartsWith("F:r:")) return r;
            var parts = t.Substring(4).Split('|');
            int.TryParse(parts[0], out r.Flight);
            r.Flight = Mathf.Clamp(r.Flight, 1, 3);
            for (int i = 1; i < parts.Length; i++) { var e = ParseExp(parts[i]); if (e != null) r.Explosions.Add(e); }
            return r;
        }
        public static Rocket RocketOf(string key) => RocketOfId(ItemData.Of(key));

        /// <summary>A star's explosion, or null (a plain star, or not a star).</summary>
        public static Explosion StarOf(string key)
        {
            if (!ItemData.Is(key, StarKey)) return null;
            var t = EnchSets.RawOf(ItemData.Of(key));
            return t != null && t.StartsWith("F:s:") ? ParseExp(t.Substring(4)) : null;
        }

        /// <summary>A rocket's key (a plain one: flight 1, no stars), or null while its number is on its way from the host.</summary>
        public static string KeyFor(Rocket r)
        {
            if (r.Flight <= 1 && r.Explosions.Count == 0) return RocketKey;
            int id = EnchSets.IdOfRaw(RocketText(r));
            return id > 0 ? ItemData.With(RocketKey, id) : null;
        }
        public static string KeyFor(Explosion e)
        {
            int id = EnchSets.IdOfRaw(StarText(e));
            return id > 0 ? ItemData.With(StarKey, id) : null;
        }

        static string ColorName(int c)
        {
            int i = System.Array.IndexOf(BannerDesigns.DiffuseColors, (uint)(c & 0xFFFFFF));
            return i >= 0 ? string.Join(" ", Dyes.Colors[i].Split('_').Select(w => char.ToUpper(w[0]) + w.Substring(1))) : "Custom";
        }
        static string Describe(Explosion e) => ShapeNames[(int)e.Shape] + " " + string.Join("/", e.Colors.Select(ColorName))
            + (e.Fades.Count > 0 ? ", fade to " + string.Join("/", e.Fades.Select(ColorName)) : "") + (e.Trail ? ", Trail" : "") + (e.Twinkle ? ", Twinkle" : "");

        /// <summary>What the name shows after it (Minecraft's tooltip): a rocket's flight and stars, a star's explosion.</summary>
        public static string Describe(string key)
        {
            if (ItemData.Is(key, RocketKey)) { var r = RocketOf(key); return $"Flight Duration: {r.Flight}" + string.Concat(r.Explosions.Select(e => "; " + Describe(e))); }
            var s = StarOf(key);
            return s != null ? Describe(s) : null;
        }

        // ------------------------------------------------------------------ the crafting grid
        static Shape? ShapeItem(string t)
        {
            if (t == "fire_charge") return Shape.LargeBall;
            if (t == "gold_nugget") return Shape.Star;
            if (t == "feather") return Shape.Burst;
            if (MobHeads.All.ContainsKey(t) || t == "wither_skeleton_skull") return Shape.Creeper;
            return null;
        }

        /// <summary>
        /// Minecraft's special firework recipes over what's in the grid (each square one item): a rocket with more gunpowder
        /// or with stars (the plain one is an ordinary recipe), a star, a star's fade. The result's key and count, or null:
        /// none of them (or the result's number not here yet). `waiting`: it would be one, once the host's number comes.
        /// </summary>
        public static (string key, int count)? Craft(IList<string> cells, out bool waiting)
        {
            waiting = false;
            int paper = 0, gunpowder = 0, diamond = 0, glow = 0;
            var stars = new List<string>(); var dyes = new List<int>(); var shapes = new List<Shape>();
            foreach (var k in cells)
            {
                var t = ItemData.TypeOf(k);
                int d = Dyes.IndexOf(k);
                if (t == "paper") paper++;
                else if (t == "gunpowder") gunpowder++;
                else if (t == StarKey) stars.Add(k);
                else if (d >= 0) dyes.Add((int)BannerDesigns.DiffuseColors[d]);
                else if (t == "diamond" || t == "scrap_diamond_ore") diamond++;
                else if (t == "glowstone_dust") glow++;
                else if (ShapeItem(t) is Shape sh) shapes.Add(sh);
                else return null;
            }
            // a rocket: paper, 1-3 gunpowder, up to seven stars (their explosions; a plain star adds nothing)
            if (paper == 1 && gunpowder >= 1 && gunpowder <= 3 && dyes.Count == 0 && diamond == 0 && glow == 0 && shapes.Count == 0 && stars.Count <= 7)
            {
                if (gunpowder == 1 && stars.Count == 0) return null;
                var r = new Rocket { Flight = gunpowder };
                foreach (var s in stars) { var e = StarOf(s); if (e != null) r.Explosions.Add(e); }
                var key = KeyFor(r);
                if (key == null) { waiting = true; return null; }
                return (key, 3);
            }
            // a star: gunpowder, 1-8 dyes, a shape, a diamond (trail), glowstone (twinkle)
            if (paper == 0 && gunpowder == 1 && stars.Count == 0 && dyes.Count >= 1 && dyes.Count <= 8 && shapes.Count <= 1 && diamond <= 1 && glow <= 1)
            {
                var e = new Explosion { Shape = shapes.Count > 0 ? shapes[0] : Shape.SmallBall, Colors = dyes, Trail = diamond > 0, Twinkle = glow > 0 };
                var key = KeyFor(e);
                if (key == null) { waiting = true; return null; }
                return (key, 1);
            }
            // a star's fade: one star with an explosion and 1-8 dyes
            if (paper == 0 && gunpowder == 0 && stars.Count == 1 && dyes.Count >= 1 && dyes.Count <= 8 && shapes.Count == 0 && diamond == 0 && glow == 0)
            {
                var e = StarOf(stars[0]);
                if (e == null) return null;
                var f = new Explosion { Shape = e.Shape, Colors = e.Colors, Fades = dyes, Trail = e.Trail, Twinkle = e.Twinkle };
                var key = KeyFor(f);
                if (key == null) { waiting = true; return null; }
                return (key, 1);
            }
            return null;
        }

        // ------------------------------------------------------------------ a star's picture: its colours on the overlay
        const int PlainStar = 0x8A8A8A; // (Minecraft's firework star tint with no explosion)
        public static int StarTint(Explosion e)
        {
            if (e == null || e.Colors.Count == 0) return PlainStar;
            if (e.Colors.Count == 1) return e.Colors[0];
            int r = 0, g = 0, b = 0;
            foreach (int c in e.Colors) { r += (c >> 16) & 255; g += (c >> 8) & 255; b += c & 255; }
            int n = e.Colors.Count;
            return ((r / n) << 16) | ((g / n) << 8) | (b / n);
        }

        /// <summary>The star's picture: the base as it is, the overlay times its colour (Minecraft's two layers).</summary>
        public static Color32[] StarPixels(int rgb)
        {
            var b = McAssets.LoadTexture("item/firework_star");
            var o = McAssets.LoadTexture("item/firework_star_overlay");
            if (b == null || b.width != 16) { if (b != null) Object.Destroy(b); if (o != null) Object.Destroy(o); return null; }
            var bp = b.GetPixels32(); var op = o != null && o.width == 16 ? o.GetPixels32() : null;
            Object.Destroy(b); if (o != null) Object.Destroy(o);
            if (op != null)
            {
                byte cr = (byte)(rgb >> 16), cg = (byte)(rgb >> 8), cb = (byte)rgb;
                for (int i = 0; i < bp.Length; i++)
                    if (op[i].a > 20) bp[i] = new Color32((byte)(op[i].r * cr / 255), (byte)(op[i].g * cg / 255), (byte)(op[i].b * cb / 255), op[i].a);
            }
            return bp;
        }

        static readonly Dictionary<int, Sprite> icons = new Dictionary<int, Sprite>();
        /// <summary>A star's icon in its colours, or null: not a coloured star (its own icon does).</summary>
        public static Sprite IconFor(string key)
        {
            var e = StarOf(key);
            if (e == null) return null;
            int c = StarTint(e);
            if (icons.TryGetValue(c, out var s)) return s;
            var px = StarPixels(c);
            return icons[c] = px == null ? null : Sprite.Create(Atlas.SpriteIconPx(px), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 100f);
        }

        static readonly Dictionary<int, (Mesh mesh, Material mat)> models = new Dictionary<int, (Mesh, Material)>();
        /// <summary>A star in hand or lying about in its colours.</summary>
        public static void ApplyModel(GrabbableObject g)
        {
            if (!(g is StackItem st) || st.ItemKey != StarKey) return;
            var mf = g.transform.Find("model")?.GetComponent<MeshFilter>();
            var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
            if (mr == null) return;
            var e = StarOf(Crafting.KeyOf(g));
            if (e == null) { mf.sharedMesh = MeshBuilder.ExtrudedSprite("item_firework_star"); mr.sharedMaterial = Atlas.Cutout; return; }
            int c = StarTint(e);
            if (!models.TryGetValue(c, out var m) || m.mesh == null)
            {
                var px = StarPixels(c);
                if (px == null) return;
                var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(px); tex.Apply();
                var mat = new Material(Atlas.Cutout) { name = "LMC_star_" + c.ToString("X6"), mainTexture = tex };
                if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", tex);
                models[c] = m = (MeshBuilder.ExtrudedPixels(px, new Rect(0, 0, 1, 1), "LMC_star_" + c.ToString("X6")), mat);
            }
            mf.sharedMesh = m.mesh; mr.sharedMaterial = m.mat;
        }

        // ------------------------------------------------------------------ using one
        /// <summary>Minecraft's lifetime in ticks: 10 a flight level and 10 more, and 0-5 and 0-6 at random.</summary>
        public static int LifeTicks(int flight) => 10 * (1 + Mathf.Clamp(flight, 0, 3)) + Random.Range(0, 6) + Random.Range(0, 7);

        /// <summary>The holder's machine: the use button with a rocket in hand, not gliding: off the block or ground looked at
        /// (Minecraft's: from where you click, a little off its face). False: nothing in reach.</summary>
        public static bool UseOnGround(PlayerControllerB p, StackItem rocket)
        {
            var cam = p.gameplayCamera.transform;
            int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer) | (1 << BlockWorld.NonSolidLayer);
            if (!Physics.Raycast(cam.position, cam.forward, out var hit, 4.5f * BlockWorld.S, mask, QueryTriggerInteraction.Ignore)) return false;
            string key = Crafting.KeyOf(rocket);
            if (!GameModes.LocalCreative && Inventory.Take(p, p.currentItemSlot, 1) == null) return false;
            BlockNet.RequestFirework(hit.point + hit.normal * 0.15f * BlockWorld.S, ItemData.Of(key), false);
            return true;
        }

        /// <summary>Host: a rocket goes up (or on a glider: attached to them); everyone flies it.</summary>
        public static void ServerLaunch(ulong sender, Vector3 at, int id, bool attached, int askedLife = 0)
        {
            var r = RocketOfId(id);
            int life = attached && askedLife > 0 ? Mathf.Min(askedLife, 60) : LifeTicks(r.Flight);
            Elytra.ServerRocket(at);
            Vector3 v = new Vector3(Gauss() * 0.001f, 0.05f, Gauss() * 0.001f);
            BlockNet.ServerFireworkShot(at, v, id, life, attached ? sender : NotAttached);
            Launched++;
        }
        static float Gauss() { float u = 1f - Random.value, w = Random.value; return Mathf.Sqrt(-2f * Mathf.Log(u)) * Mathf.Sin(2f * Mathf.PI * w); }

        /// <summary>Every machine: a rocket in the air.</summary>
        public static void Receive(Vector3 at, Vector3 vel, int id, int life, ulong attached)
        {
            var go = new GameObject("LMC_Firework");
            go.transform.position = at;
            var f = go.AddComponent<FireworkRocket>();
            f.Begin(RocketOfId(id), vel, life, attached);
        }

        // ------------------------------------------------------------------ going off
        public static readonly float[][] StarCoords = { new[] { 0f, 1f }, new[] { 0.3455f, 0.309f }, new[] { 0.9511f, 0.309f }, new[] { 0.37960f, -0.12653f }, new[] { 0.61224f, -0.80408f }, new[] { 0f, -0.35918f } };
        public static readonly float[][] CreeperCoords = { new[] { 0f, 0.2f }, new[] { 0.2f, 0.2f }, new[] { 0.2f, 0.6f }, new[] { 0.6f, 0.6f }, new[] { 0.6f, 0.2f }, new[] { 0.2f, 0.2f }, new[] { 0.2f, 0f }, new[] { 0.4f, 0f }, new[] { 0.4f, -0.6f }, new[] { 0.2f, -0.6f }, new[] { 0.2f, -0.4f }, new[] { 0f, -0.4f } };

        /// <summary>Every machine: a rocket at the end of its flight (`vel` its own, blocks a tick): its stars' sparks, the
        /// blast, the twinkle; the damage (this machine's player; the host: the monsters).</summary>
        public static void Explode(Rocket r, Vector3 at, Vector3 vel, ulong attached)
        {
            Exploded++;
            if (r.Explosions.Count == 0) return; // (Minecraft's: an empty rocket just puffs out)
            var cloud = SparkCloud.Make(at);
            for (int i = 0; i < r.Explosions.Count; i++) cloud.Burst(r.Explosions[i], vel, i * 2);
            LastBurst = string.Join(",", r.Explosions.Select(e => e.Shape.ToString())) + " sparks=" + cloud.Count;
            var cam = Camera.main != null ? Camera.main.transform.position : at;
            bool far = Vector3.Distance(cam, at) > 16f * BlockWorld.S;
            bool large = r.Explosions.Any(e => e.Shape == Shape.LargeBall);
            Sounds.Play("firework." + (large ? "largeblast" : "blast") + (far ? "_far" : ""), at, 1f, 0.95f + Random.value * 0.1f, 160f);
            if (r.Explosions.Any(e => e.Twinkle)) cloud.TwinkleSoundIn = (r.Explosions.Count * 2 - 1 + 15) / 20f;
            Hurt(r.Explosions.Count, at, attached);
        }

        /// <summary>Minecraft's dealExplosionDamage: 5 and 2 a star, full to whoever it's attached to, falling off to nothing
        /// at five blocks for the rest, if the blast sees them. Players on their own machines; monsters by the host.</summary>
        static void Hurt(int stars, Vector3 at, ulong attached)
        {
            float f = 5f + stars * 2f, s = BlockWorld.S;
            int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault & ~(1 << 3);
            var me = GameNetworkManager.Instance?.localPlayerController;
            if (me != null && !me.isPlayerDead && me.isPlayerControlled)
            {
                float d = Vector3.Distance(at, me.transform.position + Vector3.up * 0.9f * s) / s;
                float dmg = me.actualClientId == attached ? f : d <= 5f && Sees(at, me.transform.position, mask) ? f * Mathf.Sqrt((5f - d) / 5f) : 0f;
                if (dmg > 0f) { HurtMe++; me.DamagePlayer(Mathf.Max(1, Mathf.RoundToInt(dmg * 5f)), true, true, CauseOfDeath.Blast, 0, false, (me.transform.position - at).normalized * 4f); }
            }
            if (!BlockNet.IsServer || RoundManager.Instance == null) return;
            foreach (var e in RoundManager.Instance.SpawnedEnemies.ToList())
            {
                if (e == null || e.isEnemyDead) continue;
                float d = Vector3.Distance(at, e.transform.position + Vector3.up * 0.5f * s) / s;
                if (d > 5f || !Sees(at, e.transform.position, mask)) continue;
                int force = Mathf.Max(1, Mathf.RoundToInt(f * Mathf.Sqrt((5f - d) / 5f) / 5f));
                try { e.HitEnemyOnLocalClient(force, (e.transform.position - at).normalized, null, true); HurtMobs++; } catch { }
            }
        }

        static bool Sees(Vector3 at, Vector3 feet, int mask)
        {
            float s = BlockWorld.S;
            foreach (float h in new[] { 0.1f, 0.5f * s })
                if (!Physics.Linecast(at, feet + Vector3.up * h, mask, QueryTriggerInteraction.Ignore)) return true;
            return false;
        }
    }

    /// <summary>A rocket in the air (every machine): Minecraft's flight (faster and faster up, its sideways drift growing),
    /// white sparks behind it; at its end, or hitting a block with stars on it, it goes off. On a glider: with them.</summary>
    public class FireworkRocket : MonoBehaviour
    {
        Fireworks.Rocket rocket; Vector3 vel; int life, age; ulong attached; float acc;
        Transform model; SparkCloud trail;

        public void Begin(Fireworks.Rocket r, Vector3 v, int lifeTicks, ulong attachedTo)
        {
            rocket = r; vel = v; life = lifeTicks; attached = attachedTo;
            var m = new GameObject("model"); model = m.transform; model.SetParent(transform, false);
            model.localScale = Vector3.one * 0.5f * BlockWorld.S;
            m.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_firework_rocket") ? "item_firework_rocket" : "item_paper");
            var mr = m.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Cutout; Atlas.NoDecals(mr);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (attached != Fireworks.NotAttached && attached != Fireworks.ShotAngled) m.SetActive(false); // (on a glider: Minecraft draws it at their side; the boost is what shows)
            trail = SparkCloud.Make(transform.position);
        }

        PlayerControllerB Glider => attached == Fireworks.NotAttached ? null : StartOfRound.Instance.allPlayerScripts.FirstOrDefault(x => x != null && x.actualClientId == attached);

        void Update()
        {
            acc += Time.deltaTime;
            float s = BlockWorld.S;
            while (acc >= 0.05f)
            {
                acc -= 0.05f; age++;
                var g = Glider;
                if (g != null) { transform.position = g.transform.position + Vector3.up * 0.9f * s; vel = Vector3.zero; }
                else
                {
                    bool angled = attached == Fireworks.ShotAngled;
                    if (!angled) vel = new Vector3(vel.x * 1.15f, vel.y + 0.04f, vel.z * 1.15f);
                    var step = vel * s;
                    int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer);
                    // (#244: a crossbow's goes off on a monster or a player it hits, Minecraft's onHitEntity)
                    if (angled && age > 1 && Physics.Raycast(transform.position, step.normalized, out var eh, step.magnitude, (1 << 19) | (1 << 3), QueryTriggerInteraction.Collide)
                        && ((eh.collider.GetComponentInParent<EnemyAI>() is EnemyAI ea && !ea.isEnemyDead) || eh.collider.GetComponentInParent<PlayerControllerB>() != null))
                    {
                        transform.position = eh.point;
                        Fireworks.Explode(rocket, transform.position, vel, attached); End(); return;
                    }
                    if (Physics.Raycast(transform.position, step.normalized, out var hit, step.magnitude, mask, QueryTriggerInteraction.Ignore))
                    {
                        if (angled) { transform.position = hit.point; Fireworks.Explode(rocket, transform.position, vel, attached); End(); return; }
                        transform.position = hit.point;
                        if (rocket.Explosions.Count > 0) { Fireworks.Explode(rocket, transform.position, vel, attached); End(); return; }
                        vel = Vector3.zero;
                    }
                    else transform.position += step;
                    // (Minecraft's firework trail: a white spark a tick)
                    if (age % 2 == 0) trail.Add(transform.position - step * 0.5f, new Vector3(Random.Range(-0.05f, 0.05f), -vel.y * 0.5f, Random.Range(-0.05f, 0.05f)), 0xFFFFFF, -1, false, false, 6 + Random.Range(0, 6));
                }
                if (age >= life) { Fireworks.Explode(rocket, transform.position, vel, attached); End(); return; }
            }
            // (pointing the way it flies: the item's picture is drawn corner to corner, up and to the right)
            var cam = Camera.main;
            if (model != null && model.gameObject.activeSelf)
            {
                var up = vel.sqrMagnitude > 1e-6f ? vel.normalized : Vector3.up;
                var toCam = cam != null ? (cam.transform.position - transform.position).normalized : Vector3.forward;
                var side = Vector3.Cross(up, toCam).normalized;
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                var fwd = Vector3.Cross(side, up);
                model.rotation = Quaternion.LookRotation(-fwd, up) * Quaternion.Euler(0f, 0f, -45f);
            }
        }

        void End() { if (trail != null) trail.Release(); Destroy(gameObject); }
    }

    /// <summary>
    /// Sparks (every machine): Minecraft's FireworkParticles (the Starter's balls, shapes and bursts, its SparkParticle: 48-59
    /// ticks, 0.91 friction, a little gravity, shrinking through the spark sprites, fading toward its fade colour from
    /// halfway, a trail of still sparks, a twinkle). Drawn as one mesh of quads facing the camera, coloured from a small
    /// palette texture (each colour and its steps toward its fade).
    /// </summary>
    public class SparkCloud : MonoBehaviour
    {
        struct Spark { public Vector3 Pos, Vel; public int Age, Life, Pal, Fade, Steps; public bool Trail, Twinkle; }
        readonly List<Spark> sparks = new List<Spark>();
        readonly List<(int color, int fade)> pairs = new List<(int, int)>();
        readonly List<(int start, int burstAt, Fireworks.Explosion e, Vector3 vel)> pending = new List<(int, int, Fireworks.Explosion, Vector3)>();
        const int FadeSteps = 6;
        Mesh mesh; MeshRenderer mr; Material mat; Texture2D pal; bool palDirty, released; float acc; int tick;
        public float TwinkleSoundIn = -1f;
        public int Count => sparks.Count;

        public static SparkCloud Make(Vector3 at)
        {
            var go = new GameObject("LMC_Sparks"); go.transform.position = Vector3.zero;
            var c = go.AddComponent<SparkCloud>();
            c.mesh = new Mesh { name = "LMC_Sparks" }; c.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = c.mesh;
            c.mr = go.AddComponent<MeshRenderer>(); c.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; Atlas.NoDecals(c.mr);
            c.origin = at;
            return c;
        }
        Vector3 origin;

        /// <summary>No more sparks to come (its rocket's gone): it goes when the last one does.</summary>
        public void Release() => released = true;

        int PairIndex(int color, int fade)
        {
            int i = pairs.IndexOf((color, fade));
            if (i >= 0) return i;
            pairs.Add((color, fade)); palDirty = true;
            return pairs.Count - 1;
        }

        public void Add(Vector3 pos, Vector3 velBlocks, int color, int fade, bool trail, bool twinkle, int life = -1)
        {
            sparks.Add(new Spark { Pos = pos, Vel = velBlocks, Age = 0, Life = life > 0 ? life : 48 + Random.Range(0, 12), Pal = PairIndex(color, fade), Fade = fade, Trail = trail, Twinkle = twinkle });
        }

        /// <summary>An explosion `delay` ticks from now (Minecraft's: one every two ticks).</summary>
        public void Burst(Fireworks.Explosion e, Vector3 rocketVel, int delay) => pending.Add((tick, tick + delay, e, rocketVel));

        void Emit(Vector3 v, Fireworks.Explosion e)
        {
            int c = e.Colors.Count > 0 ? e.Colors[Random.Range(0, e.Colors.Count)] : 0xFFFFFF;
            int f = e.Fades.Count > 0 ? e.Fades[Random.Range(0, e.Fades.Count)] : -1;
            Add(origin, v, c, f, e.Trail, e.Twinkle);
        }

        void Go(Fireworks.Explosion e, Vector3 rv)
        {
            switch (e.Shape)
            {
                case Fireworks.Shape.SmallBall: Ball(0.25f, 2, e); break;
                case Fireworks.Shape.LargeBall: Ball(0.5f, 4, e); break;
                case Fireworks.Shape.Star: ShapeOf(0.5f, Fireworks.StarCoords, e, false); break;
                case Fireworks.Shape.Creeper: ShapeOf(0.5f, Fireworks.CreeperCoords, e, true); break;
                default:
                    {
                        float dx = Gauss() * 0.05f, dz = Gauss() * 0.05f;
                        for (int i = 0; i < 70; i++)
                            Emit(new Vector3(rv.x * 0.5f + Gauss() * 0.15f + dx, rv.y * 0.5f + Random.value * 0.5f, rv.z * 0.5f + Gauss() * 0.15f + dz), e);
                        break;
                    }
            }
        }

        // (Minecraft's createParticleBall: the points of a cube's shell, pushed out to a sphere of that speed)
        void Ball(float speed, int size, Fireworks.Explosion e)
        {
            for (int i = -size; i <= size; i++)
                for (int j = -size; j <= size; j++)
                    for (int k = -size; k <= size; k++)
                    {
                        float x = j + (Random.value - Random.value) * 0.5f, y = i + (Random.value - Random.value) * 0.5f, z = k + (Random.value - Random.value) * 0.5f;
                        float g = Mathf.Sqrt(x * x + y * y + z * z) / speed + Gauss() * 0.05f;
                        Emit(new Vector3(x / g, y / g, z / g), e);
                        if (i != -size && i != size && j != -size && j != size) k += size * 2 - 1;
                    }
        }

        // (Minecraft's createParticleShape: the outline drawn three times round, turning; a creeper's face barely turned)
        void ShapeOf(float speed, float[][] coords, Fireworks.Explosion e, bool flat)
        {
            float d = coords[0][0], ee = coords[0][1];
            Emit(new Vector3(d * speed, ee * speed, 0f), e);
            float a = Random.value * Mathf.PI, step = flat ? 0.034f : 0.34f;
            for (int i = 0; i < 3; i++)
            {
                float h = a + i * Mathf.PI * step, j = d, k = ee;
                for (int l = 1; l < coords.Length; l++)
                {
                    float m = coords[l][0], n = coords[l][1];
                    for (float o = 0.25f; o <= 1f; o += 0.25f)
                    {
                        float p = Mathf.Lerp(j, m, o) * speed, q = Mathf.Lerp(k, n, o) * speed;
                        float r = p * Mathf.Sin(h); p *= Mathf.Cos(h);
                        for (float sg = -1f; sg <= 1f; sg += 2f) Emit(new Vector3(p * sg, q, r * sg), e);
                    }
                    j = m; k = n;
                }
            }
        }

        static float Gauss() { float u = 1f - Random.value, w = Random.value; return Mathf.Sqrt(-2f * Mathf.Log(u)) * Mathf.Sin(2f * Mathf.PI * w); }

        void Update()
        {
            acc += Time.deltaTime;
            float s = BlockWorld.S;
            while (acc >= 0.05f)
            {
                acc -= 0.05f; tick++;
                for (int b = pending.Count - 1; b >= 0; b--) if (tick >= pending[b].burstAt) { Go(pending[b].e, pending[b].vel); pending.RemoveAt(b); }
                for (int i = sparks.Count - 1; i >= 0; i--)
                {
                    var sp = sparks[i];
                    if (++sp.Age >= sp.Life) { sparks.RemoveAt(i); continue; }
                    sp.Vel.y -= 0.004f; // (gravity 0.1: 0.04 a tick, times it)
                    sp.Pos += sp.Vel * s;
                    sp.Vel *= 0.91f;
                    if (sp.Age > sp.Life / 2 && sp.Fade >= 0 && sp.Steps < FadeSteps) sp.Steps++;
                    if (sp.Trail && sp.Age < sp.Life / 2 && (sp.Age + sp.Life) % 2 == 0)
                        sparks.Add(new Spark { Pos = sp.Pos, Vel = Vector3.zero, Age = sp.Life / 2, Life = sp.Life, Pal = sp.Pal, Fade = sp.Fade, Steps = sp.Steps, Trail = false, Twinkle = sp.Twinkle });
                    sparks[i] = sp;
                }
            }
            if (TwinkleSoundIn > 0f && (TwinkleSoundIn -= Time.deltaTime) <= 0f)
            {
                var cam0 = Camera.main != null ? Camera.main.transform.position : origin;
                Sounds.Play("firework.twinkle" + (Vector3.Distance(cam0, origin) > 16f * s ? "_far" : ""), origin, 1f, 0.9f + Random.value * 0.15f, 160f);
            }
            if (sparks.Count == 0 && pending.Count == 0 && (released || tick > 5) && TwinkleSoundIn <= 0f) { Destroy(gameObject); return; }
            Draw();
        }

        void OnDestroy() { if (mesh != null) Destroy(mesh); if (pal != null) Destroy(pal); if (mat != null) Destroy(mat); }

        readonly List<Vector3> verts = new List<Vector3>(); readonly List<Vector2> uvs = new List<Vector2>(); readonly List<int> tris = new List<int>();
        void Draw()
        {
            if (palDirty || mat == null) Palette();
            var camT = Camera.main != null ? Camera.main.transform : null;
            var me = GameNetworkManager.Instance?.localPlayerController;
            if (me != null && me.gameplayCamera != null && me.gameplayCamera.isActiveAndEnabled) camT = me.gameplayCamera.transform;
            if (camT == null) return;
            Vector3 right = camT.right, up = camT.up;
            verts.Clear(); uvs.Clear(); tris.Clear();
            float s = BlockWorld.S;
            foreach (var sp in sparks)
            {
                // (Minecraft's twinkle: from a third of its life, shown every other three ticks)
                if (sp.Twinkle && sp.Age >= sp.Life / 3 && ((sp.Age + sp.Life) / 3) % 2 != 0) continue;
                int frame = sp.Age * 7 / Mathf.Max(1, sp.Life); // (spark_7 down to spark_0: it shrinks)
                float half = 0.075f * s * (1f - frame / 9f) * Fireworks.DevSize;
                int b = verts.Count;
                verts.Add(sp.Pos - right * half - up * half); verts.Add(sp.Pos + right * half - up * half);
                verts.Add(sp.Pos + right * half + up * half); verts.Add(sp.Pos - right * half + up * half);
                float u = (sp.Steps + 0.5f) / (FadeSteps + 1), v = (sp.Pal + 0.5f) / Mathf.Max(1, pal.height);
                for (int k = 0; k < 4; k++) uvs.Add(new Vector2(u, v));
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            mesh.Clear();
            mesh.indexFormat = verts.Count > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        void Palette()
        {
            if (pal != null) Destroy(pal);
            int h = Mathf.Max(1, pairs.Count);
            pal = new Texture2D(FadeSteps + 1, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            {
                var (c, f) = y < pairs.Count ? pairs[y] : (0xFFFFFF, -1);
                var from = new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);
                var to = f >= 0 ? new Color(((f >> 16) & 255) / 255f, ((f >> 8) & 255) / 255f, (f & 255) / 255f) : from;
                for (int x = 0; x <= FadeSteps; x++)
                {
                    // (Minecraft's fade: a fifth of the way more a tick: 0.8^steps of the old colour left)
                    float left = x == FadeSteps ? 0f : Mathf.Pow(0.8f, x * 2);
                    pal.SetPixel(x, y, Color.Lerp(to, from, left));
                }
            }
            pal.Apply();
            if (mat == null)
            {
                mat = new Material(Shader.Find("HDRP/Unlit")) { name = "LMC_Sparks" };
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
                mr.sharedMaterial = mat;
            }
            mat.SetColor("_UnlitColor", Color.white * Fireworks.DevGlow); // (they glow: Minecraft draws them at full light)
            mat.SetTexture("_UnlitColorMap", pal);
            mat.mainTexture = pal;
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
            palDirty = false;
        }
    }
}
