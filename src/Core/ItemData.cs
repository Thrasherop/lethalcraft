namespace LethalMinecraft
{
    /// <summary>
    /// An item key can carry a number for the one item it names, after a '#': the item's saved number (a tool's uses in the
    /// low bits, #56, and its enchantments above them, #46: Enchants). Keys are what moves items through the inventory
    /// screens, chests, storage, saves and messages, so the wear and the enchantments go wherever the item does. A fresh,
    /// plain item has the plain key (and so does everything that stacks).
    /// </summary>
    public static class ItemData
    {
        public const char Sep = '#';

        /// <summary>The item's key without its number.</summary>
        public static string Base(string key)
        {
            if (key == null) return null;
            int i = key.IndexOf(Sep);
            return i < 0 ? key : key.Substring(0, i);
        }

        /// <summary>The number a key carries (0 if none).</summary>
        public static int Of(string key)
        {
            if (key == null) return 0;
            int i = key.IndexOf(Sep);
            return i >= 0 && int.TryParse(key.Substring(i + 1), out int n) && n > 0 ? n : 0;
        }

        /// <summary>A key carrying a number (the plain key for 0).</summary>
        public static string With(string key, int n)
        {
            var b = Base(key);
            return n > 0 && b != null ? b + Sep + n : b;
        }
    }
}
