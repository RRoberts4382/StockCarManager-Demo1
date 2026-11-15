using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

internal enum CarPaintScheme
{
    Classic, SpeedStripe, Sweep, Apex, SideFlash,
    Beltline, Diagonal, ReverseDiagonal, Chevron, Rebound,
    Wave, Ribbon, Taper, Triad, Ladder,
    Velocity, Gridlock, Switchback, FrontClip, RearClip,
    Horizon, SplitRail, TailFin, Overdrive, Afterburn
}

internal readonly struct CarPaintStyle
{
    public readonly CarPaintScheme Scheme;
    public readonly Color32 Primary;
    public readonly Color32 Secondary;
    public readonly Color32 Trim;
    public readonly bool HasTrim;

    public CarPaintStyle(CarPaintScheme scheme, Color32 primary, Color32 secondary, Color32 trim, bool hasTrim)
    {
        Scheme = scheme;
        Primary = primary;
        Secondary = secondary;
        Trim = trim;
        HasTrim = hasTrim;
    }

    public bool HasWrap => Scheme != CarPaintScheme.Classic || HasTrim;

    public static string SchemeName(CarPaintScheme scheme) => scheme switch
    {
        CarPaintScheme.SpeedStripe => "Speed Stripe",
        CarPaintScheme.SideFlash => "Side Flash",
        CarPaintScheme.ReverseDiagonal => "Reverse Diagonal",
        CarPaintScheme.FrontClip => "Front Clip",
        CarPaintScheme.RearClip => "Rear Clip",
        CarPaintScheme.SplitRail => "Split Rail",
        CarPaintScheme.TailFin => "Tail Fin",
        _ => scheme.ToString()
    };

    public Color32 ColorAt(Vector3 carLocal, bool hoodTrunk) => ColorAt(Project(carLocal), hoodTrunk);

    public static Vector2 Project(Vector3 point)
    {
        float length = Mathf.Clamp01((1.45f - point.z) / 2.9f);
        float height = Mathf.Clamp01((point.y - 0.12f) / 0.94f);
        float center = 1f - Mathf.Clamp01(Mathf.Abs(point.x) / 0.82f);
        return new Vector2(length, height * 0.74f + center * 0.26f);
    }

    public Texture2D BuildTexture()
    {
        const int width = 512;
        const int height = 256;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
        {
            name = $"{SchemeName(Scheme)} paint",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear
        };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = ColorAt(new Vector2((x + 0.5f) / width, (y + 0.5f) / height), false);
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    private Color32 ColorAt(Vector2 uv, bool hoodTrunk)
    {
        float u = uv.x;
        float v = uv.y;
        switch (Scheme)
        {
            case CarPaintScheme.SpeedStripe:
                // A bright pinstripe separates a wide belt from the body color.
                float belt = v - (0.23f + 0.14f * u);
                if (belt >= -0.05f && belt < 0.06f)
                    return Secondary;
                return belt >= 0.06f && belt < 0.084f ? Trim : Primary;
            case CarPaintScheme.Sweep:
                float sweep = v - (0.22f + 0.63f * u * u);
                return sweep < 0f ? Secondary : sweep < 0.032f ? Trim : Primary;
            case CarPaintScheme.Apex:
                float apex = v - (0.17f + 0.72f * Mathf.Abs(u - 0.58f));
                if (apex >= 0f && apex < 0.029f)
                    return Trim;
                if (u > 0.63f && v > 0.24f && v < 0.58f && Mathf.Repeat(u * 8.5f - v * 1.9f, 1f) < 0.19f)
                    return Trim;
                return apex < 0f ? Secondary : Primary;
            case CarPaintScheme.SideFlash:
                float flash = v - (0.12f + 0.62f * u);
                if (flash > -0.03f && flash < 0.025f)
                    return Trim;
                return flash <= -0.03f || flash is > 0.12f and < 0.2f ? Secondary : Primary;
            case CarPaintScheme.Beltline:
                return Split(v - 0.31f, 0.026f);
            case CarPaintScheme.Diagonal:
                return Split(v - (0.12f + 0.64f * u), 0.047f);
            case CarPaintScheme.ReverseDiagonal:
                return Split(v - (0.72f - 0.59f * u), 0.041f);
            case CarPaintScheme.Chevron:
                return Split(v - (0.66f - 0.85f * Mathf.Abs(u - 0.5f)), 0.032f);
            case CarPaintScheme.Rebound:
                return Split(v - (0.27f + 0.57f * Mathf.Abs(u - 0.31f)), 0.046f);
            case CarPaintScheme.Wave:
                return Split(v - (0.43f + 0.16f * Mathf.Sin(u * 2f * Mathf.PI)), 0.04f);
            case CarPaintScheme.Ribbon:
                return Banded(v - (0.33f + 0.13f * Mathf.Sin(u * Mathf.PI)), 0.085f, 0.025f);
            case CarPaintScheme.Taper:
                return Banded(v - (0.22f + 0.27f * u), 0.025f + 0.13f * u, 0.027f);
            case CarPaintScheme.Triad:
                return Split(u + 0.29f * v - 0.63f, 0.12f);
            case CarPaintScheme.Ladder:
                return v < 0.25f ? Secondary
                    : v < 0.56f && Mathf.Repeat(u * 9f - v * 1.3f, 1f) < 0.18f ? Trim : Primary;
            case CarPaintScheme.Velocity:
                return v < 0.22f ? Secondary
                    : v < 0.59f && u > 0.5f && Mathf.Repeat(u * 12f - v * 4f, 1f) < 0.22f ? Trim : Primary;
            case CarPaintScheme.Gridlock:
                if (u > 0.63f && v > 0.22f && v < 0.56f &&
                    ((Mathf.FloorToInt((u - v * 0.22f) * 19f) + Mathf.FloorToInt(v * 17f)) & 1) == 0)
                    return Trim;
                return Split(v - 0.39f, 0.024f);
            case CarPaintScheme.Switchback:
                return Split(v - (0.22f + 0.45f * Mathf.PingPong(u * 2.2f, 1f)), 0.03f);
            case CarPaintScheme.FrontClip:
                return Split(u - (0.29f + 0.27f * v), 0.044f);
            case CarPaintScheme.RearClip:
                return Split((0.74f - 0.24f * v) - u, 0.05f);
            case CarPaintScheme.Horizon:
                return v < 0.18f ? Secondary : v < 0.215f || v is > 0.51f and < 0.545f ? Trim : Primary;
            case CarPaintScheme.SplitRail:
                return Banded(Mathf.Abs(v - (0.36f + 0.13f * u)) - 0.12f, 0.036f, 0.025f);
            case CarPaintScheme.TailFin:
                return Banded(v - (0.22f + 0.62f * u * u * u), 0.095f, 0.032f);
            case CarPaintScheme.Overdrive:
                return u < 0.14f || u > 0.85f ? Split(v - 0.37f, 0.04f)
                    : Banded(v - (0.29f + 0.16f * u), 0.057f, 0.036f);
            case CarPaintScheme.Afterburn:
                return Split(v - (0.19f + 0.62f * u + 0.065f * Mathf.Sin(u * 8f * Mathf.PI)), 0.034f);
            default:
                if (hoodTrunk)
                    return Primary;
                return HasTrim && v is >= 0.12f and <= 0.17f ? Trim : Secondary;
        }
    }

    private Color32 Split(float offset, float trimWidth) =>
        offset < 0f ? Secondary : offset < trimWidth ? Trim : Primary;

    private Color32 Banded(float offset, float halfWidth, float trimWidth)
    {
        float distance = Mathf.Abs(offset);
        return distance < halfWidth ? Secondary : distance < halfWidth + trimWidth ? Trim : Primary;
    }

    public static bool Apply(Transform car, Material primary, Material secondary, CarPaintStyle paint,
        Action<Texture2D> retainTexture)
    {
        if (!paint.HasWrap || retainTexture == null)
            return false;

        bool classic = paint.Scheme == CarPaintScheme.Classic;
        var paintedMeshes = new List<MeshFilter>();
        foreach (var filter in car.GetComponentsInChildren<MeshFilter>())
        {
            if (!filter.TryGetComponent<MeshRenderer>(out var renderer) ||
                !Array.Exists(renderer.sharedMaterials, material => material == secondary || (!classic && material == primary)))
                continue;
            if (filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return false;
            paintedMeshes.Add(filter);
        }
        if (paintedMeshes.Count == 0)
            return false;

        foreach (var filter in paintedMeshes)
            ProjectMesh(car, filter);

        var texture = paint.BuildTexture();
        retainTexture(texture);
        SetPaintTexture(secondary);
        if (!classic)
            SetPaintTexture(primary);
        return true;

        void SetPaintTexture(Material material)
        {
            material.color = Color.white;
            material.SetTexture("_BaseMap", texture);
        }
    }

    private static readonly Dictionary<(Mesh Mesh, Matrix4x4 ToCar), Mesh> ProjectedMeshes = new();

    private static void ProjectMesh(Transform car, MeshFilter filter)
    {
        var toCar = Matrix4x4.identity;
        for (var node = filter.transform; node != car; node = node.parent)
            toCar = Matrix4x4.TRS(node.localPosition, node.localRotation, node.localScale) * toCar;
        var source = filter.sharedMesh;
        var key = (source, toCar);
        if (!ProjectedMeshes.TryGetValue(key, out var mesh) || mesh == null)
        {
            var positions = source.vertices;
            var coordinates = new Vector2[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                coordinates[i] = Project(toCar.MultiplyPoint3x4(positions[i]));
            mesh = Object.Instantiate(source);
            mesh.uv = coordinates;
            mesh.UploadMeshData(true);
            ProjectedMeshes[key] = mesh;
        }
        filter.sharedMesh = mesh;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetProjectedMeshes()
    {
        foreach (var mesh in ProjectedMeshes.Values)
            if (mesh != null)
                Object.Destroy(mesh);
        ProjectedMeshes.Clear();
    }
}
