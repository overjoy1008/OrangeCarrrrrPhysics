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
    ///                          — an isolated texel, see <see cref="IsAnchor"/>
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

            // From the New generation on, an atlas keys its paint with cyan — and
            // when it does, the two roles are the other way round from the demo's.
            // See KeyPaintAreas.
            bool cyanKeyed = KeyPaintAreas(skin, colour);

            if (cyanKeyed) PaintBody(skin, FixedBody, FixedBody, FixedBody);
            else PaintBody(skin, colour.BaseRed, colour.BaseGreen, colour.BaseBlue);

            Stamp(skin, colour, plate, number, digit, mirrorStamps);
        }

        /// <summary>
        /// The body colour a cyan-keyed atlas shows where it is transparent.
        ///
        /// Not invented: those texels already hold white in the atlas. Every one
        /// of the demo's twenty-six keeps its paint area as white at alpha 0 —
        /// the colour is ignored there because the alpha is what carries the key
        /// — and the New atlases keep the same white. On them it is the value
        /// that reaches the screen rather than a placeholder.
        /// </summary>
        private const byte FixedBody = 255;

        /// <summary>
        /// Finds the later client's cyan paint areas and lays the driver's colour
        /// into them. Answers whether this atlas is one that uses them.
        ///
        /// <b>The New generation swaps the two roles the demo uses.</b> On a demo
        /// atlas, alpha is the paint key — a transparent texel lets <c>base</c>
        /// through — and there is no cyan but the racing number's anchor. From the
        /// New generation on:
        ///
        /// <list type="bullet">
        /// <item>a flat fill of exact cyan is where <c>base</c> goes;</item>
        /// <item>the transparent areas are a fixed white body, not <c>base</c>.
        /// </item>
        /// </list>
        ///
        /// Reading those the demo's way is wrong twice over, and both were seen on
        /// screen: leaving the cyan alone put a sky-blue coat on all five, and
        /// then painting the transparent areas put the driver's colour everywhere
        /// the white body should have been.
        ///
        /// Two passes, and that is load-bearing. <see cref="IsAnchor"/> reads the
        /// colour of the neighbours, so writing as we went would leave the second
        /// texel of a pair looking isolated once the first had been overwritten —
        /// and turn a paint area into a racing number.
        /// </summary>
        private static bool KeyPaintAreas(Image skin, KartColorSet colour)
        {
            byte[] pixels = skin.Pixels;
            int count = skin.Width * skin.Height;

            bool[] paint = null;
            for (int y = 0; y < skin.Height; ++y)
            {
                for (int x = 0; x < skin.Width; ++x)
                {
                    int i = y * skin.Width + x;
                    if (!IsExactCyan(pixels, i * 4)) continue;
                    if (IsAnchor(skin, x, y)) continue;

                    paint ??= new bool[count];
                    paint[i] = true;
                }
            }

            // Nothing on the demo's twenty-six, so they never pay for the second
            // pass or the allocation, and they keep the demo's own reading.
            if (paint == null) return false;

            for (int i = 0; i < count; ++i)
            {
                if (!paint[i]) continue;

                int o = i * 4;
                pixels[o] = colour.BaseRed;
                pixels[o + 1] = colour.BaseGreen;
                pixels[o + 2] = colour.BaseBlue;

                // Opaque, so the body pass carries it through untouched rather
                // than blending the white body back into it.
                pixels[o + 3] = 255;
            }
            return true;
        }

        /// <summary>
        /// Cyan as the atlas author typed it, before the 16-bit match widens it.
        ///
        /// This is what separates a key from artwork. All twenty-six demo anchors
        /// are exactly (0, 255, 255), and so is every texel of the New
        /// generation's paint areas — 17,390 of 17,390 on cotton19. Neon is never
        /// exact: it is anti-aliased, so its edge texels only land on the key
        /// after <see cref="ToRgb565"/> rounds them, and not one of paragon_9th's
        /// 401 near-cyan texels is exact. Matching on the quantised value alone
        /// cannot tell the two apart, which is why both tests are here.
        /// </summary>
        private static bool IsExactCyan(byte[] pixels, int offset)
            => pixels[offset] == 0 && pixels[offset + 1] == 255 && pixels[offset + 2] == 255;

        /// <summary>
        /// The body: the template composited over a solid colour, weighted by the
        /// template's own alpha, with the filler keyed out.
        ///
        /// What it sits over is the driver's <c>base</c> on a demo atlas and the
        /// fixed white body on a cyan-keyed one — the two conventions disagree
        /// about what a transparent texel means, and this is where that is
        /// settled. See <see cref="KeyPaintAreas"/>.
        /// </summary>
        private static void PaintBody(Image skin, byte underRed, byte underGreen, byte underBlue)
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
                pixels[o] = Blend(underRed, red, alpha);
                pixels[o + 1] = Blend(underGreen, green, alpha);
                pixels[o + 2] = Blend(underBlue, blue, alpha);
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

                    if (key == KeyCyan && IsAnchor(skin, x, y))
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
        /// Whether a cyan texel is the racing number's anchor rather than art or
        /// a paint area.
        ///
        /// Two things have to hold. It must be exact 8-bit cyan, which rules out
        /// anti-aliased neon (see <see cref="IsExactCyan"/>). And it must be alone
        /// inside the box its own digit would cover — which follows from what an
        /// anchor is: it marks the middle of one 10x17 stamp, so a second anchor
        /// that close would put two numbers on top of each other, and a flat fill
        /// of the colour is a paint area rather than a marker. All twenty-six demo
        /// atlases hold to both: theirs are exact, and isolated a hundred texels
        /// apart.
        ///
        /// The later client's karts need the distinction made. Their atlases key
        /// off the same magenta and the same 45x20 block of blue, but their
        /// artwork has neon in it, and bright cyan neon quantises to the key.
        /// paragon_9th's glow lines are 401 such texels in blobs up to 150
        /// across; paragonV1_gold's are a blob plus the stray dots its
        /// anti-aliasing leaves a few texels off the edge. Neither kart has a
        /// racing number — plain paragonV1 carries no cyan at all — and reading
        /// the neon as anchors carpets the glow with digits.
        /// </summary>
        private static bool IsAnchor(Image skin, int x, int y)
        {
            byte[] pixels = skin.Pixels;

            if (!IsExactCyan(pixels, (y * skin.Width + x) * 4)) return false;

            for (int row = 0; row < DigitHeight; ++row)
            {
                int ny = y + DigitOffsetY + row;
                if (ny < 0 || ny >= skin.Height) continue;

                for (int column = 0; column < DigitWidth; ++column)
                {
                    int nx = x + DigitOffsetX + column;
                    if (nx < 0 || nx >= skin.Width) continue;
                    if (nx == x && ny == y) continue;

                    int o = (ny * skin.Width + nx) * 4;
                    if (ToRgb565(pixels[o], pixels[o + 1], pixels[o + 2]) == KeyCyan) return false;
                }
            }

            return true;
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
