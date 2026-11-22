using UnityEngine;
using UnityEngine.Rendering;
using Part = IowaPitWorkMesh.Part;

public sealed partial class TrackPreview
{
    // The straight/turn joins come from the authored Iowa plan, not the adjacent
    // banking transitions. Each of the two long bends contains two named turns.
    private static readonly (string Name, float Station)[] IowaSafetyLightSites =
    {
        ("Front straight exit", .176690538f),
        ("Turn 1 midpoint", .250251860f),
        ("Turn 2 midpoint", .397374502f),
        ("Back straight entrance", .470935824f),
        ("Back straight exit", .649670246f),
        ("Turn 3 midpoint", .723231567f),
        ("Turn 4 midpoint", .870354209f),
        ("Front straight entrance", .943915530f)
    };

    private void AddIowaSafetyLightFixtures((string Name, float Station)[] sites = null)
    {
        const float wallHeight = 1.067f;
        const float lampHeight = wallHeight + 1.524f;
        var parent = CreateObject(VenueSceneryName + " safety light fixtures", transform).transform;
        var housing = new IowaPitWorkMesh();
        var leftAmber = new IowaPitWorkMesh();
        var rightAmber = new IowaPitWorkMesh();
        var green = new IowaPitWorkMesh();
        var red = new IowaPitWorkMesh();

        AddFixture("Start and finish", definition.StartFinishFraction * lapLength);
        foreach (var site in sites ?? IowaSafetyLightSites)
            AddFixture(site.Name, site.Station * lapLength);

        var hardware = IowaFlagStandPalette("Iowa safety light powder coat", .06f,
            new Color32(21, 26, 32, 255), new Color32(67, 75, 84, 255), new Color32(160, 164, 161, 255),
            new Color32(19, 22, 25, 255), new Color32(119, 126, 132, 255), new Color32(34, 39, 45, 255));
        FinishMesh("Iowa safety light housings and fence mounts", housing, hardware);
        SetIowaSafetyFaces(FinishMesh("Iowa safety lights left amber bank", leftAmber),
            FinishMesh("Iowa safety lights right amber bank", rightAmber),
            FinishMesh("Iowa safety lights green", green), FinishMesh("Iowa safety lights red", red));

        void AddFixture(string name, float station)
        {
            var edge = SampleOutsideEdge(station);
            Vector3 mount = edge.Position + edge.Outward * .275f;
            Vector3 faceCenter = edge.Position - edge.Outward * .72f + Vector3.up * lampHeight;
            Vector3 driver = TrackPoint(station - 60f, 0f, out _, out _) + Vector3.up;
            Quaternion facing = Quaternion.LookRotation(driver - faceCenter, Vector3.up);
            var anchor = CreateObject("Safety light at " + name, parent).transform;
            anchor.SetLocalPositionAndRotation(faceCenter, facing);

            // Reinforce the existing fence locally at each requested station.
            housing.Cylinder(mount + Vector3.up * (wallHeight - .075f), mount + Vector3.up * (lampHeight + .09f),
                .068f, .060f, Part.DarkMetal, 10);
            Vector3 cantilever = mount + Vector3.up * (lampHeight + .012f);
            housing.Cylinder(cantilever, At(0f, .025f, -.11f), .061f, .047f, Part.DarkMetal, 10);
            housing.Cylinder(mount + Vector3.up * (lampHeight - .29f), Vector3.Lerp(cantilever, faceCenter, .59f),
                .035f, .032f, Part.Metal);
            housing.Cylinder(mount + Vector3.up * (lampHeight - .08f), mount + Vector3.up * (lampHeight + .082f),
                .087f, .087f, Part.Metal);

            Body(0f, 0f, -.016f, 1.13f, .55f, .24f, Part.Primary);
            Body(0f, -.055f, .108f, 1.055f, .42f, .030f, Part.DarkMetal);
            Body(0f, .272f, .139f, 1.18f, .060f, .360f, Part.Primary);
            for (int side = -1; side <= 1; side += 2)
                Body(side * .565f, .171f, .151f, .045f, .20f, .262f, Part.Primary);

            for (int row = 0; row < 2; row++)
            for (int column = 0; column < 6; column++)
            {
                // Drivers see this face from its positive Z side, so positive X is their left.
                float x = (2.5f - column) * .173f;
                float y = .128f - row * .172f;
                Body(x, y, .128f, .155f, .144f, .043f, Part.Rubber);
                IowaPitWorkMesh bank = column switch
                {
                    0 or 1 => leftAmber,
                    2 or 3 => rightAmber,
                    4 => green,
                    _ => red
                };
                Lamp(bank, x, y, .128f, .113f);
            }

            Vector3 At(float x, float y, float z) => faceCenter + facing * new Vector3(x, y, z);
            void Body(float x, float y, float z, float width, float height, float depth, Part color) =>
                housing.Box(At(x, y, z), new Vector3(width, height, depth), facing, color);
            void Lamp(IowaPitWorkMesh mesh, float x, float y, float width, float height) =>
                mesh.Box(At(x, y, .155f), new Vector3(width, height, .031f), facing, Part.Light);
        }

        MeshRenderer FinishMesh(string name, IowaPitWorkMesh source, Material material = null)
        {
            var item = CreateObject(name, parent);
            var mesh = source.Build(name, 0, 1);
            roadMeshes.Add(mesh);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }
    }
}
