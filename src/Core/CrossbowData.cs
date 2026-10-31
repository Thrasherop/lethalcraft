using System;

namespace LethalMinecraft
{
    /// <summary>
    /// A crossbow's saved number (#223; pure, the unit tests cover it). It's a tool's number (Enchants.Data): the
    /// enchantments above, and in the 13 "uses" bits its wear (the low 11, up to 2047) and what it's loaded with (the top
    /// 2: nothing, an arrow, an arrow of slowness, an arrow of poison; #244: the wear's top bit, never reached (465 uses),
    /// marks a firework rocket). So a loaded crossbow stays loaded through the
    /// inventory, chests, a drop and a save, as Minecraft's charged_projectiles does.
    /// </summary>
    public static class CrossbowData
    {
        public const int WearBits = 11, RocketBit = 1 << (WearBits - 1), WearMask = RocketBit - 1, MaxWear = WearMask;
        public enum Load : byte { None, Arrow, Slowness, Poison, Rocket }

        public static int WearOf(int data) => Enchants.UsesOf(data) & WearMask;
        public static Load LoadOf(int data) => (Enchants.UsesOf(data) & RocketBit) != 0 ? Load.Rocket : (Load)((Enchants.UsesOf(data) >> WearBits) & 3);
        public static int EnchOf(int data) => Enchants.EnchOf(data);
        public static int Data(int wear, Load load, int ench) => Enchants.Data(Math.Max(0, Math.Min(wear, MaxWear)) | (load == Load.Rocket ? RocketBit : (int)load << WearBits), ench);

        /// <summary>The arrow item a load is, and back (null / None: not an arrow).</summary>
        public static string ArrowKey(Load l) => l == Load.Arrow ? "arrow" : l == Load.Slowness ? "arrow_slowness" : l == Load.Poison ? "arrow_poison" : null;
        public static Load LoadFor(string arrowKey) => (arrowKey = ItemData.TypeOf(arrowKey)) == "arrow" ? Load.Arrow : arrowKey == "arrow_slowness" ? Load.Slowness : arrowKey == "arrow_poison" ? Load.Poison : Load.None;

        /// <summary>Minecraft's charge time in whole ticks (CrossbowItem.getChargeDuration: floor(seconds * 20)).</summary>
        public static int ChargeTicks(int quickCharge) => (int)Math.Floor(Enchants.CrossbowChargeSeconds(quickCharge) * 20f);

        /// <summary>The angles (degrees, about the shooter's up) of the arrows one shot fires: Minecraft's
        /// ProjectileWeaponItem.shoot with Multishot's projectile_count 3 and spread 10: 0, then -10 and 10.</summary>
        public static float[] ShotAngles(bool multishot) => multishot ? new[] { 0f, -10f, 10f } : new[] { 0f };

        /// <summary>A crossbow dropped by a mob: Minecraft's random wear for an equipment drop
        /// (Mob.dropCustomDeathLoot: max - nextInt(1 + nextInt(max - 3))), from two rolls in [0, 1).</summary>
        public static int DroppedWear(int maxUses, double u1, double u2)
        {
            if (maxUses <= 3) return 0;
            int inner = (int)(u1 * Math.Max(maxUses - 3, 1));
            int outer = (int)(u2 * (1 + inner));
            // (the damage it's left with: most dropped crossbows are badly worn, as Minecraft's are)
            return Math.Max(0, Math.Min(maxUses - 1, maxUses - outer));
        }
    }
}
