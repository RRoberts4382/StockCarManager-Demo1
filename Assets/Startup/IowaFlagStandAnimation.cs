using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class TrackPreview
{
    private Material[] iowaFlagFabrics;
    private IowaFlagArm iowaPrimaryFlagArm;
    private IowaFlagArm iowaSecondaryFlagArm;

    private void BuildIowaFlagArmRig(Transform official)
    {
        if (official == null)
            return;

        Material sleeve = MakeMaterial(new Color32(27, 39, 57, 255));
        Material glove = MakeMaterial(new Color32(223, 229, 231, 255));
        Material pole = MakeMaterial(new Color32(161, 172, 177, 255));
        iowaFlagFabrics = new Material[6];
        iowaFlagFabrics[(int)IowaStandSignal.Yellow] = Fabric("Yellow caution flag", new Color32(255, 213, 18, 255));
        iowaFlagFabrics[(int)IowaStandSignal.Green] = Fabric("Green racing flag", new Color32(18, 180, 71, 255));
        iowaFlagFabrics[(int)IowaStandSignal.White] = Fabric("White last-lap flag", new Color32(250, 251, 255, 255));
        iowaFlagFabrics[(int)IowaStandSignal.StageComplete] = Fabric("Green-white stage checkered flag",
            new Color32(19, 158, 73, 255), new Color32(247, 250, 255, 255));
        iowaFlagFabrics[(int)IowaStandSignal.Finish] = Fabric("Black-white race checkered flag",
            new Color32(16, 18, 22, 255), new Color32(247, 250, 255, 255));
        iowaPrimaryFlagArm = AddArm("primary", 1f);
        iowaSecondaryFlagArm = AddArm("secondary", -1f);
        iowaFlagStandEnabled = true;
        RenderIowaFlagStand();

        Material Fabric(string name, Color first, Color? other = null)
        {
            Material fabric = MakeMaterial(other.HasValue ? Color.white : first);
            fabric.name = name;
            fabric.SetFloat("_Smoothness", .12f);
            fabric.EnableKeyword("_EMISSION");
            fabric.SetColor("_EmissionColor", (other.HasValue ? Color.white : first) * .23f);
            if (other is Color second)
            {
                const int columns = 8, rows = 6;
                var checkers = new Texture2D(columns, rows, TextureFormat.RGBA32, true)
                {
                    name = name,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                var pixels = new Color[columns * rows];
                for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                    pixels[y * columns + x] = (x + y) % 2 == 0 ? first : second;
                checkers.SetPixels(pixels);
                checkers.Apply(true, true);
                carPaintTextures.Add(checkers);
                fabric.SetTexture("_BaseMap", checkers);
                fabric.SetTexture("_EmissionMap", checkers);
            }
            return fabric;
        }

        IowaFlagArm AddArm(string hand, float side)
        {
            var arm = CreateObject($"Iowa {hand} flag arm", official).transform;
            Transform upper = Box("Official upper sleeve", arm, Vector3.zero, Vector3.one, sleeve).transform;
            Transform lower = Box("Official forearm sleeve", arm, Vector3.zero, Vector3.one, sleeve).transform;
            Transform cuff = Box("Official glove", arm, Vector3.zero, Vector3.one, glove).transform;
            Transform handle = Box($"Iowa {hand} flag pole", arm, Vector3.zero, Vector3.one, pole).transform;
            GameObject cloth = CreateObject($"Iowa {hand} flag cloth", arm);
            var mesh = new Mesh { name = cloth.name };
            mesh.MarkDynamic();
            roadMeshes.Add(mesh);
            cloth.AddComponent<MeshFilter>().sharedMesh = mesh;
            var face = cloth.AddComponent<MeshRenderer>();
            face.shadowCastingMode = ShadowCastingMode.Off;
            return new IowaFlagArm(side, upper, lower, cuff, handle, face, mesh);
        }
    }

    private void RenderIowaFlagStand()
    {
        if (iowaPrimaryFlagArm == null)
            return;

        IowaStandSignal signal = iowaDisplayedFlag;
        bool stage = signal == IowaStandSignal.StageComplete;
        iowaPrimaryFlagArm.Pose(signal == IowaStandSignal.None ? null :
            iowaFlagFabrics[(int)(stage ? IowaStandSignal.Yellow : signal)], iowaFlagWaveSeconds);
        iowaSecondaryFlagArm.Pose(stage ? iowaFlagFabrics[(int)signal] : null, iowaFlagWaveSeconds);
    }

    private sealed class IowaFlagArm
    {
        private const int ClothColumns = 11, ClothRows = 7;
        private const int ClothSide = ClothColumns * ClothRows;
        private readonly float side;
        private readonly Transform upper, lower, glove, pole;
        private readonly MeshRenderer cloth;
        private readonly Mesh mesh;
        private readonly Vector3[] vertices = new Vector3[2 * ClothSide];
        private Material fabric;
        private float lastWave = -1f;

        internal IowaFlagArm(float side, Transform upper, Transform lower, Transform glove, Transform pole,
            MeshRenderer cloth, Mesh mesh)
        {
            this.side = side;
            this.upper = upper;
            this.lower = lower;
            this.glove = glove;
            this.pole = pole;
            this.cloth = cloth;
            this.mesh = mesh;

            var uv = new Vector2[vertices.Length];
            var triangles = new int[(ClothColumns - 1) * (ClothRows - 1) * 12];
            int index = 0;
            for (int row = 0; row < ClothRows; row++)
            for (int column = 0; column < ClothColumns; column++)
            {
                int vertex = row * ClothColumns + column;
                uv[vertex] = uv[vertex + ClothSide] = new Vector2(column / (ClothColumns - 1f), row / (ClothRows - 1f));
                if (column == ClothColumns - 1 || row == ClothRows - 1)
                    continue;

                int below = vertex + ClothColumns;
                triangles[index++] = vertex;
                triangles[index++] = vertex + 1;
                triangles[index++] = below;
                triangles[index++] = vertex + 1;
                triangles[index++] = below + 1;
                triangles[index++] = below;
                triangles[index++] = vertex + ClothSide;
                triangles[index++] = below + ClothSide;
                triangles[index++] = vertex + 1 + ClothSide;
                triangles[index++] = vertex + 1 + ClothSide;
                triangles[index++] = below + ClothSide;
                triangles[index++] = below + 1 + ClothSide;
            }
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            Pose(null, 0f);
        }

        internal void Pose(Material next, float waveSeconds)
        {
            bool waving = next != null;
            if (fabric == next && lastWave >= 0f && (!waving || waveSeconds == lastWave))
                return;
            if (fabric != next)
                cloth.sharedMaterial = next;
            fabric = next;
            lastWave = waveSeconds;
            cloth.enabled = waving;
            pole.gameObject.SetActive(waving);

            float beat = waveSeconds * 5.8f + (side < 0f ? 1.2f : 0f);
            float flick = Mathf.Sin(beat);
            float lift = Mathf.Cos(beat);
            Vector3 shoulder = new Vector3(side * .215f, 1.44f, .015f);
            Vector3 hand = waving
                ? new Vector3(side * (.38f + .095f * flick), 1.67f + .10f * lift, .37f + .08f * flick)
                : new Vector3(side * .255f, .91f, .083f);

            // Solve the elbow from the hand so both sleeves retain their length while waving.
            Vector3 reach = hand - shoulder;
            Vector3 aim = reach.normalized;
            const float upperLength = .29f, lowerLength = .27f;
            float distance = Mathf.Clamp(reach.magnitude, .10f, upperLength + lowerLength - .006f);
            hand = shoulder + aim * distance;
            float joint = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            float bend = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - joint * joint));
            Vector3 elbow = shoulder + aim * joint +
                Vector3.ProjectOnPlane(new Vector3(side * .8f, -.9f, -.2f), aim).normalized * bend;
            Segment(upper, shoulder, elbow, .113f, .113f);
            Segment(lower, elbow, hand, .085f, .090f);
            Segment(glove, Vector3.Lerp(elbow, hand, .89f), hand + (hand - elbow).normalized * .066f, .085f, .093f);
            if (!waving)
                return;

            Vector3 mast = new Vector3(side * (.16f + flick * .54f), .84f + lift * .10f, .53f + lift * .17f).normalized;
            Segment(pole, hand - mast * .09f, hand + mast * .89f, .023f, .023f);
            Vector3 spread = Vector3.ProjectOnPlane(new Vector3(side, -.15f, .15f + .33f * lift), mast).normalized;
            Vector3 ripple = Vector3.Cross(mast, spread).normalized;
            for (int row = 0; row < ClothRows; row++)
            for (int column = 0; column < ClothColumns; column++)
            {
                float u = column / (ClothColumns - 1f);
                float v = row / (ClothRows - 1f);
                float curl = u * (.12f * Mathf.Sin(u * 7.7f - beat * 2f + v * .95f) + .06f * lift);
                Vector3 point = hand + mast * (.29f + .55f * v - .12f * u * u) +
                    spread * (.78f * u) + ripple * curl;
                int index = row * ClothColumns + column;
                vertices[index] = vertices[index + ClothSide] = point;
            }

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static void Segment(Transform node, Vector3 start, Vector3 end, float width, float depth)
        {
            Vector3 delta = end - start;
            node.SetLocalPositionAndRotation((start + end) * .5f, Quaternion.FromToRotation(Vector3.up, delta));
            node.localScale = new Vector3(width, delta.magnitude + .015f, depth);
        }
    }
}
