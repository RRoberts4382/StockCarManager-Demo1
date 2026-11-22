using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class IowaPitWorkMesh
{
    internal enum Part
    {
        Primary, Secondary, Accent, Rubber, Metal, DarkMetal, Light,
        SkinLight, SkinMedium, SkinDark, Visor
    }

    internal const int PartCount = (int)Part.Visor + 1;

    private static readonly Vector2[] TireProfile =
    {
        new Vector2(.217f, .030f), new Vector2(.271f, .009f), new Vector2(.327f, .016f),
        new Vector2(.358f, .045f), new Vector2(.366f, .072f), new Vector2(.366f, .228f),
        new Vector2(.358f, .255f), new Vector2(.327f, .284f), new Vector2(.271f, .291f),
        new Vector2(.217f, .270f), new Vector2(.209f, .225f), new Vector2(.209f, .075f)
    };

    private static readonly Vector2[] RimProfile =
    {
        new Vector2(.181f, .127f), new Vector2(.218f, .135f), new Vector2(.226f, .243f),
        new Vector2(.221f, .265f), new Vector2(.188f, .265f), new Vector2(.182f, .241f)
    };

    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<int> triangles = new List<int>();
    private readonly List<Part> slots = new List<Part>();

    internal Mesh Build(string name, int paletteRow, int paletteHeight)
    {
        if (paletteRow < 0 || paletteRow >= paletteHeight)
            throw new ArgumentOutOfRangeException(nameof(paletteRow));

        var uv = new Vector2[vertices.Count];
        float paletteY = (paletteRow + .5f) / paletteHeight;
        for (int i = 0; i < uv.Length; i++)
            uv[i] = new Vector2(((int)slots[i] + .5f) / PartCount, paletteY);

        var mesh = new Mesh
        {
            name = name,
            indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.uv = uv;
        return mesh;
    }

    internal void Box(Vector3 center, Vector3 size, Quaternion rotation, Part part)
    {
        Vector3 x = rotation * Vector3.right * (size.x * .5f);
        Vector3 y = rotation * Vector3.up * (size.y * .5f);
        Vector3 z = rotation * Vector3.forward * (size.z * .5f);
        Vector3 a = center - x - y - z;
        Vector3 b = center + x - y - z;
        Vector3 c = center + x - y + z;
        Vector3 d = center - x - y + z;
        Vector3 e = center - x + y - z;
        Vector3 f = center + x + y - z;
        Vector3 g = center + x + y + z;
        Vector3 h = center - x + y + z;
        Face(a, b, c, d, part);
        Face(e, h, g, f, part);
        Face(a, d, h, e, part);
        Face(c, b, f, g, part);
        Face(d, c, g, h, part);
        Face(b, a, e, f, part);
    }

    internal void Cylinder(Vector3 start, Vector3 end, float startRadius, float endRadius, Part part, int sides = 8)
    {
        Vector3 delta = end - start;
        float length = delta.magnitude;
        if (length <= .00001f || Mathf.Max(startRadius, endRadius) <= 0f)
            return;

        sides = Mathf.Max(3, sides);
        Vector3 axis = delta / length;
        Vector3 right = Vector3.Cross(Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right, axis).normalized;
        Vector3 forward = Vector3.Cross(axis, right);
        float slope = (endRadius - startRadius) / length;
        int body = vertices.Count;
        for (int i = 0; i < sides; i++)
        {
            Vector3 radial = Radial(i, sides, right, forward);
            Vector3 normal = (radial - axis * slope).normalized;
            Vertex(start + radial * startRadius, normal, part);
            Vertex(end + radial * endRadius, normal, part);
        }
        for (int i = 0; i < sides; i++)
        {
            int a = body + 2 * i;
            int b = body + 2 * ((i + 1) % sides);
            if (startRadius > 0f) Triangle(a, b, b + 1);
            if (endRadius > 0f) Triangle(a, b + 1, a + 1);
        }
        Cap(start, startRadius, -axis, reverse: true);
        Cap(end, endRadius, axis, reverse: false);

        void Cap(Vector3 center, float radius, Vector3 normal, bool reverse)
        {
            if (radius <= 0f)
                return;
            int hub = Vertex(center, normal, part);
            for (int i = 0; i < sides; i++)
                Vertex(center + Radial(i, sides, right, forward) * radius, normal, part);
            for (int i = 0; i < sides; i++)
            {
                int a = hub + 1 + i;
                int b = hub + 1 + (i + 1) % sides;
                Triangle(hub, reverse ? b : a, reverse ? a : b);
            }
        }
    }

    internal void Ellipsoid(Vector3 center, Vector3 radii, Quaternion rotation, Part part, int sides = 8, int rings = 4)
    {
        sides = Mathf.Max(3, sides);
        rings = Mathf.Max(2, rings);
        int top = Vertex(center + rotation * (Vector3.up * radii.y), rotation * Vector3.up, part);
        int bottom = Vertex(center - rotation * (Vector3.up * radii.y), rotation * Vector3.down, part);
        int firstRing = vertices.Count;
        for (int ring = 1; ring < rings; ring++)
        {
            float angle = Mathf.PI * ring / rings;
            float horizontal = Mathf.Sin(angle);
            float vertical = Mathf.Cos(angle);
            for (int i = 0; i < sides; i++)
            {
                Vector3 direction = Radial(i, sides, Vector3.right, Vector3.back) * horizontal + Vector3.up * vertical;
                Vector3 normal = new Vector3(direction.x / radii.x, direction.y / radii.y, direction.z / radii.z).normalized;
                Vertex(center + rotation * Vector3.Scale(radii, direction), rotation * normal, part);
            }
        }
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            Triangle(top, firstRing + i, firstRing + next);
            int lastRing = firstRing + (rings - 2) * sides;
            Triangle(bottom, lastRing + next, lastRing + i);
            for (int ring = 0; ring < rings - 2; ring++)
            {
                int a = firstRing + ring * sides;
                Quad(a + i, a + sides + i, a + sides + next, a + next);
            }
        }
    }

    internal void TireStack(Vector3 floor, Quaternion rotation, int count = 3, bool cart = false)
    {
        var frame = new EquipmentFrame(floor, rotation);
        float deck = 0f;
        if (cart)
        {
            deck = .15f;
            Box(frame.At(0f, .12f, 0f), new Vector3(.84f, .06f, .83f), rotation, Part.Primary);
            Box(frame.At(0f, .154f, .31f), new Vector3(.69f, .012f, .045f), rotation, Part.Accent);
            Casters(frame, .31f, .31f);
            foreach (float side in new[] { -1f, 1f })
                Rod(frame, side * .315f, .13f, -.39f, side * .315f, 1.08f, -.43f, .025f, Part.Metal);
            Rod(frame, -.315f, 1.08f, -.43f, .315f, 1.08f, -.43f, .027f, Part.Rubber);
        }

        for (int tire = 0; tire < count; tire++)
        {
            Vector3 center = frame.At(0f, deck + tire * .30f, 0f);
            RadialProfile(center, rotation, TireProfile, Part.Rubber, 18);
            if (tire != count - 1)
                continue;

            RadialProfile(center, rotation, RimProfile, Part.Metal, 14);
            var rim = new EquipmentFrame(center, rotation);
            Cylinder(rim.At(0f, .135f, 0f), rim.At(0f, .150f, 0f), .181f, .181f, Part.DarkMetal, 14);
            Cylinder(rim.At(0f, .149f, 0f), rim.At(0f, .177f, 0f), .052f, .051f, Part.Metal, 8);
            for (int spoke = 0; spoke < 5; spoke++)
            {
                float a = spoke * (Mathf.PI * 2f / 5f);
                float x = Mathf.Cos(a) * .119f, z = Mathf.Sin(a) * .119f;
                Cylinder(rim.At(x, .150f, z), rim.At(x, .158f, z), .026f, .026f, Part.Rubber, 6);
            }
            for (int mark = 0; mark < 3; mark++)
            {
                Quaternion angle = rotation * Quaternion.Euler(0f, 72f + mark * 9f, 0f);
                Vector3 stripe = center + angle * new Vector3(0f, .289f, .304f);
                Box(stripe, new Vector3(.026f, .008f, .041f), angle, Part.Light);
            }
        }
    }

    internal void ToolChest(Vector3 floor, Quaternion rotation, bool tall = false)
    {
        var frame = new EquipmentFrame(floor, rotation);
        float lid = tall ? 1.12f : .75f;
        Box(frame.At(0f, (.12f + lid) * .5f, -.016f), new Vector3(1.08f, lid - .12f, .53f), rotation, Part.Primary);
        Box(frame.At(0f, lid - .024f, 0f), new Vector3(1.15f, .048f, .60f), rotation, Part.DarkMetal);
        Box(frame.At(0f, .135f, .247f), new Vector3(1.06f, .055f, .040f), rotation, Part.Accent);
        Casters(frame, .44f, .20f);
        int drawerCount = tall ? 6 : 4;
        float pitch = (lid - .21f) / drawerCount;
        for (int drawer = 0; drawer < drawerCount; drawer++)
        {
            float y = .18f + pitch * (drawer + .5f);
            Box(frame.At(0f, y, .258f), new Vector3(.93f, pitch - .014f, .034f), rotation,
                drawer == drawerCount - 1 ? Part.Accent : Part.Secondary);
            Box(frame.At(0f, y + .02f, .281f), new Vector3(.70f, .017f, .019f), rotation, Part.Metal);
        }
        Box(frame.At(.443f, lid - .083f, .280f), new Vector3(.039f, .037f, .016f), rotation, Part.Light);
    }

    internal void ServiceCart(Vector3 floor, Quaternion rotation)
    {
        var frame = new EquipmentFrame(floor, rotation);
        Casters(frame, .418f, .26f);
        foreach (float side in new[] { -1f, 1f })
        {
            Rod(frame, side * .418f, .11f, -.26f, side * .418f, .72f, -.26f, .025f, Part.Metal);
            Rod(frame, side * .418f, .11f, .26f, side * .418f, .72f, .26f, .025f, Part.Metal);
        }
        Box(frame.At(0f, .22f, 0f), new Vector3(.90f, .055f, .63f), rotation, Part.Primary);
        Box(frame.At(0f, .68f, 0f), new Vector3(.94f, .055f, .64f), rotation, Part.Primary);
        Box(frame.At(0f, .721f, -.287f), new Vector3(.92f, .059f, .046f), rotation, Part.Accent);
        Box(frame.At(0f, .721f, .287f), new Vector3(.92f, .059f, .046f), rotation, Part.Accent);
        Box(frame.At(-.228f, .757f, .028f), new Vector3(.31f, .102f, .22f), rotation, Part.Secondary);
        Box(frame.At(.188f, .734f, -.063f), new Vector3(.34f, .056f, .19f), rotation, Part.DarkMetal);
        Rod(frame, .096f, .776f, .075f, .326f, .776f, .152f, .015f, Part.Metal);
        Box(frame.At(.201f, .304f, -.041f), new Vector3(.29f, .117f, .24f), rotation, Part.DarkMetal);
    }

    internal void FloorJack(Vector3 floor, Quaternion rotation)
    {
        var frame = new EquipmentFrame(floor, rotation);
        Box(frame.At(0f, .113f, .06f), new Vector3(.34f, .11f, .91f), rotation, Part.Primary);
        Box(frame.At(0f, .163f, .10f), new Vector3(.165f, .072f, .61f), rotation, Part.Secondary);
        Rod(frame, 0f, .174f, .324f, 0f, .238f, .324f, .105f, Part.DarkMetal);
        foreach (float side in new[] { -1f, 1f })
        {
            Rod(frame, side * .145f, .068f, .373f, side * .199f, .068f, .373f, .067f, Part.Metal);
            Rod(frame, side * .145f, .044f, -.323f, side * .203f, .044f, -.323f, .043f, Part.Rubber);
        }
        Rod(frame, 0f, .16f, -.322f, 0f, .97f, -.746f, .025f, Part.Metal);
        Rod(frame, -.133f, .97f, -.746f, .133f, .97f, -.746f, .030f, Part.Rubber);
        Box(frame.At(0f, .185f, -.15f), new Vector3(.15f, .02f, .055f), rotation, Part.Accent);
    }

    internal void NitrogenBottle(Vector3 floor, Quaternion rotation)
    {
        var frame = new EquipmentFrame(floor, rotation);
        Rod(frame, 0f, .008f, 0f, 0f, .062f, 0f, .147f, Part.DarkMetal);
        Rod(frame, 0f, .061f, 0f, 0f, .809f, 0f, .126f, Part.Secondary);
        Rod(frame, 0f, .14f, 0f, 0f, .195f, 0f, .128f, Part.Primary);
        Ellipsoid(frame.At(0f, .804f, 0f), new Vector3(.126f, .133f, .126f), rotation, Part.Primary, 10);
        Rod(frame, 0f, .892f, 0f, 0f, .972f, 0f, .039f, Part.Metal);
        Rod(frame, -.073f, .966f, 0f, .073f, .966f, 0f, .027f, Part.Accent);
        Rod(frame, 0f, .922f, .027f, 0f, .922f, .092f, .038f, Part.DarkMetal);
        Rod(frame, 0f, .922f, .092f, 0f, .922f, .097f, .031f, Part.Light);
    }

    internal void FuelCan(Vector3 floor, Quaternion rotation)
    {
        var frame = new EquipmentFrame(floor, rotation);
        Box(frame.At(0f, .165f, 0f), new Vector3(.28f, .32f, .29f), rotation, Part.Primary);
        Box(frame.At(0f, .075f, 0f), new Vector3(.30f, .054f, .31f), rotation, Part.DarkMetal);
        Box(frame.At(0f, .218f, .149f), new Vector3(.22f, .053f, .011f), rotation, Part.Accent);
        Box(frame.At(-.080f, .35f, -.05f), new Vector3(.042f, .072f, .053f), rotation, Part.Secondary);
        Box(frame.At(.076f, .35f, -.05f), new Vector3(.042f, .072f, .053f), rotation, Part.Secondary);
        Rod(frame, -.087f, .384f, -.05f, .083f, .384f, -.05f, .022f, Part.Rubber);
        Rod(frame, .077f, .321f, .069f, .139f, .439f, .089f, .031f, Part.Metal);
        Rod(frame, .139f, .439f, .089f, .151f, .461f, .093f, .035f, Part.DarkMetal);
    }

    internal void ImpactWrench(Vector3 grip, Quaternion rotation)
    {
        var frame = new EquipmentFrame(grip, rotation);
        Rod(frame, 0f, -.074f, -.022f, 0f, .053f, -.009f, .033f, Part.Rubber);
        Rod(frame, 0f, .067f, -.079f, 0f, .067f, .084f, .045f, Part.Primary);
        Rod(frame, 0f, .067f, .081f, 0f, .067f, .143f, .037f, Part.Metal);
        Rod(frame, 0f, .067f, .141f, 0f, .067f, .226f, .021f, Part.DarkMetal);
        Box(frame.At(0f, .032f, .021f), new Vector3(.026f, .034f, .017f), rotation, Part.Accent);
    }

    private void Casters(EquipmentFrame frame, float halfX, float halfZ)
    {
        for (int side = -1; side <= 1; side += 2)
        for (int end = -1; end <= 1; end += 2)
        {
            float x = halfX * side, z = halfZ * end;
            Box(frame.At(x, .084f, z), new Vector3(.056f, .073f, .061f), frame.Rotation, Part.DarkMetal);
            Rod(frame, x - .030f, .039f, z, x + .030f, .039f, z, .038f, Part.Rubber);
        }
    }

    private void Rod(EquipmentFrame frame, float x0, float y0, float z0,
        float x1, float y1, float z1, float radius, Part part) =>
        Cylinder(frame.At(x0, y0, z0), frame.At(x1, y1, z1), radius, radius, part);

    private void RadialProfile(Vector3 origin, Quaternion rotation, Vector2[] profile, Part part, int sides)
    {
        int start = vertices.Count;
        Vector3 axis = rotation * Vector3.up;
        Vector3 right = rotation * Vector3.forward;
        Vector3 forward = rotation * Vector3.right;
        for (int ring = 0; ring < profile.Length; ring++)
        {
            Vector2 sample = profile[ring];
            Vector2 tangent = profile[(ring + 1) % profile.Length] - profile[(ring + profile.Length - 1) % profile.Length];
            for (int i = 0; i < sides; i++)
            {
                Vector3 radial = Radial(i, sides, right, forward);
                Vector3 normal = (radial * tangent.y - axis * tangent.x).normalized;
                Vertex(origin + radial * sample.x + axis * sample.y, normal, part);
            }
        }
        for (int ring = 0; ring < profile.Length; ring++)
        {
            int a = start + ring * sides;
            int b = start + ((ring + 1) % profile.Length) * sides;
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                Quad(a + i, a + next, b + next, b + i);
            }
        }
    }

    private static Vector3 Radial(int index, int sides, Vector3 right, Vector3 forward)
    {
        float angle = index * (2f * Mathf.PI / sides);
        return right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
    }

    private int Vertex(Vector3 position, Vector3 normal, Part part)
    {
        int index = vertices.Count;
        vertices.Add(position);
        normals.Add(normal);
        slots.Add(part);
        return index;
    }

    private void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Part part)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        Quad(Vertex(a, normal, part), Vertex(b, normal, part),
            Vertex(c, normal, part), Vertex(d, normal, part));
    }

    private void Quad(int a, int b, int c, int d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    private void Triangle(int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    private readonly struct EquipmentFrame
    {
        private readonly Vector3 origin;
        internal Quaternion Rotation { get; }

        internal EquipmentFrame(Vector3 origin, Quaternion rotation)
        {
            this.origin = origin;
            Rotation = rotation;
        }

        internal Vector3 At(float x, float y, float z) => origin + Rotation * new Vector3(x, y, z);
    }
}
