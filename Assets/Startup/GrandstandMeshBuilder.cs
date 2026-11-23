using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal static class GrandstandMeshBuilder
{
    internal const float SeatSpacing = .47f;

    internal readonly struct SeatStrip
    {
        internal readonly Vector3 First;
        internal readonly int Count;

        internal SeatStrip(Vector3 first, int count)
        {
            First = first;
            Count = count;
        }
    }

    internal enum Surface
    {
        Concrete, Steel, Aluminum, LowerSeats, UpperSeats, Rails, Glass, SuiteTrim, DarkInterior
    }

    internal sealed class SectionMesh
    {
        internal Mesh Mesh { get; }
        internal Surface[] Surfaces { get; }

        internal SectionMesh(Mesh mesh, Surface[] surfaces)
        {
            Mesh = mesh;
            Surfaces = surfaces;
        }
    }

    // The front faces -Z. Platforms start at Z = 0, and the suites extend behind the last row.
    internal static (SectionMesh Near, SectionMesh Far) Build(float width, float seatingDepth, int rows,
        float rowRise, float firstRowHeight, bool individualSeats, int aisles, int suiteFloors = 0,
        float suiteDepth = 0f, Action<SeatStrip> registerSeats = null)
    {
        if (width < 3f || seatingDepth <= 0f || rows < 1 || rowRise <= 0f || firstRowHeight < 0f || aisles < 0 ||
            suiteFloors < 0 || (suiteFloors > 0 && suiteDepth < 2f))
            throw new ArgumentException("Grandstand dimensions need a usable width, tread, rise, and suite depth.");

        var near = new MeshWriter();
        var far = new MeshWriter();
        float half = width * 0.5f;
        float tread = seatingDepth / rows;
        float top = firstRowHeight + (rows - 1) * rowRise;
        float margin = 0.30f;
        float bayWidth = (width - 2f * margin) / (aisles + 1);
        float aisleWidth = Mathf.Min(1.24f, bayWidth * 0.65f);

        BuildDeck(near);
        BuildDeck(far);
        BuildSeating(near, true);
        BuildSeating(far, false);
        BuildRails(near, true);
        BuildRails(far, false);
        if (suiteFloors > 0)
        {
            BuildSuites(near, true);
            BuildSuites(far, false);
        }

        return (near.Finish("Grandstand section"), far.Finish("Grandstand section, distant"));

        void BuildDeck(MeshWriter mesh)
        {
            float slab = Mathf.Min(0.20f, rowRise * 0.6f);
            for (int row = 0; row < rows; row++)
            {
                float y = firstRowHeight + row * rowRise;
                float z = row * tread;
                mesh.Box(-half, y - slab, z, half, y, z + tread, Surface.Concrete);
                if (row > 0)
                    mesh.Box(-half, y - rowRise, z, half, y - slab, z + Mathf.Min(0.12f, tread * 0.2f), Surface.Concrete);
            }

            if (firstRowHeight > slab)
                mesh.Box(-half, 0f, 0f, half, firstRowHeight - slab, Mathf.Min(0.32f, tread * 0.45f), Surface.Concrete);
            mesh.Box(-half, top - 0.44f, seatingDepth - 0.18f, half, top - slab, seatingDepth, Surface.Concrete);

            // Deep stands need columns and bracing under the deck. Keep the seating rake independent of the bank.
            if (top - slab < 0.35f)
                return;

            int supports = Mathf.Max(2, Mathf.CeilToInt(width / 4.5f) + 1);
            float lowerZ = Mathf.Max(0.22f, seatingDepth * 0.30f);
            float rearZ = seatingDepth - Mathf.Min(0.28f, tread * 0.45f);
            float lowerY = firstRowHeight + Mathf.Floor(lowerZ / tread) * rowRise - slab;
            float rearY = top - slab;
            for (int i = 0; i < supports; i++)
            {
                float x = Mathf.Lerp(-half + 0.22f, half - 0.22f, i / (float)(supports - 1));
                AddSupport(x, lowerZ, lowerY);
                AddSupport(x, rearZ, rearY);
                if (rearY > 0.77f && rearZ > lowerZ + 0.30f)
                {
                    mesh.Beam(new Vector3(x, 0.16f, lowerZ), new Vector3(x, rearY - 0.09f, rearZ),
                        0.12f, Surface.Steel);
                    if (lowerY > 1.2f)
                        mesh.Beam(new Vector3(x, lowerY - 0.15f, lowerZ), new Vector3(x, 0.16f, rearZ),
                            0.09f, Surface.Steel);
                }
                if (suiteFloors > 0)
                    AddSupport(x, seatingDepth + suiteDepth - 0.35f, top + 0.30f);
            }

            void AddSupport(float x, float z, float height)
            {
                if (height < 0.20f)
                    return;
                mesh.Box(x - 0.18f, 0f, z - 0.18f, x + 0.18f, 0.14f, z + 0.18f, Surface.Concrete);
                mesh.Box(x - 0.11f, 0.14f, z - 0.11f, x + 0.11f, height, z + 0.11f, Surface.Steel);
            }
        }

        void BuildSeating(MeshWriter mesh, bool detailed)
        {
            for (int row = 0; row < rows; row++)
            {
                float y = firstRowHeight + row * rowRise;
                float z = row * tread;
                var seat = row < (rows + 1) / 2 ? Surface.LowerSeats : Surface.UpperSeats;
                for (int bay = 0; bay <= aisles; bay++)
                {
                    float start = -half + margin + bay * bayWidth + (bay == 0 ? 0f : aisleWidth * 0.5f);
                    float end = -half + margin + (bay + 1) * bayWidth - (bay == aisles ? 0f : aisleWidth * 0.5f);
                    int count = Mathf.FloorToInt((end - start) / SeatSpacing);
                    float baseX = (start + end - (count - 1) * SeatSpacing) * .5f;
                    if (detailed && count > 0)
                        registerSeats?.Invoke(new SeatStrip(new Vector3(baseX, y + .412f, z + tread * .46f), count));

                    if (detailed && individualSeats)
                    {
                        for (int i = 0; i < count; i++)
                            AddChair(mesh, baseX + i * SeatSpacing, y, z, seat);
                    }
                    else if (end - start > 0.18f)
                    {
                        float front = z + tread * 0.24f;
                        float back = z + tread * 0.75f;
                        Surface surface = individualSeats ? seat : Surface.Aluminum;
                        mesh.Box(start, y + 0.32f, front, end, y + 0.41f, back, surface);
                        if (individualSeats || detailed)
                            mesh.Box(start, y + 0.40f, back - 0.055f, end, y + 0.75f,
                                Mathf.Min(z + tread * 0.96f, back + 0.035f), surface);
                        if (detailed)
                            for (float x = start + 0.14f; x < end - 0.05f; x += 1.8f)
                                mesh.Box(x - 0.044f, y, back - tread * 0.18f,
                                    x + 0.044f, y + 0.32f, back - tread * 0.07f, Surface.Steel);
                    }
                }

                if (row > 0)
                    for (int aisle = 1; aisle <= aisles; aisle++)
                    {
                        float x = -half + margin + aisle * bayWidth;
                        // Give the stairs a visible, continuous nosing across the break in the seats.
                        if (rowRise > 0.27f)
                            mesh.Box(x - aisleWidth * 0.47f, y - rowRise, z - tread * 0.32f,
                                x + aisleWidth * 0.47f, y - rowRise * 0.5f, z, Surface.Concrete);
                        mesh.Box(x - aisleWidth * 0.48f, y + 0.004f, z + tread * 0.025f,
                            x + aisleWidth * 0.48f, y + 0.023f, z + tread * 0.13f, Surface.Aluminum);
                    }
            }
        }

        void AddChair(MeshWriter mesh, float x, float y, float z, Surface color)
        {
            float front = z + tread * 0.20f;
            float back = z + tread * 0.70f;
            float backTop = z + tread * 0.86f;
            const float radius = 0.187f;
            mesh.Box(x - radius, y + 0.33f, front, x + radius, y + 0.412f, back, color);
            mesh.Box(x - 0.063f, y, back - tread * 0.18f, x + 0.063f, y + 0.33f, back - tread * 0.055f, Surface.Steel);
            mesh.SlopedBack(x - radius, x + radius, y + 0.395f, y + 0.79f, back - 0.05f, backTop,
                Mathf.Min(0.045f, tread * 0.1f), color);
        }

        void BuildRails(MeshWriter mesh, bool detailed)
        {
            float zFront = Mathf.Min(0.17f, tread * 0.24f);
            float zBack = seatingDepth - Mathf.Min(0.16f, tread * 0.20f);
            AddRakeRail(-half + 0.10f, detailed ? 2 : 6);
            AddRakeRail(half - 0.10f, detailed ? 2 : 6);
            for (int aisle = 1; aisle <= aisles; aisle++)
                AddRakeRail(-half + margin + aisle * bayWidth, detailed ? 3 : 8);

            if (suiteFloors == 0)
            {
                mesh.Beam(new Vector3(-half + 0.10f, top + 0.92f, zBack),
                    new Vector3(half - 0.10f, top + 0.92f, zBack), 0.049f, Surface.Rails);
                int posts = Mathf.CeilToInt(width / (detailed ? 2.4f : 4.3f));
                for (int i = 1; i < posts; i++)
                {
                    float x = Mathf.Lerp(-half + 0.10f, half - 0.10f, i / (float)posts);
                    mesh.Beam(new Vector3(x, top, zBack), new Vector3(x, top + 0.92f, zBack), 0.04f, Surface.Rails);
                }
            }

            void AddRakeRail(float x, int rowInterval)
            {
                float begin = firstRowHeight + 0.94f;
                float end = top + 0.94f;
                mesh.Beam(new Vector3(x, begin, zFront), new Vector3(x, end, zBack), 0.051f, Surface.Rails);
                int posts = Mathf.Max(1, Mathf.CeilToInt(rows / (float)rowInterval));
                for (int i = 0; i <= posts; i++)
                {
                    float t = i / (float)posts;
                    float z = Mathf.Lerp(zFront, zBack, t);
                    int row = Mathf.Min(rows - 1, Mathf.FloorToInt(z / tread));
                    float foot = firstRowHeight + row * rowRise;
                    mesh.Beam(new Vector3(x, foot, z), new Vector3(x, Mathf.Lerp(begin, end, t), z),
                        0.042f, Surface.Rails);
                }
            }
        }

        void BuildSuites(MeshWriter mesh, bool detailed)
        {
            const float floor = 3.30f;
            float penthouseFront = seatingDepth + Mathf.Min(0.38f, suiteDepth * 0.12f);
            float back = seatingDepth + suiteDepth - 0.10f;
            float bottom = top + 0.32f;
            float roof = bottom + suiteFloors * floor;
            float wall = Mathf.Min(0.25f, width * 0.04f);
            mesh.Box(-half + 0.04f, top + 0.04f, seatingDepth - 0.12f, half - 0.04f, bottom, back,
                Surface.Concrete);

            // A flat cap, restrained mullions and stacked glass bands give each floor a continuous press-box facade.
            mesh.Box(-half + 0.06f, bottom, penthouseFront, -half + wall, roof, back, Surface.Concrete);
            mesh.Box(half - wall, bottom, penthouseFront, half - 0.06f, roof, back, Surface.Concrete);
            mesh.Box(-half + wall, bottom, back - 0.16f, half - wall, roof, back, Surface.SuiteTrim);
            for (int f = 0; f < suiteFloors; f++)
            {
                float y = bottom + f * floor;
                float sill = y + 0.62f;
                float lintel = y + 2.86f;
                mesh.Box(-half + wall, y, penthouseFront - 0.03f, half - wall, sill, penthouseFront + 0.15f,
                    Surface.Concrete);
                mesh.Box(-half + wall, lintel, penthouseFront - 0.035f, half - wall, y + floor,
                    penthouseFront + 0.15f, Surface.Concrete);
                mesh.Box(-half + wall, sill, penthouseFront + 0.018f, half - wall, lintel, penthouseFront + 0.064f,
                    Surface.Glass);
                mesh.Box(-half + wall, sill, penthouseFront - 0.010f, half - wall, sill + 0.09f, penthouseFront + 0.056f,
                    Surface.DarkInterior);
                if (detailed)
                {
                    int bays = Mathf.Max(2, Mathf.RoundToInt((width - wall * 2f) / 2.60f));
                    for (int bay = 1; bay < bays; bay++)
                    {
                        float x = Mathf.Lerp(-half + wall, half - wall, bay / (float)bays);
                        mesh.Box(x - 0.042f, sill + 0.09f, penthouseFront - 0.016f, x + 0.042f, lintel,
                            penthouseFront + 0.021f, Surface.SuiteTrim);
                    }
                    mesh.Box(-half + wall, lintel - 0.36f, penthouseFront - 0.005f, half - wall, lintel - 0.324f,
                        penthouseFront + 0.020f, Surface.SuiteTrim);
                }
            }

            mesh.Box(-half, roof, penthouseFront - 0.15f, half, roof + 0.32f, back + 0.085f, Surface.Concrete);
            if (suiteFloors >= 3)
            {
                float left = -half + 0.31f;
                float right = half - 0.31f;
                float stepFront = penthouseFront + suiteDepth * 0.31f;
                float stepBack = back - 0.17f;
                float stepBottom = roof + 0.32f;
                float stepRoof = stepBottom + 2.05f;
                mesh.Box(left, stepBottom, stepFront, left + 0.15f, stepRoof, stepBack, Surface.Concrete);
                mesh.Box(right - 0.15f, stepBottom, stepFront, right, stepRoof, stepBack, Surface.Concrete);
                mesh.Box(left + 0.15f, stepBottom, stepBack - 0.13f, right - 0.15f, stepRoof, stepBack, Surface.SuiteTrim);
                mesh.Box(left + 0.15f, stepBottom, stepFront, right - 0.15f, stepBottom + 0.35f, stepFront + 0.13f,
                    Surface.Concrete);
                mesh.Box(left + 0.15f, stepBottom + 0.35f, stepFront + 0.02f,
                    right - 0.15f, stepRoof, stepFront + 0.08f, Surface.Glass);
                mesh.Box(left - 0.12f, stepRoof, stepFront - 0.10f, right + 0.12f, stepRoof + 0.25f, stepBack + 0.08f,
                    Surface.Concrete);
                if (detailed)
                {
                    int bays = Mathf.Max(2, Mathf.RoundToInt((right - left) / 2.6f));
                    for (int bay = 1; bay < bays; bay++)
                    {
                        float x = Mathf.Lerp(left + 0.15f, right - 0.15f, bay / (float)bays);
                        mesh.Box(x - 0.036f, stepBottom + 0.35f, stepFront - 0.006f, x + 0.036f, stepRoof,
                            stepFront + 0.024f, Surface.SuiteTrim);
                    }
                }
            }
        }
    }

    private sealed class MeshWriter
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int>[] triangles = new List<int>[(int)Surface.DarkInterior + 1];

        internal void Box(float x0, float y0, float z0, float x1, float y1, float z1, Surface surface)
        {
            Cuboid(new Vector3(x0, y0, z0), new Vector3(x1 - x0, 0f, 0f), new Vector3(0f, y1 - y0, 0f),
                new Vector3(0f, 0f, z1 - z0), surface);
        }

        internal void Beam(Vector3 from, Vector3 to, float width, Surface surface)
        {
            Vector3 direction = to - from;
            Vector3 unit = direction.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, unit).normalized;
            if (right == Vector3.zero)
                right = Vector3.right;
            Vector3 up = Vector3.Cross(unit, right);
            Vector3 crossA = right * width;
            Vector3 crossB = up * width;
            Cuboid(from - (crossA + crossB) * 0.5f, crossA, crossB, direction, surface);
        }

        internal void SlopedBack(float x0, float x1, float y0, float y1, float z0, float z1, float thickness,
            Surface surface)
        {
            Cuboid(new Vector3(x0, y0, z0), new Vector3(x1 - x0, 0f, 0f), new Vector3(0f, y1 - y0, z1 - z0),
                new Vector3(0f, 0f, thickness), surface);
        }

        private void Cuboid(Vector3 origin, Vector3 x, Vector3 y, Vector3 z, Surface surface)
        {
            Vector3 a = origin;
            Vector3 b = origin + x;
            Vector3 c = origin + y;
            Vector3 d = origin + z;
            Vector3 e = b + y;
            Vector3 f = b + z;
            Vector3 g = c + z;
            Vector3 h = b + y + z;
            Quad(a, c, e, b, surface);
            Quad(d, f, h, g, surface);
            Quad(a, d, g, c, surface);
            Quad(b, e, h, f, surface);
            Quad(c, g, h, e, surface);
            Quad(a, b, f, d, surface);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Surface surface)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 0.0000000001f)
                return;

            normal.Normalize();
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            for (int i = 0; i < 4; i++)
                normals.Add(normal);
            var group = triangles[(int)surface] ??= new List<int>();
            group.Add(start);
            group.Add(start + 1);
            group.Add(start + 2);
            group.Add(start);
            group.Add(start + 2);
            group.Add(start + 3);
        }

        internal SectionMesh Finish(string name)
        {
            var surfaces = new List<Surface>();
            for (int i = 0; i < triangles.Length; i++)
                if (triangles[i] != null)
                    surfaces.Add((Surface)i);

            var mesh = new Mesh
            {
                name = name,
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                subMeshCount = surfaces.Count
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            for (int i = 0; i < surfaces.Count; i++)
                mesh.SetTriangles(triangles[(int)surfaces[i]], i, false);
            mesh.RecalculateBounds();
            return new SectionMesh(mesh, surfaces.ToArray());
        }
    }
}
