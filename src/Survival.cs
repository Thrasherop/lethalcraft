using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft survival layer for the local player: hunger + saturation + exhaustion, natural regeneration,
    /// starvation, eating, golden apple effects (absorption / regeneration) and experience levels.
    /// Lethal Company's 100 HP maps to 10 hearts (1 half-heart = 5 HP).
    /// </summary>
    public class Survival : MonoBehaviour
    {
        public static Survival Instance;

        public int Hunger = 20;
        public float Saturation = 5f;
        public float Exhaustion;
        float lastDayTime = -1f; // the game clock at the last frame on a moon (passive hunger)
        public int Absorption;      // in LC hp (each half golden heart = 5)
        public float RegenBoostUntil;
        float regenTimer, starveTimer, regenBoostTimer;
        Vector3 lastPos;
        bool wasDead;
        // eating
        public bool Eating;
        bool notHungryShown;
        public static bool EatingNow => Instance != null && Instance.Eating;
        public float EatProgress;
        float eatSoundTimer;
        StackItem eatingStack;
        InputAction activate;
        // xp
        public static int XpTotal;
        public int XpLevel => LevelFor(XpTotal);
        public float XpFraction => (XpTotal - TotalForLevel(XpLevel)) / (float)Mathf.Max(1, XpToNext(XpLevel));
        public float LastHurtTime = -10f;
        public float AirPeakY;
        public int LastHealth = 100;
        public static bool DevBleedFix = true; // (dev "bleedfix 0": the old behaviour)

        public static float HungerRate => Plugin.HungerRate.Value;

        void Awake()
        {
            Instance = this;
            try { XpTotal = ES3.Load("LMC_XpTotal", "LCGeneralSaveData", 0); } catch { XpTotal = 0; }
        }

        void Start()
        {
            if (StartOfRound.Instance != null) StartOfRound.Instance.PlayerJumpEvent.AddListener(OnJump);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            try { if (StartOfRound.Instance != null) StartOfRound.Instance.PlayerJumpEvent.RemoveListener(OnJump); } catch { }
            SaveXp();
        }

        static void SaveXp()
        {
            try { ES3.Save("LMC_XpTotal", XpTotal, "LCGeneralSaveData"); } catch { }
        }

        void OnJump(PlayerControllerB p)
        {
            if (p != Local) return;
            AddExhaustion(p.isSprinting ? 0.2f : 0.05f);
        }

        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        public static void AddExhaustion(float amount)
        {
            if (Instance == null || !Plugin.HungerEnabled.Value || GameModes.LocalCreative) return; // creative: no hunger
            Instance.Exhaustion += amount * HungerRate;
        }

        void Update()
        {
            var p = Local;
            if (p == null) return;
            if (p.isPlayerDead) { wasDead = true; Eating = false; return; }
            if (wasDead && p.isPlayerControlled)
            {
                // respawned: Minecraft-style full hunger
                wasDead = false;
                Hunger = 20; Saturation = 5f; Exhaustion = 0; Absorption = 0;
            }
            if (!p.isPlayerControlled) return;

            float py = p.transform.position.y;
            if (p.thisController.isGrounded) AirPeakY = py; else AirPeakY = Mathf.Max(AirPeakY, py);
            if (p.health < LastHealth) LastHurtTime = Time.time;
            LastHealth = p.health;

            if (Plugin.HungerEnabled.Value)
            {
                // sprinting / swimming exhaustion by distance
                var pos = p.transform.position;
                var d = pos - lastPos; d.y = 0;
                float dist = d.magnitude;
                lastPos = pos;
                if (dist < 2f && !StartOfRound.Instance.inShipPhase)
                {
                    if (p.isSprinting) AddExhaustion(0.1f * dist);
                    else if (p.isUnderwater) AddExhaustion(0.01f * dist);
                }
                // just being on a moon makes you hungry: FoodPerDay steaks' worth from landing (8am) to 6pm, by the
                // in-game clock (never in orbit; a frozen day clock freezes it too)
                var tod = TimeOfDay.Instance;
                if (!StartOfRound.Instance.inShipPhase && tod != null && tod.lengthOfHours > 0f && Balance.FoodPerDay > 0f && !GameModes.LocalCreative)
                {
                    // in-game hours from the game's own clock (whatever speed it runs at on this moon)
                    float now = tod.currentDayTime;
                    if (lastDayTime >= 0f && now > lastDayTime)
                    {
                        const float steak = 8f + 12.8f, exhaustionPerPoint = 4f, hoursPerDay = 10f;
                        float hours = (now - lastDayTime) / tod.lengthOfHours;
                        Exhaustion += Balance.FoodPerDay * steak * exhaustionPerPoint / hoursPerDay * hours;
                    }
                    lastDayTime = now;
                }
                else lastDayTime = -1f;
                while (Exhaustion >= 4f)
                {
                    Exhaustion -= 4f;
                    if (Saturation > 0) Saturation = Mathf.Max(0, Saturation - 1f);
                    else Hunger = Mathf.Max(0, Hunger - 1);
                }

                // healed, yet still "critically injured" and bleeding (#41): the game's own critical-injury message comes
                // back to the injured player too, a network round trip later; healed past 20 in between, the flags came
                // back on, and nothing cleared them (the game heals them away only below 20, natural regeneration skips the
                // critically injured): a blood trail for good
                if (DevBleedFix && p.health >= 20 && (p.criticallyInjured || p.bleedingHeavily)) p.MakeCriticallyInjured(false);

                // natural regeneration
                if (p.health < 100 && Hunger >= 18 && !p.criticallyInjured)
                {
                    regenTimer += Time.deltaTime;
                    float interval = (Hunger >= 20 && Saturation > 0) ? 1.0f : 4f;
                    if (regenTimer >= interval)
                    {
                        regenTimer = 0;
                        Heal(p, 5);
                        AddExhaustion(6f);
                    }
                }
                else regenTimer = 0;

                // starvation (stops at half a heart unless Hardcore)
                if (Hunger <= 0)
                {
                    starveTimer += Time.deltaTime;
                    if (starveTimer >= 4f)
                    {
                        starveTimer = 0;
                        if (p.health > 10 || Plugin.HardcoreStarvation.Value)
                            p.DamagePlayer(5, true, true, CauseOfDeath.Unknown);
                    }
                }
                else starveTimer = 0;

                // too hungry to sprint
                if (Hunger <= 6 && p.sprintMeter > 0.1f) p.sprintMeter = 0.1f;
            }

            // golden apple regeneration
            if (Time.time < RegenBoostUntil)
            {
                regenBoostTimer += Time.deltaTime;
                if (regenBoostTimer >= 0.6f) { regenBoostTimer = 0; Heal(p, 5); }
            }

            UpdateEating(p);
        }

        public static void Heal(PlayerControllerB p, int amount)
        {
            if (p.health >= 100 || p.isPlayerDead) return;
            p.health = Mathf.Min(100, p.health + amount);
            if (p.health >= 20 && p.criticallyInjured) p.MakeCriticallyInjured(false);
            HUDManager.Instance.UpdateHealthUI(p.health, false);
            if (Instance != null) Instance.LastHealth = p.health;
        }

        // ------------------------------------------------------------------ eating
        void UpdateEating(PlayerControllerB p)
        {
            if (activate == null)
            {
                try { activate = IngamePlayerSettings.Instance.playerInput.actions.FindAction("ActivateItem"); } catch { }
            }
            var held = p.isHoldingObject ? p.currentlyHeldObjectServer as StackItem : null;
            bool leftHeld = (activate != null && activate.IsPressed()) || DevServer.LmbHeld;
            bool lmb = (Plugin.PlaceWithLeftClick.Value ? leftHeld : Builder.RmbHeld) && Builder.CanAct(p);
            if (held == null || held.Food == null || !lmb || held.Count <= 0)
            {
                notHungryShown = false;
                if (Eating && held != eatingStack) { }
                Eating = false;
                EatProgress = 0;
                eatingStack = null;
                return;
            }
            var food = held.Food;
            if (Hunger >= 20 && !food.Golden)
            {
                if (!notHungryShown) { notHungryShown = true; McHud.Toast("You're not hungry."); }
                return;
            }
            if (!Eating || eatingStack != held) { Eating = true; EatProgress = 0; eatingStack = held; eatSoundTimer = 0.1f; }
            EatProgress += Time.deltaTime / 1.6f;
            eatSoundTimer -= Time.deltaTime;
            if (eatSoundTimer <= 0f && EatProgress > 0.15f)
            {
                eatSoundTimer = 0.21f;
                Sounds.Play("eat", p.gameplayCamera.transform.position + p.gameplayCamera.transform.forward * 0.4f, 0.5f, Random.Range(0.85f, 1.15f), 16f);
            }
            if (EatProgress >= 1f)
            {
                Eating = false;
                EatProgress = 0;
                Hunger = Mathf.Min(20, Hunger + food.Hunger);
                Saturation = Mathf.Min(Hunger, Saturation + food.Saturation);
                if (food.Golden)
                {
                    Absorption = 20;
                    RegenBoostUntil = Time.time + 5f;
                }
                Sounds.Play("burp", p.transform.position, 0.5f, Random.Range(0.9f, 1.1f), 12f);
                BlockNet.RequestEat(held);
                eatingStack = null;
            }
        }

        /// <summary>Called from the DamagePlayer prefix: golden absorption hearts soak damage first.</summary>
        public int AbsorbDamage(int dmg)
        {
            if (Absorption <= 0 || dmg <= 0) return dmg;
            int soaked = Mathf.Min(Absorption, dmg);
            Absorption -= soaked;
            AddExhaustion(0.1f);
            return dmg - soaked;
        }

        // ------------------------------------------------------------------ xp
        public static int XpToNext(int level) => level < 16 ? 2 * level + 7 : level < 31 ? 5 * level - 38 : 9 * level - 158;

        public static int TotalForLevel(int level)
        {
            int t = 0;
            for (int i = 0; i < level; i++) t += XpToNext(i);
            return t;
        }

        public static int LevelFor(int total)
        {
            int l = 0;
            while (total >= XpToNext(l)) { total -= XpToNext(l); l++; if (l > 1000) break; }
            return l;
        }

        public static void AddXp(int amount)
        {
            if (amount <= 0) return;
            int before = LevelFor(XpTotal);
            XpTotal += amount;
            int after = LevelFor(XpTotal);
            var p = Local;
            if (p != null)
            {
                if (after > before && after % 5 == 0) Sounds.Play2D("levelup", 0.6f, 1f);
                else Sounds.Play2D("orb", 0.35f, Random.Range(0.6f, 1.3f));
            }
            SaveXp();
        }
    }
}
