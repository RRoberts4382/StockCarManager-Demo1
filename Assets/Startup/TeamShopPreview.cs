using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

internal sealed class TeamShopPreview : IDisposable
{
    private const int Layer = 30;
    private const int Width = 1800;
    private const int Height = 900;
    private const float ParkedCarScale = 2f;
    private RenderTexture image;
    private RenderTexture blurredImage;
    private Material blurMaterial;

    public RenderTexture BackgroundAtBlur(int radius)
    {
        if (radius <= 0)
            return image;

        Texture source = image;
        if (source == null)
            source = Resources.Load<Texture2D>("Startup/RaceTeamShop");
        if (source == null)
            return image;

        if (blurMaterial == null)
        {
            var shader = Resources.Load<Shader>("Startup/ShopBackgroundBlur");
            if (shader == null || !shader.isSupported)
                return image;
            blurMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (blurredImage == null)
            blurredImage = new RenderTexture(source.width, source.height, 0) { name = "Blurred shop background" };

        ApplyBackgroundBlur(source, blurredImage, blurMaterial, radius);
        return blurredImage;
    }

    internal static void ApplyBackgroundBlur(Texture source, RenderTexture target, Material material, int radius)
    {
        RenderTexture previous = RenderTexture.active;
        var horizontal = RenderTexture.GetTemporary(source.width, source.height, 0);
        try
        {
            horizontal.filterMode = FilterMode.Bilinear;
            material.SetVector("_BlurStep", new Vector4(radius / (4f * source.width), 0f, 0f, 0f));
            Graphics.Blit(source, horizontal, material);
            material.SetVector("_BlurStep", new Vector4(0f, radius / (4f * source.height), 0f, 0f));
            Graphics.Blit(horizontal, target, material);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(horizontal);
        }
    }

    public RenderTexture Render(Career career, string[] sponsorNames)
    {
        var asset = Resources.Load<GameObject>("Startup/RaceTeamShop");
        var surface = Resources.Load<Material>("Startup/TrackSurface");
        if (asset == null || surface == null || career == null)
            return null;

        var root = new GameObject("Team shop preview") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = new Vector3(0f, -3000f, 0f);
        var materials = new List<Material>();
        var paintTextures = new List<Texture2D>();
        var next = new RenderTexture(Width, Height, 24) { name = "Team shop background", antiAliasing = 4 };
        try
        {
            var shop = Object.Instantiate(asset, root.transform, false);
            var nodes = shop.GetComponentsInChildren<Transform>(true);
            Transform Find(string name) => Array.Find(nodes, node => node.name == name);
            var position = Find("ShopCameraPosition");
            var target = Find("ShopCameraTarget");
            var up = Find("ShopCameraUp");
            if (position == null || target == null || up == null)
                return null;

            int count = Mathf.Min(career.cars.Count, TeamCar.MaximumOwned);
            var team = career.TeamColors;
            var shopMaterials = new Dictionary<Material, Material>();
            foreach (var renderer in shop.GetComponentsInChildren<MeshRenderer>())
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    var source = slots[i];
                    if (source == null)
                        continue;
                    string name = source.name;
                    if (!shopMaterials.TryGetValue(source, out var material))
                    {
                        material = Paint(source.color);
                        material.name = name;
                        material.SetFloat("_Smoothness", name == "Polished light grey epoxy" ? 0.55f : 0.2f);
                        shopMaterials.Add(source, material);
                    }
                    slots[i] = material;
                }
                renderer.sharedMaterials = slots;
                for (int i = 0; i < slots.Length; i++)
                {
                    string name = slots[i]?.name;
                    if (name == "Team Toolbox Primary" || name == "Team Banner Primary")
                        Tint(renderer, i, team.Primary);
                    else if (name == "Team Shop Cabinet Secondary" || name == "Team Banner Secondary")
                        Tint(renderer, i, team.Secondary);
                }
            }

            var banner = Find("ShopBannerText");
            var facing = Find("ShopBannerFacing");
            if (banner != null && facing != null)
            {
                var title = Child("Team name");
                title.transform.SetPositionAndRotation(banner.position,
                    Quaternion.LookRotation(banner.position - facing.position, Vector3.up));
                var text = title.AddComponent<TextMesh>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.text = career.teamName;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.fontStyle = FontStyle.Bold;
                text.fontSize = 100;
                text.characterSize = 0.12f;
                var baseColor = ((Color)team.Primary).linear;
                float luminance = 0.2126f * baseColor.r + 0.7152f * baseColor.g + 0.0722f * baseColor.b;
                text.color = luminance > 0.32f ? Color.black : Color.white;
                var mesh = title.GetComponent<MeshRenderer>();
                mesh.sharedMaterial = text.font.material;
                float textWidth = mesh.localBounds.size.x;
                if (textWidth > 8f)
                    title.transform.localScale = Vector3.one * (8f / textWidth);
            }

            var camera = Child("Shop camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(position.position,
                Quaternion.LookRotation(target.position - position.position, up.position - position.position));
            camera.fieldOfView = 2f * Mathf.Atan(36f / (2f * 25f * (Width / (float)Height))) * Mathf.Rad2Deg;
            camera.aspect = Width / (float)Height;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(39, 47, 55, 255);
            camera.cullingMask = 1 << Layer;
            camera.farClipPlane = 200f;
            var stalls = Enumerable.Range(1, TeamCar.MaximumOwned)
                .Select(number => Find($"CarStall_{number:00}"))
                .Where(stall => stall != null)
                .OrderBy(stall => Mathf.Abs(camera.WorldToViewportPoint(stall.position).x - 0.5f))
                .ToArray();
            var glass = Paint(new Color32(43, 66, 87, 255));
            var rubber = Paint(new Color32(24, 25, 30, 255));
            for (int i = 0; i < Mathf.Min(count, stalls.Length); i++)
            {
                var car = career.cars[i];
                var livery = car.PaintStyle;
                var model = Child($"Car {car.number}");
                model.transform.position = stalls[i].position + Vector3.up * 0.022f;
                TrackPreview.BuildCar(model.transform, car.number, Paint(livery.Primary),
                    Paint(livery.Secondary), glass, rubber, sponsorNames, livery, paintTextures.Add, car.modelResource);
                model.transform.localScale = Vector3.one * ParkedCarScale;
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }

            AddLight("Shop overhead light", new Vector3(56f, -25f, 5f), 1.45f);
            AddLight("Shop aisle fill", new Vector3(25f, 150f, 0f), 0.45f);
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = Layer;

            camera.targetTexture = next;
            camera.Render();
            camera.targetTexture = null;
            Dispose();
            image = next;
            return image;
        }
        finally
        {
            if (image != next)
            {
                next.Release();
                Object.Destroy(next);
            }
            root.SetActive(false);
            Object.Destroy(root);
            foreach (var material in materials)
                Object.Destroy(material);
            foreach (var texture in paintTextures)
                Object.Destroy(texture);
        }

        GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            return child;
        }

        Material Paint(Color32 color)
        {
            var material = new Material(surface) { color = color };
            materials.Add(material);
            return material;
        }

        void AddLight(string name, Vector3 angles, float intensity)
        {
            var light = Child(name).AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << Layer;
            light.intensity = intensity;
            light.transform.localRotation = Quaternion.Euler(angles);
        }
    }

    private static void Tint(Renderer renderer, int slot, Color color)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block, slot);
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block, slot);
    }

    public void Dispose()
    {
        Release(image);
        Release(blurredImage);
        if (blurMaterial != null)
            Object.Destroy(blurMaterial);
        image = null;
        blurredImage = null;
        blurMaterial = null;

        static void Release(RenderTexture texture)
        {
            if (texture == null)
                return;
            texture.Release();
            Object.Destroy(texture);
        }
    }
}
