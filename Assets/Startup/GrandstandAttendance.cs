using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class GrandstandAttendance : MonoBehaviour
{
    internal const int VerticesPerSpectator = 16;
    private readonly Dictionary<string, List<Seating>> sections = new();
    private readonly GameObject[] tiers = new GameObject[2];
    private Material material;
    internal int SeatCount { get; private set; }
    internal Action RegisterImportedSeats { private get; set; }

    private readonly struct Seating
    {
        internal readonly Vector3 A, B, OutwardA, OutwardB;
        internal readonly GrandstandMeshBuilder.SeatStrip Strip;
        internal readonly float Frontage;

        internal Seating(GrandstandMeshBuilder.SeatStrip strip, Vector3 a, Vector3 b,
            Vector3 outwardA, Vector3 outwardB, float frontage)
        {
            Strip = strip;
            A = a;
            B = b;
            OutwardA = outwardA;
            OutwardB = outwardB;
            Frontage = frontage;
        }
    }

    internal void Register(string section, GrandstandMeshBuilder.SeatStrip strip, Vector3 a, Vector3 b,
        Vector3 outwardA, Vector3 outwardB, float frontage)
    {
        if (!sections.TryGetValue(section, out var seats))
            sections.Add(section, seats = new List<Seating>());
        seats.Add(new Seating(strip, a, b, outwardA, outwardB, frontage));
        SeatCount += strip.Count;
    }

    internal void SetSession(RaceSessionKind session, Func<Color, Material> makeMaterial,
        ICollection<Mesh> meshes, ICollection<Texture2D> textures)
    {
        int attendance = session == RaceSessionKind.Race ? 2 : session == RaceSessionKind.Qualifying ? 1 : 0;
        if (attendance > 0 && RegisterImportedSeats != null)
        {
            RegisterImportedSeats();
            RegisterImportedSeats = null;
        }
        if (SeatCount == 0)
            return;

        for (int i = 0; i < tiers.Length; i++)
        {
            if (i < attendance && tiers[i] == null)
                tiers[i] = BuildTier(i, makeMaterial, meshes, textures);
            if (tiers[i] != null)
                tiers[i].SetActive(i < attendance);
        }
    }

    private GameObject BuildTier(int tier, Func<Color, Material> makeMaterial,
        ICollection<Mesh> meshes, ICollection<Texture2D> textures)
    {
        if (material == null)
        {
            var palette = new Texture2D(Palette.Length, 1, TextureFormat.RGBA32, false)
            {
                name = "Grandstand spectator clothing",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            palette.SetPixels32(Palette);
            palette.Apply(false, true);
            textures.Add(palette);
            material = makeMaterial(Color.white);
            material.name = "Grandstand spectators";
            material.SetTexture("_BaseMap", palette);
            material.SetFloat("_Smoothness", .06f);
        }

        var root = new GameObject("AttendanceTier" + (tier + 1)) { layer = gameObject.layer };
        root.transform.SetParent(transform, false);
        int seatIndex = 0;
        foreach (var section in sections)
        {
            var crowd = new CrowdMesh();
            foreach (Seating seating in section.Value)
            {
                for (int i = 0; i < seating.Strip.Count; i++, seatIndex++)
                {
                    // One randomly chosen seat of each pair is occupied in qualifying.
                    bool qualifying = (seatIndex | 1) < SeatCount &&
                        ((seatIndex ^ (int)Scramble((uint)(seatIndex >> 1))) & 1) == 0;
                    if ((tier == 0) != qualifying)
                        continue;

                    Vector3 seat = seating.Strip.First;
                    seat.x += i * GrandstandMeshBuilder.SeatSpacing;
                    float t = seat.x / seating.Frontage + .5f;
                    Vector3 rear = Vector3.LerpUnclamped(seating.OutwardA, seating.OutwardB, t).normalized;
                    Vector3 position = Vector3.LerpUnclamped(seating.A, seating.B, t) + rear * seat.z + Vector3.up * seat.y;
                    crowd.Add(position, rear, Scramble((uint)seatIndex + 13579u));
                }
            }

            if (crowd.Vertices.Count == 0)
                continue;
            var item = new GameObject(section.Key + " spectators") { layer = gameObject.layer };
            item.transform.SetParent(root.transform, false);
            var mesh = crowd.Build(item.name);
            meshes.Add(mesh);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var lod = item.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.004f, new Renderer[] { renderer }) });
            lod.RecalculateBounds();
        }
        return root;
    }

    private static uint Scramble(uint value)
    {
        unchecked
        {
            value = (value ^ (value >> 16)) * 0x7feb352d;
            value = (value ^ (value >> 15)) * 0x846ca68b;
            return value ^ (value >> 16);
        }
    }

    private static readonly Color32[] Palette =
    {
        new(187, 55, 50, 255), new(43, 83, 118, 255), new(228, 172, 52, 255), new(219, 212, 193, 255),
        new(70, 116, 89, 255), new(46, 42, 51, 255), new(147, 103, 158, 255), new(70, 125, 161, 255),
        new(216, 117, 60, 255), new(229, 219, 207, 255), new(152, 55, 60, 255), new(106, 123, 141, 255),
        new(119, 148, 146, 255), new(164, 164, 158, 255), new(67, 109, 150, 255), new(172, 141, 106, 255),
        new(224, 177, 145, 255), new(186, 132, 95, 255), new(139, 91, 66, 255), new(105, 66, 47, 255),
        new(52, 58, 68, 255), new(77, 93, 110, 255), new(110, 103, 87, 255), new(54, 59, 61, 255)
    };

    private sealed class CrowdMesh
    {
        private static readonly int[] BodyTriangles =
        {
            0, 3, 1, 0, 2, 3, 0, 4, 2, 0, 5, 4, 1, 4, 5, 1, 3, 4, 2, 4, 3, 0, 1, 5
        };
        private static readonly int[] HeadTriangles =
        {
            0, 4, 2, 0, 3, 4, 0, 5, 3, 0, 2, 5, 1, 2, 4, 1, 4, 3, 1, 3, 5, 1, 5, 2
        };
        internal readonly List<Vector3> Vertices = new();
        private readonly List<Vector2> colors = new();
        private readonly List<int> triangles = new();

        internal void Add(Vector3 seat, Vector3 rear, uint seed)
        {
            Vector3 right = Vector3.Cross(Vector3.up, rear).normalized;
            seat += right * ((seed % 41u) * .001f - .02f);
            float size = .89f + ((seed >> 24) & 15u) * .012f;
            float sway = ((int)((seed >> 20) & 7u) - 3) * .009f;
            int shirt = (int)((seed >> 5) & 15u);
            int head = (int)((seed >> 9) & 3u) + 16;
            int pants = (int)((seed >> 11) & 3u) + 20;

            int first = Vertices.Count;
            Point(-.14f, .04f, -.085f, shirt);
            Point(.14f, .04f, -.085f, shirt);
            Point(-.175f + sway, .41f, -.05f, shirt);
            Point(.175f + sway, .41f, -.05f, shirt);
            Point(sway, .43f, .105f, shirt);
            Point(0f, .02f, .105f, shirt);
            AddTriangles(first, BodyTriangles);

            first = Vertices.Count;
            Point(sway, .70f, .003f, head);
            Point(sway, .43f, .003f, head);
            Point(-.105f + sway, .585f, .005f, head);
            Point(.105f + sway, .585f, .005f, head);
            Point(sway, .585f, -.12f, head);
            Point(sway, .585f, .115f, head);
            AddTriangles(first, HeadTriangles);

            first = Vertices.Count;
            Point(-.13f, .065f, -.065f, pants);
            Point(.13f, .065f, -.065f, pants);
            Point(.145f, -.06f, -.26f, pants);
            Point(-.145f, -.06f, -.26f, pants);
            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 3);

            void Point(float x, float y, float z, int color)
            {
                Vertices.Add(seat + size * (right * x + Vector3.up * y + rear * z));
                colors.Add(new Vector2((color + .5f) / Palette.Length, .5f));
            }
        }

        private void AddTriangles(int first, int[] shape)
        {
            foreach (int vertex in shape)
                triangles.Add(first + vertex);
        }

        internal Mesh Build(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = Vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(Vertices);
            mesh.SetUVs(0, colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
