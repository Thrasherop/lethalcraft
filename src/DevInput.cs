using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LethalMinecraft
{
    /// <summary>
    /// Dev only: real keyboard/mouse input for play-testing through the game's own input path (WASD movement, mouse look,
    /// clicks), driven by DevServer commands: hold keys for a while, move the mouse, press buttons. Keys and buttons are
    /// re-sent every frame while held and released when their time is up.
    /// </summary>
    public class DevInput : MonoBehaviour
    {
        public static DevInput Instance;
        readonly Dictionary<Key, float> keysUntil = new Dictionary<Key, float>();
        readonly Dictionary<MouseButton, float> buttonsUntil = new Dictionary<MouseButton, float>();
        Vector2 lookLeft; int lookFrames;
        bool wasActive;

        void Awake() => Instance = this;

        /// <summary>Hold keys for some (unscaled) seconds.</summary>
        public void Hold(IEnumerable<Key> keys, float seconds)
        {
            foreach (var k in keys) keysUntil[k] = Time.unscaledTime + seconds;
        }

        public void Click(MouseButton b, float seconds) => buttonsUntil[b] = Time.unscaledTime + Mathf.Max(0.05f, seconds);

        /// <summary>Move the mouse by (dx, dy) pixels spread over some frames (the game turns by its own sensitivity).</summary>
        public void Look(Vector2 delta, int frames) { lookLeft += delta; lookFrames = Mathf.Max(lookFrames, Mathf.Max(1, frames)); }

        public void ReleaseAll() { keysUntil.Clear(); buttonsUntil.Clear(); lookLeft = Vector2.zero; lookFrames = 0; }

        public string Describe()
        {
            float now = Time.unscaledTime;
            var keys = new List<string>();
            foreach (var kv in keysUntil) if (kv.Value > now) keys.Add(kv.Key.ToString());
            foreach (var kv in buttonsUntil) if (kv.Value > now) keys.Add("mouse" + kv.Key);
            return keys.Count == 0 ? "idle" : string.Join("+", keys);
        }

        void Update()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            float now = Time.unscaledTime;
            var held = new List<Key>();
            foreach (var kv in keysUntil) if (kv.Value > now) held.Add(kv.Key);
            bool anyKey = held.Count > 0;
            if (anyKey || wasActive)
            {
                InputSystem.QueueStateEvent(kb, held.Count > 0 ? new KeyboardState(held.ToArray()) : new KeyboardState());
            }
            var ms = new MouseState();
            bool anyButton = false;
            foreach (var kv in buttonsUntil) if (kv.Value > now) { ms = ms.WithButton(kv.Key, true); anyButton = true; }
            Vector2 d = Vector2.zero;
            if (lookFrames > 0) { d = lookLeft / lookFrames; lookLeft -= d; lookFrames--; }
            ms.delta = d;
            ms.position = mouse.position.ReadValue();
            if (anyButton || d != Vector2.zero || wasActive) InputSystem.QueueStateEvent(mouse, ms);
            wasActive = anyKey || anyButton || d != Vector2.zero;
            if (!wasActive) { keysUntil.Clear(); buttonsUntil.Clear(); }
        }
    }
}
