using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Each LOD owns its deformed copies. The damage locations live on the car, outside the LOD cache.
internal sealed partial class TrackBodyDamageSurface : MonoBehaviour
{
    private sealed class Surface
    {
        internal MeshFilter Filter;
        internal Mesh Source, Deformed;
        internal Matrix4x4 ToMesh, NormalToMesh;
        internal Vector3[] Rest, RestNormals, Points, Normals, MeshPoints, MeshNormals;
        internal int[] PaintTriangles, BumperTriangles;
        internal bool[] NerfBarMask, BumperMask;
        internal bool Mirrored, Chassis;
    }

    private readonly List<Surface> surfaces = new();
    private readonly List<Vector3> scuffPoints = new();
    private readonly List<Vector2> scuffUv = new();
    private readonly List<Color32> scuffColors = new();
    private readonly List<int> scuffTriangles = new();
    private Mesh scuffs;
    private MeshRenderer scuffRenderer;
    private int revision = -1;
    private MeshRenderer rockerChassis;

    internal void Bind(Material material, bool deformShell, bool modified, Material bodyPaint, Material hoodPaint)
    {
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!deformShell)
            {
                BindContactSurface(renderer, modified, bodyPaint, hoodPaint);
                continue;
            }
            if (renderer.name == "Chassis")
            {
                rockerChassis = renderer;
                continue;
            }
            if (!IsBumperPanel(renderer.name) &&
                renderer.name is not ("Fender" or "Door Panel" or "Quarter Panel" or "Body"))
                continue;

            // The model's original detail geometry and the coincident livery layer must move together.
            bool painted = false;
            foreach (Transform child in renderer.transform)
            {
                if (child.name != "Team Paint") continue;
                painted |= BindMesh(child.GetComponent<MeshFilter>(), receiveScuffs: true);
            }
            BindMesh(renderer.GetComponent<MeshFilter>(), receiveScuffs: !painted);
        }

        if (deformShell) BindSideNumbers();
        if (material == null || surfaces.Count == 0) return;
        var overlay = new GameObject("Contact scuffs") { layer = gameObject.layer };
        overlay.transform.SetParent(transform, false);
        scuffs = new Mesh { name = "Contact scuffs", indexFormat = IndexFormat.UInt32 };
        scuffs.MarkDynamic();
        overlay.AddComponent<MeshFilter>().sharedMesh = scuffs;
        scuffRenderer = overlay.AddComponent<MeshRenderer>();
        scuffRenderer.sharedMaterial = material;
        scuffRenderer.shadowCastingMode = ShadowCastingMode.Off;
        scuffRenderer.receiveShadows = false;
        scuffRenderer.enabled = false;
    }

    private static bool IsBumperPanel(string name) =>
        name is "Front Bumper" or "Front Bumper Aero Ducts" or "Rear Bumper";

    private void BindContactSurface(MeshRenderer renderer, bool modified, Material bodyPaint, Material hoodPaint)
    {
        if (!renderer.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
            return;
        var mesh = filter.sharedMesh;
        if (modified)
        {
            if (renderer.name != "ModifiedMetal") return;
            var toBody = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var bars = new List<int>();
            var bumpers = new List<int>();
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = toBody.MultiplyPoint3x4(vertices[i]);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                var pointA = vertices[a];
                var pointB = vertices[b];
                var pointC = vertices[c];
                List<int> panel = null;
                if (IsModifiedNerfBar(pointA) && IsModifiedNerfBar(pointB) && IsModifiedNerfBar(pointC))
                    panel = bars;
                else if (IsModifiedBumper(pointA) && IsModifiedBumper(pointB) && IsModifiedBumper(pointC))
                    panel = bumpers;
                if (panel == null) continue;
                panel.Add(a);
                panel.Add(b);
                panel.Add(c);
            }
            if (bars.Count > 0 || bumpers.Count > 0)
                BindMesh(filter, receiveScuffs: true, markTriangles: bars.ToArray(), bumperTriangles: bumpers.ToArray());
            return;
        }

        if (renderer.name is "Chassis" or "Cockpit" or "Race number" or "Sponsor decal") return;
        var paintTriangles = new List<int>();
        var materials = renderer.sharedMaterials;
        for (int slot = 0; slot < materials.Length && slot < mesh.subMeshCount; slot++)
        {
            var paint = materials[slot];
            if (paint == null || mesh.GetTopology(slot) != MeshTopology.Triangles) continue;
            if (paint == bodyPaint || paint == hoodPaint || paint.name is
                "FordMustang_Body" or "FordMustang_BodyRight" or "ChevroletCamaro_Body" or "ChevroletCamaro_BodyRight" or
                "ToyotaCamry_Body" or "ToyotaCamry_BodyRight" or "RaceTruck_BodyDetails" or "LateModel_Fascia")
                paintTriangles.AddRange(mesh.GetTriangles(slot));
        }
        if (paintTriangles.Count > 0)
            BindMesh(filter, receiveScuffs: true, markTriangles: paintTriangles.ToArray());
        else if (IsBumperPanel(renderer.name))
            // The original panel and a separate Team Paint layer have to deform together.
            BindMesh(filter, receiveScuffs: false);
    }

    // The merged ModifiedMetal mesh contains two outboard nerf-bar assemblies at these bounds in all five LODs.
    internal static bool IsModifiedNerfBar(Vector3 point) => Mathf.Abs(point.x) >= .43f &&
        point.y >= .05f && point.y <= .375f && point.z >= -.47f && point.z <= .736f;

    internal static bool IsModifiedBumper(Vector3 point) => Mathf.Abs(point.z) >= 1.06f &&
        point.y >= .14f && point.y <= .35f;

    private bool BindMesh(MeshFilter filter, bool receiveScuffs, bool chassis = false, int[] markTriangles = null,
        int[] bumperTriangles = null)
    {
        var mesh = filter?.sharedMesh;
        if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0) return false;
        var toBody = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        var toMesh = toBody.inverse;
        Vector3[] points = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (normals.Length != points.Length) return false;
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = toBody.MultiplyPoint3x4(points[i]);
            normals[i] = toMesh.transpose.MultiplyVector(normals[i]).normalized;
        }
        surfaces.Add(new Surface
        {
            Filter = filter, Source = mesh, ToMesh = toMesh, NormalToMesh = toBody.transpose,
            Rest = points, RestNormals = normals, Points = new Vector3[points.Length], Normals = new Vector3[points.Length],
            MeshPoints = new Vector3[points.Length], MeshNormals = new Vector3[points.Length],
            PaintTriangles = receiveScuffs ? markTriangles ?? mesh.triangles : null,
            BumperTriangles = bumperTriangles,
            NerfBarMask = bumperTriangles == null ? null : PanelMask(points.Length, markTriangles),
            BumperMask = bumperTriangles == null ? null : PanelMask(points.Length, bumperTriangles),
            Mirrored = toBody.determinant < 0f, Chassis = chassis
        });
        return true;
    }

    private static bool[] PanelMask(int vertexCount, int[] triangles)
    {
        var mask = new bool[vertexCount];
        foreach (int vertex in triangles) mask[vertex] = true;
        return mask;
    }

    internal void Apply(TrackBodyDamage damage)
    {
        if (revision == damage.Revision) return;
        revision = damage.Revision;
        scuffPoints.Clear();
        scuffUv.Clear();
        scuffColors.Clear();
        scuffTriangles.Clear();
        if (rockerChassis != null && HasSideDent(damage))
        {
            foreach (Transform child in rockerChassis.transform)
                if (child.name == "Team Paint") BindMesh(child.GetComponent<MeshFilter>(), receiveScuffs: false, chassis: true);
            BindMesh(rockerChassis.GetComponent<MeshFilter>(), receiveScuffs: false, chassis: true);
            rockerChassis = null;
        }
        foreach (var surface in surfaces)
        {
            if (!damage.HasMarks)
            {
                surface.Filter.sharedMesh = surface.Source;
                continue;
            }
            if (damage.HasDent)
                Deform(surface, damage.Marks);
            else
            {
                surface.Filter.sharedMesh = surface.Source;
                surface.Rest.CopyTo(surface.Points, 0);
                surface.RestNormals.CopyTo(surface.Normals, 0);
            }
            if (scuffRenderer != null && surface.PaintTriangles != null)
            {
                foreach (var mark in damage.Marks)
                    if (mark.Scuff > 0f) AddScuffs(surface, mark);
                foreach (var mark in damage.TireMarks)
                    AddScuffs(surface, mark);
            }
        }

        UpdateSideNumbers(damage);
        if (scuffRenderer == null) return;
        scuffRenderer.enabled = scuffTriangles.Count > 0;
        scuffs.Clear();
        if (!scuffRenderer.enabled) return;
        scuffs.SetVertices(scuffPoints);
        scuffs.SetUVs(0, scuffUv);
        scuffs.SetColors(scuffColors);
        scuffs.SetTriangles(scuffTriangles, 0);
    }

    private static bool HasSideDent(TrackBodyDamage damage)
    {
        foreach (var mark in damage.Marks)
            if (mark.Depth > 0f && mark.Outward.x != 0f) return true;
        return false;
    }

    private static void Deform(Surface surface, TrackBodyDamage.Mark[] marks)
    {
        bool changed = false;
        for (int i = 0; i < surface.Rest.Length; i++)
        {
            Vector3 rest = surface.Rest[i];
            Vector3 normal = surface.RestNormals[i];
            Vector3 point = rest;
            foreach (var mark in marks)
            {
                if (mark.Depth <= 0f) continue;
                bool bumper = mark.Outward.x == 0f;
                if (surface.Chassis && bumper || surface.BumperMask != null &&
                    !(bumper ? surface.BumperMask[i] : surface.NerfBarMask[i])) continue;
                // Tuck the upper rocker behind the dent. Its outer surface sits only millimeters behind the door.
                float width = surface.Chassis ? mark.Width + .35f : mark.Width;
                float height = surface.Chassis ? mark.Height + .18f : mark.Height;
                float depth = (surface.Chassis ? mark.Depth + .095f : mark.Depth) * mark.SurfaceWeight(rest);
                if (depth <= 0f) continue;
                Vector3 delta = rest - mark.Center;
                var uv = new Vector2(Vector3.Dot(delta, mark.Along) / width, delta.y / height);
                float rim = 1f - uv.sqrMagnitude;
                if (rim <= 0f) continue;

                point -= mark.Outward * (depth * rim * rim);
                // Bend the original normal with the dent to retain authored seams and shading.
                normal -= 4f * depth * rim *
                    (mark.Along * (uv.x / width) + Vector3.up * (uv.y / height));
                changed = true;
            }
            if (!surface.Chassis)
                point.x = rest.x + Mathf.Clamp(point.x - rest.x, -.10f, .10f);
            surface.Points[i] = point;
            surface.Normals[i] = normal.normalized;
            surface.MeshPoints[i] = surface.ToMesh.MultiplyPoint3x4(point);
            surface.MeshNormals[i] = surface.NormalToMesh.MultiplyVector(normal).normalized;
        }
        if (!changed)
        {
            surface.Filter.sharedMesh = surface.Source;
            return;
        }
        if (surface.Deformed == null)
        {
            surface.Deformed = Instantiate(surface.Source);
            surface.Deformed.name = surface.Source.name + " (contact dents)";
            surface.Deformed.MarkDynamic();
        }
        surface.Deformed.vertices = surface.MeshPoints;
        surface.Deformed.normals = surface.MeshNormals;
        surface.Deformed.RecalculateBounds();
        surface.Filter.sharedMesh = surface.Deformed;
    }

    private void AddScuffs(Surface surface, TrackBodyDamage.Mark mark)
    {
        int[] triangles = mark.Outward.x == 0f ? surface.BumperTriangles ?? surface.PaintTriangles : surface.PaintTriangles;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + (surface.Mirrored ? 2 : 1)];
            int c = triangles[i + (surface.Mirrored ? 1 : 2)];
            Vector3 normal = (surface.RestNormals[a] + surface.RestNormals[b] + surface.RestNormals[c]) / 3f;
            float facing = Vector3.Dot(normal, mark.Outward);
            if (facing < .30f || !mark.OnPanelSide((surface.Rest[a] + surface.Rest[b] + surface.Rest[c]) / 3f))
                continue;
            Vector2 uvA = mark.Project(surface.Rest[a]);
            Vector2 uvB = mark.Project(surface.Rest[b]);
            Vector2 uvC = mark.Project(surface.Rest[c]);
            if (Outside(uvA.x, uvB.x, uvC.x) || Outside(uvA.y, uvB.y, uvC.y)) continue;
            byte alpha = (byte)Mathf.RoundToInt(255f * mark.Scuff * Mathf.InverseLerp(.28f, .72f, facing));
            Append(a, uvA, alpha);
            Append(b, uvB, alpha);
            Append(c, uvC, alpha);
        }

        void Append(int vertex, Vector2 uv, byte alpha)
        {
            scuffTriangles.Add(scuffPoints.Count);
            scuffPoints.Add(surface.Points[vertex] + surface.Normals[vertex] * .0018f);
            scuffUv.Add(uv);
            scuffColors.Add(new Color32(mark.TireRub ? (byte)0 : (byte)255, 255, 255, alpha));
        }
        static bool Outside(float a, float b, float c) =>
            a > 1f && b > 1f && c > 1f || a < -1f && b < -1f && c < -1f;
    }

    private void OnDestroy()
    {
        DestroyProjectedNumbers();
        foreach (var surface in surfaces)
            if (surface.Deformed != null) Destroy(surface.Deformed);
        if (scuffs != null) Destroy(scuffs);
    }
}
