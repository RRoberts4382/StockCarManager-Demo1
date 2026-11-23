using UnityEngine;

public sealed partial class TrackPreview
{
    private const int CharlotteTrackId = 6;
    private const float GarageWallOpeningHalfWidth = 6.3f;
    private const float GarageBuildingDepth = 48f;
    private const float GarageCourtFront = 19f;
    private const float TexasPitSourceLength = 450f;
    private const float InfieldGarageHalfLength = 55f;
    private const float InfieldGarageFront = -7.20f;
    private const float InfieldGarageBack = 7.20f;
    private const float InfieldGarageFloorFront = InfieldGarageFront + 0.22f;
    private const float InfieldGarageFloorHeight = 0.22f;

    private void AddInfieldGarageSite(TrackPitSpline pit, float width, float roadStart, float roadEnd)
    {
        bool texas = definition.id == TexasTrackId;
        string venue = texas ? "Texas" : "Charlotte";
        float pitLength = pitLengths[1];
        // Open the wall at the two stations vacated by the moved pit stalls.
        float centerFraction = texas ? (225f + 71.2f) / TexasPitSourceLength : 0.4785f;
        float accessOffset = texas ? pitLength * (62.3f / TexasPitSourceLength) : 62.925f;
        float halfOpening = GarageWallOpeningHalfWidth / pitLength;
        float entry = centerFraction - accessOffset / pitLength;
        float exit = centerFraction + accessOffset / pitLength;

        DrawPitWall(pit, width, roadStart, roadEnd, endFraction: entry - halfOpening);
        DrawPitWall(pit, width, roadStart, roadEnd, entry + halfOpening, exit - halfOpening);
        DrawPitWall(pit, width, roadStart, roadEnd, startFraction: exit + halfOpening);

        Vector3 origin = PitPosition(pit, centerFraction, roadStart, roadEnd);
        Vector3 heading = PitTangent(pit, centerFraction, roadStart, roadEnd);
        Vector3 infield = Vector3.Cross(Vector3.up, heading).normalized * pitWallSide;
        Quaternion rotation = Quaternion.LookRotation(infield, Vector3.up);

        var asphalt = MakeMaterial(new Color32(65, 67, 70, 255));
        asphalt.SetFloat("_Smoothness", 0.07f);
        asphalt.SetTexture("_BaseMap", BristolPavementTexture(false));

        bool reuseTexasYard = texas && Resources.Load<GameObject>("Startup/TexasMotorSpeedway") != null;
        float yardElevation = reuseTexasYard ? 0.05f - origin.y : 0f;
        const float frontThreshold = 38.7f;
        const float rearThreshold = 57.3f;
        const float floorFront = GarageBuildingDepth + InfieldGarageFloorFront;
        const float floorBack = GarageBuildingDepth + InfieldGarageBack;
        if (!texas)
        {
            AddCharlotteCourt();
        }
        else if (!reuseTexasYard)
        {
            AddSurface(venue + " garage motor court", asphalt, new[]
            {
                (GarageCourtFront, 0f, 76f, 0f),
                (75f, 0f, 76f, 0f),
                (78f, 0f, 73f, 0f)
            });
        }

        AddAccessRoad(venue + " garage entry road", -1f);
        AddAccessRoad(venue + " garage exit road", 1f);

        var threshold = MakeMaterial(new Color32(145, 143, 136, 255));
        threshold.SetTexture("_BaseMap", BristolPavementTexture(true));
        float thresholdHeight = texas ? yardElevation + 0.006f : yardElevation;
        AddSurface(venue + " garage concrete threshold", threshold, new[]
        {
            (frontThreshold, 0f, InfieldGarageHalfLength, thresholdHeight),
            (texas ? 41.2f : floorFront, 0f, InfieldGarageHalfLength, thresholdHeight)
        });
        AddSurface(venue + " garage rear concrete threshold", threshold, new[]
        {
            (texas ? 54.8f : floorBack, 0f, InfieldGarageHalfLength, thresholdHeight),
            (rearThreshold, 0f, InfieldGarageHalfLength, thresholdHeight)
        });

        var garage = CreateObject(venue + " infield garage", transform).transform;
        garage.localPosition = Point(0f, GarageBuildingDepth, texas ? yardElevation : yardElevation - InfieldGarageFloorHeight);
        garage.localRotation = rotation;
        AddInfieldGarageBuilding(garage, texas);

        Vector3 Point(float x, float z, float elevation) => origin + rotation * new Vector3(x, elevation, z);

        void AddCharlotteCourt()
        {
            // Leave a shared opening for the two concrete thresholds and the garage floor.
            var depths = new[] { GarageCourtFront, frontThreshold, rearThreshold, 75f, 78f };
            const int columns = 4;
            var vertices = new Vector3[depths.Length * columns];
            var triangles = new System.Collections.Generic.List<int>(66);
            for (int row = 0; row < depths.Length; row++)
            {
                float halfWidth = row == depths.Length - 1 ? 73f : 76f;
                int vertex = row * columns;
                vertices[vertex] = Point(-halfWidth, depths[row], 0f);
                vertices[vertex + 1] = Point(-InfieldGarageHalfLength, depths[row], 0f);
                vertices[vertex + 2] = Point(InfieldGarageHalfLength, depths[row], 0f);
                vertices[vertex + 3] = Point(halfWidth, depths[row], 0f);
                if (row == depths.Length - 1) continue;

                for (int column = 0; column < columns - 1; column++)
                {
                    if (row == 1 && column == 1) continue;
                    int a = vertex + column;
                    triangles.Add(a);
                    triangles.Add(a + columns);
                    triangles.Add(a + 1);
                    triangles.Add(a + 1);
                    triangles.Add(a + columns);
                    triangles.Add(a + columns + 1);
                }
            }
            AddRoadMesh(venue + " garage motor court", asphalt, vertices, triangles.ToArray());
        }

        void AddAccessRoad(string name, float side)
        {
            float mouth = side * accessOffset;
            float pitEdge = width * 0.5f;
            if (reuseTexasYard)
            {
                // The imported paddock starts 7.5 source meters infield of the pit center.
                float yardEdge = 7.5f * pitLength / TexasPitSourceLength;
                AddSurface(name, asphalt, new[]
                {
                    (pitEdge, mouth, 5.45f, 0f),
                    (yardEdge, mouth, 5.45f, yardElevation)
                });
                return;
            }

            AddSurface(name, asphalt, new[]
            {
                (pitEdge, mouth, 5.45f, 0f),
                (pitEdge + PitWallThickness + 0.1f, mouth, 5.45f, 0f),
                (9f, mouth - side * 1f, 5f, 0f),
                (14f, mouth - side * 3.1f, 4.65f, 0f),
                (GarageCourtFront, mouth - side * 4.5f, 6f, 0f)
            });
        }

        void AddSurface(string name, Material material, (float Depth, float Center, float HalfWidth, float Elevation)[] rows)
        {
            var vertices = new Vector3[rows.Length * 2];
            var triangles = new int[(rows.Length - 1) * 6];
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                int vertex = i * 2;
                vertices[vertex] = Point(row.Center - row.HalfWidth, row.Depth, row.Elevation);
                vertices[vertex + 1] = Point(row.Center + row.HalfWidth, row.Depth, row.Elevation);
                if (i == rows.Length - 1)
                    continue;

                int triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }
            AddRoadMesh(name, material, vertices, triangles);
        }
    }
}
