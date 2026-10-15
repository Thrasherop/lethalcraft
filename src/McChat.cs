using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// #218: Minecraft's chat, for the lines Minecraft puts there (an advancement made). Not Lethal Company's chat: its own
    /// box where Minecraft's is (ChatComponent): bottom left, 40 GUI pixels above the screen's bottom, 9-pixel lines on
    /// black at half opacity 332 wide, the text in Minecraft's own font (textures/font/ascii.png, from your Minecraft) with
    /// its drop shadow, each line gone after 10 seconds, fading over the last one.
    /// </summary>
    public static class McChat
    {
        const float Life = 10f, LineH = 9f, Width = 320f;
        class Line { public float Born; public RectTransform Rt; public Image Bg; public List<Image> Glyphs = new List<Image>(); public List<Color> Cols = new List<Color>(); }
        static readonly List<Line> lines = new List<Line>();
        static GameObject root; static RectTransform box;
        static Texture2D ascii; static readonly Sprite[] glyph = new Sprite[256]; static readonly int[] advance = new int[256];
        static bool fontTried;
        public static int Added; // (dev/tests)
        public static string LastText = "";

        /// <summary>Minecraft's colours (ChatFormatting): white, green (a task or a goal), dark purple (a challenge).</summary>
        public static readonly Color White = Color.white, Green = new Color(0x55 / 255f, 1f, 0x55 / 255f), Purple = new Color(0xAA / 255f, 0f, 0xAA / 255f);

        static float Scale => Mathf.Max(1f, Mathf.Round(Screen.height / 360f)); // (the toast's: 3 at 1080 lines)

        /// <summary>A line made of coloured parts ("Ace has made the advancement " white, "[Diamonds!]" green).</summary>
        public static void Add(params (string text, Color col)[] parts)
        {
            Build();
            if (root == null) return;
            float s = Scale;
            var go = new GameObject("line", typeof(RectTransform)); go.transform.SetParent(box, false);
            var l = new Line { Born = Time.unscaledTime, Rt = (RectTransform)go.transform };
            l.Rt.anchorMin = l.Rt.anchorMax = new Vector2(0f, 0f); l.Rt.pivot = new Vector2(0f, 0f);
            l.Rt.sizeDelta = new Vector2((Width + 12f) * s, LineH * s);
            l.Bg = go.AddComponent<Image>(); l.Bg.raycastTarget = false; l.Bg.color = new Color(0f, 0f, 0f, 0.5f);
            // text at x 4 (Minecraft's 4 in from the box), its top a pixel under the line's top; the shadow a pixel down-right
            float x = 4f; var sb = new System.Text.StringBuilder();
            foreach (var (text, col) in parts)
            {
                sb.Append(text);
                foreach (char ch in text)
                {
                    int c = ch < 256 ? ch : '?';
                    if (glyph[c] != null)
                    {
                        // (shadow: Minecraft's colour & 0xFCFCFC >> 2, a quarter as bright)
                        Glyph(l, c, x + 1f, 1f + 1f, new Color(col.r * 0.25f, col.g * 0.25f, col.b * 0.25f), s);
                        Glyph(l, c, x, 1f, col, s);
                    }
                    x += advance[c];
                    if (x > Width) break;
                }
            }
            LastText = sb.ToString();
            lines.Insert(0, l); Added++;
            if (lines.Count > 10) { Object.Destroy(lines[lines.Count - 1].Rt.gameObject); lines.RemoveAt(lines.Count - 1); }
            Layout();
        }

        static void Glyph(Line l, int c, float x, float yFromTop, Color col, float s)
        {
            var g = new GameObject("g", typeof(RectTransform)); g.transform.SetParent(l.Rt, false);
            var rt = (RectTransform)g.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x * s, -yFromTop * s); rt.sizeDelta = new Vector2(8f * s, 8f * s);
            var im = g.AddComponent<Image>(); im.sprite = glyph[c]; im.color = col; im.raycastTarget = false;
            l.Glyphs.Add(im); l.Cols.Add(col);
        }

        /// <summary>Lines stacked up from the bottom one, 40 GUI pixels above the screen's bottom.</summary>
        static void Layout()
        {
            float s = Scale;
            box.anchoredPosition = new Vector2(0f, 40f * s); // (the bottom line from 40 to 49 GUI pixels up, ChatComponent)
            for (int i = 0; i < lines.Count; i++) lines[i].Rt.anchoredPosition = new Vector2(0f, i * LineH * s);
        }

        /// <summary>Every frame: each line fades over its last second (Minecraft's ((1 - age/200 ticks) x 10)^2) and goes.</summary>
        public static void Tick()
        {
            if (lines.Count == 0) return;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var l = lines[i];
                float age = Time.unscaledTime - l.Born;
                if (age >= Life || l.Rt == null) { if (l.Rt != null) Object.Destroy(l.Rt.gameObject); lines.RemoveAt(i); continue; }
                float d = Mathf.Clamp01((1f - age / Life) * 10f); d *= d;
                l.Bg.color = new Color(0f, 0f, 0f, 0.5f * d);
                for (int k = 0; k < l.Glyphs.Count; k++) { var c = l.Cols[k]; c.a = d; l.Glyphs[k].color = c; }
            }
            Layout();
        }

        static void Build()
        {
            if (root != null) return;
            LoadFont();
            if (ascii == null) return;
            root = new GameObject("LMC_McChat", typeof(RectTransform));
            Object.DontDestroyOnLoad(root);
            var c = root.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 6;
            var b = new GameObject("box", typeof(RectTransform)); b.transform.SetParent(root.transform, false);
            box = (RectTransform)b.transform;
            box.anchorMin = box.anchorMax = new Vector2(0f, 0f); box.pivot = new Vector2(0f, 0f);
            box.sizeDelta = new Vector2(1f, 1f);
        }

        /// <summary>Minecraft's ascii.png: 16 x 16 cells, a character's cell its code; its width its rightmost opaque column + 1,
        /// the advance a pixel more (BitmapProvider); a space 4 (font/include/space.json).</summary>
        static void LoadFont()
        {
            if (fontTried) return;
            fontTried = true;
            ascii = McAssets.Available ? McAssets.LoadTexture("font/ascii") : null;
            if (ascii == null) { Plugin.Log.LogWarning("Minecraft chat: no font/ascii.png in the Minecraft jar"); return; }
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
    }
}
