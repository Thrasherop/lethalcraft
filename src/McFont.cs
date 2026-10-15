using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's own font (textures/font/ascii.png, from your Minecraft), for screens that should look like Minecraft's
    /// (#218 the chat, #219 the sound options): 16 x 16 cells, a character's cell its code; its width its rightmost opaque
    /// column + 1, the advance a pixel more (BitmapProvider); a space 4 (font/include/space.json); text with Minecraft's
    /// drop shadow (a quarter as bright, a pixel down-right).
    /// </summary>
    public static class McFont
    {
        static Texture2D ascii; static readonly Sprite[] glyph = new Sprite[256]; static readonly int[] advance = new int[256];
        static bool tried;
        public static bool Ready { get { Load(); return ascii != null; } }

        static void Load()
        {
            if (tried) return;
            tried = true;
            ascii = McAssets.Available ? McAssets.LoadTexture("font/ascii") : null;
            if (ascii == null) { Plugin.Log.LogWarning("Minecraft font: no font/ascii.png in the Minecraft jar"); return; }
            ascii.filterMode = FilterMode.Point;
            int cell = ascii.width / 16;
            var px = ascii.GetPixels32();
            for (int c = 0; c < 256; c++)
            {
                int cx = c % 16, cy = c / 16;
                int x0 = cx * cell, y0 = ascii.height - (cy + 1) * cell; // (texture rows from the bottom)
                int w = 0;
                for (int x = cell - 1; x >= 0 && w == 0; x--)
                    for (int y = 0; y < cell; y++)
                        if (px[(y0 + y) * ascii.width + x0 + x].a > 0) { w = x + 1; break; }
                advance[c] = c == ' ' ? 4 : w == 0 ? 0 : Mathf.RoundToInt(w * 8f / cell) + 1;
                if (w > 0) glyph[c] = Sprite.Create(ascii, new Rect(x0, y0, cell, cell), new Vector2(0f, 1f), cell);
            }
        }

        /// <summary>A string's width in GUI pixels.</summary>
        public static float Width(string s)
        {
            Load();
            float w = 0f;
            foreach (char ch in s) w += advance[ch < 256 ? ch : '?'];
            return w;
        }

        /// <summary>
        /// Draws text under a parent (its top-left the anchor), GUI pixels x scale: x, y from the parent's top left. Each
        /// glyph an Image (shadow first). Returns the images (to recolour or fade) and the width it took.
        /// </summary>
        public static float Draw(RectTransform parent, string text, Color col, float scale, float x, float y, System.Collections.Generic.List<Image> into = null, System.Collections.Generic.List<Color> cols = null, bool shadow = true)
        {
            Load();
            float x0 = x;
            if (ascii == null) return 0f;
            foreach (char ch in text)
            {
                int c = ch < 256 ? ch : '?';
                if (glyph[c] != null)
                {
                    if (shadow) One(parent, c, x + 1f, y + 1f, new Color(col.r * 0.25f, col.g * 0.25f, col.b * 0.25f, col.a), scale, into, cols);
                    One(parent, c, x, y, col, scale, into, cols);
                }
                x += advance[c];
            }
            return x - x0;
        }

        static void One(RectTransform parent, int c, float x, float y, Color col, float s, System.Collections.Generic.List<Image> into, System.Collections.Generic.List<Color> cols)
        {
            var g = new GameObject("g", typeof(RectTransform)); g.transform.SetParent(parent, false);
            var rt = (RectTransform)g.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x * s, -y * s); rt.sizeDelta = new Vector2(8f * s, 8f * s);
            var im = g.AddComponent<Image>(); im.sprite = glyph[c]; im.color = col; im.raycastTarget = false;
            into?.Add(im); cols?.Add(col);
        }
    }
}
