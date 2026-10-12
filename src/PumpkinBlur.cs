using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// Wearing a carved pumpkin (#130): Minecraft's view through its eye holes (misc/pumpkinblur), over the screen.
    /// </summary>
    public static class PumpkinBlur
    {
        static Image img;
        static bool tried;
        public static bool Showing => img != null && img.enabled;

        public static void Set(bool on)
        {
            if (!on) { if (img != null && img.enabled) img.enabled = false; return; }
            if (img == null)
            {
                if (tried) return;
                tried = true;
                var tex = McAssets.LoadTexture("misc/pumpkinblur");
                if (tex == null) return;
                var go = new GameObject("LMC_PumpkinBlur", typeof(RectTransform));
                Object.DontDestroyOnLoad(go);
                var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 4;
                var ig = new GameObject("blur", typeof(RectTransform));
                ig.transform.SetParent(go.transform, false);
                var rt = (RectTransform)ig.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
                img = ig.AddComponent<Image>();
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                img.raycastTarget = false;
            }
            if (!img.enabled) img.enabled = true;
        }
    }
}
