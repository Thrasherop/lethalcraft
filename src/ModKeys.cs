using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using UnityEngine.InputSystem;

namespace LethalMinecraft
{
    /// <summary>
    /// The mod's own keys (pocket crafting, hotbar slots). With LethalCompanyInputUtils installed they appear in the
    /// game's Settings > Controls menu and can be rebound there; without it they come from the config file.
    /// </summary>
    public static class ModKeys
    {
        public const string InputUtilsGuid = "com.rune580.LethalCompanyInputUtils";
        public static InputAction PocketCraft;
        public static readonly InputAction[] Hotbar = new InputAction[9];
        public static bool Rebindable { get; private set; }

        /// <summary>Called from the plugin's Awake (InputUtils needs its action sets created there).</summary>
        public static void Init()
        {
            if (Chainloader.PluginInfos.ContainsKey(InputUtilsGuid))
            {
                try { FromInputUtils(); Rebindable = true; }
                catch (System.Exception e) { Plugin.Log.LogWarning("InputUtils keybinds unavailable, using config keys: " + e.Message); }
            }
            if (!Rebindable)
            {
                PocketCraft = new InputAction("LMC_PocketCraft", InputActionType.Button, "<Keyboard>/" + Plugin.CraftKey.Value.ToLowerInvariant());
                for (int i = 0; i < 9; i++) Hotbar[i] = new InputAction("LMC_Hotbar" + (i + 1), InputActionType.Button, "<Keyboard>/" + (i + 1));
                PocketCraft.Enable();
                foreach (var a in Hotbar) a.Enable();
            }
            Plugin.Log.LogInfo(Rebindable ? "Keybinds: rebindable in Settings > Controls (LethalCompanyInputUtils)" : "Keybinds: from the config file (install LethalCompanyInputUtils to rebind in-game)");
        }

        // kept out of Init so the InputUtils types are only touched when that mod is installed
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void FromInputUtils()
        {
            var set = new LmcInputActions();
            PocketCraft = set.PocketCraft;
            Hotbar[0] = set.Hotbar1; Hotbar[1] = set.Hotbar2; Hotbar[2] = set.Hotbar3;
            Hotbar[3] = set.Hotbar4; Hotbar[4] = set.Hotbar5; Hotbar[5] = set.Hotbar6;
            Hotbar[6] = set.Hotbar7; Hotbar[7] = set.Hotbar8; Hotbar[8] = set.Hotbar9;
        }
    }

    /// <summary>The key set registered with LethalCompanyInputUtils (shown as "LethalCraft" in Controls).</summary>
    public class LmcInputActions : LethalCompanyInputUtils.Api.LcInputActions
    {
        [LethalCompanyInputUtils.Api.InputAction("<Keyboard>/i", Name = "Pocket crafting (2x2)")] public InputAction PocketCraft { get; set; }
        [LethalCompanyInputUtils.Api.InputAction("<Keyboard>/1", Name = "Hotbar slot 1")] public InputAction Hotbar1 { get; set; }
        [LethalCompanyInputUtils.Api.InputAction("<Keyboard>/2", Name = "Hotbar slot 2")] public InputAction Hotbar2 { get; set; }
        [LethalCompanyInputUtils.Api.InputAction("<Keyboard>/3", Name = "Hotbar slot 3")] public InputAction Hotbar3 { get; set; }
        [LethalCompanyInputUtils.Api.InputAction("<Keyboard>/4", Name = "Hotbar slot 4")] public InputAction Hotbar4 { get; set; }
        [LethalCompanyInputUtils.Api.InputAction("<Keyboard>/5", Name = "Hotbar slot 5")] public InputAction Hotbar5 { get; set; }
        [LethalCompanyInputUtils.Api.InputAction("<Keyboard>/6", Name = "Hotbar slot 6")] public InputAction Hotbar6 { get; set; }
        [LethalCompanyInputUtils.Api.InputAction("<Keyboard>/7", Name = "Hotbar slot 7")] public InputAction Hotbar7 { get; set; }
        [LethalCompanyInputUtils.Api.InputAction("<Keyboard>/8", Name = "Hotbar slot 8")] public InputAction Hotbar8 { get; set; }
        [LethalCompanyInputUtils.Api.InputAction("<Keyboard>/9", Name = "Hotbar slot 9")] public InputAction Hotbar9 { get; set; }
    }
}
