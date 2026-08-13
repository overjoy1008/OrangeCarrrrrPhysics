using System;
using System.Collections.Generic;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// One kart that is not from either client — a guest.
    ///
    /// Everything about a recovered kart is transcribed: its geometry is the
    /// model-root AABB out of <c>kart.rho</c> and its dynamics are rows of
    /// <c>parameter.xml</c>. A guest has no such source, so the fields here say
    /// what it should be measured and matched <em>against</em> instead, and the
    /// importer derives the rest from the model it actually ships.
    /// </summary>
    public sealed class KartGuestSpec
    {
        /// <summary>The name the spec asset, the prefab and the K menu use.</summary>
        public string AssetName;

        /// <summary>What the HUD calls it. Guests are not in the gallery grid, so
        /// unlike a recovered kart there is nothing to derive this from.</summary>
        public string DisplayName;

        /// <summary>Model file under <c>Art/Karts/Models/Guest</c>.</summary>
        public string ModelFile;

        /// <summary>Body texture beside it, drawn opaque, or null for a model
        /// that has none — then <see cref="BodyColorHtml"/> is used.</summary>
        public string BodyTexture;

        /// <summary>
        /// Flat colour, as <c>#RRGGBB</c>, for a model that ships no texture.
        ///
        /// An STL is the case that needs it: the format carries triangles and
        /// nothing else — no UVs, no materials — so there is no atlas to put on it
        /// and no way to invent one. A single colour is the honest result rather
        /// than a texture stretched over unwrapped geometry.
        /// </summary>
        public string BodyColorHtml;

        /// <summary>
        /// Normal map beside the body texture, or null.
        ///
        /// Only the normal map is taken. The same export also ships roughness and
        /// metallic as separate images, and URP's Lit wants those packed into one
        /// texture's channels — repacking them is work with a visible cost of
        /// nothing much on a cartoon banana, so they are left on disk.
        /// </summary>
        public string NormalTexture;

        /// <summary>
        /// A second texture drawn alpha-clipped, for the part of the model that
        /// is a card rather than geometry, or null when there is none. Maxwell's
        /// whiskers are two crossed quads and come out as black flags without it.
        /// </summary>
        public string CutoutTexture;

        /// <summary>
        /// Sub-mesh whose name marks it as the cutout part, and — when the
        /// facing is derived rather than given — which end of the model the nose
        /// is. Only consulted for <see cref="ModelRotationDeg"/> being null.
        /// </summary>
        public string FaceMeshHint;

        /// <summary>
        /// Rotation in degrees about X, Y and Z — exactly the three numbers the
        /// transform inspector shows — that stands the model up and points it
        /// down +Z, or null to work it out from the mesh.
        ///
        /// Deriving it is a guess and giving it is not. The mesh says which axis
        /// is longest and, through the face submesh, which end the nose is on,
        /// but nothing in it says which way is up: a cat lying on its side is a
        /// perfectly plausible cat, and Maxwell's own file disagrees with itself
        /// about the matter — it declares <c>UpAxis = 1</c> and then hands its
        /// root node a -90 degree correction. So once someone has turned the
        /// model in the inspector and seen it face front, that reading wins.
        ///
        /// Applied before anything is measured. A model measured lying down would
        /// be fitted by its height, which sizes the wrong animal.
        /// </summary>
        public float[] ModelRotationDeg;

        /// <summary>
        /// The kart line this one is sized against. The importer scales the model
        /// uniformly until its length matches that line's mean, so a guest sits
        /// in traffic at the same size as the karts around it whatever units it
        /// was modelled in. Its width and height are then whatever the model's
        /// own proportions make them — the alternative is a squashed cat.
        /// </summary>
        public string SizeReference;

        /// <summary>
        /// Fit by height to this many metres instead, or 0 to use
        /// <see cref="SizeReference"/>.
        ///
        /// For a model that stands rather than lies: the banana cat is 100 units
        /// tall against a 59 unit footprint, and fitting its length to the paragon
        /// line would leave it nearly four metres high.
        ///
        /// Height rather than a multiplier on the length fit, because height does
        /// not move when the yaw does. A multiplier would silently change what it
        /// meant the moment the model was turned a quarter turn, since the fit
        /// divides by whichever horizontal extent is on Z.
        /// </summary>
        public float SizeHeightMeters;

        /// <summary>
        /// Engine set, as a <see cref="KartEnginePreset"/> name. A guest is in no
        /// generation, so this is a choice rather than something its row implies.
        /// </summary>
        public string EnginePreset;

        /// <summary>
        /// A booster one-shot of this kart's own, under
        /// <c>Audio/Kart/Guest</c>, or null to use its engine set's.
        ///
        /// Per kart rather than per preset because that is what it is: the sound
        /// belongs to the guest, not to a generation it is not part of. It
        /// replaces only the booster — the motor, the drift and the impacts still
        /// come from the set, so the kart still sounds like it is on a track.
        /// </summary>
        public string BoosterSound;

        /// <summary>
        /// A theme of this kart's own, under <c>Audio/Music/Guest</c>, that
        /// replaces the track's while it is being driven.
        ///
        /// The recovered music belongs to the track — a village course plays the
        /// village theme — so a kart that brings its own is a guest thing and
        /// nothing else. Picking another kart puts the track's theme back.
        /// </summary>
        public string ThemeMusic;

        /// <summary>
        /// Seconds to skip into <see cref="BoosterSound"/>, for a sample that
        /// opens on silence. Measured off the file, not guessed.
        /// </summary>
        public float BoosterSoundStart;

        /// <summary>
        /// A second take inside the same file, or 0 for none.
        ///
        /// The OIIA booster holds both: a fast run from 1.48 s and a slow one from
        /// 4.15 s, with silence between them. An item boost picks one at random,
        /// which is why the two live in one clip rather than two — they are one
        /// recording and splitting the file would only be a second thing to keep.
        /// </summary>
        public float BoosterSoundSlowStart;

        /// <summary>
        /// What the spin speed is multiplied by when the slow take is playing, so
        /// the cat turns at the speed it is singing at.
        /// </summary>
        public float SlowSpinScale;

        /// <summary>
        /// The blend shape that morphs the model into its second look, or null
        /// for a guest that has only one.
        ///
        /// The OIIA cat ships two shapes in one mesh: sitting at weight 0 and its
        /// spin pose at 100. The importer leaves it at 0 — that is what the kart
        /// drives around as — and <c>KartGuestSpin</c> takes it to 100 while the
        /// booster is lit.
        /// </summary>
        public string SpinBlendShape;

        /// <summary>
        /// Degrees per second the model turns while boosting, or 0 for a guest
        /// that does not spin. The morph and the spin ride the same ramp, so one
        /// value being set without the other is a half-finished effect rather
        /// than a different one.
        /// </summary>
        public float SpinDegreesPerSecond;

        /// <summary>
        /// Seconds to wind the spin and the morph fully in, and the same to wind
        /// them out. Without it a boost snaps the cat into its spin pose mid-frame.
        /// </summary>
        public float SpinRampSeconds;

        /// <summary>
        /// This kart's own drift lean, or 0 to keep the standard row's.
        ///
        /// The recovered 0.07 is tuned for a 2004 kart: a wide, flat body whose
        /// roll is most of a hand's width at the wheel. The cats are half that
        /// wide and much taller, so the same torque throws the body from lock to
        /// lock through a drift and the suspension visibly hunts left and right.
        ///
        /// Written out rather than expressed as a fraction of 0.07, because it is
        /// a value someone arrived at by watching the kart rather than a
        /// relationship to the recovered one. The <c>L</c> key walks the same
        /// range at run time; this is only where each kart starts.
        ///
        /// Only the drift lean. The steer lean is an order of magnitude smaller
        /// already and does not misbehave.
        /// </summary>
        public float DriftLean;

        /// <summary>True when this guest has a second look to switch to.</summary>
        public bool Spins =>
            !string.IsNullOrEmpty(SpinBlendShape) || SpinDegreesPerSecond != 0f;

        /// <summary>Attribution the model's licence requires be carried with it.</summary>
        public string Credit;
    }

    /// <summary>
    /// The karts that are nobody's: models brought in from outside both clients.
    ///
    /// They are kept out of <see cref="KartDemoData.Karts"/> on purpose. That
    /// table is the recovered <c>KARTS[]</c> and the tests read it as such — every
    /// row has a gallery cell, a generation and an engine set that follows from
    /// its grade. A guest has none of those, and adding one there would either
    /// break those checks or, worse, quietly make a cat look like evidence.
    /// The catalog appends them after the recovered twenty-six instead, so the
    /// K menu reaches them and nothing else changes.
    /// </summary>
    public static class KartGuestData
    {
        /// <summary>Sized against the paragon line, on the 9th's engine.</summary>
        public const string Maxwell = "maxwell";

        /// <summary>The same, and it spins its second shape while boosting.</summary>
        public const string Oiia = "oiia";

        /// <summary>An STL, so it has geometry and a colour and nothing else.</summary>
        public const string Banana = "banana";

        private static readonly KartGuestSpec[] GuestTable =
        {
            new KartGuestSpec
            {
                AssetName = Maxwell,
                DisplayName = "맥스웰",
                ModelFile = "maxwell.fbx",
                BodyTexture = "maxwell_body.jpg",
                CutoutTexture = "maxwell_whiskers.png",
                FaceMeshHint = "whiskers",

                // Read off the inspector with the model turned until it faced
                // front. He arrives nose-up rather than upright, which is the one
                // thing the mesh could not have told us.
                ModelRotationDeg = new[] { -90f, 90f, 0f },
                SizeReference = "paragon",
                EnginePreset = KartEnginePreset.Jiu,

                // See DriftLean. The recovered row is 0.07.
                DriftLean = 0.03f,
                Credit =
                    "\"Maxwell the cat (Dingus)\" by bean (alwayshasbean), " +
                    "CC-BY-4.0. See maxwell_license.txt beside the model.",
            },
            new KartGuestSpec
            {
                AssetName = Oiia,
                DisplayName = "오이야 캣",
                ModelFile = "oiia.fbx",
                BodyTexture = "oiia_body.png",

                // Left to the importer, and unlike Maxwell that is an
                // evidence-backed choice rather than a hope. Maxwell's file
                // carries a -90 correction on its root node and still arrives
                // nose-up; this one carries none and its mesh already runs from
                // y = -1.1 up to 37.6, which is a model standing on y = 0. Its
                // long axis is Z, so the derived yaw is zero.
                //
                // Front-to-back is the part nothing here can settle: the mesh is
                // one piece with two shapes rather than two objects, so there is
                // no face sub-mesh to read a direction off. If he drives
                // backwards this becomes { 0, 180, 0 }.
                ModelRotationDeg = null,

                SizeReference = "paragon",
                EnginePreset = KartEnginePreset.Jiu,

                // See DriftLean. The recovered row is 0.07.
                DriftLean = 0.03f,
                BoosterSound = "oiia_booster.wav",

                // The file is 12.5 s long and its first 1.48 s are digital
                // silence — not quiet, zero — so playing it from the top would
                // spend half the boost saying nothing. Both numbers were read off
                // the samples: the fast take runs 1.48 to 3.10 and the slow one
                // starts at 4.15, with silence between.
                BoosterSoundStart = 1.48f,
                BoosterSoundSlowStart = 4.15f,
                SlowSpinScale = 0.5f,
                SpinBlendShape = "body",
                SpinDegreesPerSecond = 1440f,
                SpinRampSeconds = 0.15f,
                Credit =
                    "\"Oiiaioooooiai Cat\" by Zhuier, CC-BY-4.0. " +
                    "See oiia_license.txt beside the model.",
            },
            new KartGuestSpec
            {
                AssetName = Banana,
                DisplayName = "바나나 캣",

                // The textured re-export. It started as the STL, which carries
                // triangles and nothing else — no UVs, no materials — and was
                // converted to OBJ and drawn in one flat colour until a version
                // with an unwrap and a painted atlas turned up.
                ModelFile = "banana.fbx",
                BodyTexture = "banana_body.png",
                NormalTexture = "banana_normal.png",

                // Both read off the inspector with the model placed by hand. The
                // importer's own guess was a quarter turn out and, fitted by
                // length like the cats, nearly four metres tall.
                ModelRotationDeg = new[] { 0f, 0f, 0f },
                SizeHeightMeters = 1.95f,

                EnginePreset = KartEnginePreset.Jiu,

                // See DriftLean. The recovered row is 0.07.
                DriftLean = 0.03f,
                BoosterSound = "banana_booster.wav",
                BoosterSoundStart = 0.15f,
                ThemeMusic = "banana_theme.wav",
                Credit =
                    "\"banana cat\" by TFigure, cults3d.com/en/3d-model/art/banana-cat-tfigure, " +
                    "unwrapped and textured through Meshy. Cults personal-use licence, " +
                    "tagged No AI — check before this is distributed or used commercially.",
            },
        };

        public static IReadOnlyList<KartGuestSpec> Guests { get; } = GuestTable;

        public static KartGuestSpec Find(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return null;
            foreach (KartGuestSpec guest in GuestTable)
            {
                if (string.Equals(guest.AssetName, assetName, StringComparison.Ordinal)) return guest;
            }
            return null;
        }

        public static bool IsGuest(string assetName) => Find(assetName) != null;

        /// <summary>
        /// The mean length of a kart line, which is what a guest is scaled to.
        ///
        /// Averaged rather than taken from one kart because the line is not one
        /// size: the six paragons run from 2.20 to 2.41 long, and picking any one
        /// of them would be picking it for no reason.
        /// </summary>
        public static float ReferenceLength(string sizeReference)
        {
            if (string.IsNullOrEmpty(sizeReference)) return 0f;

            float total = 0f;
            int count = 0;
            foreach (KartSpec kart in KartDemoData.Karts)
            {
                if (!kart.AssetName.StartsWith(sizeReference, StringComparison.Ordinal)) continue;
                total += kart.Geometry.HalfLength * 2f;
                ++count;
            }
            return count == 0 ? 0f : total / count;
        }
    }
}
