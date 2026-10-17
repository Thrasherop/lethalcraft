using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// Screens drawn the way Minecraft draws them (the sign and book screens, #223): its GUI scale (the largest whole
    /// number that leaves 320 x 240 GUI pixels, Window.calculateScale's "auto"), positions in GUI pixels from the top left,
    /// its pictures from the player's Minecraft, its font, its buttons (widget/button, nine-sliced with a 3-pixel border).
    /// </summary>
    public static class McGui
    {
        public static int Scale => Mathf.Max(1, Mathf.Min(Screen.width / 320, Screen.height / 240));
        /// <summary>The screen in GUI pixels (Minecraft rounds up).</summary>
        public static int Width => Mathf.CeilToInt(Screen.width / (float)Scale);
        public static int Height => Mathf.CeilToInt(Screen.height / (float)Scale);

        /// <summary>A screen-space canvas, one unit a screen pixel, and its root (the whole screen, origin top left).</summary>
        public static RectTransform NewCanvas(string name, int order, out Canvas canvas)
        {
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
            var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = 1f;
            go.AddComponent<GraphicRaycaster>();
            var root = new GameObject("Root", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(go.transform, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            return root;
        }

        /// <summary>An empty box at (x, y), w x h GUI pixels, under parent (from its top left).</summary>
        public static RectTransform Box(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            int s = Scale;
            rt.anchoredPosition = new Vector2(x * s, -y * s); rt.sizeDelta = new Vector2(w * s, h * s);
            return rt;
        }

        /// <summary>The screen dimmed behind a screen in the world (Minecraft's in-game background).</summary>
        public static Image Dim(RectTransform root)
        {
            var im = new GameObject("Dim", typeof(RectTransform)).AddComponent<Image>();
            var rt = im.rectTransform; rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            im.color = new Color(0.063f, 0.063f, 0.063f, 0.75f);
            return im;
        }

        /// <summary>Part of a picture (u, v, w, h in its pixels from the top left, the picture texW x texH) drawn at (x, y),
        /// dw x dh GUI pixels: Minecraft's blit.</summary>
        public static RawImage Blit(RectTransform parent, Texture2D tex, float x, float y, float dw, float dh, float u, float v, float uw, float vh, float texW, float texH)
        {
            var box = Box(parent, "Blit", x, y, dw, dh);
            var ri = box.gameObject.AddComponent<RawImage>(); ri.raycastTarget = false;
            if (tex == null) { ri.color = new Color(0.6f, 0.45f, 0.25f); return ri; }
            tex.filterMode = FilterMode.Point;
            ri.texture = tex;
            ri.uvRect = new Rect(u / texW, 1f - (v + vh) / texH, uw / texW, vh / texH);
            return ri;
        }

        static UnityEngine.Sprite button, buttonHi, buttonOff;
        static UnityEngine.Sprite Sliced(string path, int border = 3)
        {
            var t = McAssets.Available ? McAssets.LoadTexture(path) : null;
            if (t == null) return null;
            t.filterMode = FilterMode.Point;
            float b = border * t.width / 200f; // (the sprite's border, 200 wide in GUI pixels: Minecraft's nine-slice)
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        /// <summary>A picture button (the book's page arrows): its sprite, the highlighted one under the mouse.</summary>
        public static Button SpriteButton(RectTransform parent, string sprite, float x, float y, float w, float h, Action onClick)
        {
            var box = Box(parent, "Sprite " + sprite, x, y, w, h);
            var im = box.gameObject.AddComponent<Image>();
            var t = McAssets.Available ? McAssets.LoadTexture("gui/sprites/" + sprite) : null;
            var b = box.gameObject.AddComponent<Button>(); b.targetGraphic = im;
            b.onClick.AddListener(() => onClick());
            if (t == null) { im.color = new Color(0.5f, 0.3f, 0.2f); return b; }
            t.filterMode = FilterMode.Point;
            im.sprite = UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
            var hl = McAssets.LoadTexture("gui/sprites/" + sprite + "_highlighted");
            if (hl != null)
            {
                hl.filterMode = FilterMode.Point;
                b.transition = Selectable.Transition.SpriteSwap;
                var ss = b.spriteState; ss.highlightedSprite = UnityEngine.Sprite.Create(hl, new Rect(0, 0, hl.width, hl.height), new Vector2(0.5f, 0.5f)); b.spriteState = ss;
            }
            return b;
        }

        /// <summary>A Minecraft button: its picture nine-sliced to w x h, the label centred in white with a shadow, lighter
        /// under the mouse; grey and dead when not active.</summary>
        public static Button Button(RectTransform parent, string label, float x, float y, float w, float h, Action onClick)
        {
            if (button == null) { button = Sliced("gui/sprites/widget/button"); buttonHi = Sliced("gui/sprites/widget/button_highlighted"); buttonOff = Sliced("gui/sprites/widget/button_disabled"); }
            var box = Box(parent, "Button " + label, x, y, w, h);
            var im = box.gameObject.AddComponent<Image>();
            int s = Scale;
            if (button != null)
            {
                im.sprite = button; im.type = Image.Type.Sliced;
                im.pixelsPerUnitMultiplier = button.texture.width / (200f * s); // (the border 3 GUI pixels: 3s screen pixels)
            }
            else im.color = new Color(0.4f, 0.4f, 0.4f);
            var b = box.gameObject.AddComponent<Button>();
            b.targetGraphic = im;
            if (buttonHi != null)
            {
                b.transition = Selectable.Transition.SpriteSwap;
                var ss = b.spriteState; ss.highlightedSprite = buttonHi; ss.selectedSprite = button; ss.pressedSprite = buttonHi; ss.disabledSprite = buttonOff; b.spriteState = ss;
            }
            b.onClick.AddListener(() => onClick());
            Box(box, "Label", 0, 0, w, h);
            SetLabel(b, label);
            return b;
        }

        /// <summary>A button's label (centred, white with a shadow; grey when the button's not active).</summary>
        public static void SetLabel(Button b, string label)
        {
            var labelBox = (RectTransform)b.transform.Find("Label");
            if (labelBox == null) return;
            Clear(labelBox);
            int s = Scale;
            float w = labelBox.sizeDelta.x / s, h = labelBox.sizeDelta.y / s;
            McFont.Draw(labelBox, label, b.interactable ? Color.white : new Color(0.627f, 0.627f, 0.627f), s, Mathf.Floor((w - McFont.Width(label)) / 2f), Mathf.Floor((h - 8f) / 2f));
        }

        /// <summary>Minecraft's text (its font) at (x, y) GUI pixels: one line.</summary>
        public static float Text(RectTransform parent, string text, float x, float y, Color col, bool shadow, float scale = 1f)
        {
            float s = Scale * scale;
            return McFont.Draw(parent, text, col, s, x / scale, y / scale, null, null, shadow) * scale;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>Minecraft's word wrap (StringSplitter): lines no wider than width, broken after a space where it can, in
        /// the middle of a word that won't fit on its own; a newline always breaks. Each line as (start, length) into s, the
        /// text without the break.</summary>
        public static List<(int start, int len)> Wrap(string s, float width)
        {
            var lines = new List<(int, int)>();
            int start = 0;
            while (true)
            {
                int nl = s.IndexOf('\n', start);
                int end = nl < 0 ? s.Length : nl;
                // (this paragraph, start..end)
                int ls = start;
                while (true)
                {
                    float w = 0f; int i = ls, lastSpace = -1;
                    while (i < end)
                    {
                        float a = McFont.Width(s[i].ToString());
                        if (w + a > width && i > ls) break;
                        if (s[i] == ' ') lastSpace = i;
                        w += a; i++;
                    }
                    if (i >= end) { lines.Add((ls, end - ls)); break; }
                    // (break after the last space on the line, the space dropped; no space: mid-word)
                    if (lastSpace >= ls) { lines.Add((ls, lastSpace - ls)); ls = lastSpace + 1; }
                    else { lines.Add((ls, i - ls)); ls = i; }
                }
                if (nl < 0) break;
                start = nl + 1;
            }
            return lines;
        }

        /// <summary>Minecraft's text cursor blinks: on for 300 ms, off for 300 (TextCursorUtils).</summary>
        public static bool CursorOn(float since) => ((int)((Time.unscaledTime - since) * 1000f) / 300) % 2 == 0;

        /// <summary>The insert cursor (a 1-pixel bar a line high, from a pixel above the line).</summary>
        public static void Bar(RectTransform parent, float x, float y, float lineH, Color col, float scale = 1f)
        {
            var im = Box(parent, "Cursor", x, y - scale, scale, (lineH + 1f) * scale).gameObject.AddComponent<Image>();
            im.color = col; im.raycastTarget = false;
        }
    }
}
