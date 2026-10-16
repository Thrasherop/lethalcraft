namespace LethalMinecraft
{
    /// <summary>
    /// An item key can carry a number for the one item it names, after a '#': the item's saved number (a tool's uses in the
    /// low bits, #56, and its enchantments above them, #46: Enchants). Keys are what moves items through the inventory
    /// screens, chests, storage, saves and messages, so the wear and the enchantments go wherever the item does. A fresh,
    /// plain item has the plain key (and so does everything that stacks).
    /// (#228) A key can also carry a custom name, last, after a '~': the number of an anvil's name (ItemNames), e.g.
    /// "pickaxe#1234~3" or "cobblestone~7". The name goes with the key the same way; things named differently don't stack.
    /// </summary>
    public static class ItemData
    {
        public const char Sep = '#', NameSep = '~';
        static readonly char[] Seps = { Sep, NameSep };

        /// <summary>The item's key without its number or name.</summary>
        public static string Base(string key)
        {
            if (key == null) return null;
            int i = key.IndexOfAny(Seps);
            return i < 0 ? key : key.Substring(0, i);
        }

        /// <summary>The number a key carries (0 if none).</summary>
        public static int Of(string key)
        {
            if (key == null) return 0;
            int i = key.IndexOf(Sep);
            if (i < 0) return 0;
            int j = key.IndexOf(NameSep, i);
            var s = j < 0 ? key.Substring(i + 1) : key.Substring(i + 1, j - i - 1);
            return int.TryParse(s, out int n) && n > 0 ? n : 0;
        }

        /// <summary>A key carrying a number (the plain key for 0), its name kept.</summary>
        public static string With(string key, int n)
        {
            var b = Base(key);
            if (b == null) return null;
            return (n > 0 ? b + Sep + n : b) + NameSuffix(key);
        }

        /// <summary>(#228) The number of the name a key carries (0: none).</summary>
        public static int NameId(string key)
        {
            if (key == null) return 0;
            int j = key.IndexOf(NameSep);
            return j >= 0 && int.TryParse(key.Substring(j + 1), out int n) && n > 0 ? n : 0;
        }

        /// <summary>(#228) The key with that name (0: its name taken off).</summary>
        public static string WithName(string key, int nameId)
        {
            if (key == null) return null;
            int j = key.IndexOf(NameSep);
            var k = j < 0 ? key : key.Substring(0, j);
            return nameId > 0 ? k + NameSep + nameId : k;
        }

        /// <summary>(#228) The key without its name (what recipes and machines go by).</summary>
        public static string Unnamed(string key) => WithName(key, 0);

        static string NameSuffix(string key)
        {
            int j = key.IndexOf(NameSep);
            return j < 0 ? "" : key.Substring(j);
        }
    }
}
