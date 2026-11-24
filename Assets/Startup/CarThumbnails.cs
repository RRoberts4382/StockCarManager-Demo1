using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class CarThumbnails : IDisposable
{
    private const int Layer = 30;
    internal static readonly Color32 BackgroundColor = new Color32(31, 39, 48, 255);
    private readonly int width;
    private readonly bool highQuality;
    private readonly bool tighterOrbit;
    private readonly Dictionary<string, (string Livery, RenderTexture Image)> images = new();
    private Camera orbitCamera;
    private RenderTexture orbitImage;
    private Action releaseOrbitScene;
    private float yaw = 55f;
    private float pitch = 16f;

    public CarThumbnails(int width = 256) : this(width, highQuality: false) { }

    public static CarThumbnails CreateLargePreview(bool tighterOrbit = false) =>
        new CarThumbnails(2560, highQuality: true, tighterOrbit: tighterOrbit);

    private CarThumbnails(int width, bool highQuality, bool tighterOrbit = false)
    {
        this.width = width;
        this.highQuality = highQuality;
        this.tighterOrbit = tighterOrbit;
    }

    public RenderTexture Get(TeamCar car, string[] sponsorNames, CarPaintStyle? preview = null, bool overhead = false, string cacheScope = null,
        bool transparentBackground = false, bool retainOrbitScene = true, int? displayNumber = null, TrackDefinition track = null, string displayNumberText = null)
    {
        int number = displayNumber ?? car.number;
        string numberText = displayNumberText ?? car.rivalRaceNumberText;
        string key = car.chassisNumber ?? number.ToString();
        if (cacheScope != null)
            key = cacheScope + "|" + key;
        if (overhead)
            key += ":overhead";
        if (transparentBackground)
            key += ":transparent";
        string highestModel = track?.CarModelForTrack(car.modelResource) ??
            CarTypeDefinition.ForModel(car.modelResource).ModelForLod(car.modelResource, 5);
        var livery = preview ?? car.PaintStyle;
        string liveryKey = $"{car.modelResource}|{highestModel}|{number}|{numberText}|{livery.Scheme}|{livery.Primary}|{livery.Secondary}|{livery.Trim}|{livery.HasTrim}|{livery.NumberColor}|{livery.NumberFont}|{livery.NumberBold}|{livery.NumberItalic}|{livery.HideRenderedNumber}|{livery.Template.asset}|{string.Join("|", sponsorNames)}";
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
        // Keep the portrait preview's short edge at the landscape preview's height.
        int renderWidth = overhead && highQuality ? width * 9 / 16 : width;
        int height = overhead ? renderWidth * 3 / 2 : renderWidth * 9 / 16;
        var image = new RenderTexture(renderWidth, height, highQuality ? 0 : 16);
        RenderTexture renderTarget = image;
        image.name = $"Car {number} thumbnail";
        bool keepScene = false;
        try
        {
            if (highQuality)
            {
                // Resolve into a single-sample texture so UI Toolkit can use mipmaps when scaling it.
                image.useMipMap = true;
                image.autoGenerateMips = false;
                image.filterMode = FilterMode.Trilinear;
                var descriptor = new RenderTextureDescriptor(renderWidth, height, image.graphicsFormat, 24)
                {
                    msaaSamples = 8
                };
                descriptor.msaaSamples = Mathf.Max(1, SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor));
                renderTarget = new RenderTexture(descriptor) { name = image.name + " render target" };
            }

            TrackPreview.BuildCar(root.transform, number, Paint(livery.Primary), Paint(livery.Secondary),
                Paint(new Color32(43, 66, 87, 255)), Paint(new Color32(24, 25, 30, 255)), sponsorNames, livery, paintTextures.Add,
                car.modelResource, visualModelResource: highestModel, numberText: numberText);
            foreach (var child in root.GetComponentsInChildren<Transform>())
                child.gameObject.layer = Layer;

            var camera = Child("Thumbnail camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = transparentBackground ? Color.clear : (Color)BackgroundColor;
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
            camera.allowMSAA = renderTarget.antiAliasing > 1;
            camera.targetTexture = renderTarget;
            Render(camera, image);
            images.Add(key, (liveryKey, image));
            if (preview.HasValue && !overhead && retainOrbitScene)
            {
                keepScene = true;
                orbitCamera = camera;
                orbitImage = image;
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
            if (renderTarget != image)
            {
                renderTarget.Release();
                UnityEngine.Object.Destroy(renderTarget);
            }
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
        var preview = new Image { scaleMode = ScaleMode.ScaleToFit, tooltip = "Left-drag horizontally to rotate the car; drag vertically to change the viewing angle." };
        preview.AddToClassList("car-paint-preview");
        preview.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;
            preview.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        });
        preview.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!preview.HasPointerCapture(evt.pointerId) || (evt.pressedButtons & 1) == 0)
                return;
            RotatePreview(evt.deltaPosition);
            preview.MarkDirtyRepaint();
            evt.StopPropagation();
        });
        preview.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button != 0 || !preview.HasPointerCapture(evt.pointerId))
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
            Render(orbitCamera, orbitImage);
        }
        finally
        {
            scene.SetActive(false);
        }
    }

    private static void Render(Camera camera, RenderTexture image)
    {
        camera.Render();
        var target = camera.targetTexture;
        if (target == image)
            return;
        if (!image.IsCreated())
            image.Create();
        if (target.antiAliasing > 1)
            target.ResolveAntiAliasedSurface(image);
        else
            Graphics.Blit(target, image);
        image.GenerateMips();
    }

    private void PositionOrbitCamera(Camera camera)
    {
        if (tighterOrbit)
        {
            // Keep the elevated view wide enough for the car's full footprint as it rotates.
            float angle = pitch * Mathf.Deg2Rad;
            camera.orthographicSize = Mathf.Max(1.02f, 1.64f * Mathf.Sin(angle) + .50f * Mathf.Cos(angle) + .08f);
        }
        var target = new Vector3(0f, 0.5f, 0f);
        camera.transform.localPosition = target + Quaternion.Euler(-pitch, yaw, 0f) * (Vector3.forward * 6.3f);
        camera.transform.localRotation = Quaternion.LookRotation(target - camera.transform.localPosition);
    }

    private void ReleaseOrbitScene()
    {
        if (orbitCamera != null)
            orbitCamera.targetTexture = null;
        orbitCamera = null;
        orbitImage = null;
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
