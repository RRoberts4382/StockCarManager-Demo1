using UnityEngine;
using UnityEngine.Rendering;
using Part = IowaPitWorkMesh.Part;

public sealed partial class TrackPreview
{
    private Material iowaResponseVehiclePaint;
    private static readonly Color32 AmbulanceWarningLensColor = new Color32(226, 44, 54, 255);
    private static readonly Color32 TowWarningLensColor = new Color32(255, 179, 40, 255);
    private const float IowaAmbulanceLightBarY = 2.65f;
    private const float IowaAmbulanceLightBarZ = .60f;
    private const float IowaTowLightBarY = 2.23f;
    private const float IowaTowLightBarZ = 1.13f;
    private Material[,] responseWarningMaterials;
    private int responseWarningBank = -1;
    private double responseWarningStarted;

    private void AddIowaResponseVehicles(Vector3 ambulancePosition, Vector3 truckPosition,
        Vector3 facing, int gateIndex)
    {
        Quaternion rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(facing, Vector3.up), Vector3.up);
        if (iowaResponseVehiclePaint == null)
            iowaResponseVehiclePaint = CreateIowaResponseVehiclePaint();

        var ambulance = new IowaPitWorkMesh();
        BuildIowaAmbulance(ambulance);
        AddVehicle(ambulance, ambulancePosition, $"Iowa gate {gateIndex} ambulance", 0);

        var truck = new IowaPitWorkMesh();
        BuildIowaTowTruck(truck);
        AddVehicle(truck, truckPosition, $"Iowa gate {gateIndex} tow truck", 1);

        void AddVehicle(IowaPitWorkMesh shape, Vector3 position, string name, int paint)
        {
            var vehicle = CreateObject(name, transform);
            vehicle.transform.SetLocalPositionAndRotation(position, rotation);
            Mesh combined = shape.Build(name, paint, 2);
            roadMeshes.Add(combined);
            vehicle.AddComponent<MeshFilter>().sharedMesh = combined;
            vehicle.AddComponent<MeshRenderer>().sharedMaterial = iowaResponseVehiclePaint;
            AddIowaResponseWarningLamps(vehicle.transform, ambulance: paint == 0);
            RegisterIowaResponseVehicle(vehicle.transform, gateIndex, ambulance: paint == 0);
        }
    }

    private static void BuildIowaAmbulance(IowaPitWorkMesh shape)
    {
        IowaResponseBox(shape, 0f, .66f, -.08f, 1.75f, .25f, 5.45f, Part.DarkMetal);
        IowaResponseAxle(shape, -1.96f, 1.12f, .40f);
        IowaResponseAxle(shape, 1.97f, 1.12f, .40f);
        AddIowaResponseCab(shape);

        // Box-body ambulance with independent medical compartment and rear doors.
        IowaResponseBox(shape, 0f, 1.67f, -1.16f, 2.11f, 1.66f, 3.76f, Part.Primary);
        IowaResponseBox(shape, 0f, 2.53f, -1.16f, 2.04f, .12f, 3.72f, Part.Primary);
        IowaResponseBox(shape, 0f, 1.12f, -1.16f, 2.129f, .21f, 3.77f, Part.Accent);
        IowaResponseBox(shape, 0f, .61f, -2.995f, 2.15f, .17f, .23f, Part.DarkMetal);
        IowaResponseBox(shape, 0f, .54f, -3.045f, 1.75f, .079f, .20f, Part.Metal);

        foreach (float side in new[] { -1f, 1f })
        {
            IowaResponseBox(shape, side * 1.060f, 1.88f, -.65f, .014f, .38f, .63f, Part.Visor);
            IowaResponseBox(shape, side * 1.064f, 1.73f, -2.02f, .017f, .51f, .51f, Part.Accent);
            IowaResponseBox(shape, side * 1.075f, 1.73f, -2.02f, .007f, .099f, .36f, Part.Light);
            IowaResponseBox(shape, side * 1.076f, 1.73f, -2.02f, .007f, .36f, .099f, Part.Light);
            IowaResponseBox(shape, side * .49f, 1.78f, -3.048f, .48f, .41f, .014f, Part.Visor);
            IowaResponseBox(shape, side * .087f, 1.27f, -3.05f, .03f, .055f, .027f, Part.Metal);
            IowaResponseBox(shape, side * .96f, .80f, -3.052f, .123f, .159f, .023f, Part.SkinLight);
        }
        IowaResponseBox(shape, 0f, 1.61f, -3.049f, .023f, 1.42f, .016f, Part.DarkMetal);
        IowaResponseLightBarHousing(shape, IowaAmbulanceLightBarY, IowaAmbulanceLightBarZ);
    }

    private static void BuildIowaTowTruck(IowaPitWorkMesh shape)
    {
        IowaResponseBox(shape, 0f, .62f, -.13f, 1.83f, .28f, 5.70f, Part.DarkMetal);
        IowaResponseAxle(shape, 1.97f, 1.13f, .42f);
        IowaResponseAxle(shape, -1.61f, 1.14f, .42f);
        AddIowaResponseCab(shape);
        IowaResponseBox(shape, 0f, 2.11f, 1.14f, 1.83f, .055f, .94f, Part.Accent);

        // Stowed recovery boom and underlift, with the hook tucked into the bed.
        IowaResponseBox(shape, 0f, .93f, -1.51f, 2.20f, .42f, 3.22f, Part.Primary);
        IowaResponseBox(shape, 0f, 1.17f, -1.50f, 2.17f, .065f, 3.12f, Part.DarkMetal);
        IowaResponseBox(shape, 0f, .58f, -3.10f, 2.20f, .18f, .22f, Part.DarkMetal);
        IowaResponseBox(shape, 0f, .72f, -2.94f, .41f, .14f, .46f, Part.Metal);
        IowaResponseBox(shape, 0f, .68f, -2.78f, 1.18f, .115f, .19f, Part.Secondary);
        IowaResponseBox(shape, 0f, 1.23f, -.39f, 1.19f, .18f, .69f, Part.Secondary);

        var boomBase = new Vector3(0f, 1.23f, -.36f);
        var boomEnd = new Vector3(0f, 2.52f, -2.34f);
        shape.Box((boomBase + boomEnd) * .5f,
            new Vector3(.34f, .285f, Vector3.Distance(boomBase, boomEnd)),
            Quaternion.LookRotation(boomEnd - boomBase), Part.Accent);
        shape.Cylinder(new Vector3(0f, 2.51f, -2.34f), new Vector3(0f, 1.64f, -2.34f),
            .023f, .023f, Part.Metal, 8);
        IowaResponseBox(shape, 0f, 1.62f, -2.36f, .23f, .12f, .17f, Part.DarkMetal);

        foreach (float side in new[] { -1f, 1f })
        {
            shape.Cylinder(new Vector3(side * .46f, 1.16f, -1.23f),
                new Vector3(side * .14f, 2.35f, -2.02f), .075f, .057f, Part.Secondary, 8);
            IowaResponseBox(shape, side * 1.095f, .99f, -1.33f, .019f, .17f, 2.46f, Part.Accent);
            IowaResponseBox(shape, side * .996f, .82f, -3.126f, .175f, .103f, .027f, Part.SkinLight);
        }

        for (int stripe = 0; stripe < 8; stripe++)
        {
            float x = (stripe - 3.5f) * .15f;
            shape.Box(new Vector3(x, .88f, -3.135f), new Vector3(.064f, .198f, .013f),
                Quaternion.Euler(0f, 0f, x > 0f ? -34f : 34f), Part.Accent);
        }
        IowaResponseLightBarHousing(shape, IowaTowLightBarY, IowaTowLightBarZ);
    }

    private static void AddIowaResponseCab(IowaPitWorkMesh shape)
    {
        IowaResponseBox(shape, 0f, 1.13f, 1.63f, 1.96f, .87f, 2.07f, Part.Primary);
        IowaResponseBox(shape, 0f, .90f, 2.51f, 1.88f, .39f, 1.08f, Part.Primary);
        IowaResponseBox(shape, 0f, .64f, 2.95f, 2.04f, .17f, .31f, Part.DarkMetal);
        IowaResponseBox(shape, 0f, 1.68f, 1.17f, 1.76f, .69f, 1.10f, Part.Primary);
        IowaResponseBox(shape, 0f, 2.029f, 1.13f, 1.84f, .101f, 1.14f, Part.Primary);
        shape.Box(new Vector3(0f, 1.70f, 1.757f), new Vector3(1.52f, .46f, .10f),
            Quaternion.Euler(-19f, 0f, 0f), Part.Visor);
        IowaResponseBox(shape, 0f, 1.68f, 1.820f, .032f, .385f, .023f, Part.DarkMetal);
        IowaResponseBox(shape, 0f, .99f, 3.056f, 1.10f, .25f, .05f, Part.DarkMetal);
        for (int bar = 0; bar < 3; bar++)
            IowaResponseBox(shape, 0f, .909f + bar * .08f, 3.083f, 1.02f, .024f, .017f, Part.Metal);

        foreach (float side in new[] { -1f, 1f })
        {
            IowaResponseBox(shape, side * .884f, 1.71f, 1.29f, .031f, .35f, .70f, Part.Visor);
            IowaResponseBox(shape, side * .982f, 1.31f, 1.17f, .015f, .035f, .20f, Part.Metal);
            IowaResponseBox(shape, side * .817f, .98f, 3.057f, .266f, .136f, .041f, Part.Light);
            IowaResponseBox(shape, side * 1.055f, 1.56f, 1.63f, .096f, .175f, .172f, Part.DarkMetal);
        }
    }

    private static void IowaResponseAxle(IowaPitWorkMesh shape, float station, float halfWidth, float radius)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            var inner = new Vector3(side * (halfWidth - .27f), radius, station);
            var outer = new Vector3(side * halfWidth, radius, station);
            shape.Cylinder(inner, outer, radius, radius, Part.Rubber, 12);
            shape.Cylinder(outer - Vector3.right * (side * .016f), outer + Vector3.right * (side * .002f),
                radius * .62f, radius * .61f, Part.Metal, 12);
            shape.Cylinder(outer, outer + Vector3.right * (side * .013f),
                radius * .29f, radius * .29f, Part.DarkMetal, 8);
        }
    }

    private static void IowaResponseLightBarHousing(IowaPitWorkMesh shape, float y, float station)
    {
        IowaResponseBox(shape, 0f, y - .033f, station, 1.42f, .069f, .31f, Part.DarkMetal);
        IowaResponseBox(shape, 0f, y + .016f, station, .40f, .05f, .25f, Part.Metal);
    }

    private void AddIowaResponseWarningLamps(Transform vehicle, bool ambulance)
    {
        responseWarningMaterials ??= new Material[2, 2];
        int color = ambulance ? 0 : 1;
        float roofY = ambulance ? IowaAmbulanceLightBarY : IowaTowLightBarY;
        float roofZ = ambulance ? IowaAmbulanceLightBarZ : IowaTowLightBarZ;
        for (int bank = 0; bank < 2; bank++)
        {
            float side = bank == 0 ? -1f : 1f;
            var lenses = new IowaPitWorkMesh();
            IowaResponseBox(lenses, side * .47f, roofY + .022f, roofZ, .42f, .065f, .259f, Part.Light);
            if (ambulance)
            {
                IowaResponseBox(lenses, side * .88f, 2.18f, -3.052f, .25f, .155f, .023f, Part.Light);
                IowaResponseBox(lenses, side * .79f, 2.565f, -2.80f, .31f, .078f, .24f, Part.Light);
            }

            string name = bank == 0 ? "Response warning lamps left" : "Response warning lamps right";
            var item = CreateObject(name, vehicle);
            Mesh mesh = lenses.Build(name, 0, 1);
            roadMeshes.Add(mesh);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            Material material = responseWarningMaterials[color, bank];
            if (material == null)
            {
                material = MakeMaterial(ambulance ? AmbulanceWarningLensColor : TowWarningLensColor);
                material.name = $"{(ambulance ? "Ambulance red" : "Tow yellow")} {name}";
                material.SetFloat("_Smoothness", .24f);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                responseWarningMaterials[color, bank] = material;
            }
            renderer.sharedMaterial = material;
        }
    }

    private void UpdateResponseVehicleWarningLights()
    {
        if (responseWarningMaterials == null)
            return;

        int bank = -1;
        if (TrackCautionLampsActive)
        {
            if (responseWarningBank < 0)
                responseWarningStarted = Time.unscaledTimeAsDouble;
            bank = (int)((Time.unscaledTimeAsDouble - responseWarningStarted) / .14d) & 1;
        }

        if (bank == responseWarningBank)
            return;
        responseWarningBank = bank;

        for (int side = 0; side < 2; side++)
        {
            responseWarningMaterials[0, side]?.SetColor("_EmissionColor",
                bank == side ? new Color(3.1f, .085f, .065f) : Color.black);
            responseWarningMaterials[1, side]?.SetColor("_EmissionColor",
                bank == side ? new Color(3.0f, 1.76f, .04f) : Color.black);
        }
    }

    private static void IowaResponseBox(IowaPitWorkMesh shape, float x, float y, float z,
        float width, float height, float length, Part paint) =>
        shape.Box(new Vector3(x, y, z), new Vector3(width, height, length), Quaternion.identity, paint);

    private Material CreateIowaResponseVehiclePaint()
    {
        const int rows = 2;
        int width = IowaPitWorkMesh.PartCount;
        var palette = new Texture2D(width, rows, TextureFormat.RGBA32, false)
        {
            name = "Iowa emergency response vehicle palette",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        var colors = new Color32[width * rows];
        TeamRow(0, new Color32(233, 237, 229, 255), new Color32(112, 31, 34, 255), new Color32(192, 34, 45, 255));
        TeamRow(1, new Color32(42, 54, 66, 255), new Color32(166, 121, 34, 255), new Color32(243, 182, 44, 255));
        palette.SetPixels32(colors);
        palette.Apply(false, true);
        carPaintTextures.Add(palette);

        Material material = MakeMaterial(Color.white);
        material.name = "Iowa emergency response vehicles";
        material.SetTexture("_BaseMap", palette);
        material.SetFloat("_Smoothness", .24f);
        return material;

        void TeamRow(int row, Color32 body, Color32 secondary, Color32 accent)
        {
            void Paint(Part part, Color32 color) => colors[row * width + (int)part] = color;
            Paint(Part.Primary, body);
            Paint(Part.Secondary, secondary);
            Paint(Part.Accent, accent);
            Paint(Part.Rubber, new Color32(31, 34, 37, 255));
            Paint(Part.Metal, new Color32(153, 163, 167, 255));
            Paint(Part.DarkMetal, new Color32(54, 63, 70, 255));
            Paint(Part.Light, new Color32(243, 243, 232, 255));
            Paint(Part.SkinLight, AmbulanceWarningLensColor);
            Paint(Part.SkinMedium, TowWarningLensColor);
            Paint(Part.Visor, new Color32(64, 93, 109, 255));
        }
    }
}
