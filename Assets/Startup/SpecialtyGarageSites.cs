using System.IO;
using UnityEngine;

public sealed partial class TrackPreview
{
    private const string BristolGarageWallName = "Bristol backstretch pit wall";
    private const string BristolGarageWallResource = "Startup/BristolGarageWall";

    private void AddSpecialtyGarageSite()
    {
        if (pitRoute == null)
            return;

        switch (definition.id)
        {
            case CotaTrackId:
                AddCotaGarageSite();
                break;
            case KansasTrackId:
                AddKansasGarageSite();
                break;
            case MartinsvilleTrackId:
                AddMartinsvilleGarageSite();
                break;
            case BristolTrackId:
                AddBristolGarageSite();
                break;
        }
    }

    private void AddKansasGarageSite()
    {
        const float elevation = 0.033f;
        Material asphalt = SpecialtyGarageAsphalt();
        AddSpecialtyGarage("Kansas", new Vector3(0f, elevation, -67f), Quaternion.identity, 1f, 4);
        AddSpecialtyGarageRoad("Kansas garage motor court", asphalt,
            (OnGrade(-65f, -94f), OnGrade(-65f, -44f)),
            (OnGrade(65f, -94f), OnGrade(65f, -44f)));
        AddApproach(-1f, "Kansas garage entry road");
        AddApproach(1f, "Kansas garage exit road");

        Vector3 OnGrade(float x, float z) => new Vector3(x, elevation, z);

        void AddApproach(float side, string name)
        {
            // Both mouths sit beyond the outermost stalls on the original apron.
            AddSpecialtyGarageRoad(name, asphalt,
                (new Vector3(side * 222.8f, 0.018f, -111.60f), new Vector3(side * 234.0f, 0.018f, -111.60f)),
                (OnGrade(side * 222.7f, -106.9f), OnGrade(side * 233.6f, -106.6f)),
                (OnGrade(side * 219.0f, -100.0f), OnGrade(side * 230.0f, -97.3f)),
                (OnGrade(side * 211.0f, -94.6f), OnGrade(side * 222.4f, -89.4f)),
                (OnGrade(side * 190.0f, -90.9f), OnGrade(side * 193.6f, -80.8f)),
                (OnGrade(side * 65f, -89.5f), OnGrade(side * 65f, -78.9f)));
        }
    }

    private void AddMartinsvilleGarageSite()
    {
        const float elevation = 0.027f;
        Material asphalt = SpecialtyGarageAsphalt();
        AddSpecialtyGarage("Martinsville", new Vector3(0f, elevation, 0f), Quaternion.identity, 1f, 3);
        AddSpecialtyGarageRoad("Martinsville garage motor court", asphalt,
            (OnGrade(-65f, -22f), OnGrade(-65f, 29f)),
            (OnGrade(65f, -22f), OnGrade(65f, 29f)));
        AddApproach(-1f, "Martinsville garage entry road");
        AddApproach(1f, "Martinsville garage exit road");

        Vector3 OnGrade(float x, float z) => new Vector3(x, elevation, z);

        void AddApproach(float side, string name)
        {
            // Use the spaces past the end caps of the imported infield pit wall.
            AddSpecialtyGarageRoad(name, asphalt,
                (new Vector3(side * 133.9f, 0.009f, 30.8f), new Vector3(side * 141.5f, 0.009f, 25.6f)),
                (OnGrade(side * 130.7f, 29.0f), OnGrade(side * 136.5f, 21.0f)),
                (OnGrade(side * 122.1f, 27.0f), OnGrade(side * 124.1f, 17.5f)),
                (OnGrade(side * 112.0f, 25.3f), OnGrade(side * 112.8f, 15.7f)),
                (OnGrade(side * 65f, 24.5f), OnGrade(side * 65f, 14.5f)));
        }
    }

    private void AddBristolGarageSite()
    {
        if (!TryOpenBristolGarageWall())
            return;

        const float elevation = 0.024f;
        Material asphalt = SpecialtyGarageAsphalt();
        AddSpecialtyGarage("Bristol", new Vector3(0f, elevation, -39.15f), Quaternion.identity, 0.82f, 6);
        AddSpecialtyGarageRoad("Bristol garage motor court", asphalt,
            (OnGrade(-56f, -52.65f), OnGrade(-56f, -25.65f)),
            (OnGrade(56f, -52.65f), OnGrade(56f, -25.65f)));
        AddSpecialtyGarageRoad("Bristol garage entry road", asphalt,
            (new Vector3(-86.4f, 0.021f, -55.6f), new Vector3(-96.2f, 0.021f, -55.85f)),
            (OnGrade(-84.9f, -51.7f), OnGrade(-94.2f, -48.3f)),
            (OnGrade(-77.9f, -48.6f), OnGrade(-82.6f, -39.2f)),
            (OnGrade(-67.3f, -46.7f), OnGrade(-69.2f, -36.6f)),
            (OnGrade(-56f, -45.8f), OnGrade(-56f, -35.6f)));
        AddSpecialtyGarageRoad("Bristol garage exit road", asphalt,
            (new Vector3(65.6f, 0.021f, -55.50f), new Vector3(75.3f, 0.021f, -55.54f)),
            (OnGrade(65.6f, -52.8f), OnGrade(75.2f, -52.2f)),
            (OnGrade(64.3f, -50.1f), OnGrade(73.7f, -47.0f)),
            (OnGrade(61.1f, -47.6f), OnGrade(68.2f, -40.2f)),
            (OnGrade(56f, -45.8f), OnGrade(56f, -35.6f)));

        Vector3 OnGrade(float x, float z) => new Vector3(x, elevation, z);
    }

    private void AddCotaGarageSite()
    {
        const float courtHeight = 9.57f;
        const float courtEnd = 114.0f;
        Material asphalt = SpecialtyGarageAsphalt();
        Material concrete = MakeMaterial(new Color32(148, 145, 135, 255));
        concrete.SetTexture("_BaseMap", BristolPavementTexture(true));
        concrete.SetFloat("_Smoothness", 0.08f);
        var pitOrigin = new Vector3(-339.1702f, 0f, 218.97548f);
        var along = new Vector3(-0.9266571f, 0f, -0.3759076f);
        Vector3 inward = Vector3.Cross(along, Vector3.up);

        // A level terrace between the elevated pit lane and the uphill infield verge.
        AddSpecialtyGarage("COTA", Point(57.5f, 18.35f, courtHeight),
            Quaternion.LookRotation(inward, Vector3.up), 0.82f, 2);
        Vector3 courtFrontStart = Point(3.5f, 4.8f, courtHeight);
        Vector3 courtBackStart = Point(3.5f, 31.9f, courtHeight);
        Vector3 courtFrontEnd = Point(courtEnd, 4.8f, courtHeight);
        Vector3 courtBackEnd = Point(courtEnd, 31.9f, courtHeight);
        AddSpecialtyGarageRoad("COTA garage motor court", asphalt,
            (courtFrontStart, courtBackStart),
            (courtFrontEnd, courtBackEnd));
        AddSpecialtyGarageFoundation("COTA garage terrace retaining", concrete,
            new[] { courtFrontStart, courtBackStart, courtBackEnd, courtFrontEnd }, -0.20f);

        // Meet the pit edge beyond its last stall and follow the contour back to the court.
        (Vector3 A, Vector3 B)[] approach =
        {
            (AtPitEdge(252.55f), AtPitEdge(263.30f)),
            (Point(251.5f, 6.0f, 11.30f), Point(261.5f, 10.0f, 11.33f)),
            (Point(248.0f, 7.2f, 11.30f), Point(256.0f, 13.4f, 11.34f)),
            (Point(239.7f, 7.5f, 11.25f), Point(244.0f, 15.4f, 11.27f)),
            (Point(229.0f, 7.7f, 11.17f), Point(229.7f, 17.0f, 11.17f)),
            (Point(208.0f, 7.8f, 10.91f), Point(208.0f, 17.1f, 10.91f)),
            (Point(180.0f, 7.8f, 10.39f), Point(180.0f, 17.1f, 10.39f)),
            (Point(150.0f, 7.8f, 9.88f), Point(150.0f, 17.1f, 9.88f)),
            (Point(128.0f, 7.8f, 9.68f), Point(128.0f, 17.1f, 9.68f)),
            (Point(courtEnd, 7.8f, courtHeight), Point(courtEnd, 17.1f, courtHeight))
        };
        AddSpecialtyGarageRoad("COTA garage access road", asphalt, approach);
        var approachOutline = new Vector3[approach.Length * 2];
        for (int i = 0; i < approach.Length; i++)
        {
            approachOutline[i] = approach[i].A;
            approachOutline[approachOutline.Length - 1 - i] = approach[i].B;
        }
        AddSpecialtyGarageFoundation("COTA garage ramp retaining", concrete, approachOutline, -0.20f);

        Vector3 Point(float distance, float offset, float height) =>
            pitOrigin + along * distance + inward * offset + Vector3.up * height;

        Vector3 AtPitEdge(float distance) => Point(distance, 3.86f,
            PitHeight(pitHeights[1], pitHeights[2], distance / pitLengths[1]) + 0.002f);
    }

    private Material SpecialtyGarageAsphalt()
    {
        Material asphalt = MakeMaterial(new Color32(65, 67, 70, 255));
        asphalt.SetTexture("_BaseMap", BristolPavementTexture(false));
        asphalt.SetFloat("_Smoothness", 0.07f);
        return asphalt;
    }

    private void AddSpecialtyGarage(string venue, Vector3 position, Quaternion rotation, float widthScale, int style)
    {
        Transform garage = CreateObject(venue + " infield garage", transform).transform;
        garage.localPosition = position;
        garage.localRotation = rotation;
        garage.localScale = new Vector3(widthScale, 1f, 1f);
        AddInfieldGarageBuilding(garage, style: style);
    }

    private void AddSpecialtyGarageRoad(string name, Material asphalt, params (Vector3 A, Vector3 B)[] sections)
    {
        var vertices = new Vector3[sections.Length * 2];
        var triangles = new int[(sections.Length - 1) * 6];
        for (int i = 0; i < sections.Length; i++)
        {
            int v = i * 2;
            vertices[v] = sections[i].A;
            vertices[v + 1] = sections[i].B;
            if (i == 0)
                continue;

            int previous = v - 2;
            int t = (i - 1) * 6;
            bool forward = Vector3.Cross(vertices[v] - vertices[previous], vertices[previous + 1] - vertices[previous]).y > 0f;
            triangles[t] = previous;
            triangles[t + 1] = forward ? v : previous + 1;
            triangles[t + 2] = forward ? previous + 1 : v;
            triangles[t + 3] = previous + 1;
            triangles[t + 4] = forward ? v : v + 1;
            triangles[t + 5] = forward ? v + 1 : v;
        }
        AddRoadMesh(name, asphalt, vertices, triangles);
    }

    private void AddSpecialtyGarageFoundation(string name, Material concrete, Vector3[] outline, float bottom)
    {
        var vertices = new Vector3[outline.Length * 4];
        var triangles = new int[outline.Length * 6];
        float area = 0f;
        for (int i = 0; i < outline.Length; i++)
        {
            Vector3 start = outline[i];
            Vector3 end = outline[(i + 1) % outline.Length];
            area += start.x * end.z - end.x * start.z;
            int v = i * 4;
            vertices[v] = start;
            vertices[v + 1] = end;
            vertices[v + 2] = new Vector3(start.x, bottom, start.z);
            vertices[v + 3] = new Vector3(end.x, bottom, end.z);
        }

        bool clockwise = area < 0f;
        for (int i = 0; i < outline.Length; i++)
        {
            int v = i * 4;
            int t = i * 6;
            triangles[t] = v;
            triangles[t + 1] = clockwise ? v + 2 : v + 1;
            triangles[t + 2] = clockwise ? v + 1 : v + 2;
            triangles[t + 3] = v + 1;
            triangles[t + 4] = clockwise ? v + 2 : v + 3;
            triangles[t + 5] = clockwise ? v + 3 : v + 2;
        }
        AddRoadMesh(name, concrete, vertices, triangles);
    }

    private bool TryOpenBristolGarageWall()
    {
        Transform stands = transform.Find("Bristol Grandstands");
        MeshRenderer original = stands == null ? null : System.Array.Find(
            stands.GetComponentsInChildren<MeshRenderer>(), item => item.name == BristolGarageWallName);
        TextAsset wall = Resources.Load<TextAsset>(BristolGarageWallResource);
        if (original == null || wall == null)
        {
            Debug.LogWarning("Bristol garage requires its backstretch pit wall opening.", this);
            return false;
        }

        // The clipped wall is baked from the imported mesh without changing the FBX read/write setting.
        using var reader = new BinaryReader(new MemoryStream(wall.bytes, writable: false));
        var vertices = new Vector3[reader.ReadInt32()];
        var triangles = new int[reader.ReadInt32()];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        for (int i = 0; i < triangles.Length; i++)
            triangles[i] = reader.ReadInt32();

        AddRoadMesh(BristolGarageWallName, original.sharedMaterial, vertices, triangles);
        original.enabled = false;
        return true;
    }
}