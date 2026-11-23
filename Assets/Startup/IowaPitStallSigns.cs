using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Part = IowaPitWorkMesh.Part;

public sealed partial class TrackPreview
{
    private const float IowaPitMarkerBottom = 3.00f;
    private const float IowaPitMarkerHeight = .84f;
    private const float IowaPitMarkerSpacing = .035f;

    private void AddIowaPitStallSign(Transform parent, int number, Vector3 stall, Vector3 center,
        Vector3 travel, Vector3 infield, Color primary, Color accent)
    {
        Vector3 forward = Vector3.ProjectOnPlane(travel, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float wallSide = Mathf.Sign(Vector3.Dot(right, infield));
        const float footingMargin = .17f;
        float wallBack = definition.pitRoad.widthMeters * .5f + PitWallThickness;
        float poleX = Vector3.Dot(center - stall, right) + (wallBack + footingMargin) * wallSide;

        var sign = CreateObject($"Iowa pit stall sign No. {number}", parent);
        // The native pit path adds 2 cm of driving clearance over the pavement.
        Vector3 floor = new Vector3(stall.x, center.y - .02f, stall.z);
        sign.transform.SetLocalPositionAndRotation(floor, Quaternion.LookRotation(forward, Vector3.up));

        string digits = Math.Abs((long)number).ToString(CultureInfo.InvariantCulture);
        float widthUnits = IowaPitMarkerSpacing * (digits.Length - 1);
        foreach (char digit in digits)
            widthUnits += IowaPitMarkerGlyphs[digit - '0'].Width;
        float widthScale = Mathf.Min(IowaPitMarkerHeight, 1.55f / widthUnits);
        float span = widthUnits * widthScale;

        const float attachment = IowaPitMarkerBottom + IowaPitMarkerHeight - .075f;
        var support = new IowaPitWorkMesh();
        support.Box(new Vector3(poleX, .016f, 0f), new Vector3(.162f, .032f, .18f),
            Quaternion.identity, Part.DarkMetal);
        support.Box(new Vector3(0f, attachment, 0f), new Vector3(span - .11f, .035f, .024f),
            Quaternion.identity, Part.DarkMetal);
        Mesh combined = support.Build(sign.name, 0, 1);

        var art = new IowaPitMarkerMesh(combined);
        art.AddPole(poleX, wallSide, attachment);
        art.AddDigits(digits, widthUnits, widthScale, 1f);
        art.AddDigits(digits, widthUnits, widthScale, -1f);
        art.Apply(combined);

        roadMeshes.Add(combined);
        sign.AddComponent<MeshFilter>().sharedMesh = combined;
        var renderer = sign.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = IowaPitStallSignMaterial(primary, accent);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private sealed class IowaPitMarkerMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<int> triangles;

        internal IowaPitMarkerMesh(Mesh support)
        {
            support.GetVertices(vertices);
            support.GetNormals(normals);
            support.GetUVs(0, uv);
            triangles = new List<int>(support.GetTriangles(0));
        }

        internal void AddPole(float pole, float wallSide, float height)
        {
            const int slices = 21;
            const int sides = 8;
            Vector3 a = new Vector3(pole, .022f, 0f);
            // Rise behind the wall before flexing toward the sign above the stall.
            Vector3 b = new Vector3(pole - .002f * wallSide, 2.99f, 0f);
            Vector3 c = new Vector3(pole - .038f * wallSide, height + .13f, 0f);
            Vector3 d = new Vector3(0f, height, 0f);
            int start = vertices.Count;
            for (int slice = 0; slice <= slices; slice++)
            {
                float t = (float)slice / slices;
                float m = 1f - t;
                Vector3 point = a * (m * m * m) + b * (3f * m * m * t)
                    + c * (3f * m * t * t) + d * (t * t * t);
                Vector3 axis = ((b - a) * (m * m) + (c - b) * (2f * m * t)
                    + (d - c) * (t * t)).normalized;
                Vector3 radial = Vector3.Cross(axis, Vector3.forward);
                float radius = Mathf.Lerp(.035f, .018f, t);
                for (int side = 0; side < sides; side++)
                {
                    float angle = Mathf.PI * 2f * side / sides;
                    Vector3 normal = Vector3.forward * Mathf.Cos(angle) + radial * Mathf.Sin(angle);
                    Vertex(point + normal * radius, normal, Part.Metal);
                }
            }

            for (int slice = 0; slice < slices; slice++)
            for (int side = 0; side < sides; side++)
            {
                int current = start + slice * sides + side;
                int next = start + slice * sides + (side + 1) % sides;
                Triangle(current, next, next + sides);
                Triangle(current, next + sides, current + sides);
            }

            EndCap(start, a, (a - b).normalized, false);
            EndCap(start + slices * sides, d, (d - c).normalized, true);

            void EndCap(int ring, Vector3 point, Vector3 normal, bool terminal)
            {
                int hub = Vertex(point, normal, Part.Metal);
                for (int side = 0; side < sides; side++)
                    Vertex(vertices[ring + side], normal, Part.Metal);
                for (int side = 0; side < sides; side++)
                {
                    int v = hub + 1 + side;
                    int next = hub + 1 + (side + 1) % sides;
                    Triangle(hub, terminal ? v : next, terminal ? next : v);
                }
            }
        }

        internal void AddDigits(string digits, float width, float horizontalScale, float face)
        {
            // Each face is culled from behind, so the reversed cutouts never
            // protrude past the legible silhouette when viewed from either end.
            float next = -width * .5f;
            Vector3 normal = Vector3.forward * face;
            foreach (char digit in digits)
            {
                IowaPitMarkerGlyph glyph = IowaPitMarkerGlyphs[digit - '0'];
                AddLayer(Part.DarkMetal, glyph.Outline);
                AddLayer(Part.Accent, glyph.Rim);
                AddLayer(Part.Primary, glyph.Fill);
                next += glyph.Width + IowaPitMarkerSpacing;

                void AddLayer(Part paint, byte[] faces)
                {
                    var used = new Dictionary<int, int>();
                    for (int i = 0; i < faces.Length; i += 3)
                        Triangle(Point(faces[i]), Point(faces[i + 2]), Point(faces[i + 1]));

                    int Point(int index)
                    {
                        if (used.TryGetValue(index, out int vertex))
                            return vertex;
                        // Viewed from local +Z, right on the sign is local -X.
                        float x = -face * (next + glyph.Points[2 * index] * .001f) * horizontalScale;
                        float y = IowaPitMarkerBottom + glyph.Points[2 * index + 1] * .001f * IowaPitMarkerHeight;
                        vertex = Vertex(new Vector3(x, y, face * .014f), normal, paint);
                        used.Add(index, vertex);
                        return vertex;
                    }
                }
            }
        }

        private int Vertex(Vector3 position, Vector3 normal, Part paint)
        {
            int index = vertices.Count;
            vertices.Add(position);
            normals.Add(normal);
            uv.Add(new Vector2(((int)paint + .5f) / IowaPitWorkMesh.PartCount, .5f));
            return index;
        }

        private void Triangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        internal void Apply(Mesh mesh)
        {
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
        }
    }

    private Material IowaPitStallSignMaterial(Color primary, Color accent)
    {
        var palette = new Texture2D(IowaPitWorkMesh.PartCount, 1, TextureFormat.RGBA32, false)
        {
            name = "Iowa pit stall sign colors",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        var colors = new Color32[IowaPitWorkMesh.PartCount];
        Color contrast = Mathf.Abs(primary.grayscale - accent.grayscale) < .21f
            ? (primary.grayscale < .52f ? new Color32(247, 240, 212, 255) : new Color32(28, 34, 38, 255))
            : accent;
        colors[(int)Part.Primary] = primary;
        colors[(int)Part.Accent] = contrast;
        colors[(int)Part.Metal] = new Color32(225, 180, 43, 255);
        colors[(int)Part.DarkMetal] = new Color32(27, 33, 39, 255);
        palette.SetPixels32(colors);
        palette.Apply(false, true);
        carPaintTextures.Add(palette);

        Material material = MakeMaterial(Color.white);
        material.name = "Iowa pit stall sign paint";
        material.SetTexture("_BaseMap", palette);
        material.SetFloat("_Smoothness", .17f);
        return material;
    }

    private readonly struct IowaPitMarkerGlyph
    {
        internal readonly float Width;
        internal readonly short[] Points;
        internal readonly byte[] Outline, Rim, Fill;

        internal IowaPitMarkerGlyph(float width, short[] points, byte[] outline, byte[] rim, byte[] fill)
        {
            Width = width;
            Points = points;
            Outline = outline;
            Rim = rim;
            Fill = fill;
        }
    }

    // Authored stencil digits with pretriangulated keylines. Positions are in
    // thousandths of the numeral height, and indices are counterclockwise.
    private static readonly IowaPitMarkerGlyph[] IowaPitMarkerGlyphs =
    {
        // 0
        new IowaPitMarkerGlyph(0.746f,
            new short[]
            {
                97, 0, 538, 0, 669, 128, 746, 868, 639, 1000, 208, 1000, 78, 872, 0, 133, 261, 297,
                303, 698, 324, 728, 473, 728, 486, 703, 443, 302, 422, 272, 273, 272, 103, 13, 533, 13,
                656, 134, 733, 864, 633, 987, 214, 987, 90, 866, 13, 137, 248, 294, 290, 703, 317, 741,
                481, 741, 499, 706, 456, 297, 429, 259, 265, 259, 111, 27, 527, 27, 643, 140, 718, 860,
                626, 973, 219, 973, 103, 860, 28, 140, 233, 292, 277, 708, 310, 755, 490, 755, 513, 708,
                470, 292, 436, 245, 256, 245,
            },
            new byte[]
            {
                22, 6, 23, 7, 23, 6, 16, 0, 1, 7, 0, 16, 23, 7, 16, 6, 22, 21,
                21, 5, 6, 31, 15, 24, 15, 8, 24, 25, 9, 10, 10, 26, 25, 9, 25, 8,
                14, 30, 29, 29, 13, 14, 11, 12, 28, 27, 11, 28, 13, 29, 12, 3, 19, 2,
                17, 1, 2, 2, 18, 17, 19, 3, 20, 4, 20, 3, 18, 2, 19, 15, 31, 30,
                30, 14, 15, 26, 10, 11, 11, 27, 26, 5, 21, 20, 20, 4, 5, 1, 17, 16,
                28, 12, 29, 24, 8, 25,
            },
            new byte[]
            {
                39, 23, 16, 16, 32, 39, 38, 37, 22, 37, 21, 22, 40, 47, 31, 24, 40, 31,
                41, 25, 26, 26, 42, 41, 40, 24, 25, 25, 41, 40, 22, 23, 38, 30, 46, 45,
                45, 29, 30, 28, 44, 27, 44, 43, 27, 44, 28, 45, 33, 17, 18, 18, 34, 33,
                35, 19, 36, 20, 36, 19, 19, 35, 18, 17, 33, 32, 31, 47, 46, 46, 30, 31,
                42, 26, 27, 27, 43, 42, 21, 37, 36, 36, 20, 21, 32, 16, 17, 29, 45, 28,
                34, 18, 35, 23, 39, 38,
            },
            new byte[]
            {
                39, 32, 47, 47, 40, 39, 40, 41, 38, 41, 42, 38, 37, 38, 42, 33, 34, 46,
                45, 46, 34, 44, 45, 34, 43, 44, 35, 35, 36, 43, 47, 32, 33, 33, 46, 47,
                37, 42, 43, 43, 36, 37, 44, 34, 35, 39, 40, 38,
            }),
        // 1
        new IowaPitMarkerGlyph(0.533f,
            new short[]
            {
                0, 0, 508, 0, 533, 234, 410, 234, 490, 1000, 282, 1000, 30, 827, 2, 562, 204, 685,
                157, 234, 25, 234, 14, 13, 497, 13, 519, 221, 396, 221, 476, 987, 286, 987, 42, 820,
                18, 587, 220, 709, 168, 221, 36, 221, 30, 27, 484, 27, 503, 207, 380, 207, 460, 973,
                290, 973, 56, 812, 35, 614, 237, 737, 181, 207, 49, 207,
            },
            new byte[]
            {
                0, 11, 10, 7, 18, 6, 20, 9, 21, 10, 11, 21, 21, 9, 10, 18, 8, 19,
                6, 18, 17, 13, 1, 2, 1, 13, 12, 3, 14, 13, 13, 2, 3, 5, 16, 15,
                15, 4, 5, 11, 0, 1, 1, 12, 11, 14, 3, 15, 16, 5, 17, 6, 17, 5,
                4, 15, 3, 18, 7, 8, 9, 20, 8, 19, 8, 20,
            },
            new byte[]
            {
                31, 20, 32, 32, 21, 22, 11, 22, 21, 28, 17, 29, 24, 12, 13, 12, 24, 23,
                14, 25, 24, 16, 27, 26, 12, 23, 22, 27, 16, 28, 22, 11, 12, 24, 13, 14,
                15, 26, 14, 26, 15, 16, 17, 28, 16, 18, 29, 17, 19, 30, 29, 29, 18, 19,
                25, 14, 26, 21, 32, 20, 20, 31, 19, 30, 19, 31,
            },
            new byte[]
            {
                31, 32, 22, 23, 24, 25, 27, 30, 26, 31, 22, 23, 23, 25, 31, 30, 27, 28,
                26, 30, 25, 28, 29, 30, 31, 25, 30,
            }),
        // 2
        new IowaPitMarkerGlyph(0.760f,
            new short[]
            {
                0, 0, 669, 0, 695, 243, 307, 243, 311, 286, 662, 528, 737, 642, 760, 867, 663, 1000,
                213, 1000, 103, 891, 81, 681, 315, 681, 324, 771, 340, 785, 507, 785, 517, 775, 511, 720,
                476, 681, 129, 454, 34, 321, 14, 13, 658, 13, 681, 230, 293, 230, 299, 293, 653, 537,
                724, 646, 747, 863, 657, 987, 218, 987, 115, 885, 95, 694, 303, 694, 312, 777, 335, 798,
                513, 798, 530, 779, 523, 714, 485, 672, 138, 444, 46, 316, 30, 27, 645, 27, 665, 216,
                277, 216, 286, 301, 643, 547, 710, 651, 732, 860, 650, 973, 224, 973, 129, 878, 111, 708,
                291, 708, 299, 784, 330, 812, 519, 812, 545, 784, 537, 708, 494, 661, 148, 434, 60, 311,
            },
            new byte[]
            {
                41, 20, 21, 0, 21, 20, 10, 32, 31, 40, 19, 41, 20, 41, 19, 32, 10, 11,
                12, 13, 33, 25, 24, 3, 3, 4, 25, 13, 34, 33, 10, 31, 30, 30, 9, 10,
                18, 39, 17, 35, 14, 15, 15, 36, 35, 38, 37, 16, 17, 39, 38, 38, 16, 17,
                36, 15, 16, 16, 37, 36, 7, 28, 6, 6, 28, 27, 2, 23, 1, 22, 1, 23,
                6, 27, 5, 26, 5, 27, 28, 7, 29, 8, 29, 7, 21, 0, 1, 1, 22, 21,
                5, 26, 4, 25, 4, 26, 14, 35, 13, 34, 13, 35, 9, 30, 29, 29, 8, 9,
                3, 24, 23, 23, 2, 3, 12, 33, 32, 32, 11, 12, 19, 40, 18, 39, 18, 40,
            },
            new byte[]
            {
                62, 41, 42, 31, 53, 52, 61, 40, 62, 41, 62, 40, 24, 25, 46, 24, 46, 45,
                34, 55, 54, 53, 32, 33, 33, 54, 53, 31, 52, 51, 51, 30, 31, 37, 38, 59,
                59, 58, 37, 59, 38, 60, 39, 60, 38, 37, 58, 36, 58, 57, 36, 28, 49, 27,
                48, 27, 49, 44, 22, 23, 27, 48, 26, 49, 28, 50, 29, 50, 28, 22, 44, 43,
                26, 48, 47, 46, 25, 47, 55, 34, 35, 56, 55, 35, 35, 36, 56, 36, 57, 56,
                30, 51, 50, 22, 43, 42, 42, 21, 22, 24, 45, 44, 44, 23, 24, 26, 47, 25,
                50, 29, 30, 53, 31, 32, 54, 33, 34, 60, 39, 61, 40, 61, 39, 21, 42, 41,
            },
            new byte[]
            {
                55, 56, 51, 62, 42, 45, 45, 46, 62, 61, 62, 46, 54, 55, 53, 52, 53, 55,
                55, 51, 52, 47, 59, 60, 59, 47, 48, 58, 59, 48, 48, 49, 58, 49, 50, 58,
                57, 58, 50, 45, 42, 43, 51, 56, 57, 57, 50, 51, 61, 46, 60, 43, 44, 45,
                47, 60, 46,
            }),
        // 3
        new IowaPitMarkerGlyph(0.741f,
            new short[]
            {
                0, 0, 532, 0, 663, 118, 696, 436, 617, 498, 709, 560, 741, 868, 634, 1000, 105, 1000,
                80, 766, 483, 766, 487, 759, 474, 638, 439, 612, 206, 612, 184, 397, 421, 397, 446, 375,
                433, 248, 417, 234, 25, 234, 14, 13, 527, 13, 650, 124, 683, 431, 596, 499, 697, 568,
                728, 864, 628, 987, 117, 987, 95, 779, 490, 779, 500, 762, 486, 631, 443, 599, 218, 599,
                198, 410, 426, 410, 460, 381, 445, 242, 422, 221, 36, 221, 30, 27, 522, 27, 637, 131,
                668, 424, 572, 500, 684, 576, 713, 860, 621, 973, 129, 973, 110, 793, 498, 793, 514, 765,
                499, 623, 448, 585, 230, 585, 214, 424, 431, 424, 475, 386, 459, 235, 427, 207, 49, 207,
            },
            new byte[]
            {
                0, 21, 20, 20, 21, 41, 8, 30, 29, 36, 14, 15, 30, 8, 9, 39, 17, 18,
                39, 18, 40, 19, 40, 18, 37, 16, 17, 34, 13, 35, 40, 19, 41, 14, 36, 35,
                35, 13, 14, 12, 33, 32, 11, 12, 32, 10, 11, 32, 31, 10, 32, 23, 2, 24,
                5, 6, 26, 6, 27, 26, 3, 24, 2, 24, 4, 25, 24, 3, 4, 5, 26, 4,
                27, 6, 28, 7, 28, 6, 2, 23, 1, 22, 1, 23, 4, 26, 25, 21, 0, 1,
                1, 22, 21, 17, 39, 38, 37, 17, 38, 33, 12, 34, 13, 34, 12, 28, 7, 29,
                8, 29, 7, 10, 31, 30, 30, 9, 10, 16, 37, 36, 36, 15, 16, 20, 41, 19,
            },
            new byte[]
            {
                41, 42, 62, 42, 41, 21, 29, 51, 50, 35, 57, 56, 40, 61, 60, 60, 39, 40,
                57, 37, 58, 55, 34, 56, 61, 40, 62, 57, 35, 36, 46, 25, 47, 54, 53, 33,
                53, 32, 33, 32, 53, 31, 52, 31, 53, 44, 23, 45, 47, 26, 27, 27, 48, 47,
                48, 27, 49, 28, 49, 27, 43, 22, 23, 23, 44, 43, 25, 46, 45, 22, 43, 42,
                59, 38, 60, 39, 60, 38, 58, 37, 38, 38, 59, 58, 54, 33, 55, 34, 55, 33,
                49, 28, 50, 42, 21, 22, 45, 23, 24, 45, 24, 25, 26, 47, 25, 29, 50, 28,
                51, 29, 30, 31, 52, 51, 51, 30, 31, 35, 56, 34, 37, 57, 36, 41, 62, 40,
            },
            new byte[]
            {
                56, 57, 58, 58, 55, 56, 62, 42, 61, 46, 59, 45, 53, 54, 47, 47, 48, 53,
                52, 53, 48, 48, 49, 52, 61, 42, 43, 60, 61, 43, 43, 44, 60, 44, 45, 60,
                45, 59, 60, 59, 46, 58, 55, 58, 46, 46, 54, 55, 52, 49, 50, 54, 46, 47,
                50, 51, 52,
            }),
        // 4
        new IowaPitMarkerGlyph(0.722f,
            new short[]
            {
                329, 0, 582, 0, 603, 199, 698, 199, 722, 432, 627, 432, 687, 1000, 426, 1000, 30, 482,
                0, 199, 350, 199, 268, 451, 393, 609, 377, 451, 343, 13, 571, 13, 591, 211, 686, 211,
                708, 420, 613, 420, 673, 987, 433, 987, 42, 477, 14, 211, 364, 211, 241, 439, 410, 652,
                388, 439, 359, 27, 558, 27, 579, 226, 673, 226, 692, 405, 598, 405, 657, 973, 440, 973,
                56, 472, 30, 226, 380, 226, 212, 424, 430, 699, 401, 424,
            },
            new byte[]
            {
                22, 8, 23, 9, 23, 8, 10, 14, 24, 27, 12, 13, 14, 10, 0, 27, 13, 11,
                2, 16, 15, 18, 3, 4, 3, 18, 17, 5, 19, 18, 18, 4, 5, 3, 17, 2,
                16, 2, 17, 14, 0, 1, 1, 15, 14, 19, 5, 20, 7, 8, 21, 7, 21, 20,
                20, 6, 7, 15, 1, 2, 6, 20, 5, 10, 24, 23, 23, 9, 10, 8, 22, 21,
                12, 27, 26, 25, 11, 12, 12, 26, 25, 11, 25, 27,
            },
            new byte[]
            {
                22, 37, 36, 35, 21, 22, 16, 30, 29, 17, 32, 31, 30, 16, 31, 32, 19, 33,
                21, 35, 34, 15, 29, 28, 24, 28, 38, 33, 19, 34, 28, 14, 15, 29, 15, 16,
                17, 31, 16, 32, 17, 18, 32, 18, 19, 20, 34, 19, 34, 20, 21, 37, 22, 23,
                24, 38, 37, 37, 23, 24, 28, 24, 14, 41, 27, 25, 22, 36, 35, 27, 41, 26,
                40, 26, 41, 39, 25, 26, 26, 40, 39, 25, 39, 41,
            },
            new byte[]
            {
                39, 36, 37, 38, 41, 39, 33, 30, 31, 31, 32, 33, 35, 40, 34, 29, 30, 38,
                41, 38, 30, 30, 33, 41, 41, 33, 40, 34, 40, 33, 36, 39, 40, 40, 35, 36,
                39, 37, 38, 28, 29, 38,
            }),
        // 5
        new IowaPitMarkerGlyph(0.755f,
            new short[]
            {
                0, 0, 551, 0, 682, 118, 722, 500, 612, 622, 309, 622, 323, 757, 730, 757, 755, 1000,
                105, 1000, 42, 397, 448, 397, 474, 368, 463, 269, 429, 243, 26, 243, 14, 13, 546, 13,
                669, 124, 708, 495, 607, 609, 295, 609, 311, 770, 718, 770, 741, 987, 117, 987, 56, 410,
                453, 410, 487, 372, 476, 262, 433, 230, 37, 230, 30, 27, 541, 27, 656, 131, 694, 491,
                600, 595, 279, 595, 299, 784, 705, 784, 725, 973, 129, 973, 72, 424, 460, 424, 502, 377,
                489, 254, 438, 216, 50, 216,
            },
            new byte[]
            {
                0, 16, 15, 15, 16, 31, 25, 9, 26, 26, 9, 10, 22, 5, 6, 5, 22, 21,
                30, 14, 31, 12, 13, 29, 12, 29, 28, 20, 4, 5, 18, 2, 19, 24, 7, 8,
                7, 24, 23, 19, 2, 3, 2, 18, 1, 17, 1, 18, 19, 3, 20, 4, 20, 3,
                16, 0, 1, 1, 17, 16, 29, 13, 30, 14, 30, 13, 12, 28, 11, 27, 11, 28,
                22, 6, 23, 9, 25, 24, 24, 8, 9, 7, 23, 6, 11, 27, 26, 26, 10, 11,
                15, 31, 14, 5, 21, 20,
            },
            new byte[]
            {
                32, 31, 16, 41, 25, 42, 31, 32, 47, 37, 21, 38, 38, 21, 22, 46, 30, 47,
                45, 44, 29, 44, 28, 29, 34, 18, 19, 19, 35, 34, 40, 23, 24, 23, 40, 39,
                33, 17, 18, 18, 34, 33, 20, 36, 35, 35, 19, 20, 17, 33, 32, 45, 29, 46,
                30, 46, 29, 43, 27, 28, 28, 44, 43, 38, 22, 39, 25, 41, 40, 32, 16, 17,
                21, 37, 36, 36, 20, 21, 23, 39, 22, 40, 24, 25, 26, 42, 25, 27, 43, 42,
                42, 26, 27, 31, 47, 30,
            },
            new byte[]
            {
                47, 32, 46, 41, 42, 37, 37, 38, 41, 45, 33, 34, 44, 45, 34, 34, 35, 44,
                36, 44, 35, 46, 32, 33, 33, 45, 46, 44, 36, 43, 41, 38, 40, 37, 43, 36,
                39, 40, 38, 43, 37, 42,
            }),
        // 6
        new IowaPitMarkerGlyph(0.709f,
            new short[]
            {
                109, 0, 549, 0, 669, 119, 709, 500, 600, 622, 286, 622, 299, 739, 331, 757, 679, 757,
                705, 1000, 210, 1000, 80, 882, 0, 122, 248, 254, 259, 364, 286, 388, 443, 388, 461, 368,
                449, 258, 423, 234, 266, 234, 115, 13, 543, 13, 657, 124, 696, 495, 594, 609, 272, 609,
                287, 747, 327, 770, 668, 770, 690, 987, 215, 987, 92, 876, 13, 126, 235, 249, 247, 370,
                281, 401, 449, 401, 474, 372, 462, 251, 428, 221, 260, 221, 121, 27, 537, 27, 643, 131,
                681, 491, 588, 595, 256, 595, 273, 755, 324, 784, 655, 784, 675, 973, 221, 973, 106, 869,
                28, 131, 220, 245, 234, 377, 275, 415, 455, 415, 489, 377, 475, 245, 433, 207, 254, 207,
            },
            new byte[]
            {
                33, 12, 0, 33, 0, 21, 31, 10, 32, 11, 32, 10, 35, 13, 14, 27, 5, 6,
                15, 36, 14, 35, 14, 36, 5, 27, 26, 41, 20, 34, 20, 13, 34, 13, 35, 34,
                12, 33, 11, 39, 38, 17, 19, 40, 39, 18, 19, 39, 39, 17, 18, 37, 16, 17,
                17, 38, 37, 3, 24, 2, 9, 30, 8, 23, 1, 2, 2, 24, 23, 24, 3, 25,
                4, 25, 3, 8, 30, 29, 1, 23, 22, 21, 0, 1, 1, 22, 21, 20, 41, 40,
                40, 19, 20, 36, 15, 16, 16, 37, 36, 7, 28, 6, 27, 6, 28, 28, 7, 29,
                10, 31, 30, 30, 9, 10, 5, 26, 25, 25, 4, 5, 8, 29, 7, 32, 11, 33,
            },
            new byte[]
            {
                21, 42, 33, 54, 33, 42, 52, 31, 53, 32, 53, 31, 27, 49, 48, 47, 26, 48,
                34, 55, 62, 62, 41, 34, 55, 34, 35, 56, 55, 35, 35, 36, 56, 36, 57, 56,
                33, 54, 32, 38, 39, 60, 60, 59, 38, 60, 39, 61, 40, 61, 39, 38, 59, 37,
                59, 58, 37, 44, 23, 45, 25, 46, 45, 45, 24, 25, 30, 51, 29, 50, 29, 51,
                43, 22, 23, 23, 44, 43, 42, 21, 22, 22, 43, 42, 41, 62, 61, 61, 40, 41,
                57, 36, 37, 37, 58, 57, 49, 27, 28, 31, 52, 51, 24, 45, 23, 26, 47, 46,
                46, 25, 26, 27, 48, 26, 49, 28, 50, 29, 50, 28, 51, 30, 31, 53, 32, 54,
            },
            new byte[]
            {
                55, 56, 54, 54, 42, 62, 55, 54, 62, 57, 47, 56, 47, 48, 53, 52, 53, 48,
                48, 49, 52, 61, 43, 44, 60, 61, 44, 59, 60, 44, 44, 45, 59, 58, 59, 45,
                45, 46, 58, 62, 42, 43, 43, 61, 62, 50, 51, 49, 52, 49, 51, 47, 57, 58,
                58, 46, 47, 56, 47, 53, 54, 56, 53,
            }),
        // 7
        new IowaPitMarkerGlyph(0.703f,
            new short[]
            {
                0, 0, 278, 0, 681, 785, 703, 1000, 34, 1000, 9, 757, 392, 757, 21, 13, 271, 13,
                668, 789, 689, 987, 46, 987, 23, 770, 413, 770, 44, 27, 262, 27, 655, 793, 673, 973,
                59, 973, 39, 784, 436, 784,
            },
            new byte[]
            {
                12, 4, 5, 4, 12, 11, 10, 2, 3, 2, 10, 9, 8, 1, 2, 13, 6, 7,
                7, 0, 1, 1, 8, 7, 2, 9, 8, 12, 5, 6, 6, 13, 12, 4, 11, 10,
                10, 3, 4, 0, 7, 6,
            },
            new byte[]
            {
                18, 11, 19, 19, 11, 12, 16, 9, 17, 20, 13, 14, 15, 14, 8, 11, 18, 17,
                14, 7, 8, 15, 8, 9, 17, 9, 10, 17, 10, 11, 13, 20, 19, 19, 12, 13,
                7, 14, 13, 9, 16, 15,
            },
            new byte[]
            {
                16, 17, 20, 20, 17, 18, 18, 19, 20, 16, 20, 15, 14, 15, 20,
            }),
        // 8
        new IowaPitMarkerGlyph(0.750f,
            new short[]
            {
                101, 0, 549, 0, 669, 108, 702, 423, 643, 498, 718, 573, 750, 878, 640, 1000, 211, 1000,
                81, 882, 49, 577, 108, 502, 33, 427, 0, 112, 248, 250, 259, 347, 287, 378, 441, 378,
                460, 351, 450, 254, 429, 224, 261, 224, 290, 647, 300, 744, 329, 776, 483, 776, 502, 748,
                492, 652, 471, 622, 303, 622, 106, 13, 544, 13, 657, 115, 689, 419, 626, 499, 705, 579,
                736, 874, 635, 987, 216, 987, 93, 876, 62, 581, 125, 501, 45, 421, 13, 117, 235, 247,
                246, 352, 282, 391, 448, 391, 473, 354, 462, 250, 435, 211, 253, 211, 277, 645, 288, 749,
                323, 789, 490, 789, 515, 752, 504, 647, 477, 609, 295, 609, 113, 27, 538, 27, 643, 122,
                674, 415, 607, 500, 692, 585, 721, 869, 628, 973, 222, 973, 107, 869, 77, 585, 143, 500,
                59, 415, 28, 122, 221, 245, 233, 358, 275, 405, 455, 405, 488, 358, 476, 245, 443, 197,
                244, 197, 262, 642, 274, 755, 317, 803, 497, 803, 530, 755, 518, 642, 485, 595, 286, 595,
            },
            new byte[]
            {
                42, 12, 43, 13, 43, 12, 39, 9, 40, 10, 40, 9, 1, 31, 0, 0, 30, 13,
                43, 13, 30, 41, 11, 42, 12, 42, 11, 40, 10, 11, 11, 41, 40, 38, 8, 39,
                9, 39, 8, 14, 45, 44, 51, 21, 44, 14, 44, 21, 45, 14, 15, 59, 29, 52,
                22, 52, 29, 52, 22, 23, 53, 52, 23, 23, 24, 53, 24, 54, 53, 45, 15, 16,
                16, 46, 45, 18, 19, 49, 49, 48, 18, 20, 50, 49, 49, 19, 20, 18, 48, 17,
                47, 17, 48, 26, 57, 56, 57, 26, 27, 26, 56, 25, 56, 55, 25, 58, 57, 28,
                57, 27, 28, 8, 38, 7, 33, 4, 34, 6, 36, 5, 35, 5, 36, 32, 2, 3,
                33, 32, 3, 4, 33, 3, 5, 35, 4, 31, 1, 2, 2, 32, 31, 4, 35, 34,
                36, 6, 37, 7, 37, 6, 21, 51, 50, 50, 20, 21, 46, 16, 17, 17, 47, 46,
                29, 59, 58, 58, 28, 29, 54, 24, 25, 25, 55, 54, 30, 0, 31, 37, 7, 38,
            },
            new byte[]
            {
                73, 42, 43, 70, 39, 40, 42, 73, 72, 68, 38, 69, 73, 43, 30, 30, 60, 73,
                71, 41, 72, 41, 71, 70, 39, 70, 69, 69, 38, 39, 75, 45, 46, 76, 75, 46,
                89, 59, 82, 59, 52, 82, 83, 82, 52, 52, 53, 83, 53, 54, 83, 54, 84, 83,
                81, 51, 74, 51, 44, 74, 44, 45, 74, 45, 75, 74, 50, 80, 79, 49, 50, 79,
                48, 49, 79, 79, 78, 48, 47, 48, 78, 77, 47, 78, 61, 31, 62, 33, 63, 32,
                35, 66, 65, 62, 31, 32, 32, 63, 62, 34, 64, 63, 64, 34, 65, 37, 67, 66,
                66, 36, 37, 58, 88, 87, 57, 58, 87, 56, 57, 87, 87, 86, 56, 55, 56, 86,
                85, 55, 86, 31, 61, 60, 51, 81, 80, 80, 50, 51, 76, 46, 47, 47, 77, 76,
                59, 89, 88, 88, 58, 59, 84, 54, 55, 55, 85, 84, 38, 68, 67, 60, 30, 31,
                63, 33, 34, 35, 65, 34, 36, 66, 35, 67, 37, 38, 70, 40, 41, 42, 72, 41,
            },
            new byte[]
            {
                72, 73, 74, 73, 60, 81, 74, 73, 81, 72, 74, 75, 71, 72, 75, 75, 76, 71,
                76, 89, 71, 89, 82, 71, 70, 71, 82, 82, 83, 70, 69, 70, 83, 83, 84, 69,
                68, 69, 84, 87, 64, 65, 86, 87, 65, 65, 66, 86, 61, 62, 80, 79, 80, 62,
                62, 63, 79, 78, 79, 63, 63, 64, 78, 77, 78, 64, 88, 77, 64, 64, 87, 88,
                86, 66, 85, 66, 67, 85, 81, 60, 61, 61, 80, 81, 89, 76, 77, 77, 88, 89,
                68, 84, 85, 85, 67, 68,
            }),
        // 9
        new IowaPitMarkerGlyph(0.709f,
            new short[]
            {
                4, 0, 499, 0, 629, 118, 709, 878, 600, 1000, 160, 1000, 40, 881, 0, 500, 109, 378,
                422, 378, 411, 269, 376, 243, 30, 243, 248, 632, 259, 742, 286, 766, 443, 766, 461, 746,
                449, 636, 423, 612, 266, 612, 18, 13, 494, 13, 617, 124, 696, 874, 594, 987, 166, 987,
                52, 876, 13, 505, 115, 391, 437, 391, 423, 262, 380, 230, 41, 230, 235, 628, 247, 749,
                281, 779, 449, 779, 474, 751, 462, 630, 428, 599, 260, 599, 34, 27, 488, 27, 603, 131,
                681, 869, 588, 973, 171, 973, 66, 869, 28, 509, 121, 405, 452, 405, 436, 254, 385, 216,
                54, 216, 220, 623, 234, 755, 275, 793, 455, 793, 489, 755, 475, 623, 433, 585, 254, 585,
            },
            new byte[]
            {
                21, 12, 0, 12, 21, 33, 8, 29, 7, 28, 7, 29, 27, 6, 28, 7, 28, 6,
                13, 34, 41, 41, 20, 13, 34, 13, 14, 35, 34, 14, 14, 15, 35, 15, 36, 35,
                26, 5, 27, 6, 27, 5, 10, 32, 31, 9, 10, 31, 9, 31, 30, 19, 40, 18,
                4, 25, 24, 24, 3, 4, 2, 23, 1, 22, 1, 23, 3, 24, 2, 18, 40, 39,
                17, 18, 39, 39, 38, 17, 16, 17, 38, 37, 16, 38, 21, 0, 1, 1, 22, 21,
                32, 11, 33, 32, 10, 11, 20, 41, 40, 40, 19, 20, 36, 15, 16, 16, 37, 36,
                5, 26, 25, 25, 4, 5, 9, 30, 29, 29, 8, 9, 12, 33, 11, 23, 2, 24,
            },
            new byte[]
            {
                54, 33, 42, 21, 42, 33, 49, 28, 29, 29, 50, 49, 48, 27, 49, 56, 34, 35,
                34, 56, 55, 62, 41, 55, 34, 55, 41, 36, 57, 35, 56, 35, 57, 27, 48, 47,
                47, 26, 27, 32, 53, 31, 30, 52, 51, 31, 53, 52, 39, 60, 59, 38, 39, 59,
                23, 44, 22, 43, 22, 44, 45, 24, 46, 24, 25, 46, 24, 45, 23, 40, 61, 60,
                60, 39, 40, 58, 37, 38, 38, 59, 58, 22, 43, 42, 53, 32, 54, 41, 62, 61,
                61, 40, 41, 57, 36, 37, 37, 58, 57, 26, 47, 46, 46, 25, 26, 42, 21, 22,
                28, 49, 27, 30, 51, 50, 50, 29, 30, 52, 30, 31, 33, 54, 32, 44, 23, 45,
            },
            new byte[]
            {
                49, 50, 62, 62, 55, 49, 48, 49, 55, 55, 56, 48, 56, 57, 48, 57, 47, 48,
                59, 60, 45, 52, 53, 43, 43, 44, 52, 51, 52, 44, 61, 51, 60, 59, 45, 58,
                45, 46, 58, 43, 53, 42, 54, 42, 53, 47, 57, 58, 58, 46, 47, 51, 44, 60,
                45, 60, 44, 51, 61, 62, 62, 50, 51,
            }),
    };
}

