using System;
using System.IO;
using System.Text;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Reader for the KTRK v2 mesh container that
    /// <c>DeveloperTools/AssetImporters/track-mesh-exporter</c> writes, matching
    /// <c>kart_track_scene.c</c>'s loader byte for byte.
    ///
    /// <code>
    /// header:
    ///   char   magic[4] = "KTRK"
    ///   uint32 version = 2
    ///   uint32 mesh_count
    ///   uint32 total_vertex_count
    ///   uint32 total_triangle_count
    ///   float  bounds_min[3]
    ///   float  bounds_max[3]
    /// mesh[mesh_count]:
    ///   char   name_utf8[96]
    ///   char   texture_utf8[96]
    ///   uint32 flags
    ///   uint32 vertex_count
    ///   uint32 index_count
    ///   vertex[vertex_count] { float x, y, z, u, v }
    ///   uint32 index[index_count]
    /// </code>
    ///
    /// Version 1 is refused. It differs only in what a mesh's flags mean — bit 0
    /// used to be a guess from the node's name and is now the asset's own
    /// <c>property/road</c> tag — so accepting a stale export would silently
    /// report that nothing in a track is solid.
    ///
    /// Coordinates are the engine's: X and Y on the ground, Z up. Converting them
    /// is the importer's job, not this reader's.
    /// </summary>
    public static class KtrkFile
    {
        public const uint Version = 2u;
        public const int NameBytes = 96;

        /// <summary>Bit 0: this geometry is in the collision set the original builds.</summary>
        public const uint MeshCollidable = 1u;

        public struct Vertex
        {
            public float X;
            public float Y;
            public float Z;
            public float U;
            public float V;
        }

        public sealed class Mesh
        {
            public string Name;
            public string Texture;
            public uint Flags;
            public Vertex[] Vertices;
            public int[] Indices;

            public bool IsCollidable => (Flags & MeshCollidable) != 0u;
        }

        public sealed class Scene
        {
            public uint Version;
            public float[] Minimum = new float[3];
            public float[] Maximum = new float[3];
            public Mesh[] Meshes = Array.Empty<Mesh>();

            public int TotalVertexCount
            {
                get
                {
                    int total = 0;
                    foreach (Mesh mesh in Meshes) total += mesh.Vertices.Length;
                    return total;
                }
            }

            public int TotalTriangleCount
            {
                get
                {
                    int total = 0;
                    foreach (Mesh mesh in Meshes) total += mesh.Indices.Length / 3;
                    return total;
                }
            }
        }

        public static Scene Load(string path) => Read(File.ReadAllBytes(path));

        public static Scene Read(byte[] data)
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            byte[] magic = reader.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != 'K' || magic[1] != 'T' ||
                magic[2] != 'R' || magic[3] != 'K')
            {
                throw new InvalidDataException("Not a KTRK file: bad magic.");
            }

            var scene = new Scene { Version = reader.ReadUInt32() };
            if (scene.Version != Version)
            {
                throw new InvalidDataException(
                    $"KTRK version {scene.Version} is not supported; re-export at version {Version}.");
            }

            uint meshCount = reader.ReadUInt32();
            uint totalVertexCount = reader.ReadUInt32();
            uint totalTriangleCount = reader.ReadUInt32();

            // The loader's own limits, so a corrupt header cannot ask for a huge
            // allocation before anything gets to validate it.
            if (meshCount > 100000u || totalVertexCount > 10000000u || totalTriangleCount > 10000000u)
            {
                throw new InvalidDataException("KTRK header counts are out of range.");
            }

            for (int i = 0; i < 3; ++i) scene.Minimum[i] = reader.ReadSingle();
            for (int i = 0; i < 3; ++i) scene.Maximum[i] = reader.ReadSingle();

            scene.Meshes = new Mesh[meshCount];
            for (uint index = 0; index < meshCount; ++index)
            {
                var mesh = new Mesh
                {
                    Name = ReadFixedString(reader, NameBytes),
                    Texture = ReadFixedString(reader, NameBytes),
                    Flags = reader.ReadUInt32(),
                };

                uint vertexCount = reader.ReadUInt32();
                uint indexCount = reader.ReadUInt32();
                if (indexCount % 3u != 0u)
                {
                    throw new InvalidDataException(
                        $"KTRK mesh {index} has {indexCount} indices, which is not a whole number of triangles.");
                }

                mesh.Vertices = new Vertex[vertexCount];
                for (uint v = 0; v < vertexCount; ++v)
                {
                    mesh.Vertices[v] = new Vertex
                    {
                        X = reader.ReadSingle(),
                        Y = reader.ReadSingle(),
                        Z = reader.ReadSingle(),
                        U = reader.ReadSingle(),
                        V = reader.ReadSingle(),
                    };
                }

                mesh.Indices = new int[indexCount];
                for (uint i = 0; i < indexCount; ++i)
                {
                    uint value = reader.ReadUInt32();
                    if (value >= vertexCount)
                    {
                        throw new InvalidDataException(
                            $"KTRK mesh {index} index {i} is out of range.");
                    }
                    mesh.Indices[i] = (int)value;
                }

                scene.Meshes[index] = mesh;
            }

            return scene;
        }

        private static string ReadFixedString(BinaryReader reader, int byteCount)
        {
            byte[] bytes = reader.ReadBytes(byteCount);
            int length = Array.IndexOf<byte>(bytes, 0);
            if (length < 0) length = bytes.Length;
            return Encoding.UTF8.GetString(bytes, 0, length);
        }
    }
}
