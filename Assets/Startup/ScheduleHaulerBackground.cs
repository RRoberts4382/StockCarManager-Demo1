using System;
using UnityEngine;
using Object = UnityEngine.Object;

internal sealed class ScheduleHaulerBackground : IDisposable
{
    private const int BlurPixels = 8;
    private RenderTexture image;
    private (Color32 Primary, Color32 Secondary, int Number, string Sponsor) design;

    public RenderTexture Render(Career career, string sponsor)
    {
        if (career == null)
            return null;
        int number = career.cars?.Count > 0 ? career.cars[0].number : 0;
        var paint = career.TeamColors;
        var next = (paint.Primary, paint.Secondary, number, sponsor);
        if (image != null && design.Equals(next))
            return image;

        var source = Resources.Load<TextAsset>("Startup/ScheduleHauler");
        if (source == null)
            return null;

        var artwork = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            if (!artwork.LoadImage(source.bytes))
                return null;

            int width = artwork.width;
            int height = artwork.height;
            var pixels = artwork.GetPixels32();
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Color32 pixel = pixels[index];
                bool onTrailer = x >= width * 0.085f && x <= width * 0.525f &&
                    y >= height * 0.28f && y <= height * 0.8f && pixel.r > pixel.g;
                bool onCab = x >= width * 0.52f && x <= width * 0.89f &&
                    y >= height * 0.22f && y <= height * 0.8f && pixel.g > 130 && pixel.g > pixel.r;
                if (onTrailer || onCab)
                    pixels[index] = RaceEventView.RecolorArtworkPixel(pixel, paint.Primary, paint.Secondary);
            }

            AddLettering(pixels, width, height, paint.Primary, sponsor, number);
            artwork.SetPixels32(pixels);
            artwork.Apply(false, true);

            Dispose();
            image = new RenderTexture(width, height, 0) { name = "Blurred schedule hauler" };
            var shader = Resources.Load<Shader>("Startup/ShopBackgroundBlur");
            if (shader != null && shader.isSupported)
            {
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    TeamShopPreview.ApplyBackgroundBlur(artwork, image, material, BlurPixels);
                }
                finally
                {
                    Object.Destroy(material);
                }
            }
            else
                Graphics.Blit(artwork, image);
            design = next;
            return image;
        }
        finally
        {
            RenderTexture.active = previous;
            Object.Destroy(artwork);
        }
    }

    private static void AddLettering(Color32[] pixels, int width, int height, Color32 trailer, string sponsor, int number)
    {
        string name = sponsor?.ToUpperInvariant() ?? string.Empty;
        string digits = number > 0 ? number.ToString() : string.Empty;
        if (name.Length + digits.Length == 0)
            return;

        const int fontSize = 96;
        const FontStyle style = FontStyle.Bold;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        font.RequestCharactersInTexture(name + digits, fontSize, style);
        Texture atlas = font.material.mainTexture;
        if (atlas == null)
            return;
        var atlasPixels = ReadAtlas(atlas);
        var color = ((Color)trailer).grayscale > 0.63f ? new Color32(21, 25, 32, 255) : new Color32(255, 255, 255, 255);
        DrawLine(name, 0.54f, 75f);
        DrawLine(digits, name.Length == 0 ? 0.47f : 0.37f, 145f);

        void DrawLine(string value, float baseline, float desiredSize)
        {
            DrawArtworkLettering(pixels, width, height, color, value, 0.305f, baseline, 0.365f,
                desiredSize, 0.085f, font, atlas, atlasPixels);
        }
    }

    internal static void DrawArtworkLettering(Color32[] pixels, int width, int height, Color32 color,
        string value, float centerX, float baseline, float availableWidth, float desiredSize)
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null || string.IsNullOrEmpty(value))
            return;
        font.RequestCharactersInTexture(value, 96, FontStyle.Bold);
        Texture atlas = font.material.mainTexture;
        if (atlas != null)
            DrawArtworkLettering(pixels, width, height, color, value, centerX, baseline, availableWidth,
                desiredSize, 0f, font, atlas, ReadAtlas(atlas));
    }

    private static void DrawArtworkLettering(Color32[] pixels, int width, int height, Color32 color,
        string value, float centerX, float baseline, float availableWidth, float desiredSize, float slope,
        Font font, Texture atlas, Color32[] atlasPixels)
    {
        const int fontSize = 96;
        const FontStyle style = FontStyle.Bold;
        if (value.Length == 0)
            return;
        var glyphs = new CharacterInfo[value.Length];
        int totalAdvance = 0;
        for (int i = 0; i < value.Length; i++)
        {
            font.GetCharacterInfo(value[i], out glyphs[i], fontSize, style);
            totalAdvance += glyphs[i].advance;
        }

        float scale = Mathf.Min(desiredSize * height / (941f * fontSize), width * availableWidth / Mathf.Max(1, totalAdvance));
        float center = width * centerX;
        float pen = center - totalAdvance * scale * 0.5f;
        foreach (var glyph in glyphs)
        {
            if (glyph.glyphWidth > 0 && glyph.glyphHeight > 0)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(pen + glyph.minX * scale));
                int right = Mathf.Min(width, Mathf.CeilToInt(pen + glyph.maxX * scale));
                for (int x = left; x < right; x++)
                {
                    // The photographed trailer's horizontal edges descend toward the cab.
                    float line = height * baseline - (x - center) * slope * height / width * (1672f / 941f);
                    float u = (x + 0.5f - pen - glyph.minX * scale) / (glyph.glyphWidth * scale);
                    int bottom = Mathf.Max(0, Mathf.CeilToInt(line + glyph.minY * scale));
                    int top = Mathf.Min(height, Mathf.CeilToInt(line + glyph.maxY * scale));
                    for (int y = bottom; y < top; y++)
                    {
                        float v = (y + 0.5f - line - glyph.minY * scale) / (glyph.glyphHeight * scale);
                        Vector2 uv = Vector2.Lerp(
                            Vector2.Lerp(glyph.uvBottomLeft, glyph.uvBottomRight, u),
                            Vector2.Lerp(glyph.uvTopLeft, glyph.uvTopRight, u), v);
                        int ax = Mathf.Clamp((int)(uv.x * atlas.width), 0, atlas.width - 1);
                        int ay = Mathf.Clamp((int)(uv.y * atlas.height), 0, atlas.height - 1);
                        int index = y * width + x;
                        pixels[index] = Color32.Lerp(pixels[index], color, atlasPixels[ay * atlas.width + ax].a / 255f);
                    }
                }
            }
            pen += glyph.advance * scale;
        }
    }

    private static Color32[] ReadAtlas(Texture atlas)
    {
        var copy = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var readable = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(atlas, copy);
            RenderTexture.active = copy;
            readable.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0, false);
            return readable.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(copy);
            Object.Destroy(readable);
        }
    }

    public void Dispose()
    {
        if (image == null)
            return;
        image.Release();
        Object.Destroy(image);
        image = null;
    }
}
