using NUnit.Framework;
using OrangeCarrrrr.Core;

namespace OrangeCarrrrr.Tests
{
    /// <summary>
    /// The atlas repaint, pinned against what <c>0x00417160</c> does.
    ///
    /// The stamp behaviour is the part worth guarding: the plate's key is a solid
    /// block of blue, and the original relies on the first hit overwriting the
    /// rest so the plate lands once. A painter that scans a frozen copy passes
    /// every other check here and still stamps the plate 900 times on cotten5.
    /// </summary>
    public sealed class KartSkinPainterTests
    {
        private static readonly KartColorSet Pink = KartColorTable.At(KartColorTable.DefaultIndex);

        private static KartSkinPainter.Image Solid(
            int width, int height, byte red, byte green, byte blue, byte alpha)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < width * height; ++i)
            {
                pixels[i * 4] = red;
                pixels[i * 4 + 1] = green;
                pixels[i * 4 + 2] = blue;
                pixels[i * 4 + 3] = alpha;
            }
            return new KartSkinPainter.Image(pixels, width, height);
        }

        private static void Set(
            KartSkinPainter.Image image, int x, int y,
            byte red, byte green, byte blue, byte alpha)
        {
            int o = (y * image.Width + x) * 4;
            image.Pixels[o] = red;
            image.Pixels[o + 1] = green;
            image.Pixels[o + 2] = blue;
            image.Pixels[o + 3] = alpha;
        }

        private static (byte R, byte G, byte B, byte A) Get(
            KartSkinPainter.Image image, int x, int y)
        {
            int o = (y * image.Width + x) * 4;
            return (image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]);
        }

        [Test]
        public void ShippedDefaultIsPink()
        {
            Assert.AreEqual("pink", KartColorTable.NameAt(KartColorTable.DefaultIndex));
            Assert.AreEqual(10, KartColorTable.Count);
        }

        [Test]
        public void FillerIsKeyedOutRatherThanTinted()
        {
            KartSkinPainter.Image skin = Solid(4, 4, 255, 0, 255, 255);

            KartSkinPainter.Paint(skin, Pink, default, default);

            var texel = Get(skin, 2, 2);
            Assert.AreEqual(0, texel.A, "The atlas filler has to come out transparent.");
        }

        [Test]
        public void TransparentTemplateTexelsTakeTheDriversColour()
        {
            // Alpha 0 means the template contributes nothing, so `base` shows
            // through unchanged. This is the bulk of the body.
            KartSkinPainter.Image skin = Solid(2, 2, 12, 34, 56, 0);

            KartSkinPainter.Paint(skin, Pink, default, default);

            var texel = Get(skin, 0, 0);
            Assert.AreEqual(Pink.BaseRed, texel.R);
            Assert.AreEqual(Pink.BaseGreen, texel.G);
            Assert.AreEqual(Pink.BaseBlue, texel.B);
            Assert.AreEqual(255, texel.A);
        }

        [Test]
        public void OpaqueTemplateTexelsKeepTheirOwnColour()
        {
            KartSkinPainter.Image skin = Solid(2, 2, 12, 34, 56, 255);

            KartSkinPainter.Paint(skin, Pink, default, default);

            var texel = Get(skin, 1, 1);
            Assert.AreEqual(12, texel.R);
            Assert.AreEqual(34, texel.G);
            Assert.AreEqual(56, texel.B);
        }

        [Test]
        public void PlateIsStampedOnceFromTheBlockCorner()
        {
            // A 2x2 block of key blue with a 2x2 plate over it. Scanning the
            // image being written, the hit at (1, 1) covers the whole block and
            // the remaining three keys are gone before the scan reaches them.
            KartSkinPainter.Image skin = Solid(6, 6, 0, 0, 0, 0);
            for (int y = 1; y <= 2; ++y)
            {
                for (int x = 1; x <= 2; ++x) Set(skin, x, y, 0, 0, 255, 255);
            }

            var plate = new KartSkinPainter.Image(
                new byte[]
                {
                    10, 10, 10, 255,   20, 20, 20, 255,
                    30, 30, 30, 255,   40, 40, 40, 255,
                }, 2, 2);

            KartSkinPainter.Paint(skin, Pink, plate, default);

            Assert.AreEqual(10, Get(skin, 1, 1).R, "Plate top-left goes on the key corner.");
            Assert.AreEqual(20, Get(skin, 2, 1).R);
            Assert.AreEqual(30, Get(skin, 1, 2).R);
            Assert.AreEqual(40, Get(skin, 2, 2).R);

            // A second stamp from the key at (2, 1) would have written the
            // plate's top-left over (2, 1) and spilled onto (3, 1).
            Assert.AreEqual(
                Pink.BaseRed, Get(skin, 3, 1).R,
                "The plate must not be stamped again from the block's other texels.");
        }

        [Test]
        public void PlateTakesNoColourAtAll()
        {
            KartSkinPainter.Image skin = Solid(3, 3, 0, 0, 0, 0);
            Set(skin, 0, 0, 0, 0, 255, 255);

            var plate = new KartSkinPainter.Image(new byte[] { 7, 8, 9, 255 }, 1, 1);

            KartSkinPainter.Paint(skin, KartColorTable.At(0), plate, default);

            var texel = Get(skin, 0, 0);
            Assert.AreEqual(7, texel.R);
            Assert.AreEqual(8, texel.G);
            Assert.AreEqual(9, texel.B);
        }

        [Test]
        public void RacingNumberIsCentredOnItsCyanAnchor()
        {
            // The digit is 10x17 placed at (x-5, y-8), so an anchor at (5, 8)
            // puts the digit's own origin exactly at (0, 0). The body is opaque
            // black so it survives the body pass untouched and anything the
            // stamp reaches is visible against it.
            KartSkinPainter.Image skin = Solid(20, 20, 0, 0, 0, 255);
            Set(skin, 5, 8, 0, 255, 255, 255);

            // A fully opaque strip: every covered texel takes `base` outright.
            var number = Solid(
                KartSkinPainter.DigitWidth * 10, KartSkinPainter.DigitHeight, 0, 0, 0, 255);

            KartColorSet red = KartColorTable.At(0);
            KartSkinPainter.Paint(skin, red, default, number);

            Assert.AreEqual(red.BaseRed, Get(skin, 0, 0).R, "Digit origin lands at the anchor minus (5, 8).");
            Assert.AreEqual(
                red.BaseRed, Get(skin, KartSkinPainter.DigitWidth - 1, KartSkinPainter.DigitHeight - 1).R);

            Assert.AreEqual(
                0, Get(skin, KartSkinPainter.DigitWidth, KartSkinPainter.DigitHeight).R,
                "The stamp must not run past the digit's 10x17 box.");
        }

        [Test]
        public void CyanArtworkIsNotMistakenForAnAnchor()
        {
            // A run of near-cyan is neon, not a marker: paragon_9th's glow lines
            // are 401 texels that quantise to the key. Near-cyan rather than
            // exact, because that is what anti-aliased artwork actually is — an
            // exact run is the New generation's paint area instead.
            KartSkinPainter.Image skin = Solid(20, 20, 0, 0, 0, 255);
            Set(skin, 5, 8, 4, 253, 250, 255);
            Set(skin, 6, 8, 4, 253, 250, 255);

            var number = Solid(
                KartSkinPainter.DigitWidth * 10, KartSkinPainter.DigitHeight, 0, 0, 0, 255);

            KartColorSet red = KartColorTable.At(0);
            KartSkinPainter.Paint(skin, red, default, number);

            Assert.AreEqual(0, Get(skin, 0, 0).R, "No digit is stamped off a two-texel run.");

            // And the run itself survives, rather than being cleared to its
            // neighbours the way a real anchor is.
            Assert.AreEqual((byte)253, Get(skin, 5, 8).G);
            Assert.AreEqual((byte)250, Get(skin, 6, 8).B);
        }

        [Test]
        public void ALoneTexelBesideCyanArtworkIsNotAnAnchorEither()
        {
            // paragonV1_gold's neon leaves single cyan texels a few off the edge
            // of the glow, which touch nothing but are still art. An anchor is
            // alone inside the box its own digit covers, because two that close
            // would stamp their numbers on top of each other.
            KartSkinPainter.Image skin = Solid(30, 30, 0, 0, 0, 255);
            Set(skin, 10, 10, 4, 253, 250, 255);
            Set(skin, 11, 10, 4, 253, 250, 255);
            Set(skin, 14, 12, 4, 253, 250, 255);   // detached, inside the digit box
            Set(skin, 25, 25, 0, 255, 255, 255);   // exact and clear of everything

            var number = Solid(
                KartSkinPainter.DigitWidth * 10, KartSkinPainter.DigitHeight, 0, 0, 0, 255);

            KartColorSet red = KartColorTable.At(0);
            KartSkinPainter.Paint(skin, red, default, number);

            Assert.AreEqual((byte)253, Get(skin, 14, 12).G, "The fringe texel is left as art.");
            Assert.AreEqual(
                red.BaseRed, Get(skin, 20, 17).R,
                "The one clear of the artwork is still an anchor and still stamps.");
        }

        [Test]
        public void AFlatFillOfExactCyanTakesTheDriversColour()
        {
            // From the New generation on, the atlas marks the colourable area with
            // a flat fill of cyan instead of with alpha. Left alone it reaches the
            // screen as cyan, which put a sky-blue coat over all five New karts.
            KartSkinPainter.Image skin = Solid(20, 20, 0, 0, 0, 255);
            for (int y = 4; y < 12; ++y)
            {
                for (int x = 4; x < 12; ++x) Set(skin, x, y, 0, 255, 255, 255);
            }

            KartColorSet red = KartColorTable.At(0);
            KartSkinPainter.Paint(skin, red, default, default);

            var texel = Get(skin, 8, 8);
            Assert.AreEqual(red.BaseRed, texel.R, "the fill is where the driver's colour goes");
            Assert.AreEqual(red.BaseGreen, texel.G);
            Assert.AreEqual(red.BaseBlue, texel.B);
            Assert.AreEqual((byte)255, texel.A);

            // The black around it is opaque art and must not have been touched.
            Assert.AreEqual(0, Get(skin, 0, 0).R);
        }

        [Test]
        public void OnACyanKeyedAtlasTheTransparentAreaIsAFixedWhiteBody()
        {
            // The other half of the swap, and the one that is easy to miss: on a
            // demo atlas a transparent texel is where the driver's colour shows,
            // and on a cyan-keyed one it is the fixed white body. Reading it the
            // demo's way put the driver's colour over the whole kart.
            KartSkinPainter.Image skin = Solid(20, 20, 255, 255, 255, 0);
            for (int y = 4; y < 12; ++y)
            {
                for (int x = 4; x < 12; ++x) Set(skin, x, y, 0, 255, 255, 255);
            }

            KartColorSet red = KartColorTable.At(0);
            KartSkinPainter.Paint(skin, red, default, default);

            var body = Get(skin, 0, 0);
            Assert.AreEqual((byte)255, body.R, "the transparent area stays white");
            Assert.AreEqual((byte)255, body.G);
            Assert.AreEqual((byte)255, body.B);
            Assert.AreEqual((byte)255, body.A);

            Assert.AreEqual(red.BaseRed, Get(skin, 8, 8).R, "and the cyan fill takes the colour");
        }

        [Test]
        public void WithoutCyanTheTransparentAreaIsStillTheDriversColour()
        {
            // The demo's own reading, unchanged: no cyan in the atlas means alpha
            // is the paint key and all twenty-six keep behaving as they did.
            KartSkinPainter.Image skin = Solid(8, 8, 255, 255, 255, 0);

            KartColorSet red = KartColorTable.At(0);
            KartSkinPainter.Paint(skin, red, default, default);

            var texel = Get(skin, 4, 4);
            Assert.AreEqual(red.BaseRed, texel.R);
            Assert.AreEqual(red.BaseGreen, texel.G);
            Assert.AreEqual(red.BaseBlue, texel.B);
        }

        [Test]
        public void NearCyanIsArtworkAndKeepsItsColour()
        {
            // Neon is anti-aliased, so its texels only land on the key once
            // ToRgb565 rounds them. Not one of paragon_9th's 401 near-cyan texels
            // is exact 8-bit, and painting them would flatten the glow.
            KartSkinPainter.Image skin = Solid(20, 20, 0, 0, 0, 255);
            for (int y = 4; y < 12; ++y)
            {
                for (int x = 4; x < 12; ++x) Set(skin, x, y, 4, 253, 250, 255);
            }

            Assert.AreEqual(
                KartSkinPainter.KeyCyan, KartSkinPainter.ToRgb565(4, 253, 250),
                "this colour must still match the key in 16 bits, or the test proves nothing");

            KartSkinPainter.Paint(skin, KartColorTable.At(0), default, default);

            var texel = Get(skin, 8, 8);
            Assert.AreEqual(4, texel.R, "near-cyan artwork is left as it was");
            Assert.AreEqual(253, texel.G);
            Assert.AreEqual(250, texel.B);
        }

        [Test]
        public void RacingNumberFollowsTheKartsGrade()
        {
            // Simulator-side: the original stamps 0 on every kart it builds.
            Assert.AreEqual(0, KartSkinPainter.RacingNumberFor("practice1"));
            Assert.AreEqual(1, KartSkinPainter.RacingNumberFor("burst1"));
            Assert.AreEqual(3, KartSkinPainter.RacingNumberFor("cotten3"));
            Assert.AreEqual(5, KartSkinPainter.RacingNumberFor("solid5"));
            Assert.AreEqual(5, KartSkinPainter.RacingNumberFor("saber5"));

            // Anything without a trailing grade falls back to the demo's own 0.
            Assert.AreEqual(0, KartSkinPainter.RacingNumberFor("mine"));
            Assert.AreEqual(0, KartSkinPainter.RacingNumberFor(null));
            Assert.AreEqual(0, KartSkinPainter.RacingNumberFor(" "));
        }

        [Test]
        public void TheStampedDigitIsTheOneAsked()
        {
            // A strip whose digits are told apart by alpha: digit n is opaque
            // only in its own column band.
            const int width = KartSkinPainter.DigitWidth * 10;
            var strip = new byte[width * KartSkinPainter.DigitHeight * 4];
            for (int y = 0; y < KartSkinPainter.DigitHeight; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    // Only the first column of each digit is opaque.
                    strip[(y * width + x) * 4 + 3] =
                        (byte)(x % KartSkinPainter.DigitWidth == 0 ? 255 : 0);
                }
            }
            var number = new KartSkinPainter.Image(strip, width, KartSkinPainter.DigitHeight);

            KartSkinPainter.Image skin = Solid(20, 20, 0, 0, 0, 255);
            Set(skin, 5, 8, 0, 255, 255, 255);

            KartColorSet red = KartColorTable.At(0);
            KartSkinPainter.Paint(skin, red, default, number, digit: 4);

            // Digit 4's opaque column lands at the digit box's left edge.
            Assert.AreEqual(red.BaseRed, Get(skin, 0, 0).R);
            Assert.AreEqual(0, Get(skin, 1, 0).R, "Only the asked-for digit is stamped.");
        }

        [Test]
        public void KeysAreMatchedInTheOriginalsSixteenBitForm()
        {
            Assert.AreEqual(KartSkinPainter.KeyCyan, KartSkinPainter.ToRgb565(0, 255, 255));
            Assert.AreEqual(KartSkinPainter.KeyBlue, KartSkinPainter.ToRgb565(0, 0, 255));
            Assert.AreEqual(KartSkinPainter.KeyMagenta, KartSkinPainter.ToRgb565(255, 0, 255));
        }
    }
}
