using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Surface = GrandstandMeshBuilder.Surface;

internal static class TrackGrandstandScenery
{
    private const float ConcreteInsideWall = 0.23f;
    private const float BayClearance = 0.22f;

    private readonly struct StandRun
    {
        internal readonly string Name;
        internal readonly float Start, End, Setback, Foundation, Depth, Rise, BayLength;
        internal readonly int Rows, SuiteFloors;
        internal readonly bool Chairs, HasWalkway;
        internal readonly Color32 Lower, Upper;

        internal StandRun(string name, float start, float end, float setback, float foundation,
            float depth, int rows, float rise, float bayLength, bool chairs, Color32 lower, Color32 upper,
            int suiteFloors = 0, bool hasWalkway = true)
        {
            Name = name;
            Start = start;
            End = end;
            Setback = setback;
            Foundation = foundation;
            Depth = depth;
            Rows = rows;
            Rise = rise;
            BayLength = bayLength;
            Chairs = chairs;
            HasWalkway = hasWalkway;
            Lower = lower;
            Upper = upper;
            SuiteFloors = suiteFloors;
        }
    }

    private static readonly Color32 Blue = new Color32(38, 128, 182, 255);
    private static readonly Color32 Gold = new Color32(214, 156, 42, 255);
    private static readonly Color32 Purple = new Color32(131, 82, 129, 255);
    private static readonly Color32 Aluminum = new Color32(157, 171, 181, 255);
    private static readonly Color32 Red = new Color32(146, 62, 62, 255);
    private static readonly Color32 Green = new Color32(92, 128, 105, 255);
    private static readonly Color32 Sand = new Color32(171, 147, 105, 255);

    // The ranges are measured from start/finish on the game layout. Depths,
    // setbacks and heights are estimates for the modular venue scenery.
    private static readonly Dictionary<int, (string Venue, StandRun[] Runs)> LargeVenues =
        new Dictionary<int, (string, StandRun[])>
        {
            [4] = ("Darlington", new[]
            {
                Run("Pearson Tower", -.132f, -.072f, 25, 9f, Red, Aluminum, bayLength: 23f),
                Run("Wallace Grandstand", -.070f, .105f, 34, 9.5f, Blue, Aluminum, suites: 1),
                Run("Brasington Tower", .113f, .185f, 25, 11f, Aluminum, Red, bayLength: 23f)
            }),
            [6] = ("Charlotte", new[]
            {
                Run("Turn Four Bleachers", -.171f, -.103f, 24, 11f, Aluminum, Red),
                Run("Frontstretch Grandstand", -.098f, .108f, 48, 12f, Blue, Red, suites: 2),
                Run("Turn One Terrace", .113f, .174f, 30, 12f, Red, Aluminum)
            }),
            [7] = ("EchoPark", new[]
            {
                Run("Frontstretch Grandstand", -.132f, .105f, 35, 12f, Green, Aluminum, suites: 1),
                Run("South Bleachers", .111f, .166f, 19, 10f, Aluminum, Green, bayLength: 24f)
            }),
            [9] = ("Phoenix", new[]
            {
                Run("West Grandstand", -.097f, -.033f, 25, 11f, Sand, Red, bayLength: 23f),
                Run("Frontstretch Grandstand", -.030f, .095f, 43, 12f, Blue, Sand, suites: 2, bayLength: 23f),
                Run("East Terrace", .100f, .142f, 20, 11f, Aluminum, Sand, bayLength: 20f)
            }),
            [10] = ("Homestead-Miami", new[]
            {
                Run("Frontstretch Grandstand", -.106f, .113f, 37, 11f, Blue, Aluminum, suites: 1),
                Run("Frontstretch End Bleachers", .118f, .147f, 16, 10f, Aluminum, Blue, bayLength: 23f)
            }),
            [11] = ("Michigan", new[]
            {
                Run("Frontstretch Grandstand", -.142f, .102f, 43, 13f, Blue, Red, suites: 2),
                Run("North Bleachers", .110f, .158f, 22, 10f, Aluminum, Blue)
            }),
            [12] = ("New Hampshire", new[]
            {
                Run("Main Grandstand", -.120f, .114f, 37, 10f, Blue, Gold, suites: 1),
                Run("South Bleachers", .122f, .167f, 19, 8f, Aluminum, Aluminum, bayLength: 22f)
            }),
            [13] = ("North Wilkesboro", new[]
            {
                Run("Frontstretch Grandstand", -.106f, .112f, 24, 7f, Red, Aluminum, suites: 1, bayLength: 20f),
                Run("Backstretch Terrace", .425f, .562f, 12, 9f, Aluminum, Blue, bayLength: 20f)
            }),
            [14] = ("Rockingham", new[]
            {
                Run("Frontstretch Grandstand", -.119f, .112f, 33, 10f, Blue, Gold, suites: 1, bayLength: 25f),
                Run("South Bleachers", .122f, .154f, 14, 8f, Aluminum, Aluminum, bayLength: 18f)
            }),
            [16] = ("Nashville Superspeedway", new[]
            {
                Run("Frontstretch Grandstand", -.109f, .117f, 31, 11f, Purple, Blue, suites: 2),
                Run("South Terrace", .124f, .147f, 17, 9f, Aluminum, Purple, bayLength: 23f)
            }),
            [17] = ("Nashville Fairgrounds", new[]
            {
                Run("Frontstretch Bleachers", -.101f, .107f, 19, 6f, Aluminum, Gold, suites: 1, bayLength: 18f),
                Run("Turn One Terrace", .116f, .156f, 13, 7f, Sand, Gold, bayLength: 16f)
            }),
            [18] = ("Pocono", new[]
            {
                Run("West Grandstand", -.116f, -.045f, 25, 12f, Aluminum, Blue),
                Run("Main Grandstand", -.041f, .067f, 39, 13f, Blue, Red, suites: 2),
                Run("East Grandstand", .072f, .130f, 26, 12f, Red, Aluminum)
            }),
            [19] = ("Indianapolis", new[]
            {
                Run("Main Straight Grandstand", -.113f, .115f, 39, 13f, Aluminum, Blue, suites: 2),
                Run("Turn One Grandstand", .125f, .186f, 25, 11f, Blue, Aluminum, bayLength: 26f),
                Run("North Short Chute Bleachers", .571f, .633f, 19, 10f, Aluminum, Aluminum)
            }),
            [20] = ("Talladega", new[]
            {
                Run("West Grandstand", -.165f, -.079f, 35, 15f, Blue, Aluminum),
                Run("Frontstretch Grandstand", -.075f, .072f, 48, 17f, Blue, Red, suites: 2),
                Run("East Grandstand", .076f, .132f, 32, 15f, Red, Aluminum)
            }),
            [21] = ("Circuit of the Americas", new[]
            {
                Run("Main Grandstand", -.026f, .032f, 28, 22f, Red, Aluminum, suites: 1, bayLength: 23f),
                Run("Turn One Bleachers", .070f, .083f, 16, 17f, Aluminum, Red, bayLength: 15f)
            }),
            [22] = ("Watkins Glen", new[]
            {
                Run("Frontstretch Grandstand", -.045f, .052f, 22, 14f, Blue, Aluminum, suites: 1),
                Run("Frontstretch Approach Bleachers", -.106f, -.071f, 13, 13f, Aluminum, Aluminum,
                    bayLength: 22f)
            }),
            [23] = ("Sonoma", new[]
            {
                // The game layout places pit road outside the start/finish straight.
                Run("Turnside Grandstand", .117f, .156f, 31, 14f, Aluminum, Sand, suites: 1, bayLength: 18f),
                Run("Back-course Terrace", .472f, .507f, 15, 15f, Sand, Red, bayLength: 19f)
            }),
            [24] = ("Chicagoland", new[]
            {
                Run("Frontstretch Grandstand", -.133f, .129f, 39, 12f, Blue, Aluminum, suites: 1)
            }),
            [26] = ("Las Vegas", new[]
            {
                Run("West Grandstand", -.142f, -.063f, 31, 12f, Purple, Gold),
                Run("Frontstretch Grandstand", -.058f, .081f, 46, 13f, Purple, Sand, suites: 2),
                Run("East Grandstand", .086f, .157f, 31, 12f, Gold, Aluminum)
            }),
            [32] = ("Kansas", new[]
            {
                Run("West Grandstand", -.145f, -.068f, 26, 11f, Blue, Aluminum),
                Run("Frontstretch Grandstand", -.063f, .076f, 38, 11f, Blue, Gold, suites: 2),
                Run("East Grandstand", .081f, .144f, 26, 11f, Aluminum, Blue)
            }),
            [34] = ("World Wide Technology Raceway", new[]
            {
                Run("Frontstretch Grandstand", -.129f, .103f, 33, 10f, Aluminum, Blue, suites: 1),
                Run("South Terrace", .110f, .149f, 17, 9f, Red, Aluminum, bayLength: 22f)
            }),
            [TrackPreview.IowaTrackId] = ("Iowa", new[]
            {
                new StandRun("Turn Four Bleachers", -.156f, -.086f, 6.6f, .14f, 10.2f, 15, .35f, 23f, false, Aluminum, Blue),
                new StandRun("Turn Four Lower Tier", -.084f, -.033f, 7.2f, .14f, 11.2f, 16, .36f, 25f, true, Blue, Sand),
                new StandRun("Turn Four Upper Tier", -.084f, -.033f, 17.7f, 8.6f, 10.5f, 14, .39f, 25f, true, Blue, Aluminum,
                    hasWalkway: false),
                new StandRun("Start and Finish Grandstand", -.031f, .034f, 7.4f, .14f, 20.5f, 31, .41f, 25f, true, Blue, Aluminum),
                new StandRun("Turn One Lower Tier", .036f, .080f, 7.2f, .14f, 11.2f, 16, .36f, 24f, true, Blue, Sand),
                new StandRun("Turn One Upper Tier", .036f, .080f, 17.7f, 8.6f, 10.5f, 14, .39f, 24f, true, Blue, Aluminum,
                    hasWalkway: false),
                new StandRun("Turn One Bleachers", .082f, .151f, 6.6f, .14f, 10.2f, 15, .35f, 23f, false, Aluminum, Blue)
            })
        };

    private static StandRun Run(string name, float start, float end, int rows, float setback,
        Color32 primary, Color32 secondary, int suites = 0, float bayLength = 32f)
    {
        return new StandRun(name, start, end, setback, .14f, rows * .68f + .8f, rows,
            rows < 24 ? .35f : .42f, bayLength, false, primary, secondary, suites);
    }

    // Lap fractions are estimated from the venue seating map and the game's final spline.
    // The long backstretch stays open between the two groups of curved stands.
    private static readonly StandRun[] MartinsvilleRuns =
    {
        new StandRun("West Bleachers", .010f, .051f, 5.7f, .06f, 9.2f, 13, .36f, 23f, false, Blue, Aluminum),
        new StandRun("Richard Petty Tower", .051f, .103f, 5.7f, .06f, 22f, 32, .41f, 26f, true, Blue,
            new Color32(148, 156, 162, 255)),
        new StandRun("Blue Ridge Tower", .103f, .284f, 5.7f, .06f, 29f, 43, .42f, 29f, true, Blue,
            new Color32(173, 46, 50, 255)),
        new StandRun("Glenn Wood Tower", .284f, .344f, 13f, 1.78f, 12.5f, 18, .40f, 15f, true, Gold,
            new Color32(106, 141, 85, 255)),
        new StandRun("Old Dominion Tower and Suites", .344f, .442f, 13f, 1.78f, 20f, 28, .46f, 17f, true, Gold,
            new Color32(203, 110, 44, 255), 3),
        new StandRun("Clay Earles Tower", .442f, .498f, 13f, 1.78f, 14f, 20, .41f, 15f, true, Gold, Purple),
        new StandRun("Bill France Tower", .798f, .946f, 8.6f, 1.75f, 14.4f, 21, .40f, 16f, false, Purple, Purple),
    };

    internal static GameObject AddMartinsville(Transform parent, int layer, float lapLength, float ground,
        Func<float, (Vector3 Position, Vector3 Outward)> sampleEdge, Func<Color, Material> makeMaterial,
        ICollection<Mesh> ownedMeshes)
    {
        var root = new GameObject("Martinsville Grandstands") { layer = layer };
        root.transform.SetParent(parent, false);
        var attendance = root.AddComponent<GrandstandAttendance>();
        var palette = new Dictionary<(Surface, Color32), Material>();
        var walkwayMaterial = makeMaterial(new Color32(173, 161, 144, 255));
        walkwayMaterial.name = "Grandstand walkway concrete";
        walkwayMaterial.SetFloat("_Smoothness", .12f);

        foreach (var run in MartinsvilleRuns)
        {
            var runRoot = new GameObject(run.Name) { layer = layer };
            runRoot.transform.SetParent(root.transform, false);
            float start = run.Start * lapLength;
            float end = run.End * lapLength;
            float foundation = ground + run.Foundation;
            int bays = Mathf.CeilToInt((end - start) / run.BayLength);
            AddWalkway(runRoot.transform, layer, start, end, run.Setback, foundation, ground,
                sampleEdge, walkwayMaterial, ownedMeshes);

            for (int bay = 0; bay < bays; bay++)
            {
                var first = sampleEdge(Mathf.Lerp(start, end, (float)bay / bays));
                var last = sampleEdge(Mathf.Lerp(start, end, (float)(bay + 1) / bays));
                Vector3 frontA = first.Position + first.Outward * run.Setback;
                Vector3 frontB = last.Position + last.Outward * run.Setback;
                frontA.y = frontB.y = foundation;
                float frontage = Vector3.Distance(frontA, frontB);
                if (Vector3.Dot(frontB - frontA, Vector3.Cross(Vector3.up, first.Outward)) < 0f)
                {
                    (frontA, frontB) = (frontB, frontA);
                    (first, last) = (last, first);
                }

                var geometry = GrandstandMeshBuilder.Build(frontage - BayClearance, run.Depth, run.Rows,
                    run.Rise, .65f, run.Chairs, frontage > 25f ? 2 : 1, run.SuiteFloors, 7.2f,
                    seats => attendance.Register(run.Name, seats, frontA, frontB, first.Outward, last.Outward, frontage));
                var section = new GameObject($"Bay {bay + 1}") { layer = layer };
                section.transform.SetParent(runRoot.transform, false);
                section.transform.localPosition = (frontA + frontB) * .5f;

                // Curve the bay by spreading its rear edge along the outside normals.
                // Columns and the suite walls remain vertical through the turns.
                MeshRenderer near = AddLevel("Seating detail", geometry.Near);
                MeshRenderer far = AddLevel("Seating rows", geometry.Far);
                var lod = section.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(.19f, new Renderer[] { near }), new LOD(.004f, new Renderer[] { far }) });
                lod.RecalculateBounds();

                MeshRenderer AddLevel(string label, GrandstandMeshBuilder.SectionMesh level)
                {
                    var mesh = level.Mesh;
                    mesh.name = run.Name + " " + label;
                    var vertices = mesh.vertices;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 p = vertices[i];
                        float across = p.x / frontage + .5f;
                        Vector3 outward = Vector3.LerpUnclamped(first.Outward, last.Outward, across).normalized;
                        vertices[i] = Vector3.LerpUnclamped(frontA, frontB, across) - section.transform.localPosition +
                            outward * p.z + Vector3.up * p.y;
                    }
                    mesh.vertices = vertices;
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    ownedMeshes.Add(mesh);

                    var materials = new Material[level.Surfaces.Length];
                    for (int i = 0; i < materials.Length; i++)
                        materials[i] = StandMaterial(level.Surfaces[i], run, Aluminum, palette, makeMaterial);
                    return AddMesh(section.transform, layer, label, mesh, materials);
                }
            }
        }

        return root;
    }

    internal static GameObject AddLargeTrack(int trackId, Transform parent, int layer, float lapLength,
        float finishFraction, float ground, Func<float, (Vector3 Position, Vector3 Outward)> sampleEdge,
        Func<Color, Material> makeMaterial, ICollection<Mesh> ownedMeshes, GameObject importedScenery,
        ICollection<Renderer> importedGrandstandRenderers)
    {
        string importedVenue = trackId switch
        {
            27 => "Bristol",
            28 => "Orange County",
            29 => "South Boston",
            30 => "Daytona",
            31 => "Texas",
            _ => null
        };
        if (importedVenue != null)
        {
            if (importedScenery == null)
                return null;
            foreach (var renderer in importedScenery.GetComponentsInChildren<MeshRenderer>(true))
                if (IsImportedStand(trackId, renderer.name))
                    importedGrandstandRenderers.Add(renderer);

            var authored = CreateChild(parent, layer, importedVenue + " Grandstands");
            if (trackId != 27)
            {
                var importedAttendance = authored.AddComponent<GrandstandAttendance>();
                importedAttendance.RegisterImportedSeats = () => ImportedGrandstandSeating.Register(trackId,
                    importedScenery, parent, importedAttendance, lapLength, sampleEdge);
            }
            // Bristol's FBX already contains the paved connection from its wall to the facade.
            if (trackId == 30)
                AddTrackWalkway(authored.transform, -.188f, .149f, 18.5f, ground + .06f);
            else if (trackId == 31)
                AddTrackWalkway(authored.transform, -.188f, .197f, 6.3f, ground + .06f);
            return authored;
        }

        if (lapLength <= TrackDefinition.MetersPerMile * .5f || !LargeVenues.TryGetValue(trackId, out var venue))
            return null;

        var root = CreateChild(parent, layer, venue.Venue + " Grandstands");
        var attendance = root.AddComponent<GrandstandAttendance>();
        var palette = new Dictionary<(Surface, Color32), Material>();
        foreach (var run in venue.Runs)
        {
            var section = CreateChild(root.transform, layer, run.Name);
            float start = (finishFraction + run.Start) * lapLength;
            float end = (finishFraction + run.End) * lapLength;
            float foundation = ground + run.Foundation;
            for (int point = 0; point <= 8; point++)
            {
                float station = Mathf.Lerp(start, end, point / 8f);
                foundation = Mathf.Max(foundation, sampleEdge(station).Position.y + .02f);
            }
            if (run.HasWalkway)
                AddTrackWalkway(section.transform, run.Start, run.End, run.Setback, foundation);

            var nearBatch = new StandMeshBatch();
            var farBatch = new StandMeshBatch();
            int bays = Mathf.CeilToInt((end - start) / run.BayLength);
            for (int bay = 0; bay < bays; bay++)
            {
                var first = sampleEdge(Mathf.Lerp(start, end, (float)bay / bays));
                var last = sampleEdge(Mathf.Lerp(start, end, (float)(bay + 1) / bays));
                Vector3 a = first.Position + first.Outward * run.Setback;
                Vector3 b = last.Position + last.Outward * run.Setback;
                a.y = b.y = foundation;
                if (Vector3.Dot(b - a, Vector3.Cross(Vector3.up, first.Outward)) < 0f)
                {
                    (a, b) = (b, a);
                    (first, last) = (last, first);
                }

                float frontage = Vector3.Distance(a, b);
                if (frontage <= 3f + BayClearance)
                    continue;
                var geometry = GrandstandMeshBuilder.Build(frontage - BayClearance, run.Depth, run.Rows, run.Rise,
                    .65f, run.Chairs, frontage > 25f ? 2 : 1, run.SuiteFloors, 7.2f,
                    seats => attendance.Register(run.Name, seats, a, b, first.Outward, last.Outward, frontage));
                Color32 benchColor = bay % 4 == 3 ? run.Upper : run.Lower;
                nearBatch.Add(geometry.Near, a, b, first.Outward, last.Outward, frontage, benchColor, run,
                    palette, makeMaterial);
                farBatch.Add(geometry.Far, a, b, first.Outward, last.Outward, frontage, benchColor, run,
                    palette, makeMaterial);
            }

            MeshRenderer near = nearBatch.Finish(section.transform, layer, "Grandstand detail", ownedMeshes);
            MeshRenderer far = farBatch.Finish(section.transform, layer, "Grandstand rows", ownedMeshes);
            if (near != null && far != null)
            {
                var lod = section.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(.19f, new Renderer[] { near }), new LOD(.004f, new Renderer[] { far }) });
                lod.RecalculateBounds();
            }
        }
        return root;

        void AddTrackWalkway(Transform walkwayParent, float start, float end, float setback, float foundation)
        {
            var material = makeMaterial(new Color32(173, 165, 153, 255));
            material.name = "Grandstand walkway concrete";
            material.SetFloat("_Smoothness", .12f);
            // These venues already have a wall at the sampled outer pavement edge.
            float innerOffset = trackId is 4 or 21 or 30 or 31 or 32 or 34 or TrackPreview.IowaTrackId ? ConcreteInsideWall : .015f;
            AddWalkway(walkwayParent, layer, (finishFraction + start) * lapLength,
                (finishFraction + end) * lapLength, setback, foundation, ground, sampleEdge,
                material, ownedMeshes, innerOffset, .02f);
        }
    }

    internal static void SetAttendance(GameObject grandstands, RaceSessionKind session, Func<Color, Material> makeMaterial,
        ICollection<Mesh> meshes, ICollection<Texture2D> textures)
    {
        if (grandstands != null && grandstands.TryGetComponent<GrandstandAttendance>(out var attendance))
            attendance.SetSession(session, makeMaterial, meshes, textures);
    }

    private static bool IsImportedStand(int trackId, string name)
    {
        return trackId switch
        {
            27 => name.StartsWith("Seating section", StringComparison.Ordinal) ||
                name.StartsWith("Concourse and facade", StringComparison.Ordinal) ||
                name.StartsWith("Suite glazing", StringComparison.Ordinal) ||
                name.StartsWith("Suite roof", StringComparison.Ordinal),
            28 => name.StartsWith("Bleacher ", StringComparison.Ordinal) ||
                name.StartsWith("Red grandstand fascia", StringComparison.Ordinal) ||
                name.StartsWith("Grandstand booth", StringComparison.Ordinal),
            29 => name.Contains("grandstand ", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Frontstretch suite building ", StringComparison.Ordinal),
            30 => name.StartsWith("Grandstand ", StringComparison.Ordinal) ||
                name.StartsWith("Daytona Rising exterior facade", StringComparison.Ordinal) ||
                name.StartsWith("Suite glazing band", StringComparison.Ordinal) ||
                name.StartsWith("Fan injector", StringComparison.Ordinal),
            31 => name.StartsWith("Grandstand ", StringComparison.Ordinal),
            _ => false
        };
    }

    private sealed class StandMeshBatch
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly Dictionary<Material, List<int>> groups = new Dictionary<Material, List<int>>();

        internal void Add(GrandstandMeshBuilder.SectionMesh section, Vector3 frontA, Vector3 frontB,
            Vector3 outwardA, Vector3 outwardB, float frontage, Color32 benchColor, StandRun run,
            Dictionary<(Surface, Color32), Material> palette, Func<Color, Material> makeMaterial)
        {
            Mesh mesh = section.Mesh;
            int offset = vertices.Count;
            foreach (Vector3 vertex in mesh.vertices)
            {
                float position = vertex.x / frontage + .5f;
                Vector3 outward = Vector3.LerpUnclamped(outwardA, outwardB, position).normalized;
                vertices.Add(Vector3.LerpUnclamped(frontA, frontB, position) + outward * vertex.z +
                    Vector3.up * vertex.y);
            }

            for (int submesh = 0; submesh < section.Surfaces.Length; submesh++)
            {
                Surface surface = section.Surfaces[submesh];
                Material material = StandMaterial(surface, run, benchColor, palette, makeMaterial);
                if (!groups.TryGetValue(material, out var indices))
                {
                    indices = new List<int>();
                    groups.Add(material, indices);
                }
                foreach (int index in mesh.GetTriangles(submesh))
                    indices.Add(offset + index);
            }

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(mesh);
            else
                UnityEngine.Object.DestroyImmediate(mesh);
        }

        internal MeshRenderer Finish(Transform parent, int layer, string name, ICollection<Mesh> ownedMeshes)
        {
            if (vertices.Count == 0)
                return null;
            var mesh = new Mesh
            {
                name = parent.name + " " + name,
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                subMeshCount = groups.Count
            };
            mesh.SetVertices(vertices);
            var materials = new Material[groups.Count];
            int submesh = 0;
            foreach (var group in groups)
            {
                materials[submesh] = group.Key;
                mesh.SetTriangles(group.Value, submesh++, false);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ownedMeshes.Add(mesh);
            return AddMesh(parent, layer, name, mesh, materials);
        }
    }

    private static Material StandMaterial(Surface surface, StandRun run, Color32 benchColor,
        Dictionary<(Surface, Color32), Material> palette, Func<Color, Material> makeMaterial)
    {
        Color32 color = surface switch
        {
            Surface.Concrete => new Color32(161, 155, 145, 255),
            Surface.Steel => new Color32(76, 89, 100, 255),
            Surface.Aluminum => benchColor,
            Surface.LowerSeats => run.Lower,
            Surface.UpperSeats => run.Upper,
            Surface.Rails => new Color32(166, 172, 173, 255),
            Surface.Glass => new Color32(44, 74, 84, 255),
            Surface.SuiteTrim => new Color32(220, 217, 207, 255),
            _ => new Color32(40, 45, 50, 255),
        };
        if (palette.TryGetValue((surface, color), out var existing))
            return existing;

        Material material = makeMaterial(color);
        material.name = "Grandstand " + surface;
        material.SetFloat("_Smoothness", surface == Surface.Glass ? .72f : .23f);
        palette.Add((surface, color), material);
        return material;
    }

    private static void AddWalkway(Transform parent, int layer, float start, float end, float setback,
        float foundation, float ground, Func<float, (Vector3 Position, Vector3 Outward)> sampleEdge,
        Material material, ICollection<Mesh> ownedMeshes, float innerOffset = ConcreteInsideWall, float innerLift = -.04f)
    {
        // Wedge sections share the bay-front chord. Paving continues under the first row.
        int steps = Mathf.CeilToInt((end - start) / 1.35f);
        var vertices = new List<Vector3>(steps * 12 + 8);
        var triangles = new List<int>(steps * 18 + 12);
        var previous = WalkwayRow(start);
        AddQuad(previous.Inner, previous.Outer, Bottom(previous.Outer), Bottom(previous.Inner),
            previous.Inner - WalkwayRow(start + 1f).Inner);
        for (int i = 1; i <= steps; i++)
        {
            var next = WalkwayRow(Mathf.Lerp(start, end, (float)i / steps));
            AddQuad(previous.Inner, next.Inner, next.Outer, previous.Outer, Vector3.up);
            Vector3 outward = previous.Outer - previous.Inner;
            AddQuad(previous.Inner, next.Inner, Bottom(next.Inner), Bottom(previous.Inner), -outward);
            AddQuad(previous.Outer, next.Outer, Bottom(next.Outer), Bottom(previous.Outer), outward);
            if (i == steps)
                AddQuad(next.Inner, next.Outer, Bottom(next.Outer), Bottom(next.Inner), next.Inner - previous.Inner);
            previous = next;
        }

        var mesh = new Mesh { name = "Outside wall concrete walkway" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        ownedMeshes.Add(mesh);
        AddMesh(parent, layer, "Concrete walkway", mesh, new[] { material });

        (Vector3 Inner, Vector3 Outer) WalkwayRow(float station)
        {
            var edge = sampleEdge(station);
            Vector3 inner = edge.Position + edge.Outward * innerOffset;
            inner.y = Mathf.Max(ground + .025f, edge.Position.y + innerLift);
            Vector3 outer = edge.Position + edge.Outward * (setback + 1.2f);
            outer.y = foundation;
            return (inner, outer);
        }

        Vector3 Bottom(Vector3 point) => new Vector3(point.x, ground - .12f, point.z);

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            bool facing = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f;
            triangles.Add(first);
            triangles.Add(first + (facing ? 1 : 2));
            triangles.Add(first + (facing ? 2 : 1));
            triangles.Add(first);
            triangles.Add(first + (facing ? 2 : 3));
            triangles.Add(first + (facing ? 3 : 2));
        }
    }

    private static MeshRenderer AddMesh(Transform parent, int layer, string name, Mesh mesh, Material[] materials)
    {
        var item = CreateChild(parent, layer, name);
        item.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = item.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        return renderer;
    }

    private static GameObject CreateChild(Transform parent, int layer, string name)
    {
        var item = new GameObject(name) { layer = layer };
        item.transform.SetParent(parent, false);
        return item;
    }
}
