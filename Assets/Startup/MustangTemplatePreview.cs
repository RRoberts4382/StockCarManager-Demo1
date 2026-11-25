using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

// Projects the donor atlas onto the full-detail Cup Mustang while retaining stock detail materials.
internal static class MustangTemplatePreview
{
    public static bool Apply(MeshRenderer renderer, Transform car, Material paint, Action<Object> retainAsset)
    {
        string chart = renderer.name switch
        {
            "HoodPaint" or "Hood Flaps" => "hood",
            "Door Panel" or "Fenders" or "Rear Quarter Panel" or "Rear Quarter Panel Right Pit" or
                "Side Skirts" or "Quarter Panel.001" or "Fender.001" => "side",
            "Front Bumper" or "Front Bumper Aero Ducts" => "front",
            "Rear Bumper" => "rear",
            "Body" or "Greenhouse.036" or "Decklid" or "Hood Louvers.001" => "black",
            _ => null
        };
        if (chart == null || !renderer.TryGetComponent<MeshFilter>(out var filter) ||
            filter.sharedMesh == null || !filter.sharedMesh.isReadable)
            return false;

        var materials = renderer.sharedMaterials;
        var slots = Enumerable.Range(0, materials.Length)
            .Where(i => materials[i]?.name is "BodyPaint" or "HoodTrunkPaint" or "FordMustang_Body" or
                "FordMustang_BodyRight" || renderer.name == "Fender.001")
            .ToArray();
        if (slots.Length == 0)
            return false;

        var source = filter.sharedMesh;
        var mapped = Object.Instantiate(source);
        retainAsset(mapped);
        var vertices = source.vertices;
        var uv = new Vector2[vertices.Length];
        var originalUv = source.uv;
        var toCar = car.worldToLocalMatrix * renderer.localToWorldMatrix;
        for (int i = 0; i < uv.Length; i++)
        {
            bool lamp = chart == "rear" && originalUv.Length == uv.Length && originalUv[i].x > .917f && originalUv[i].y < .26f;
            uv[i] = Project(lamp ? "taillight" : chart, toCar.MultiplyPoint3x4(vertices[i]));
        }
        mapped.uv = uv;
        if (slots.Length == materials.Length)
        {
            filter.sharedMesh = mapped;
            renderer.sharedMaterials = slots.Select(_ => paint).ToArray();
            return true;
        }

        // Keep the original UVs on glass, ducts and other non-paint slots.
        var details = Object.Instantiate(source);
        retainAsset(details);
        var triangles = new List<int>();
        foreach (int slot in slots)
        {
            triangles.AddRange(source.GetTriangles(slot));
            details.SetTriangles(Array.Empty<int>(), slot, false);
        }
        filter.sharedMesh = details;
        mapped.subMeshCount = 1;
        mapped.SetTriangles(triangles, 0, false);
        var layer = new GameObject("Template paint") { layer = renderer.gameObject.layer };
        layer.transform.SetParent(renderer.transform, false);
        layer.AddComponent<MeshFilter>().sharedMesh = mapped;
        var overlay = layer.AddComponent<MeshRenderer>();
        overlay.sharedMaterial = paint;
        overlay.shadowCastingMode = renderer.shadowCastingMode;
        overlay.receiveShadows = renderer.receiveShadows;
        return true;
    }

    public static void AddWindshieldBanner(MeshRenderer windshield, Transform car, Material paint,
        Texture2D bannerTexture, Action<Object> retainAsset)
    {
        if (!windshield.TryGetComponent<MeshFilter>(out var filter) ||
            filter.sharedMesh == null || !filter.sharedMesh.isReadable)
            return;
        int glass = Array.FindIndex(windshield.sharedMaterials, material => material?.name == "FordMustang_Glass");
        if (glass < 0)
            return;

        var banner = new Material(paint);
        retainAsset(banner);
        banner.SetTexture("_BaseMap", bannerTexture);
        banner.SetFloat("_Metallic", 0f);
        banner.SetFloat("_Smoothness", .16f);
        banner.SetFloat("_AlphaClip", 1);
        banner.SetFloat("_Cutoff", .5f);
        banner.SetFloat("_Cull", 0);
        banner.EnableKeyword("_ALPHATEST_ON");
        banner.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        banner.SetOverrideTag("RenderType", "TransparentCutout");

        var source = filter.sharedMesh;
        var mesh = Object.Instantiate(source);
        retainAsset(mesh);
        mesh.subMeshCount = 1;
        mesh.SetTriangles(source.GetTriangles(glass), 0, false);
        var vertices = mesh.vertices;
        var uv = new Vector2[vertices.Length];
        var toCar = car.worldToLocalMatrix * windshield.localToWorldMatrix;
        for (int i = 0; i < uv.Length; i++)
        {
            Vector3 p = toCar.MultiplyPoint3x4(vertices[i]);
            float z = p.z - .22f * p.x * p.x;
            uv[i] = new Vector2((.69f - p.x) / 1.38f, (.53f - z) / .21f);
        }
        mesh.uv = uv;
        var layer = new GameObject("Template windshield banner") { layer = windshield.gameObject.layer };
        layer.transform.SetParent(windshield.transform, false);
        layer.transform.position += car.TransformVector(new Vector3(0, .006f, .006f));
        layer.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = layer.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = banner;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    public static Texture2D PrepareBanner(Texture2D source)
    {
        const int width = 512;
        const int height = 96;
        var result = new Texture2D(width, height, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color32[width * height];
        for (int y = 2; y < height - 2; y++)
        {
            for (int x = 2; x < width - 2; x++)
            {
                float atlasX = Mathf.Lerp(463, 438, y / (height - 1f));
                float atlasY = Mathf.Lerp(536, 366, x / (width - 1f));
                pixels[y * width + x] = source.GetPixelBilinear(atlasX / 768f, 1 - atlasY / 768f);
            }
        }
        result.SetPixels32(pixels);
        result.Apply(true, true);
        return result;
    }

    private static Vector2 Project(string chart, Vector3 p)
    {
        float x;
        float y;
        switch (chart)
        {
            case "hood":
                x = Scale(p.z, .856f, 2.166f, 540, 744);
                y = Scale(p.x, -.814f, .814f, 326, 574);
                break;
            case "front":
                x = Scale(p.x, -.994f, .994f, 346, 26);
                y = Scale(p.y, .101f, .79f, 104, 31);
                break;
            case "rear":
                x = Scale(p.x, -.905f, .905f, 391, 604);
                y = p.y < .88f ? Scale(p.y, .263f, .88f, 108, 59) : Scale(p.y, .88f, .982f, 59, 16);
                break;
            case "taillight":
                x = p.x < 0 ? Scale(p.x, -.770f, -.57f, 269, 319) : Scale(p.x, .57f, .770f, 323, 372);
                y = Scale(p.y, .697f, .870f, 142, 117);
                break;
            case "side":
                x = SideLength(p.z);
                float height = Scale(p.y, .20f, .91f, 0, 1);
                if (p.x > 0)
                {
                    float top = x < 115 ? Scale(x, 10, 115, 617, 608) :
                        x < 230 ? Scale(x, 115, 230, 608, 620) :
                        x < 430 ? Scale(x, 230, 430, 621, 635) : 638;
                    float bottom = x < 230 ? Scale(x, 10, 230, 722, 745) : 751;
                    y = Mathf.Lerp(bottom, top, height);
                }
                else
                {
                    float top = x < 120 ? Scale(x, 10, 120, 288, 305) :
                        x < 250 ? Scale(x, 120, 250, 305, 281) :
                        x < 480 ? Scale(x, 250, 480, 281, 270) : 262;
                    float bottom = x < 230 ? Scale(x, 10, 230, 184, 156) : 155;
                    y = Mathf.Lerp(bottom, top, height);
                }
                break;
            default:
                x = 299;
                y = 425;
                break;
        }
        return new Vector2(x / 768f, 1 - y / 768f);
    }

    private static float SideLength(float z)
    {
        if (z <= -1.347f)
            return Scale(z, -2.17f, -1.347f, 12, 178);
        if (z <= 1.447f)
            return Scale(z, -1.347f, 1.447f, 178, 563);
        return Scale(z, 1.447f, 1.91f, 563, 615);
    }

    private static float Scale(float value, float from, float to, float start, float end) =>
        Mathf.Lerp(start, end, Mathf.InverseLerp(from, to, value));

    public static Texture2D PrepareAtlas(Texture2D source)
    {
        var pixels = source.GetPixels32();
        int size = source.width;
        // Extend printable colors into the donor's background and wheel cutouts.
        // The Mustang wheel openings have different contours from the donor mesh.
        Extend(pixels, size, new RectInt(9, 150, 609, 160), (x, y) =>
            y < 151 + Arch(x, 181, 52, 84) || x > 512 && y < 152 + Arch(x, 576, 59, 95) ||
            x is > 176 and < 291 && y > 287);
        Extend(pixels, size, new RectInt(9, 586, 609, 170), (x, y) =>
            y > 755 - Arch(x, 181, 48, 79) || x > 517 && y > 755 - Arch(x, 576, 61, 91) ||
            x is > 174 and < 302 && y < 622);
        FitHoodArt(pixels, source.GetPixels32(), size);
        Extend(pixels, size, new RectInt(25, 29, 322, 77), (_, _) => false);
        Extend(pixels, size, new RectInt(390, 16, 216, 94), (_, _) => false);

        Extend(pixels, size, new RectInt(268, 117, 105, 26), (_, _) => false);

        var cleaned = new Texture2D(size, size, TextureFormat.RGBA32, true);
        cleaned.SetPixels32(pixels);
        cleaned.wrapMode = TextureWrapMode.Clamp;
        cleaned.filterMode = FilterMode.Trilinear;
        cleaned.Apply(true, true);
        return cleaned;
    }

    private static void FitHoodArt(Color32[] atlas, Color32[] source, int size)
    {
        // The game hood has two raised louver assemblies. Move the lettering behind and
        // between them, and keep the Ford badge on the nose end of the hood.
        int left = 536 * size / 768;
        int right = 746 * size / 768;
        for (int row = 323 * size / 768; row < 576 * size / 768; row++)
        {
            float y = (row + .5f) * 768 / size;
            for (int column = left; column < right; column++)
            {
                float x = (column + .5f) * 768 / size;
                var color = new Color32(3, 3, 3, 255);
                if (x is >= 567 and <= 625 && y is >= 386 and <= 514)
                    color = Sample(Mathf.Lerp(575, 677, (x - 567) / 58), Mathf.Lerp(365, 548, (y - 386) / 128));
                if (x is >= 700 and <= 737 && y is >= 416 and <= 484)
                    color = Sample(Mathf.Lerp(721, 743, (x - 700) / 37), Mathf.Lerp(413, 483, (y - 416) / 68));
                atlas[(size - 1 - row) * size + column] = color;
            }
        }

        Color32 Sample(float x, float y) => source[(size - 1 - (int)(y * size / 768)) * size + (int)(x * size / 768)];
    }

    private static float Arch(float x, float center, float radius, float height)
    {
        float offset = (x - center) / radius;
        return Mathf.Sqrt(Mathf.Max(0, 1 - offset * offset)) * height;
    }

    private static void Extend(Color32[] pixels, int size, RectInt region, Func<float, float, bool> exclude)
    {
        int left = region.xMin * size / 768;
        int top = region.yMin * size / 768;
        int width = region.xMax * size / 768 - left;
        int height = region.yMax * size / 768 - top;
        var pending = new Queue<int>();
        var colors = new int[width * height];
        Array.Fill(colors, -1);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int address = Address(x, y);
                var p = pixels[address];
                bool background = Math.Abs(p.r - 83) <= 9 && Math.Abs(p.g - 71) <= 9 && Math.Abs(p.b - 65) <= 9;
                if (background || exclude((x + left) * 768f / size, (y + top) * 768f / size))
                    continue;
                int index = y * width + x;
                colors[index] = address;
                pending.Enqueue(index);
            }
        }

        while (pending.Count > 0)
        {
            int index = pending.Dequeue();
            int x = index % width;
            int y = index / width;
            if (x > 0) Fill(index - 1, colors[index]);
            if (x + 1 < width) Fill(index + 1, colors[index]);
            if (y > 0) Fill(index - width, colors[index]);
            if (y + 1 < height) Fill(index + width, colors[index]);
        }
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[Address(x, y)] = pixels[colors[y * width + x]];

        int Address(int x, int y) => (size - 1 - top - y) * size + left + x;
        void Fill(int target, int source)
        {
            if (colors[target] >= 0)
                return;
            colors[target] = source;
            pending.Enqueue(target);
        }
    }
}
