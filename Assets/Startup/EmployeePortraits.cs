using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class EmployeePortraits : IDisposable
{
    private const int Columns = 4;
    private const int Rows = 3;
    private const int Size = 144;

    private readonly (Color32[] Pixels, int Width, int Height)[] atlases =
        new (Color32[], int, int)[2];
    private readonly Dictionary<(int Atlas, int Tile, bool Mirror, Color32 Fabric, Color32 Trim), Texture2D> portraits =
        new Dictionary<(int, int, bool, Color32, Color32), Texture2D>();

    public Texture2D Get(Employee employee, Color32 fabric, Color32 trim)
    {
        uint identity = 2166136261;
        string description = $"{employee.FullName}|{employee.age}|{employee.gender}|{employee.hometownCity}|{employee.hometownState}";
        foreach (char character in description)
            identity = unchecked((identity ^ character) * 16777619);

        int atlasIndex = (int)(identity % (uint)atlases.Length);
        bool mirror = (identity & 2) != 0;
        int row = employee.gender == "Woman" ? 0 : employee.gender == "Man" ? 1 : 2;
        int column = employee.age < 29 ? 0 : employee.age < 40 ? 1 : employee.age < 54 ? 2 : 3;
        var key = (atlasIndex, row * Columns + column, mirror, fabric, trim);
        if (portraits.TryGetValue(key, out var portrait))
            return portrait;

        var atlas = atlases[atlasIndex];
        if (atlas.Pixels == null)
        {
            var asset = Resources.Load<TextAsset>($"Startup/EmployeePortraitAtlas{atlasIndex + 1}");
            if (asset == null)
                return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(asset.bytes))
                atlases[atlasIndex] = atlas = (texture.GetPixels32(), texture.width, texture.height);
            UnityEngine.Object.Destroy(texture);
            if (atlas.Pixels == null)
                return null;
        }

        var pixels = new Color32[Size * Size];
        int tileWidth = atlas.Width / Columns;
        int tileHeight = atlas.Height / Rows;
        int originX = column * tileWidth;
        int originY = (Rows - row - 1) * tileHeight;
        for (int y = 0; y < Size; y++)
        {
            int sourceY = originY + (2 * y + 1) * tileHeight / (2 * Size);
            for (int x = 0; x < Size; x++)
            {
                int sampleX = mirror ? Size - x - 1 : x;
                int sourceX = originX + (2 * sampleX + 1) * tileWidth / (2 * Size);
                Color32 color = atlas.Pixels[sourceY * atlas.Width + sourceX];
                pixels[y * Size + x] = y < Size * 0.34f ? TintUniform(color, fabric, trim) : color;
            }
        }

        portrait = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "Employee portrait",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        portrait.SetPixels32(pixels);
        portrait.Apply(false, true);
        portraits.Add(key, portrait);
        return portrait;
    }

    private static Color32 TintUniform(Color32 pixel, Color32 fabric, Color32 trim)
    {
        int cyan = Math.Min(pixel.g, pixel.b) - pixel.r;
        int magenta = Math.Min(pixel.r, pixel.b) - pixel.g;
        int strength = Math.Max(cyan, magenta);
        if (strength < 18)
            return pixel;

        Color teamColor = cyan > magenta ? fabric : trim;
        float light = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) / 255f;
        Color shaded = light <= 0.85f
            ? teamColor * (light / 0.85f)
            : Color.Lerp(teamColor, Color.white, (light - 0.85f) * 1.5f);
        shaded.a = 1f;
        return Color.Lerp(pixel, shaded, Mathf.InverseLerp(18f, 60f, strength));
    }

    public void Dispose()
    {
        foreach (var portrait in portraits.Values)
            UnityEngine.Object.Destroy(portrait);
        portraits.Clear();
    }
}
