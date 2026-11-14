using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class CarThumbnails : IDisposable
{
    private const int Layer = 30;
    private readonly Dictionary<string, (string Livery, RenderTexture Image)> images = new();

    public RenderTexture Get(TeamCar car, string[] sponsorNames)
    {
        int number = car.number;
        string key = car.chassisNumber ?? number.ToString();
        var livery = car.Paint;
        string liveryKey = $"{number}|{livery.HoodAndTrunk}|{livery.RoofAndSides}|{string.Join("|", sponsorNames)}";
        if (images.TryGetValue(key, out var cached))
        {
            if (cached.Livery == liveryKey)
                return cached.Image;
            cached.Image.Release();
            UnityEngine.Object.Destroy(cached.Image);
            images.Remove(key);
        }

        var surface = Resources.Load<Material>("Startup/TrackSurface");
        if (surface == null)
            return null;

        var root = new GameObject("Car thumbnail") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = new Vector3(0f, -3000f, 0f);
        var materials = new List<Material>(4);
        var image = new RenderTexture(256, 144, 16) { name = $"Car {number} thumbnail" };
        try
        {
            TrackPreview.BuildCar(root.transform, number, Paint(livery.HoodAndTrunk), Paint(livery.RoofAndSides),
                Paint(new Color32(43, 66, 87, 255)), Paint(new Color32(24, 25, 30, 255)), sponsorNames);
            foreach (var child in root.GetComponentsInChildren<Transform>())
                child.gameObject.layer = Layer;

            var camera = Child("Thumbnail camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(31, 39, 48, 255);
            camera.orthographic = true;
            camera.orthographicSize = 1.24f;
            camera.farClipPlane = 25f;
            camera.transform.localPosition = new Vector3(4f, 3.5f, 5f);
            camera.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 0.5f, 0f) - camera.transform.localPosition);
            var light = Child("Thumbnail light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << Layer;
            light.intensity = 1.25f;
            light.transform.localRotation = Quaternion.Euler(48f, -28f, 0f);
            camera.targetTexture = image;
            camera.Render();
            camera.targetTexture = null;
            images.Add(key, (liveryKey, image));
            return image;
        }
        catch
        {
            image.Release();
            UnityEngine.Object.Destroy(image);
            throw;
        }
        finally
        {
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            foreach (var material in materials)
                UnityEngine.Object.Destroy(material);
        }

        Material Paint(Color32 color)
        {
            var material = new Material(surface) { color = color };
            materials.Add(material);
            return material;
        }

        GameObject Child(string name)
        {
            var child = new GameObject(name) { layer = Layer };
            child.transform.SetParent(root.transform, false);
            return child;
        }
    }

    public void Dispose()
    {
        foreach (var (_, image) in images.Values)
        {
            image.Release();
            UnityEngine.Object.Destroy(image);
        }
        images.Clear();
    }
}