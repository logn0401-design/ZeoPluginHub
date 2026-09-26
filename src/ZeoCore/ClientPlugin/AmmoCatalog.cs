using System;
using System.Collections.Generic;

namespace ZeoCore
{
    internal sealed class AmmoCatalogEntry
    {
        public string Key;
        public string Subtype;
        public string CleanName;
        public string ServerName;
        public int DefaultWant;
    }

    internal static class AmmoCatalog
    {
        internal static readonly AmmoCatalogEntry[] Entries =
        {
            new AmmoCatalogEntry { Key="PDC40", Subtype="sdx_ammomagazinePdc40mm", CleanName="40mm PDC", ServerName="40mm PDC Ammo", DefaultWant=2000 },
            new AmmoCatalogEntry { Key="PDC40IMP", Subtype="sdx_ammomagazinePdc40mmImprovised", CleanName="40mm Improvised PDC", ServerName="40mm PDC Ammo Improvised", DefaultWant=1000 },
            new AmmoCatalogEntry { Key="PDC50", Subtype="sdx_ammomagazinePdc50mm", CleanName="50mm PDC", ServerName="50mm PDC Ammo", DefaultWant=1500 },
            new AmmoCatalogEntry { Key="SABOT80", Subtype="sdx_ammomagazineSabot80mm", CleanName="80mm Sabot", ServerName="80mm Sabot", DefaultWant=1000 },
            new AmmoCatalogEntry { Key="SABOT80IMP", Subtype="sdx_ammomagazineSabot80mmImprovised", CleanName="80mm Improvised Sabot", ServerName="80mm Sabot Improvised", DefaultWant=1000 },
            new AmmoCatalogEntry { Key="SABOT100", Subtype="sdx_ammomagazineSabot100mm", CleanName="100mm Sabot", ServerName="100mm Tungsten-Uranium Sabot", DefaultWant=600 },
            new AmmoCatalogEntry { Key="TORP160", Subtype="sdx_ammomagazineTorpedo160mm", CleanName="160mm Torpedo", ServerName="160mm Plasma Torpedo", DefaultWant=20 },
            new AmmoCatalogEntry { Key="TORP190", Subtype="sdx_ammomagazineTorpedo190mmImprovised", CleanName="190mm Torpedo", ServerName="190mm Improvised Torpedo", DefaultWant=20 },
            new AmmoCatalogEntry { Key="TORP220", Subtype="sdx_ammomagazineTorpedo220mm", CleanName="220mm Torpedo", ServerName="220mm Plasma Torpedo", DefaultWant=10 },
        };

        internal static AmmoCatalogEntry FindSubtype(string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype)) return null;
            for (int i=0;i<Entries.Length;i++)
                if (string.Equals(Entries[i].Subtype, subtype, StringComparison.OrdinalIgnoreCase)) return Entries[i];
            return null;
        }
    }
}
