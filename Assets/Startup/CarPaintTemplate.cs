using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using Object = UnityEngine.Object;

[Serializable]
internal struct CarPaintTemplateAssignment
{
    public string asset;
    public string fileName;

    internal bool IsAssigned => !string.IsNullOrEmpty(asset);
}

internal sealed class CarPaintTemplate : IDisposable
{
    private const long MaximumFileBytes = 80L * 1024 * 1024;
    private static readonly Dictionary<string, CarPaintTemplate> ActiveTemplates = new();
    private static readonly object ImportLock = new();
    private string assignedAsset;
    private int users;
    public readonly Texture2D Texture;
    private Texture2D fordAtlas;
    private Texture2D fordBanner;
    public bool UsesFordAtlas { get; }

    private CarPaintTemplate(Texture2D texture, bool tga)
    {
        Texture = texture;
        UsesFordAtlas = tga && texture.width == 2048 && texture.height == 2048;
    }

    public Texture2D FordAtlas => fordAtlas ??= MustangTemplatePreview.PrepareAtlas(Texture);

    public Texture2D FordBanner => fordBanner ??= MustangTemplatePreview.PrepareBanner(Texture);

    public static CarPaintTemplate Load(string path) => Decode(ReadFile(path), Path.GetFileName(path));

    internal static CarPaintTemplateAssignment Import(string path)
    {
        string name = Path.GetFileName(path);
        string extension = ImageExtension(name);
        byte[] data = ReadFile(path);
        using var verified = Decode(data, name);
        using var sha = SHA256.Create();
        string asset = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant() + extension;
        string storePath = AssetPath(asset);
        lock (ImportLock)
        {
            if (!File.Exists(storePath))
                SeriesSettingsStore.AtomicWrite(storePath, temp => File.WriteAllBytes(temp, data));
        }
        return new CarPaintTemplateAssignment { asset = asset, fileName = name };
    }

    internal static CarPaintTemplate AcquireAssigned(CarPaintTemplateAssignment assignment)
    {
        if (!assignment.IsAssigned)
            return null;
        if (ActiveTemplates.TryGetValue(assignment.asset, out var existing))
        {
            existing.users++;
            return existing;
        }

        try
        {
            var template = Load(AssetPath(assignment.asset));
            template.assignedAsset = assignment.asset;
            template.users = 1;
            ActiveTemplates.Add(assignment.asset, template);
            return template;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // An unavailable library file can be cleared or replaced in Paint Scheme.
            return null;
        }
    }

    internal void ReleaseAssigned()
    {
        if (--users != 0)
            return;
        ActiveTemplates.Remove(assignedAsset);
        Dispose();
    }

    private static string AssetPath(string asset)
    {
        string extension = ImageExtension(asset);
        if (asset.Length != 64 + extension.Length)
            throw new ArgumentException("Invalid paint template reference.");
        for (int i = 0; i < 64; i++)
            if (asset[i] is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                throw new ArgumentException("Invalid paint template reference.");
        if (!asset.EndsWith(extension, StringComparison.Ordinal))
            throw new ArgumentException("Invalid paint template reference.");
        return Path.Combine(Application.persistentDataPath, "PaintTemplates", asset);
    }

    private static string ImageExtension(string name)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension is not (".tga" or ".png" or ".jpg" or ".jpeg"))
            throw new ArgumentException("Choose a TGA, PNG, or JPEG paint template.");
        return extension;
    }

    private static byte[] ReadFile(string path)
    {
        if (new FileInfo(path).Length > MaximumFileBytes)
            throw new ArgumentException("Choose a paint template smaller than 80 MB.");
        return File.ReadAllBytes(path);
    }

    private static CarPaintTemplate Decode(byte[] data, string name)
    {
        bool tga = ImageExtension(name) == ".tga";
        var texture = tga ? DecodeTga(data) : new Texture2D(2, 2, TextureFormat.RGBA32, true);
        try
        {
            if (!tga && !texture.LoadImage(data, markNonReadable: true))
                throw new ArgumentException("The paint template image could not be read.");
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            return new CarPaintTemplate(texture, tga);
        }
        catch
        {
            Object.Destroy(texture);
            throw;
        }
    }

    public void Dispose()
    {
        Object.Destroy(Texture);
        if (fordAtlas != null)
            Object.Destroy(fordAtlas);
        if (fordBanner != null)
            Object.Destroy(fordBanner);
    }

    private static Texture2D DecodeTga(byte[] data)
    {
        if (data.Length < 18 || data[1] != 0 || data[2] is not (2 or 10) || data[16] is not (24 or 32) ||
            (data[17] & 0xc0) != 0)
            throw new ArgumentException("Choose an RGB or RGBA TGA paint template.");

        int width = data[12] | data[13] << 8;
        int height = data[14] | data[15] << 8;
        if (width < 1 || height < 1 || width > 4096 || height > 4096)
            throw new ArgumentException("TGA paint templates must be between 1 and 4096 pixels on each side.");

        int offset = 18 + data[0];
        int stride = data[16] / 8;
        bool compressed = data[2] == 10;
        bool fromRight = (data[17] & 0x10) != 0;
        bool fromTop = (data[17] & 0x20) != 0;
        var pixels = new Color32[width * height];
        int index = 0;
        while (index < pixels.Length)
        {
            if (offset >= data.Length)
                throw new ArgumentException("The TGA paint template is incomplete.");
            int packet = compressed ? data[offset++] : 0;
            int count = (packet & 127) + 1;
            bool repeated = (packet & 128) != 0;
            int sourceCount = repeated ? 1 : count;
            if (count > pixels.Length - index || sourceCount * stride > data.Length - offset)
                throw new ArgumentException("The TGA paint template has invalid pixel data.");

            for (int i = 0; i < count; i++, index++)
            {
                int byteIndex = offset + (repeated ? 0 : i * stride);
                int x = index % width;
                int y = index / width;
                if (fromRight) x = width - 1 - x;
                if (fromTop) y = height - 1 - y;
                pixels[y * width + x] = new Color32(data[byteIndex + 2], data[byteIndex + 1], data[byteIndex], 255);
            }
            offset += sourceCount * stride;
        }

        var result = new Texture2D(width, height, TextureFormat.RGBA32, true);
        result.SetPixels32(pixels);
        result.Apply(true);
        return result;
    }
}
