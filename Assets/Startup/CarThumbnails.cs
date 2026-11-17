using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class CarThumbnails : IDisposable
{
    private const int Layer = 30;
    internal static readonly Color32 BackgroundColor = new Color32(31, 39, 48, 255);
    private readonly int width;
    private readonly Dictionary<string, (string Livery, RenderTexture Image)> images = new();
    private Camera orbitCamera;
    private Action releaseOrbitScene;
    private float yaw = 55f;
    private float pitch = 16f;

    public CarThumbnails(int width = 256) => this.width = width;

    public RenderTexture Get(TeamCar car, string[] sponsorNames, CarPaintStyle? preview = null, bool overhead = false)
    {
        int number = car.number;
        string key = car.chassisNumber ?? number.ToString();
        if (overhead)
            key += ":overhead";
        var livery = preview ?? car.PaintStyle;
        string liveryKey = $"{number}|{livery.Scheme}|{livery.Primary}|{livery.Secondary}|{livery.Trim}|{livery.HasTrim}|{string.Join("|", sponsorNames)}";
        if (images.TryGetValue(key, out var cached))
        {
            if (cached.Livery == liveryKey)
                return cached.Image;
            if (preview.HasValue)
                ReleaseOrbitScene();
            cached.Image.Release();
            UnityEngine.Object.Destroy(cached.Image);
            images.Remove(key);
        }

        var surface = Resources.Load<Material>("Startup/TrackSurface");
        if (surface == null)
            return null;

        var root = new GameObject("Car thumbnail") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = new Vector3(0f, preview.HasValue ? -4000f : -3000f, 0f);
        var materials = new List<Material>(4);
        var paintTextures = new List<Texture2D>();
        var image = overhead ? new RenderTexture(width, width * 3 / 2, 16) : new RenderTexture(width, width * 9 / 16, 16);
        image.name = $"Car {number} thumbnail";
        bool keepScene = false;
        try
        {
            TrackPreview.BuildCar(root.transform, number, Paint(livery.Primary), Paint(livery.Secondary),
                Paint(new Color32(43, 66, 87, 255)), Paint(new Color32(24, 25, 30, 255)), sponsorNames, livery, paintTextures.Add);
            foreach (var child in root.GetComponentsInChildren<Transform>())
                child.gameObject.layer = Layer;

            var camera = Child("Thumbnail camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.orthographic = true;
            camera.orthographicSize = 1.24f;
            camera.farClipPlane = 25f;
            if (overhead)
            {
                camera.orthographicSize = 1.68f;
                camera.transform.localPosition = Vector3.up * 6f;
                camera.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            }
            else if (preview.HasValue)
                PositionOrbitCamera(camera);
            else
            {
                camera.transform.localPosition = new Vector3(4f, 3.5f, 5f);
                camera.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 0.5f, 0f) - camera.transform.localPosition);
            }
            var light = Child("Thumbnail light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << Layer;
            light.intensity = 1.25f;
            light.transform.localRotation = Quaternion.Euler(48f, -28f, 0f);
            camera.targetTexture = image;
            camera.Render();
            images.Add(key, (liveryKey, image));
            if (preview.HasValue && !overhead)
            {
                keepScene = true;
                orbitCamera = camera;
                releaseOrbitScene = DestroyScene;
            }
            else
                camera.targetTexture = null;
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
            if (!keepScene)
                DestroyScene();
        }

        void DestroyScene()
        {
            UnityEngine.Object.Destroy(root);
            foreach (var material in materials)
                UnityEngine.Object.Destroy(material);
            foreach (var texture in paintTextures)
                UnityEngine.Object.Destroy(texture);
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

    public Image CreateOrbitPreview()
    {
        var preview = new Image { scaleMode = ScaleMode.ScaleToFit, tooltip = "Right-drag to rotate the car" };
        preview.AddToClassList("car-paint-preview");
        preview.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 1)
                return;
            preview.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        });
        preview.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!preview.HasPointerCapture(evt.pointerId) || (evt.pressedButtons & 2) == 0)
                return;
            RotatePreview(evt.deltaPosition);
            preview.MarkDirtyRepaint();
            evt.StopPropagation();
        });
        preview.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button != 1 || !preview.HasPointerCapture(evt.pointerId))
                return;
            preview.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        });
        return preview;
    }

    private void RotatePreview(Vector2 drag)
    {
        if (orbitCamera == null)
            return;

        yaw = Mathf.Repeat(yaw + drag.x * 0.6f, 360f);
        pitch = Mathf.Clamp(pitch - drag.y * 0.45f, 4f, 80f);
        PositionOrbitCamera(orbitCamera);
        var scene = orbitCamera.transform.parent.gameObject;
        scene.SetActive(true);
        try
        {
            orbitCamera.Render();
        }
        finally
        {
            scene.SetActive(false);
        }
    }

    private void PositionOrbitCamera(Camera camera)
    {
        var target = new Vector3(0f, 0.5f, 0f);
        camera.transform.localPosition = target + Quaternion.Euler(-pitch, yaw, 0f) * (Vector3.forward * 6.3f);
        camera.transform.localRotation = Quaternion.LookRotation(target - camera.transform.localPosition);
    }

    private void ReleaseOrbitScene()
    {
        if (orbitCamera != null)
            orbitCamera.targetTexture = null;
        orbitCamera = null;
        releaseOrbitScene?.Invoke();
        releaseOrbitScene = null;
    }

    public void Dispose()
    {
        ReleaseOrbitScene();
        yaw = 55f;
        pitch = 16f;
        foreach (var (_, image) in images.Values)
        {
            image.Release();
            UnityEngine.Object.Destroy(image);
        }
        images.Clear();
    }
}
