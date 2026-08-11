using System;

namespace OrangeCarrrrr.Core
{
    /// <summary>
    /// The asset-to-world transform every track scene goes through, ported from
    /// <c>kart_track_scene_world_vertex</c>.
    ///
    /// The KTRK exporter keeps the original axes, whose X runs opposite to the
    /// simulator's display convention, so every scene is mirrored in X. The scene
    /// is then centred on its own AABB and dropped so the start line's plane sits
    /// at z = 0.
    ///
    /// Mirroring one axis reverses a triangle's winding, so anything that reads a
    /// face normal has to put the winding back — see
    /// <see cref="KartTrackCollision.ReadTriangle"/>.
    /// </summary>
    [Serializable]
    public struct KartTrackTransform
    {
        public float CenterX;
        public float CenterY;
        public float GroundZ;
        public bool MirrorX;

        public static KartTrackTransform FromSpec(TrackSpec track) => new KartTrackTransform
        {
            CenterX = (track.Minimum.X + track.Maximum.X) * 0.5f,
            CenterY = (track.Minimum.Y + track.Maximum.Y) * 0.5f,
            GroundZ = track.StartKind == KartTrackStartKind.None
                ? track.Minimum.Z
                : track.StartLine.Z,
            // A property of the export, not of any one track, so every scene
            // uses the same mirror.
            MirrorX = true,
        };

        public KartVec3 ToWorld(in KartVec3 assetVertex) => new KartVec3(
            MirrorX ? CenterX - assetVertex.X : assetVertex.X - CenterX,
            assetVertex.Y - CenterY,
            assetVertex.Z - GroundZ);
    }

    /// <summary>
    /// One collidable mesh: a slice of the scene's shared vertex and index
    /// arrays, plus the asset-space bounds the broadphase rejects on.
    /// </summary>
    [Serializable]
    public struct KartCollisionMesh
    {
        public int VertexStart;
        public int VertexCount;
        public int IndexStart;
        public int IndexCount;
        public KartVec3 Minimum;
        public KartVec3 Maximum;
    }

    /// <summary>
    /// The triangle set the physics collides against, in asset space.
    ///
    /// Only meshes the asset marks collidable are here. The original builds its
    /// collision grid from the subtrees carrying a <c>property/road</c> block and
    /// from nothing else (0x00432390), so everything else is scenery it drives
    /// through — on village_R01 that is 55 meshes out of 375, about 3,000
    /// triangles, which is why a per-mesh AABB reject is all the broadphase this
    /// needs.
    /// </summary>
    public sealed class KartCollisionScene
    {
        public KartVec3[] Vertices = Array.Empty<KartVec3>();
        public int[] Indices = Array.Empty<int>();
        public KartCollisionMesh[] Meshes = Array.Empty<KartCollisionMesh>();

        public int TriangleCount
        {
            get
            {
                int total = 0;
                foreach (KartCollisionMesh mesh in Meshes) total += mesh.IndexCount / 3;
                return total;
            }
        }
    }
}
