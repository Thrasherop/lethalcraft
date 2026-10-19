using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// A bottle o' enchanting (#223, from pillager outposts' chests): thrown with the use button (Minecraft's
    /// ThrownExperienceBottle: thrown a little upward, falling faster than a snowball), it shatters where it lands into
    /// 3-11 experience (3 + two rolls of 0-4), which goes to the nearest player within 8 blocks, as Minecraft's orbs fly
    /// to whoever's near. Every machine flies its own; the host breaks it and gives the experience.
    /// </summary>
    public class ExperienceBottle : MonoBehaviour
    {
        public const string Key = "experience_bottle";
        public const float Speed = 14f, Gravity = 14f, Life = 5f;
        public static int Thrown, Broken, XpGiven; // (dev/tests)
        Vector3 vel; float age;

        /// <summary>The local player's use button with one in hand.</summary>
        public static void Throw(PlayerControllerB p)
        {
            if (!GameModes.LocalCreative && Inventory.Take(p, p.currentItemSlot, 1) == null) return;
            var cam = p.gameplayCamera.transform;
            // (Minecraft's: 20 degrees above where you look)
            var dir = Quaternion.AngleAxis(-20f, cam.right) * cam.forward;
            BlockNet.RequestXpBottle(cam.position + cam.forward * 0.5f, dir * Speed);
            Thrown++;
        }

        public static void Launch(Vector3 from, Vector3 vel)
        {
            var go = new GameObject("LMC_XpBottle"); go.transform.position = from;
            var b = go.AddComponent<ExperienceBottle>(); b.vel = vel;
            var model = new GameObject("model"); model.transform.SetParent(go.transform, false); model.transform.localScale = Vector3.one * 0.4f;
            model.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_experience_bottle") ? "item_experience_bottle" : "item_glass_bottle");
            var mr = model.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Cutout; Atlas.NoDecals(mr);
            Sounds.Play("snowball.throw", from, 0.6f, Random.Range(0.9f, 1.1f), 16f);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > Life) { Destroy(gameObject); return; }
            vel += Vector3.down * Gravity * Time.deltaTime;
            var p = transform.position; var step = vel * Time.deltaTime; float len = step.magnitude;
            transform.Rotate(Vector3.right, 600f * Time.deltaTime, Space.Self);
            if (len > 1e-4f && Physics.Raycast(p, step / len, out var hit, len + 0.05f, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore))
            {
                Shatter(hit.point + hit.normal * 0.1f);
                return;
            }
            transform.position = p + step;
        }

        void Shatter(Vector3 at)
        {
            Broken++;
            Sounds.Play("break.glass", at, 0.9f, Random.Range(0.9f, 1.1f), 20f);
            SplashBits.Play(at, new Color(0.22f, 0.36f, 0.78f)); // (Minecraft's splash colour for it, 0x385DC6)
            if (BlockNet.IsServer)
            {
                int xp = 3 + Random.Range(0, 5) + Random.Range(0, 5);
                PlayerControllerB best = null; float bd = 8f * BlockWorld.S;
                foreach (var pl in StartOfRound.Instance.allPlayerScripts)
                {
                    if (pl == null || !pl.isPlayerControlled || pl.isPlayerDead) continue;
                    float d = Vector3.Distance(pl.transform.position, at);
                    if (d < bd) { bd = d; best = pl; }
                }
                if (best != null) { BlockNet.ServerXp(XpOrb.Anyone, xp, at); XpGiven += xp; } // (#244: orbs where it broke)
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// A goat horn (#223, from pillager outposts' chests: Minecraft's regular horns, Ponder, Sing, Seek and Feel). The use
    /// button sounds it, heard far off (Minecraft's 256 blocks), then it rests 7 seconds (its use duration).
    /// </summary>
    public static class GoatHorn
    {
        public static readonly string[] Instruments = { "ponder", "sing", "seek", "feel" };
        public const string KeyPrefix = "goat_horn_";
        public const float Cooldown = 7f;
        static float readyAt;
        public static int Blown; // (dev/tests)

        public static int InstrumentOf(string key) => (key = ItemData.TypeOf(key)) != null && key.StartsWith(KeyPrefix) ? System.Array.IndexOf(Instruments, key.Substring(KeyPrefix.Length)) : -1;

        public static void Use(PlayerControllerB p, string key)
        {
            int i = InstrumentOf(key);
            if (i < 0 || Time.time < readyAt) return;
            readyAt = Time.time + Cooldown;
            BlockNet.RequestHorn(p.gameplayCamera.transform.position, (byte)i);
            Blown++;
        }

        /// <summary>Every client: someone sounded one there.</summary>
        public static void Play(Vector3 at, byte instrument) => Sounds.Play("horn." + instrument, at, 1f, 1f, 256f * BlockWorld.S);
    }
}
