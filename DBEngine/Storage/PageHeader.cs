namespace DBEngine.Storage;

internal record struct PageHeader
{ 
    // 16 bytes utilisés, 16 bytes réservés pour usage futur = 32 bytes total
    internal uint PageId;        // 4 bytes — numéro unique de la page
    internal PageType Type;      // 1 byte  — type de la page
    internal byte Flags;         // 1 byte  — usage futur
    internal ushort FreeBytes;   // 2 bytes — espace libre restant
    internal uint NextPageId;    // 4 bytes — page suivante (chaînage), 0 si aucune
    internal uint PrevPageId;    // 4 bytes — page précédente, 0 si aucune
}
