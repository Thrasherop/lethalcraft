using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// The spyglass (#229), Minecraft's SpyglassItem: hold the use button to look through it (up to a minute, 1200 ticks,
    /// then it's put down; still held, it comes up again). Looking through it the view narrows to a tenth
    /// (AbstractClientPlayer.getFieldOfViewModifier: 0.1, eased in and out as GameRenderer.tickFov does, halfway each tick),
    /// the look turns an eighth as fast (MouseHandler), you walk at a fifth (an item in use), and the scope's picture
    /// (misc/spyglass_scope) is over the screen, black round it, opening out from half size (Hud.extractSpyglassOverlay).
    /// Its sounds: item.spyglass.use and stop_using (heard by its holder: the mod has no message for the others).
    /// </summary>
    [HarmonyPatch]
    public static class Spyglass
    {
        public const string Key = "spyglass";
        public const float UseSeconds = 60f, Zoom = 0.1f, LookFactor = 0.125f;
        public static bool Scoping { get; private set; }
        public static int Uses; // (dev/tests)
        static float usedFor, fovMod = 1f, scopeScale = 0.5f, readyAt;
        static int heldFrame = -1;

        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        /// <summary>Every frame on the holder's machine while a spyglass is in hand.</summary>
        public static void Hold(PlayerControllerB p, bool useHeld)
        {
            heldFrame = Time.frameCount;
            if (useHeld && !Scoping && Time.time >= readyAt) Start(p);
            else if (!useHeld && Scoping) Stop(p);
            if (!Scoping) return;
            usedFor += Time.deltaTime;
            if (usedFor >= UseSeconds) { Stop(p); readyAt = Time.time + 0.2f; } // (finishUsingItem; still held, used again)
        }

        /// <summary>Not in hand any more: put down.</summary>
        public static void Release() { if (Scoping) Stop(Local); }

        static void Start(PlayerControllerB p)
        {
            Scoping = true; usedFor = 0f; Uses++;
            if (p != null) Sounds.Play("spyglass.use", p.gameplayCamera.transform.position, 0.5f, new[] { 1.15f, 1.33f, 1.4f, 1.55f }[Random.Range(0, 4)]);
        }

        static void Stop(PlayerControllerB p)
        {
            Scoping = false;
            if (p != null) Sounds.Play("spyglass.stop", p.gameplayCamera.transform.position, 0.5f, new[] { 1f, 0.8f, 0.9f }[Random.Range(0, 3)]);
        }

        /// <summary>The view after the game set it: narrowed while looking through (and easing back after).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPostfix]
        static void AfterUpdate(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p == null || p != Local) return;
            // (put down when the hand stopped holding it up: a screen opened, the player died, it left the hand)
            if (Scoping && (p.isPlayerDead || Time.frameCount - heldFrame > 2)) Stop(p);
            float target = Scoping ? Zoom : 1f;
            fovMod = target + (fovMod - target) * Mathf.Pow(0.5f, Time.deltaTime * 20f);
            if (Mathf.Abs(fovMod - 1f) < 0.002f) fovMod = 1f;
            else if (p.gameplayCamera != null) p.gameplayCamera.fieldOfView = p.targetFOV * fovMod;
            Overlay(Scoping && !p.isPlayerDead);
        }

        /// <summary>The look turns slower through it (MouseHandler: the sensitivity cubed, without its x8).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "CalculateNormalLookingInput"), HarmonyPrefix]
        static void SlowLook(PlayerControllerB __instance, ref Vector2 inputVector)
        {
            if (Scoping && __instance == Local) inputVector *= LookFactor;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "CalculateSmoothLookingInput"), HarmonyPrefix]
        static void SlowSmoothLook(PlayerControllerB __instance, ref Vector2 inputVector)
        {
            if (Scoping && __instance == Local) inputVector *= LookFactor;
        }

        // ------------------------------------------------------------------ the scope over the screen
        static GameObject canvas;
        static RectTransform scope;
        static readonly RectTransform[] bars = new RectTransform[4];
        static bool tried;
        public static bool Showing => canvas != null && canvas.activeSelf;

        static void Overlay(bool on)
        {
            if (!on)
            {
                scopeScale = 0.5f;
                if (canvas != null && canvas.activeSelf) canvas.SetActive(false);
                return;
            }
            if (canvas == null)
            {
                if (tried) return;
                tried = true;
                var tex = McAssets.LoadTexture("misc/spyglass_scope");
                if (tex == null) return;
                canvas = new GameObject("LMC_SpyglassScope", typeof(RectTransform));
                Object.DontDestroyOnLoad(canvas);
                var c = canvas.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 4; // (as the pumpkin's view: under the hotbar)
                scope = Child("scope", Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)), Color.white);
                for (int i = 0; i < 4; i++) bars[i] = Child("bar" + i, null, Color.black);
            }
            if (!canvas.activeSelf) canvas.SetActive(true);
            // (Hud: the scale eases from a half to 1.125, half the way each tick; the picture a square of the screen's
            // shorter side times that, black all round it)
            scopeScale = Mathf.Lerp(scopeScale, 1.125f, Mathf.Clamp01(0.5f * Time.deltaTime * 20f));
            var size = ((RectTransform)canvas.transform).rect.size;
            float side = Mathf.Floor(Mathf.Min(size.x, size.y) * scopeScale);
            float left = (size.x - side) / 2f, top = (size.y - side) / 2f;
            Place(scope, left, top, side, side);
            Place(bars[0], 0f, top + side, size.x, Mathf.Max(0f, size.y - top - side)); // (below)
            Place(bars[1], 0f, 0f, size.x, Mathf.Max(0f, top));                          // (above)
            Place(bars[2], 0f, top, Mathf.Max(0f, left), side);                           // (left)
            Place(bars[3], left + side, top, Mathf.Max(0f, size.x - left - side), side);  // (right)
        }

        static RectTransform Child(string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite; img.color = color; img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f); // (from the top left, as the GUI's)
            return rt;
        }

        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
