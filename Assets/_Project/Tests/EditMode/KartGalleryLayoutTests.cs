using System.Collections.Generic;
using NUnit.Framework;
using OrangeCarrrrr.Core;

namespace OrangeCarrrrr.Tests
{
    /// <summary>
    /// The gallery's grouping and the engine set that rides on it.
    ///
    /// Worth pinning because both fail quietly: a kart in the wrong cell still
    /// renders, and a kart on the wrong engine still drives. The checks that
    /// matter are the whole-table ones — every kart has a cell, no two share one,
    /// and the grid has holes only where the client ships no kart.
    /// </summary>
    public sealed class KartGalleryLayoutTests
    {
        [Test]
        public void EveryKartInTheTableHasACellAndNoCellIsClaimedTwice()
        {
            var seen = new Dictionary<string, string>();

            foreach (KartSpec kart in KartDemoData.Karts)
            {
                KartGalleryLayout.Find(kart.AssetName, out int row, out int column);

                Assert.That(row, Is.InRange(0, KartGalleryLayout.RowCount - 1), kart.AssetName);
                Assert.That(column, Is.InRange(0, KartGalleryLayout.ColumnCount - 1), kart.AssetName);

                string key = $"{row},{column}";
                Assert.That(
                    seen.ContainsKey(key), Is.False,
                    $"cell {key} is both {seen.GetValueOrDefault(key)} and {kart.AssetName}");
                seen[key] = kart.AssetName;
            }
        }

        [Test]
        public void TheGridIsFullExceptWhereTheClientShipsNothing()
        {
            // The five families run the whole way down. Practice skips SR, Z7 and
            // HT, and the paragon only starts at the 9th — those are the only
            // holes, and a new one would mean a kart had gone missing.
            for (int row = 0; row < KartGalleryLayout.RowCount; ++row)
            {
                for (int column = KartGalleryLayout.BurstColumn;
                     column <= KartGalleryLayout.SolidColumn; ++column)
                {
                    Assert.That(
                        KartGalleryLayout.At(row, column), Is.Not.Null,
                        $"row {row} column {column}");
                }
            }

            foreach (int row in new[]
                     {
                         KartGalleryLayout.C1Row, KartGalleryLayout.ProRow,
                         KartGalleryLayout.NewRow, KartGalleryLayout.Paragon9Row,
                         KartGalleryLayout.ParagonXRow, KartGalleryLayout.ParagonV1Row,
                     })
            {
                Assert.That(
                    KartGalleryLayout.At(row, KartGalleryLayout.PracticeColumn), Is.Not.Null,
                    $"practice row {row}");
            }

            foreach (int row in new[]
                     { KartGalleryLayout.SrRow, KartGalleryLayout.Z7Row, KartGalleryLayout.HtRow })
            {
                Assert.That(
                    KartGalleryLayout.At(row, KartGalleryLayout.PracticeColumn), Is.Null,
                    $"the client ships no practice kart at row {row}");
            }

            // Everything from the spector column right only starts at the 9th.
            for (int row = 0; row < KartGalleryLayout.Paragon9Row; ++row)
            {
                for (int column = KartGalleryLayout.SpectorColumn;
                     column < KartGalleryLayout.ColumnCount; ++column)
                {
                    Assert.That(
                        KartGalleryLayout.At(row, column), Is.Null, $"row {row} column {column}");
                }
            }

            // And from the 9th on they are all filled but two, and both gaps are
            // the client's rather than an omission here: the spector has no 9th,
            // and the golden storm blade has no 9th or X model.
            for (int row = KartGalleryLayout.Paragon9Row; row < KartGalleryLayout.RowCount; ++row)
            {
                for (int column = KartGalleryLayout.SpectorColumn;
                     column < KartGalleryLayout.ColumnCount; ++column)
                {
                    bool expected = true;
                    if (column == KartGalleryLayout.SpectorColumn)
                    {
                        expected = row != KartGalleryLayout.Paragon9Row;
                    }
                    else if (column == KartGalleryLayout.GoldenStormBladeColumn)
                    {
                        // V1 only. bike52 is in no pack, and bike58, bike60 and
                        // bike62 ship as param.xml with no model.
                        expected = row == KartGalleryLayout.ParagonV1Row;
                    }

                    Assert.That(
                        KartGalleryLayout.At(row, column) != null, Is.EqualTo(expected),
                        $"row {row} column {column}");
                }
            }
        }

        [Test]
        public void ThePracticeKartIsNotAGradeOne()
        {
            // Its name ends in a 1, which is exactly the read a grade would use.
            Assert.AreEqual(KartGalleryLayout.C1Row, KartGalleryLayout.RowOf("practice1"));
            Assert.AreEqual(
                KartGalleryLayout.PracticeColumn, KartGalleryLayout.ColumnOf("practice1"));

            Assert.AreEqual(KartGalleryLayout.C1Row, KartGalleryLayout.RowOf("burst1"));
            Assert.AreEqual(KartGalleryLayout.BurstColumn, KartGalleryLayout.ColumnOf("burst1"));
        }

        [Test]
        public void EachFamilyHasItsOwnColour()
        {
            Assert.AreEqual(0, KartGalleryLayout.ColourOf("practice1"), "red");
            Assert.AreEqual(4, KartGalleryLayout.ColourOf("burst3"), "teal, the table's sky blue");
            Assert.AreEqual(3, KartGalleryLayout.ColourOf("cotten5"), "green");
            Assert.AreEqual(0, KartGalleryLayout.ColourOf("marathon2"), "red");
            Assert.AreEqual(5, KartGalleryLayout.ColourOf("saber4"), "blue");
            Assert.AreEqual(1, KartGalleryLayout.ColourOf("solid1"), "yellow");
        }

        [Test]
        public void EveryParagonGoldIsOrangeAndEveryBaseIsRed()
        {
            // Orange, not the solid column's yellow: the gold paragon's own livery
            // is already gold, and yellow on top of it read as another solid.
            // Both spellings ship: _golden on the 9th, _gold on X and V1.
            foreach (KartSpec kart in KartDemoData.Karts)
            {
                if (!kart.AssetName.StartsWith("paragon")) continue;

                bool gold = kart.AssetName.IndexOf(
                    "gold", System.StringComparison.OrdinalIgnoreCase) >= 0;
                Assert.AreEqual(
                    gold ? 2 : 0, KartGalleryLayout.ColourOf(kart.AssetName), kart.AssetName);
            }

            Assert.AreEqual("orange", KartColorTable.NameAt(2));

            Assert.AreEqual(0, KartGalleryLayout.ColourOf("spectorV1"), "red");
            Assert.AreEqual(5, KartGalleryLayout.ColourOf("whiteKnightV1"), "blue");
            Assert.AreEqual(0, KartGalleryLayout.ColourOf("slrProV1"), "red");
            Assert.AreEqual(0, KartGalleryLayout.ColourOf("goldKnightV1"), "red");
            Assert.AreEqual(9, KartGalleryLayout.ColourOf("mantisV1"), "white");
            Assert.AreEqual(7, KartGalleryLayout.ColourOf("stormbladeV1_gold"), "black");
            Assert.AreEqual(0, KartGalleryLayout.ColourOf("artemisV1"), "red");
            Assert.AreEqual(
                1, KartGalleryLayout.ColourOf("solid17"), "the solid column stays yellow");
        }

        [Test]
        public void TheEngineSetFollowsTheGeneration()
        {
            Assert.AreEqual(KartEnginePreset.Classic, KartEnginePreset.For("practice1"));
            Assert.AreEqual(KartEnginePreset.Classic, KartEnginePreset.For("cotten5"));
            Assert.AreEqual(KartEnginePreset.Classic, KartEnginePreset.For("solid5"));

            Assert.AreEqual(KartEnginePreset.Sr, KartEnginePreset.For("burst6"));
            Assert.AreEqual(KartEnginePreset.Z7, KartEnginePreset.For("marathon11"));
            Assert.AreEqual(KartEnginePreset.Ht, KartEnginePreset.For("cotton15"));
            Assert.AreEqual(KartEnginePreset.New, KartEnginePreset.For("saber18_newsaber"));

            // The practice column follows its row like everything else.
            Assert.AreEqual(KartEnginePreset.Classic, KartEnginePreset.For("practice5"));
            Assert.AreEqual(KartEnginePreset.New, KartEnginePreset.For("practice6"));
            Assert.AreEqual(KartEnginePreset.Jiu, KartEnginePreset.For("practice7speed"));
            Assert.AreEqual(KartEnginePreset.X, KartEnginePreset.For("practiceX"));
            Assert.AreEqual(KartEnginePreset.V1, KartEnginePreset.For("practiceV1"));

            // The demo spells its own cotton "cotten"; the later client's is
            // "cotton", and the two must not be confused for each other.
            Assert.AreEqual(KartEnginePreset.Classic, KartEnginePreset.For("cotten1"));
            Assert.AreEqual(KartEnginePreset.Sr, KartEnginePreset.For("cotton6"));

            // The family karts of the last three generations, whose names give no
            // hint of their grade: the 9th burst is burst22 and the 9th marathon
            // marathon19, and a trailing-digit read would put them in rows 2 and 9.
            Assert.AreEqual(KartEnginePreset.Jiu, KartEnginePreset.For("burst22"));
            Assert.AreEqual(KartEnginePreset.Jiu, KartEnginePreset.For("marathon19"));
            Assert.AreEqual(KartEnginePreset.Jiu, KartEnginePreset.For("solid26"));
            Assert.AreEqual(KartEnginePreset.X, KartEnginePreset.For("cottonX"));
            Assert.AreEqual(KartEnginePreset.V1, KartEnginePreset.For("saberV1"));

            Assert.AreEqual(KartEnginePreset.Jiu, KartEnginePreset.For("paragon_9th"));
            Assert.AreEqual(KartEnginePreset.Jiu, KartEnginePreset.For("paragon_9th_golden"));
            Assert.AreEqual(KartEnginePreset.X, KartEnginePreset.For("paragonX"));
            Assert.AreEqual(KartEnginePreset.X, KartEnginePreset.For("paragonX_gold"));
            Assert.AreEqual(KartEnginePreset.V1, KartEnginePreset.For("paragonV1"));
            Assert.AreEqual(KartEnginePreset.V1, KartEnginePreset.For("paragonV1_gold"));

            // An unknown kart is driveable on the demo's engine rather than silent.
            Assert.AreEqual(KartEnginePreset.Classic, KartEnginePreset.For("something_new"));
            Assert.AreEqual(KartEnginePreset.Classic, KartEnginePreset.For(null));
        }

        [Test]
        public void EveryKartIsNamedAndNoTwoShareAName()
        {
            var seen = new Dictionary<string, string>();

            foreach (KartSpec kart in KartDemoData.Karts)
            {
                string name = KartDisplayName.For(kart.AssetName);

                Assert.That(name, Is.Not.Empty, kart.AssetName);
                Assert.That(
                    name, Is.Not.EqualTo(kart.AssetName),
                    $"{kart.AssetName} fell through to its asset name");

                // A duplicate would make two rows of the K list read the same,
                // which is worse than no mapping at all.
                Assert.That(
                    seen.ContainsKey(name), Is.False,
                    $"'{name}' is both {seen.GetValueOrDefault(name)} and {kart.AssetName}");
                seen[name] = kart.AssetName;
            }

            Assert.That(seen.Count, Is.EqualTo(KartDemoData.Karts.Count));
        }

        [Test]
        public void TheNamesFollowTheGameNotTheArchive()
        {
            // The practice kart is named by hand: it drops the generation at C1,
            // takes it at PRO, switches to the short 연카 for New and 9, then goes
            // back to the long form for X and V1.
            Assert.AreEqual("연습카트", KartDisplayName.For("practice1"));
            Assert.AreEqual("연습카트 PRO", KartDisplayName.For("practice5"));
            Assert.AreEqual("뉴 연카", KartDisplayName.For("practice6"));
            Assert.AreEqual("스피드 연카 9", KartDisplayName.For("practice7speed"));
            Assert.AreEqual("연습카트 X", KartDisplayName.For("practiceX"));
            Assert.AreEqual("연습카트 V1", KartDisplayName.For("practiceV1"));

            // The demo's five grades carry a letter as well as a number.
            Assert.AreEqual("버스트 C1", KartDisplayName.For("burst1"));
            Assert.AreEqual("코튼 E2", KartDisplayName.For("cotten2"));
            Assert.AreEqual("마라톤 G3", KartDisplayName.For("marathon3"));
            Assert.AreEqual("세이버 R4", KartDisplayName.For("saber4"));
            Assert.AreEqual("솔리드 PRO", KartDisplayName.For("solid5"));

            // The later ones, whose asset names say nothing at all.
            Assert.AreEqual("버스트 SR", KartDisplayName.For("burst6"));
            Assert.AreEqual("마라톤 Z7", KartDisplayName.For("marathon11"));
            Assert.AreEqual("코튼 HT", KartDisplayName.For("cotton15"));
            Assert.AreEqual("솔리드 9", KartDisplayName.For("solid26"));
            Assert.AreEqual("버스트 9", KartDisplayName.For("burst22"));
            Assert.AreEqual("코튼 X", KartDisplayName.For("cottonX"));
            Assert.AreEqual("세이버 V1", KartDisplayName.For("saberV1"));

            // New goes in front of the family rather than after it.
            Assert.AreEqual("뉴 버스트", KartDisplayName.For("burst12"));
            Assert.AreEqual("뉴 세이버", KartDisplayName.For("saber18_newsaber"));
            Assert.AreEqual("뉴 솔리드", KartDisplayName.For("solid17"));

            Assert.AreEqual("파라곤 9", KartDisplayName.For("paragon_9th"));
            Assert.AreEqual("골든 파라곤 9", KartDisplayName.For("paragon_9th_golden"));
            Assert.AreEqual("골든 파라곤 X", KartDisplayName.For("paragonX_gold"));
            Assert.AreEqual("파라곤 V1", KartDisplayName.For("paragonV1"));

            // Both spellings of Cotton are the same family.
            Assert.AreEqual("코튼 C1", KartDisplayName.For("cotten1"));
            Assert.AreEqual("코튼 SR", KartDisplayName.For("cotton6"));

            // The six that only run from the 9th on. Two of them are named
            // nothing like their assets: the black knight is slrPro, and the
            // golden storm blade was a bike before it was a kart.
            Assert.AreEqual("스펙터 X", KartDisplayName.For("spectorX"));
            Assert.AreEqual("흑기사 9", KartDisplayName.For("slrPro7"));
            Assert.AreEqual("흑기사 V1", KartDisplayName.For("slrProV1"));
            Assert.AreEqual("백기사 X", KartDisplayName.For("whiteKnightX"));
            Assert.AreEqual("황금기사 9", KartDisplayName.For("goldKnight9"));
            Assert.AreEqual("멘티스 9", KartDisplayName.For("mantis3"));
            Assert.AreEqual("골든 스톰 블레이드 V1", KartDisplayName.For("stormbladeV1_gold"));
            Assert.AreEqual("아르테미스 X", KartDisplayName.For("artemisX"));
        }

        [Test]
        public void TheSimulatorOpensOnTheRedParagon()
        {
            Assert.AreEqual("paragon_9th", KartDemoData.DefaultKart.AssetName);
            Assert.AreEqual("red", KartColorTable.NameAt(KartColorTable.SimulatorIndex));

            // The recovered value is still readable beside the bench's choice.
            Assert.AreEqual("pink", KartColorTable.NameAt(KartColorTable.DefaultIndex));
        }
    }
}
