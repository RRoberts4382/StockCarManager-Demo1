using System;
using UnityEngine;
using Object = UnityEngine.Object;

internal sealed class TeamShopExteriorBackground : IDisposable
{
    private const int BlurPixels = 8;
    private RenderTexture image;
    private (string Name, Color32 Primary, Color32 Secondary) design;

    public RenderTexture Render(Career career)
    {
        if (career == null)
            return null;

        var paint = career.TeamColors;
        var next = (career.teamName, paint.Primary, paint.Secondary);
        if (image != null && design.Equals(next))
            return image;

        var source = Resources.Load<TextAsset>("Startup/TeamShopExterior");
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
            for (int y = Mathf.RoundToInt(height * 0.40f); y < height * 0.75f; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    Color32 pixel = pixels[index];
                    if (pixel.r > pixel.g && pixel.b > pixel.g)
                    {
                        pixels[index] = RaceEventView.RecolorArtworkPixel(pixel, paint.Primary, paint.Secondary);
                    }
                    else if (x >= width * 0.55f && x <= width * 0.64f && y <= height * 0.55f)
                    {
                        // Limit the cyan key to the door surround so reflections and sky stay natural.
                        float strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(45f, 120f, Mathf.Min(pixel.g, pixel.b) - pixel.r));
                        pixels[index] = Color32.Lerp(pixel, paint.Secondary, strength);
                    }
                }
            }

            ScheduleHaulerBackground.DrawArtworkLettering(pixels, width, height,
                new Color32(27, 33, 40, 255), career.teamName, 0.503f, 0.243f, 0.46f, 93f);
            artwork.SetPixels32(pixels);
            artwork.Apply(false, true);

            Dispose();
            image = new RenderTexture(width, height, 0) { name = "Blurred team shop exterior" };
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

    public void Dispose()
    {
        if (image == null)
            return;
        image.Release();
        Object.Destroy(image);
        image = null;
    }
}
