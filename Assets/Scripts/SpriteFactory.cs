using UnityEngine;

namespace MergeUp
{
    /// <summary>
    /// The game ships no art. The one sprite, a white rounded square, is generated at start-up and
    /// tinted with SpriteRenderer.color, so there are no image assets to import or commit.
    /// </summary>
    public static class SpriteFactory
    {
        private static Sprite rounded;

        /// <summary>A white rounded square exactly one world unit across, with a one pixel anti-aliased edge.</summary>
        public static Sprite RoundedSquare()
        {
            if (rounded != null) return rounded;
            const int size = 64;
            const float radius = 14f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance outside the inner (square minus corner radius) box: <= 0 inside the shape.
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - radius), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            rounded.name = "MergeUp_RoundedSquare";
            return rounded;
        }
    }
}
