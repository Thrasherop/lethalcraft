using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The game's FinishGeneratingNewLevelClientRpc runs twice on the host (its send stage, then its execute stage), so a
    /// postfix on it does too: the outposts, the traders, the squid and the vines rolled twice a landing. Once() lets one
    /// through per landing (a few seconds apart at least).
    /// </summary>
    public static class Landing
    {
        public static bool Once(ref float last)
        {
            if (Time.time - last < 5f) return false;
            last = Time.time;
            return true;
        }
    }
}
