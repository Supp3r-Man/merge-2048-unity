using UnityEngine;

namespace MergeUp
{
    /// <summary>Tile colours, generated from the tile's power of two so any value up to 131072 and beyond has one.</summary>
    public static class Palette
    {
        public static readonly Color Background = new Color(0.07f, 0.09f, 0.14f);
        public static readonly Color BoardColor = new Color(0.14f, 0.17f, 0.25f);
        public static readonly Color EmptyCell = new Color(0.19f, 0.23f, 0.33f);

        /// <summary>Which power of two: 2 -> 1, 4 -> 2, 8 -> 3, ...</summary>
        public static int Level(int value)
        {
            int level = 0;
            while (value > 1) { value >>= 1; level++; }
            return level;
        }

        /// <summary>Hue walks from teal toward magenta and then keeps cycling; brightness rises with the level.</summary>
        public static Color TileColor(int value)
        {
            int level = Mathf.Max(1, Level(value));
            float hue = (0.5f + 0.055f * (level - 1)) % 1f;
            float sat = Mathf.Min(0.75f, 0.35f + 0.04f * level);
            float val = Mathf.Min(1f, 0.70f + 0.025f * level);
            return Color.HSVToRGB(hue, sat, val);
        }

        /// <summary>Dark text on pale tiles, white on saturated ones.</summary>
        public static Color TextColor(int value)
        {
            return Level(value) <= 2 ? new Color(0.1f, 0.12f, 0.2f) : Color.white;
        }
    }
}
