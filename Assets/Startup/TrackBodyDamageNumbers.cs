using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed partial class TrackBodyDamageSurface
{
    private sealed class SideNumber
    {
        internal TextMesh Text;
        internal MeshRenderer Flat, Projected;
        internal Mesh Mesh;
        internal float Side;
    }

    internal readonly struct NumberVertex
    {
        internal readonly Vector2 Ink;
        internal readonly Vector3 Point;

        internal NumberVertex(Vector2 ink, Vector3 point)
        {
            Ink = ink;
            Point = point;
        }
    }

    private readonly List<SideNumber> sideNumbers = new(2);
    private readonly List<CharacterInfo> numberGlyphs = new(4);
    private readonly List<NumberVertex> numberPolygon = new(8), numberClip = new(8);
    private readonly List<Vector3> numberPoints = new();
    private readonly List<Vector2> numberUv = new();
    private readonly List<Color32> numberColors = new();
    private readonly List<int> numberTriangles = new();
    private TrackBodyDamage numberDamage;
    private bool numberAtlasDirty;

    private void BindSideNumbers()
    {
        foreach (var label in GetComponentsInChildren<TextMesh>(true))
        {
            if (label.name != "Race number" || !label.TryGetComponent<MeshRenderer>(out var renderer)) continue;
            Vector3 outward = transform.InverseTransformDirection(-label.transform.forward);
            if (Mathf.Abs(outward.x) < .8f) continue;
            sideNumbers.Add(new SideNumber { Text = label, Flat = renderer, Side = Mathf.Sign(outward.x) });
        }
    }

    private void OnEnable()
    {
        Font.textureRebuilt += OnNumberAtlasRebuilt;
        numberAtlasDirty = true;
    }

    private void OnDisable() => Font.textureRebuilt -= OnNumberAtlasRebuilt;

    private void OnNumberAtlasRebuilt(Font font)
    {
        foreach (var number in sideNumbers)
            if (number.Text.font == font) numberAtlasDirty = true;
    }

    private void LateUpdate()
    {
        if (numberAtlasDirty && numberDamage != null)
            UpdateSideNumbers(numberDamage);
    }

    private void UpdateSideNumbers(TrackBodyDamage damage)
    {
        numberDamage = damage;
        // Request both labels before reading any UVs, so an atlas rebuild cannot invalidate the first label.
        foreach (var number in sideNumbers)
            if (HasDentOnSide(damage, number.Side) && number.Text.font != null)
                number.Text.font.RequestCharactersInTexture(number.Text.text, number.Text.fontSize, number.Text.fontStyle);

        foreach (var number in sideNumbers)
        {
            bool projected = HasDentOnSide(damage, number.Side) && ProjectNumber(number);
            number.Flat.enabled = !projected;
            if (number.Projected != null) number.Projected.enabled = projected;
        }
        numberAtlasDirty = false;
    }

    private static bool HasDentOnSide(TrackBodyDamage damage, float side)
    {
        foreach (var mark in damage.Marks)
            if (mark.Outward.x == side && mark.Depth > 0f) return true;
        return false;
    }

    private bool ProjectNumber(SideNumber number)
    {
        var text = number.Text;
        if (text.font == null || !TrackPreview.TryGetCarNumberAscent(text, out float ascent)) return false;
        numberGlyphs.Clear();
        int advance = 0;
        foreach (char character in text.text)
        {
            if (!text.font.GetCharacterInfo(character, out var glyph, text.fontSize, text.fontStyle)) return false;
            advance += glyph.advance;
            numberGlyphs.Add(glyph);
        }
        Bounds line = number.Flat.localBounds;
        if (advance <= 0 || line.size.x <= 0f) return false;

        numberPoints.Clear();
        numberUv.Clear();
        numberColors.Clear();
        numberTriangles.Clear();
        Matrix4x4 toInk = text.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        float units = line.size.x / advance;
        var baseline = new Vector2(line.min.x, line.max.y - ascent * units);
        float pen = baseline.x;
        foreach (var glyph in numberGlyphs)
        {
            var min = new Vector2(pen + glyph.minX * units, baseline.y + glyph.minY * units);
            var max = new Vector2(pen + glyph.maxX * units, baseline.y + glyph.maxY * units);
            pen += glyph.advance * units;
            if (max.x <= min.x || max.y <= min.y) continue;

            foreach (var surface in surfaces)
            {
                int[] triangles = surface.PaintTriangles;
                if (triangles == null) continue;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + (surface.Mirrored ? 2 : 1)], c = triangles[i + (surface.Mirrored ? 1 : 2)];
                    float facing = (surface.RestNormals[a].x + surface.RestNormals[b].x + surface.RestNormals[c].x) * number.Side;
                    float across = (surface.Rest[a].x + surface.Rest[b].x + surface.Rest[c].x) * number.Side;
                    if (facing < .9f || across < 1.16f) continue;

                    numberPolygon.Clear();
                    numberPolygon.Add(Vertex(a));
                    numberPolygon.Add(Vertex(b));
                    numberPolygon.Add(Vertex(c));
                    ClipNumberPolygon(numberPolygon, numberClip, min, max);
                    if (numberPolygon.Count < 3) continue;
                    int start = numberPoints.Count;
                    foreach (var vertex in numberPolygon)
                    {
                        float u = Mathf.InverseLerp(min.x, max.x, vertex.Ink.x);
                        float v = Mathf.InverseLerp(min.y, max.y, vertex.Ink.y);
                        numberUv.Add(Vector2.Lerp(Vector2.Lerp(glyph.uvBottomLeft, glyph.uvBottomRight, u),
                            Vector2.Lerp(glyph.uvTopLeft, glyph.uvTopRight, u), v));
                        numberPoints.Add(vertex.Point);
                        numberColors.Add(text.color);
                    }
                    for (int triangle = 1; triangle < numberPolygon.Count - 1; triangle++)
                    {
                        numberTriangles.Add(start);
                        numberTriangles.Add(start + triangle);
                        numberTriangles.Add(start + triangle + 1);
                    }

                    NumberVertex Vertex(int index) => new NumberVertex(toInk.MultiplyPoint3x4(surface.Rest[index]),
                        surface.Points[index] + surface.Normals[index] * .0011f);
                }
            }
        }
        if (numberTriangles.Count == 0) return false;

        if (number.Mesh == null)
        {
            var decal = new GameObject("Conforming side number") { layer = gameObject.layer };
            decal.transform.SetParent(transform, false);
            number.Mesh = new Mesh { name = "Conforming side number" };
            number.Mesh.MarkDynamic();
            decal.AddComponent<MeshFilter>().sharedMesh = number.Mesh;
            number.Projected = decal.AddComponent<MeshRenderer>();
            number.Projected.sharedMaterial = number.Flat.sharedMaterial;
            number.Projected.shadowCastingMode = ShadowCastingMode.Off;
            number.Projected.receiveShadows = false;
        }
        number.Mesh.Clear();
        number.Mesh.SetVertices(numberPoints);
        number.Mesh.SetUVs(0, numberUv);
        number.Mesh.SetColors(numberColors);
        number.Mesh.SetTriangles(numberTriangles, 0);
        return true;
    }

    internal static void ClipNumberPolygon(List<NumberVertex> polygon, List<NumberVertex> buffer, Vector2 min, Vector2 max)
    {
        Clip(polygon, buffer, 0, min.x, true);
        Clip(buffer, polygon, 0, max.x, false);
        Clip(polygon, buffer, 1, min.y, true);
        Clip(buffer, polygon, 1, max.y, false);

        static void Clip(List<NumberVertex> input, List<NumberVertex> output, int axis, float limit, bool lowerBound)
        {
            output.Clear();
            if (input.Count == 0) return;
            NumberVertex previous = input[^1];
            float previousCoord = previous.Ink[axis];
            bool previousInside = lowerBound ? previousCoord >= limit : previousCoord <= limit;
            foreach (var current in input)
            {
                float coord = current.Ink[axis];
                bool inside = lowerBound ? coord >= limit : coord <= limit;
                if (inside != previousInside)
                {
                    float fraction = (limit - previousCoord) / (coord - previousCoord);
                    output.Add(new NumberVertex(Vector2.LerpUnclamped(previous.Ink, current.Ink, fraction),
                        Vector3.LerpUnclamped(previous.Point, current.Point, fraction)));
                }
                if (inside) output.Add(current);
                previous = current;
                previousCoord = coord;
                previousInside = inside;
            }
        }
    }

    private void DestroyProjectedNumbers()
    {
        foreach (var number in sideNumbers)
            if (number.Mesh != null) Destroy(number.Mesh);
    }
}
