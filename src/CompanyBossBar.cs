using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// A boss bar at the Company (#63, [HUD] CompanyBossBar): Minecraft's purple boss bar at the top of the screen, titled
    /// "The Company", always full (the crew isn't hurting the Company by selling to it; [HUD] CompanyBossBarQuota fills it
    /// with the quota sold instead). Only on the Company's moon, landed, outside the ship.
    /// Sprites and font from your Minecraft install (plain bars without it).
    /// </summary>
    public class CompanyBossBar : MonoBehaviour
    {
        public const string Title = "The Company";
        RectTransform root;
        Image back, fill, notchBack, notchFill;
        PixelText title;
        CanvasGroup group;

        void Awake()
        {
            var go = new GameObject("LMC_BossBar", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 19;
            canvas.pixelPerfect = true;
            group = go.AddComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false; group.alpha = 0f;
            root = (RectTransform)new GameObject("root", typeof(RectTransform)).transform;
            root.SetParent(go.transform, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            // (Minecraft's: a 182x5 bar, its name just above it; here 32 px from the top, below the helmet's visor edge)
            back = Img("back", Sprite("gui/sprites/boss_bar/purple_background", new Color32(70, 20, 90, 255)), -91, -32, 182, 5);
            fill = Img("fill", Sprite("gui/sprites/boss_bar/purple_progress", new Color32(200, 60, 230, 255)), -91, -32, 182, 5);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            notchBack = Img("notchb", Sprite("gui/sprites/boss_bar/notched_10_background", new Color32(0, 0, 0, 0)), -91, -32, 182, 5);
            notchFill = Img("notchf", Sprite("gui/sprites/boss_bar/notched_10_progress", new Color32(0, 0, 0, 0)), -91, -32, 182, 5);
            notchFill.type = Image.Type.Filled; notchFill.fillMethod = Image.FillMethod.Horizontal;
            var tr = (RectTransform)new GameObject("title", typeof(RectTransform)).transform;
            tr.SetParent(root, false);
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
            tr.pivot = Vector2.zero; tr.sizeDelta = Vector2.zero; // (how PixelText lays out: from its bottom left)
            tr.anchoredPosition = new Vector2(0, -30);
            title = tr.gameObject.AddComponent<PixelText>();
            title.Alignment = PixelText.Align.Center;
            title.Set(Title, Color.white);
        }

        static Sprite Sprite(string path, Color32 fallback)
        {
            var t = McAssets.Available ? McAssets.LoadTexture(path) : null;
            if (t == null) { t = new Texture2D(1, 1); t.SetPixel(0, 0, fallback); t.Apply(); }
            t.filterMode = FilterMode.Point;
            return UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 1f);
        }

        Image Img(string name, Sprite s, float x, float y, float w, float h)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = s; img.raycastTarget = false;
            return img;
        }

        /// <summary>On the Company's moon, landed, outside the ship (and the Minecraft HUD on).</summary>
        public static bool Showing
        {
            get
            {
                var sor = StartOfRound.Instance;
                var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
                if (!Plugin.CompanyBossBar.Value || !Plugin.MinecraftHud.Value || sor == null || p == null) return false;
                var lvl = sor.currentLevel;
                bool company = lvl != null && (lvl.sceneName == "CompanyBuilding" || lvl.levelID == 3);
                return company && sor.shipHasLanded && !sor.inShipPhase && !p.isInHangarShipRoom && !p.isPlayerDead;
            }
        }

        /// <summary>How far the crew is to the quota (0..1).</summary>
        public static float Progress
        {
            get
            {
                var t = TimeOfDay.Instance;
                return t == null || t.profitQuota <= 0 ? 0f : Mathf.Clamp01(t.quotaFulfilled / (float)t.profitQuota);
            }
        }

        void Update()
        {
            bool show = Showing;
            group.alpha = show ? 1f : 0f;
            if (!show) return;
            int s = Mathf.Clamp(Mathf.FloorToInt(Screen.height / 360f), 1, 8);
            root.localScale = Vector3.one * s;
            // (full, like a boss you can't hurt: selling isn't damaging the Company; the quota in it only if asked for)
            float f = Plugin.CompanyBossBarQuota.Value ? Progress : 1f;
            fill.fillAmount = f; notchFill.fillAmount = f;
        }

        public static string Describe() => $"showing={Showing} fill={(Plugin.CompanyBossBarQuota.Value ? Progress : 1f):0.00} progress={Progress:0.00} quota={TimeOfDay.Instance?.quotaFulfilled}/{TimeOfDay.Instance?.profitQuota}";
    }
}
