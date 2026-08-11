using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// Repaints a kart's atlas for a colour set, the way <c>0x00417160</c> builds
    /// a kart.
    ///
    /// The atlas that ships beside <c>model.1s</c> is not a finished skin. It is a
    /// template with three kinds of texel in it, and the game assembles the real
    /// thing when the kart is created:
    ///
    ///   magenta (255, 0, 255)  the atlas filler, keyed out to transparent
    ///   cyan    (0, 255, 255)  anchors the racing number, one 10x17 digit
    ///   blue    (0, 0, 255)    top-left of the 45x20 number plate
    ///
    /// Everything else is alpha-composited over the driver's <c>base</c> colour,
    /// which is what makes the same mesh come out pink or teal.
    ///
    /// Two details are load-bearing:
    ///
    /// 1. The stamp pass reads the image it is writing into, exactly as the
    ///    original walks the composited atlas in place. The plate's key is a
    ///    solid 45x20 block of blue, so the hit at its top-left corner overwrites
    ///    all 900 of them and the scan finds no more. Scanning a frozen copy
    ///    instead stamps the plate 900 times.
    /// 2. Keys are matched after quantising to RGB565, because the original holds
    ///    the atlas in 16 bits and compares there. cotten5 happens to carry the
    ///    keys as exact 8-bit values, but matching the way the original does
    ///    keeps every other kart's atlas reading the same.
    ///
    /// Everything here is plain byte arrays in RGBA8 so the algorithm can be
    /// tested without a Texture2D; the Unity wrapper lives in the Runtime
    /// assembly.
    /// </summary>
    public static class KartSkinPainter
    {
        /// <summary>(0, 255, 255) in RGB565 — the racing number's anchor.</summary>
        public const ushort KeyCyan = 0x07FF;

        /// <summary>(0, 0, 255) — the number plate's top-left corner.</summary>
        public const ushort KeyBlue = 0x001F;

        /// <summary>(255, 0, 255) — the atlas filler.</summary>
        public const ushort KeyMagenta = 0xF81F;

        /// <summary>The racing number is one digit of a 100x17 ten-digit strip.</summary>
        public const int DigitWidth = 10;
        public const int DigitHeight = 17;

        /// <summary>The cyan texel sits at the middle of the digit it anchors.</summary>
        public const int DigitOffsetX = -5;
        public const int DigitOffsetY = -8;

        /// <summary>The demo builds every kart with digit 0.</summary>
        public const int RacingNumberDigit = 0;

        /// <summary>The strip carries ten digits side by side.</summary>
        public const int DigitCount = 10;

        /// <summary>
        /// The kart's grade, read off the end of its asset name: burst3 is a 3,
        /// solid5 a 5. The practice kart is the one without a grade, so it keeps
        /// the demo's own 0.
        ///
        /// This is a simulator-side choice. The original stamps digit 0 on every
        /// kart it builds; nothing in the archives ties the number to the model.
        /// </summary>
        public static int RacingNumberFor(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName)) return RacingNumberDigit;

            string name = assetName.Trim();
            if (name.StartsWith("practice", System.StringComparison.OrdinalIgnoreCase))
            {
                return RacingNumberDigit;
            }

            char last = name[name.Length - 1];
            if (last < '0' || last > '9') return RacingNumberDigit;

            return last - '0';
        }

        /// <summary>
        /// The plate's key is a solid block of this size — 900 texels on every one
        /// of the twenty-six karts. Used to cover the key when the plate itself is
        /// not being stamped.
        /// </summary>
        public const int PlateWidth = 45;
        public const int PlateHeight = 20;

        /// <summary>An 8-bit RGBA image, as every input and the output here.</summary>
        public readonly struct Image
        {
            public readonly byte[] Pixels;
            public readonly int Width;
            public readonly int Height;

            public Image(byte[] pixels, int width, int height)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
            }

            public bool IsValid =>
                Pixels != null && Width > 0 && Height > 0
                && Pixels.Length >= Width * Height * 4;
        }

        /// <summary>
        /// Paints <paramref name="skin"/> in place.
        ///
        /// <paramref name="plate"/> and <paramref name="number"/> are the two
        /// shared images beside the kart archives; either may be invalid, in
        /// which case that stamp is skipped and the rest still runs.
        /// </summary>
        public static void Paint(
            Image skin,
            KartColorSet colour,
            Image plate,
            Image number,
            int digit = RacingNumberDigit,
            bool mirrorStamps = false)
        {
            if (!skin.IsValid) throw new ArgumentException("Skin image is empty.", nameof(skin));

            PaintBody(skin, colour);
            Stamp(skin, colour, plate, number, digit, mirrorStamps);
        }

        /// <summary>
        /// The body: the template composited over a solid <c>base</c>, weighted by
        /// the template's own alpha, with the filler keyed out.
        /// </summary>
        private static void PaintBody(Image skin, KartColorSet colour)
        {
            byte[] pixels = skin.Pixels;
            int count = skin.Width * skin.Height;

            for (int i = 0; i < count; ++i)
            {
                int o = i * 4;
                byte red = pixels[o];
                byte green = pixels[o + 1];
                byte blue = pixels[o + 2];

                if (ToRgb565(red, green, blue) == KeyMagenta)
                {
                    // Keyed out before the blend, so the filler never tints.
                    pixels[o + 3] = 0;
                    continue;
                }

                byte alpha = pixels[o + 3];
                pixels[o] = Blend(colour.BaseRed, red, alpha);
                pixels[o + 1] = Blend(colour.BaseGreen, green, alpha);
                pixels[o + 2] = Blend(colour.BaseBlue, blue, alpha);
                pixels[o + 3] = 255;
            }
        }

        /// <summary>
        /// The racing number and the plate, scanning the image being written.
        /// </summary>
        private static void Stamp(
            Image skin, KartColorSet colour, Image plate, Image number, int digit,
            bool mirrorStamps)
        {
            byte[] pixels = skin.Pixels;
            int width = skin.Width;
            int height = skin.Height;

            bool canStampNumber = number.IsValid;
            bool canStampPlate = plate.IsValid;

            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    int o = (y * width + x) * 4;
                    ushort key = ToRgb565(pixels[o], pixels[o + 1], pixels[o + 2]);

                    if (key == KeyCyan)
                    {
                        // Cleared first, then stamped over. The anchor sits in the
                        // middle of the roundel, which is exactly where digit 0
                        // has its hole: the blend leaves alpha-0 texels alone, so
                        // stamping on its own would leave the cyan marker showing
                        // through the centre of the number.
                        ClearKey(skin, colour, x, y);
                        if (canStampNumber) StampNumber(skin, colour, number, digit, mirrorStamps, x, y);
                    }
                    else if (key == KeyBlue)
                    {
                        if (canStampPlate) StampPlate(skin, plate, mirrorStamps, x, y);
                        else Fill(skin, colour, x, y, PlateWidth, PlateHeight);
                    }
                }
            }
        }

        /// <summary>
        /// Replaces a single key texel with the art around it.
        ///
        /// The neighbours are the roundel the anchor sits in, so taking one of
        /// them makes the marker disappear into the artwork rather than leaving a
        /// dot of paint where the art is white. Falls back to the driver's colour
        /// when every neighbour is itself a key.
        /// </summary>
        private static void ClearKey(Image skin, KartColorSet colour, int x, int y)
        {
            byte[] pixels = skin.Pixels;
            int target = (y * skin.Width + x) * 4;

            for (int i = 0; i < 4; ++i)
            {
                int nx = x + (i == 0 ? -1 : i == 1 ? 1 : 0);
                int ny = y + (i == 2 ? -1 : i == 3 ? 1 : 0);
                if (nx < 0 || nx >= skin.Width || ny < 0 || ny >= skin.Height) continue;

                int source = (ny * skin.Width + nx) * 4;
                ushort key = ToRgb565(pixels[source], pixels[source + 1], pixels[source + 2]);
                if (key == KeyCyan || key == KeyBlue || key == KeyMagenta) continue;
                if (pixels[source + 3] == 0) continue;

                pixels[target] = pixels[source];
                pixels[target + 1] = pixels[source + 1];
                pixels[target + 2] = pixels[source + 2];
                pixels[target + 3] = 255;
                return;
            }

            Fill(skin, colour, x, y, 1, 1);
        }

        /// <summary>
        /// Paints over a key with the driver's colour.
        ///
        /// A key texel is a marker, never something meant to be seen. Leaving one
        /// in place because its stamp was unavailable is what puts a solid blue
        /// rectangle on the back of the kart and a cyan dot on its side, so a
        /// stamp that is skipped still has to cover what it would have covered.
        /// </summary>
        private static void Fill(
            Image skin, KartColorSet colour, int originX, int originY, int width, int height)
        {
            byte[] pixels = skin.Pixels;

            for (int row = 0; row < height; ++row)
            {
                int y = originY + row;
                if (y < 0 || y >= skin.Height) continue;

                for (int column = 0; column < width; ++column)
                {
                    int x = originX + column;
                    if (x < 0 || x >= skin.Width) continue;

                    int o = (y * skin.Width + x) * 4;
                    pixels[o] = colour.BaseRed;
                    pixels[o + 1] = colour.BaseGreen;
                    pixels[o + 2] = colour.BaseBlue;
                    pixels[o + 3] = 255;
                }
            }
        }

        /// <summary>
        /// One digit of the strip, blended in through its alpha so the number
        /// takes the driver's colour rather than the strip's.
        /// </summary>
        private static void StampNumber(
            Image skin, KartColorSet colour, Image number, int digit, bool mirror,
            int anchorX, int anchorY)
        {
            byte[] pixels = skin.Pixels;
            if (digit < 0 || digit >= DigitCount) digit = RacingNumberDigit;
            int sourceX = digit * DigitWidth;

            for (int row = 0; row < DigitHeight; ++row)
            {
                int targetY = anchorY + DigitOffsetY + row;
                if (targetY < 0 || targetY >= skin.Height) continue;
                if (row >= number.Height) continue;

                for (int column = 0; column < DigitWidth; ++column)
                {
                    int targetX = anchorX + DigitOffsetX + column;
                    if (targetX < 0 || targetX >= skin.Width) continue;

                    int read = mirror ? DigitWidth - 1 - column : column;
                    if (sourceX + read >= number.Width) continue;
                    byte alpha = number.Pixels[
                        (row * number.Width + sourceX + read) * 4 + 3];

                    int o = (targetY * skin.Width + targetX) * 4;
                    pixels[o] = Blend(pixels[o], colour.BaseRed, alpha);
                    pixels[o + 1] = Blend(pixels[o + 1], colour.BaseGreen, alpha);
                    pixels[o + 2] = Blend(pixels[o + 2], colour.BaseBlue, alpha);
                    pixels[o + 3] = 255;
                }
            }
        }

        /// <summary>
        /// The plate, copied straight in: it takes no colour at all, which is why
        /// the NEXON logo reads the same on every kart.
        /// </summary>
        private static void StampPlate(
            Image skin, Image plate, bool mirror, int anchorX, int anchorY)
        {
            byte[] pixels = skin.Pixels;

            for (int row = 0; row < plate.Height; ++row)
            {
                int targetY = anchorY + row;
                if (targetY < 0 || targetY >= skin.Height) continue;

                for (int column = 0; column < plate.Width; ++column)
                {
                    int targetX = anchorX + column;
                    if (targetX < 0 || targetX >= skin.Width) continue;

                    int read = mirror ? plate.Width - 1 - column : column;
                    int source = (row * plate.Width + read) * 4;
                    int target = (targetY * skin.Width + targetX) * 4;

                    pixels[target] = plate.Pixels[source];
                    pixels[target + 1] = plate.Pixels[source + 1];
                    pixels[target + 2] = plate.Pixels[source + 2];
                    pixels[target + 3] = 255;
                }
            }
        }

        /// <summary><c>over</c> laid on <c>under</c> at <c>alpha</c>, in 8 bits.</summary>
        private static byte Blend(byte under, byte over, byte alpha)
            => (byte)((under * (255 - alpha) + over * alpha) / 255);

        /// <summary>The 16-bit form the original compares its keys in.</summary>
        public static ushort ToRgb565(byte red, byte green, byte blue)
            => (ushort)(((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3));
    }
}
