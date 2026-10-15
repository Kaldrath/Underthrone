using UnityEngine;

namespace Underthrone.Client
{
    /// Generates simple 8-direction pixel silhouettes so the slice runs before real art exists.
    /// A dark visor band shows which way the figure faces: centred when it faces you (S), at the
    /// side for W/E, absent from behind (N).
    internal static class PlaceholderSprites
    {
        private const int WIDTH = 24;
        private const int HEIGHT = 40;
        private const float PIXELS_PER_UNIT = 20f; // 2 m tall figure

        public static DirectionalSpriteSet Build(Color body, Color shade, Color visor)
        {
            var sprites = new Sprite[8];
            for (int orientation = 0; orientation < 8; orientation++)
            {
                sprites[orientation] = Draw(orientation, body, shade, visor);
            }
            return DirectionalSpriteSet.Create(sprites, mirrorEastFromWest: false);
        }

        private static Sprite Draw(int orientation, Color body, Color shade, Color visor)
        {
            var tex = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var clear = new Color(0, 0, 0, 0);
            var pixels = new Color[WIDTH * HEIGHT];
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                pixels[pixelIndex] = clear;
            }

            // Side views are narrower than front/back views.
            bool side = orientation == 2 || orientation == 6;
            bool diagonal = orientation % 2 == 1;
            int halfWidth = side ? 4 : diagonal ? 6 : 8;
            int cx = WIDTH / 2;
            Fill(pixels, cx - halfWidth, 2, cx + halfWidth, 26, body, shade);          // body + legs
            Fill(pixels, cx - 5, 27, cx + 5, 37, body, shade);                           // head
            Fill(pixels, cx - 1, 2, cx, 12, clear, clear);                               // gap between legs

            // Visor: 0 S centred, 1 SW / 7 SE offset, 2 W / 6 E at the edge, 3-5 none.
            int visorOffset = orientation switch
            {
                0 => 0,
                1 => -2,
                7 => 2,
                2 => -4,
                6 => 4,
                _ => int.MinValue,
            };
            if (visorOffset != int.MinValue)
            {
                Fill(pixels, cx + visorOffset - 3, 31, cx + visorOffset + 3, 33, visor, visor);
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, WIDTH, HEIGHT), new Vector2(0.5f, 0f), PIXELS_PER_UNIT);
        }

        /// Fills a rect lit from the left: left third body colour, the rest shaded.
        private static void Fill(Color[] pixels, int x0, int y0, int x1, int y1, Color lit, Color shaded)
        {
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(HEIGHT - 1, y1); y++)
            {
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(WIDTH - 1, x1); x++)
                {
                    bool litSide = x < x0 + (x1 - x0 + 1) / 3;
                    pixels[y * WIDTH + x] = litSide ? lit : shaded;
                }
            }
        }
    }
}
