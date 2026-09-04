using SoulPlayer.Cassettes;
using UnityEngine;

namespace SoulPlayer.UI
{
    internal static class SoulTapeRarityPresentation
    {
        internal static string GetLabel(SoulTapeRarity rarity)
        {
            return rarity.ToString().ToUpperInvariant();
        }

        internal static Color GetAccent(SoulTapeRarity rarity)
        {
            switch (rarity)
            {
                case SoulTapeRarity.Uncommon:
                    return new Color(0.38f, 0.72f, 0.46f, 1f);
                case SoulTapeRarity.Rare:
                    return new Color(0.34f, 0.58f, 0.88f, 1f);
                case SoulTapeRarity.Epic:
                    return new Color(0.65f, 0.43f, 0.86f, 1f);
                case SoulTapeRarity.Legendary:
                    return new Color(0.88f, 0.66f, 0.25f, 1f);
                default:
                    return new Color(0.56f, 0.59f, 0.60f, 1f);
            }
        }
    }
}
