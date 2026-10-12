using HarmonyLib;
using TMPro;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Dead, the game hides its whole HUD (the chat with it), so a command typed while spectating (#129) was typed blind.
    /// While you're dead and typing, and for a few seconds after you send, this shows the line you're typing and the
    /// chat's last lines (the command's answer among them), in the chat's own font, where the chat would be.
    /// </summary>
    [HarmonyPatch]
    public static class DeadChat
    {
        static TextMeshProUGUI text;
        static float showUntil;
        public static bool Showing => text != null && text.enabled;

        [HarmonyPatch(typeof(HUDManager), "Update"), HarmonyPostfix]
        static void Tick(HUDManager __instance)
        {
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            bool dead = p != null && p.isPlayerDead;
            bool typing = dead && p.isTypingChat;
            if (typing) showUntil = Time.time + 5f;
            bool on = dead && (typing || Time.time < showUntil) && __instance.chatText != null;
            if (!on) { if (text != null && text.enabled) text.enabled = false; return; }
            if (text == null)
            {
                var go = new GameObject("LMC_DeadChat", typeof(RectTransform));
                Object.DontDestroyOnLoad(go);
                var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 6;
                var tg = new GameObject("text", typeof(RectTransform));
                tg.transform.SetParent(go.transform, false);
                var rt = (RectTransform)tg.transform;
                rt.anchorMin = new Vector2(0.02f, 0.06f); rt.anchorMax = new Vector2(0.5f, 0.32f); rt.offsetMin = rt.offsetMax = Vector2.zero;
                text = tg.AddComponent<TextMeshProUGUI>();
                text.font = __instance.chatText.font;
                text.fontSize = 22f;
                text.alignment = TextAlignmentOptions.BottomLeft;
                text.enableWordWrapping = true;
                text.raycastTarget = false;
            }
            var lines = (__instance.chatText.text ?? "").Split('\n');
            int from = Mathf.Max(0, lines.Length - 3);
            string recent = string.Join("\n", lines, from, lines.Length - from);
            text.text = recent + (typing ? "\n<color=#ffffff>> " + (__instance.chatTextField != null ? __instance.chatTextField.text : "") + "_</color>" : "");
            text.enabled = true;
        }
    }
}
