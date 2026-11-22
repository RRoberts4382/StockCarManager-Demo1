using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class TrackPreview
{
    private void AddIowaTrackVehicles()
    {
        if (customLayout == null)
            return;

        const float infieldY = .02f;
        AddHaulerRows();
        var site = new IowaVehicleSite(transform);
        var parked = AddRecreationalVehicles();
        AddIowaParkingLots(site, parked.Surfaces);
        AddIowaCampDetails(site, parked.Spaces, parked.Surfaces);

        void AddHaulerRows()
        {
            var prefab = Resources.Load<GameObject>("Startup/RaceHauler");
            if (prefab == null)
            {
                Debug.LogWarning("Iowa Speedway could not load the race hauler model.", this);
                return;
            }

            var group = CreateObject("Iowa parked haulers", transform).transform;
            var paint = new MaterialPropertyBlock();
            int number = 1;
            foreach (float x in new[] { 247f, 351f })
            {
                Vector3 center = IowaLayoutPoint(x, 279.5f, infieldY);
                Vector3 away = (center - IowaLayoutPoint(309f, 279.5f, infieldY)).normalized;
                bool tractorFacesForward = true;
                for (int bay = 0; bay < 20; bay++)
                {
                    var hauler = Instantiate(prefab, group, false);
                    hauler.name = $"Iowa race hauler {number}";
                    hauler.transform.localPosition = center + Vector3.forward * ((9.5f - bay) * 4.8f);
                    if (bay == 0)
                    {
                        var tractor = hauler.transform.Find("Tractor")?.GetComponent<Renderer>();
                        var trailer = hauler.transform.Find("Trailer")?.GetComponent<Renderer>();
                        if (tractor != null && trailer != null)
                        {
                            Vector3 delta = tractor.bounds.center - trailer.bounds.center;
                            tractorFacesForward = hauler.transform.InverseTransformDirection(delta).z > 0f;
                        }
                    }
                    hauler.transform.localRotation = Quaternion.LookRotation(tractorFacesForward ? away : -away);
                    foreach (var child in hauler.GetComponentsInChildren<Transform>(true))
                        child.gameObject.layer = PreviewLayer;

                    var livery = TeamLivery(number++);
                    Color roofColor = Color.Lerp(livery.RoofAndSides, livery.HoodAndTrunk, .25f);
                    foreach (var renderer in hauler.GetComponentsInChildren<MeshRenderer>())
                    {
                        Material[] slots = renderer.sharedMaterials;
                        for (int slot = 0; slot < slots.Length; slot++)
                        {
                            if (slots[slot] == null)
                                continue;
                            bool primary = slots[slot].name.StartsWith("Hauler Primary Paint", StringComparison.Ordinal);
                            bool roof = slots[slot].name.StartsWith("Hauler Roof Paint", StringComparison.Ordinal);
                            bool accent = slots[slot].name.StartsWith("Hauler Accent Paint", StringComparison.Ordinal);
                            if (!primary && !roof && !accent)
                                continue;

                            Color color = roof ? roofColor : primary ? livery.RoofAndSides : livery.HoodAndTrunk;
                            paint.Clear();
                            paint.SetColor("_BaseColor", color);
                            paint.SetColor("_Color", color);
                            paint.SetFloat("_Smoothness", roof ? .16f : .28f);
                            renderer.SetPropertyBlock(paint, slot);
                        }
                    }
                }
            }
        }

        (List<IowaParkingSpace> Spaces, Material[] Surfaces) AddRecreationalVehicles()
        {
            const float parkingPitch = 4.8f;
            var geometry = new IowaRvMesh();
            var spaces = new List<IowaParkingSpace>();
            var parking = new System.Random(350217);

            // Keep the seven supplied RV rows, shifting their centers by the nearby road clearance.
            AddRow(infieldY, new Vector2(405f, 280f), new Vector2(503f, 234f));
            AddRow(infieldY, new Vector2(415f, 295f), new Vector2(485f, 262f));
            AddRow(infieldY, new Vector2(417f, 313f), new Vector2(479f, 292f));
            AddRow(infieldY, new Vector2(384.5f, 295.5f), new Vector2(387f, 315f));
            AddRow(infieldY, new Vector2(413f, 341.5f), new Vector2(441f, 341f), new Vector2(464f, 337.5f),
                new Vector2(479f, 334f));
            AddRow(infieldY, new Vector2(515f, 341f), new Vector2(536f, 325f), new Vector2(554f, 305f),
                new Vector2(570f, 279f));
            AddRow(groundHeight, new Vector2(401f, 390.5f), new Vector2(471f, 390.5f), new Vector2(508f, 386f),
                new Vector2(537f, 378f), new Vector2(559f, 367.5f), new Vector2(576f, 352.5f),
                new Vector2(590f, 334f), new Vector2(606f, 307f));

            Material[] surfaces =
            {
                MakeMaterial(new Color32(225, 223, 213, 255)),
                MakeMaterial(new Color32(177, 172, 153, 255)),
                MakeMaterial(new Color32(185, 196, 204, 255)),
                MakeMaterial(new Color32(214, 204, 188, 255)),
                MakeMaterial(new Color32(92, 121, 127, 255)),
                MakeMaterial(new Color32(136, 101, 73, 255)),
                MakeMaterial(new Color32(96, 107, 128, 255)),
                MakeMaterial(new Color32(141, 110, 98, 255)),
                MakeMaterial(new Color32(46, 61, 69, 255)),
                MakeMaterial(new Color32(42, 42, 41, 255)),
                MakeMaterial(new Color32(173, 175, 169, 255)),
                MakeMaterial(new Color32(137, 49, 43, 255))
            };
            surfaces[8].SetFloat("_Smoothness", .45f);
            AddIowaVehicleMesh("Iowa RV parking", geometry, surfaces);
            return (spaces, surfaces);

            void AddRow(float surface, params Vector2[] pixels)
            {
                var points = new Vector3[pixels.Length];
                var distances = new float[pixels.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = IowaLayoutPoint(pixels[i].x, pixels[i].y, surface);
                    if (i > 0)
                        distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
                }

                float length = distances[distances.Length - 1];
                int count = Mathf.Max(1, Mathf.FloorToInt(length / parkingPitch));
                int segment = 1;
                for (int i = 0; i < count; i++)
                {
                    float station = (i + .5f) * length / count;
                    while (segment < points.Length - 1 && distances[segment] < station)
                        segment++;
                    var position = Vector3.Lerp(points[segment - 1], points[segment],
                        Mathf.InverseLerp(distances[segment - 1], distances[segment], station));
                    float trackStation = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, position);
                    Vector3 facing = TrackPoint(trackStation, 0f, out _, out _) - position;
                    facing.y = 0f;
                    float yaw = 1.5f + (float)parking.NextDouble() * 3.5f;
                    if (parking.Next(2) == 0) yaw = -yaw;
                    IowaParkingSpace? parked = geometry.AddRv(site, position, Quaternion.LookRotation(facing), yaw);
                    if (parked.HasValue) spaces.Add(parked.Value);
                }
            }
        }
    }

    private sealed class IowaRvMesh
    {
        private const int Glass = 8;
        private const int Tire = 9;
        private const int Metal = 10;
        private const int TailLight = 11;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int>[] triangles = new List<int>[12];
        private readonly System.Random appearance = new System.Random(35187);
        private Vector3 origin;
        private Quaternion rotation;

        internal IowaRvMesh()
        {
            for (int i = 0; i < triangles.Length; i++)
                triangles[i] = new List<int>();
        }

        internal IowaParkingSpace? AddRv(IowaVehicleSite site, Vector3 position, Quaternion heading, float yaw)
        {
            int style = appearance.Next(3);
            int body = appearance.Next(4);
            int accent = 4 + appearance.Next(4);
            float halfWidth = style == 1 ? 1.17f : 1.30f;
            float halfLength = (style == 0 ? 5.45f : style == 1 ? 4.55f : 4.05f) +
                (float)appearance.NextDouble() * .35f;
            float top = style == 0 ? 3.55f : style == 1 ? 3.37f : 3.06f;
            float halfDepth = halfLength + (style == 2 ? .78f : .18f);
            var space = new IowaParkingSpace(position, Quaternion.Euler(0f, yaw, 0f) * heading, halfWidth + .13f, halfDepth);
            if (!site.TryReserve(space))
            {
                space = new IowaParkingSpace(position, Quaternion.Euler(0f, -yaw, 0f) * heading, halfWidth + .13f, halfDepth);
                if (!site.TryReserve(space))
                {
                    space = new IowaParkingSpace(position, heading, halfWidth + .13f, halfDepth);
                    if (!site.TryReserve(space)) return null;
                }
            }

            origin = space.Center;
            rotation = space.Heading;
            float bodyFront = style == 2 ? halfLength : halfLength - .70f;

            Box(-halfWidth, .68f, -halfLength, halfWidth, top - .12f, bodyFront, body);
            Box(-halfWidth + .04f, top - .12f, -halfLength + .09f, halfWidth - .04f, top, bodyFront - .09f, body);
            Box(-halfWidth - .015f, 1.10f, -halfLength, halfWidth + .015f, 1.31f, bodyFront, accent);
            Box(-halfWidth - .015f, 1.63f, -halfLength, halfWidth + .015f, 1.83f, bodyFront, accent);
            Box(-halfWidth + .04f, .53f, -halfLength - .08f, halfWidth - .04f, .74f, -halfLength, Metal);
            Box(-.42f, top, -1.45f, .42f, top + .22f, -.53f, Metal);
            Box(-.27f, top, 1.13f, .27f, top + .10f, 1.67f, Glass);
            Box(-.73f, 2.04f, -halfLength - .025f, .73f, 2.62f, -halfLength, Glass);
            for (int side = -1; side <= 1; side += 2)
            {
                float inside = side * (halfWidth - .025f);
                float outside = side * (halfWidth + .028f);
                float left = Mathf.Min(inside, outside);
                float right = Mathf.Max(inside, outside);
                for (int w = 0; w < 3; w++)
                {
                    float z = -halfLength + .73f + w * 1.61f;
                    Box(left, 2.04f, z, right, 2.58f, z + (w == 1 ? 1.20f : .94f), Glass);
                }
                Box(left, .70f, bodyFront - 1.40f, right, 2.58f, bodyFront - .84f, accent);
                Box(left, 2.02f, bodyFront - 1.32f, right, 2.49f, bodyFront - .92f, Glass);
                Box(side * .99f - .19f, 1.02f, -halfLength - .022f,
                    side * .99f + .19f, 1.18f, -halfLength, TailLight);
                Box(side * .85f - .13f, .68f, halfLength, side * .85f + .13f, .84f, halfLength + .018f, Metal);
                AddWheel(side * (halfWidth - .20f), -halfLength * .62f);
                AddWheel(side * (halfWidth - .20f), style == 2 ? -halfLength * .36f : halfLength * .65f);
            }

            if (style == 2)
            {
                Box(-.49f, 2.27f, halfLength, .49f, 2.68f, halfLength + .025f, Glass);
                Box(-.13f, .34f, halfLength, .13f, .48f, halfLength + .70f, Metal);
                return space;
            }

            float windshieldBottom = style == 0 ? 1.72f : 1.55f;
            float windshieldTop = style == 0 ? 2.98f : 2.30f;
            Box(-halfWidth + .05f, .67f, bodyFront, halfWidth - .05f, windshieldTop + .15f, halfLength, body);
            Box(-halfWidth + .13f, windshieldBottom, halfLength, halfWidth - .13f, windshieldTop, halfLength + .018f, Glass);
            Box(-halfWidth + .08f, .62f, halfLength, halfWidth - .08f, .78f, halfLength + .075f, accent);
            if (style == 1)
                Box(-halfWidth, 2.66f, bodyFront - .15f, halfWidth, top - .18f, halfLength + .15f, body);
            return space;
        }

        internal void AddParkedCar(Vector3 position, Quaternion heading, int style, float halfLength, int color)
        {
            origin = position;
            rotation = heading;
            float width = style == 0 ? .91f : .96f;
            float rear = -halfLength;
            float front = halfLength;
            float shoulder = style == 1 ? .94f : .86f;
            Box(-width, .36f, rear + .12f, width, shoulder, front - .10f, color);
            Box(-width + .04f, .42f, rear, width - .04f, .68f, rear + .14f, color);
            Box(-width + .07f, .42f, front - .12f, width - .07f, .66f, front, color);
            Box(-width + .06f, .31f, rear + .02f, width - .06f, .42f, rear + .21f, Tire);
            Box(-width + .06f, .31f, front - .22f, width - .06f, .42f, front - .02f, Tire);
            if (style == 1)
            {
                CarCabin(-.63f, 1.05f, shoulder, 1.61f, width * .87f, .15f, .27f, color);
                Box(-width + .13f, .94f, rear + .20f, width - .13f, .97f, -.68f, Tire);
                Box(-width, .92f, rear + .14f, -width + .14f, 1.13f, -.63f, color);
                Box(width - .14f, .92f, rear + .14f, width, 1.13f, -.63f, color);
                Box(-width + .03f, .92f, rear + .13f, width - .03f, 1.12f, rear + .25f, color);
            }
            else if (style == 2)
            {
                CarCabin(-1.66f, 1.09f, shoulder, 1.59f, width * .88f, .14f, .42f, color);
                Box(-.50f, 1.59f, -1.18f, .50f, 1.64f, .65f, Metal);
            }
            else
            {
                CarCabin(-1.22f, 1.04f, shoulder, 1.43f, width * .86f, .54f, .47f, color);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                float x = width * .94f * side;
                AddWheel(x, rear + .80f, .315f, .12f);
                AddWheel(x, front - .81f, .315f, .12f);
                Box(.62f * side - .20f, .62f, front - .125f, .62f * side + .20f, .76f, front - .082f, Metal);
                Box(.65f * side - .17f, .66f, rear + .048f, .65f * side + .17f, .78f, rear + .079f, TailLight);
            }
        }

        private void CarCabin(float rear, float front, float belt, float roof, float halfWidth,
            float rearSlope, float windscreenSlope, int paint)
        {
            var a = new Vector3(-halfWidth, belt, rear);
            var b = new Vector3(halfWidth, belt, rear);
            var c = new Vector3(halfWidth, belt, front);
            var d = new Vector3(-halfWidth, belt, front);
            var e = new Vector3(-halfWidth + .11f, roof, rear + rearSlope);
            var f = new Vector3(halfWidth - .11f, roof, rear + rearSlope);
            var g = new Vector3(halfWidth - .11f, roof, front - windscreenSlope);
            var h = new Vector3(-halfWidth + .11f, roof, front - windscreenSlope);
            Quad(e, h, g, f, paint);
            Quad(a, d, h, e, Glass);
            Quad(c, b, f, g, Glass);
            Quad(d, c, g, h, Glass);
            Quad(b, a, e, f, Glass);
        }

        internal void AddLawnChair(Vector3 position, Quaternion heading, int fabric)
        {
            origin = position;
            rotation = heading;
            Box(-.27f, .40f, -.26f, .27f, .44f, .26f, fabric);
            Box(-.265f, .45f, -.295f, .265f, .87f, -.245f, fabric);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * .252f;
                Box(x - .027f, .02f, -.259f, x + .027f, .88f, -.214f, Metal);
                Box(x - .027f, .02f, .218f, x + .027f, .58f, .269f, Metal);
                Box(x - .034f, .58f, -.245f, x + .034f, .635f, .232f, Metal);
            }
        }

        internal void AddGrill(Vector3 position, Quaternion heading, bool red)
        {
            origin = position;
            rotation = heading;
            int paint = red ? TailLight : Tire;
            Box(-.32f, .62f, -.25f, .32f, .83f, .25f, Tire);
            Box(-.279f, .83f, -.216f, .279f, 1.04f, .216f, paint);
            Box(-.155f, .918f, .222f, .155f, .964f, .292f, Metal);
            Box(-.33f, .27f, -.237f, .33f, .315f, .237f, Metal);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * .265f;
                Box(x - .027f, .015f, -.212f, x + .027f, .64f, -.156f, Metal);
                Box(x - .027f, .015f, .156f, x + .027f, .64f, .212f, Metal);
            }
        }

        internal void AddGolfCart(Vector3 position, Quaternion heading, int paint)
        {
            origin = position;
            rotation = heading;
            Box(-.62f, .29f, -1.14f, .62f, .52f, 1.14f, paint);
            Box(-.56f, .49f, .63f, .56f, .72f, 1.08f, paint);
            Box(-.53f, .60f, -.63f, .53f, .79f, -.09f, Tire);
            Box(-.53f, .79f, -.61f, .53f, 1.03f, -.49f, Tire);
            Box(-.60f, 1.64f, -.81f, .60f, 1.73f, .76f, paint);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * .53f;
                Box(x - .024f, .42f, -.69f, x + .024f, 1.67f, -.644f, Metal);
                Box(x - .024f, .42f, .62f, x + .024f, 1.67f, .668f, Metal);
                AddWheel(x, -.74f, .229f, .105f);
                AddWheel(x, .72f, .229f, .105f);
            }
        }

        internal void AddTent(Vector3 position, Quaternion heading, int fabric)
        {
            origin = position;
            rotation = heading;
            var a = new Vector3(-1.09f, .03f, -1.29f);
            var b = new Vector3(1.09f, .03f, -1.29f);
            var c = new Vector3(1.09f, .03f, 1.29f);
            var d = new Vector3(-1.09f, .03f, 1.29f);
            var ridgeRear = new Vector3(0f, 1.43f, -1.29f);
            var ridgeFront = new Vector3(0f, 1.43f, 1.29f);
            Quad(a, d, ridgeFront, ridgeRear, fabric);
            Quad(c, b, ridgeRear, ridgeFront, fabric);
            Triangle(b, a, ridgeRear, fabric);
            Triangle(d, c, ridgeFront, fabric);
            Triangle(new Vector3(-.60f,.07f,1.295f), new Vector3(.60f,.07f,1.295f),
                new Vector3(0f,1.06f,1.295f), Tire);
        }
        internal bool HasGeometry => vertices.Count > 0;

        internal Mesh Build()
        {
            var mesh = new Mesh
            {
                name = "Iowa campground vehicles",
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
                subMeshCount = triangles.Length
            };
            mesh.SetVertices(vertices);
            for (int i = 0; i < triangles.Length; i++)
                mesh.SetTriangles(triangles[i], i, false);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private void AddWheel(float x, float z, float radius = .44f, float halfDepth = .18f)
        {
            Vector3 Ring(float axis, float angle) => new Vector3(axis, radius + radius * Mathf.Cos(angle),
                z + radius * Mathf.Sin(angle));
            Vector3 near = new Vector3(x - halfDepth, radius, z);
            Vector3 far = new Vector3(x + halfDepth, radius, z);
            for (int i = 0; i < 8; i++)
            {
                float a = i * (Mathf.PI * .25f);
                float b = (i + 1) * (Mathf.PI * .25f);
                Vector3 aNear = Ring(near.x, a), bNear = Ring(near.x, b);
                Vector3 aFar = Ring(far.x, a), bFar = Ring(far.x, b);
                Quad(aNear, bNear, bFar, aFar, Tire);
                Triangle(near, bNear, aNear, Tire);
                Triangle(far, aFar, bFar, Tire);
            }
        }

        private void Box(float x0, float y0, float z0, float x1, float y1, float z1, int material)
        {
            var a = new Vector3(x0, y0, z0);
            var b = new Vector3(x1, y0, z0);
            var c = new Vector3(x1, y0, z1);
            var d = new Vector3(x0, y0, z1);
            var e = new Vector3(x0, y1, z0);
            var f = new Vector3(x1, y1, z0);
            var g = new Vector3(x1, y1, z1);
            var h = new Vector3(x0, y1, z1);

            Quad(a, b, c, d, material);
            Quad(e, h, g, f, material);
            Quad(a, d, h, e, material);
            Quad(c, b, f, g, material);
            Quad(d, c, g, h, material);
            Quad(b, a, e, f, material);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int material)
        {
            int start = vertices.Count;
            vertices.Add(origin + rotation * a);
            vertices.Add(origin + rotation * b);
            vertices.Add(origin + rotation * c);
            vertices.Add(origin + rotation * d);
            var slot = triangles[material];
            slot.Add(start);
            slot.Add(start + 1);
            slot.Add(start + 2);
            slot.Add(start);
            slot.Add(start + 2);
            slot.Add(start + 3);
        }

        private void Triangle(Vector3 a, Vector3 b, Vector3 c, int material)
        {
            var slot = triangles[material];
            slot.Add(vertices.Count);
            vertices.Add(origin + rotation * a);
            slot.Add(vertices.Count);
            vertices.Add(origin + rotation * b);
            slot.Add(vertices.Count);
            vertices.Add(origin + rotation * c);
        }
    }

    private void AddIowaVehicleMesh(string name, IowaRvMesh geometry, Material[] surfaces)
    {
        if (!geometry.HasGeometry)
            return;
        Mesh mesh = geometry.Build();
        mesh.name = name;
        roadMeshes.Add(mesh);
        var group = CreateObject(name, transform);
        group.AddComponent<MeshFilter>().sharedMesh = mesh;
        group.AddComponent<MeshRenderer>().sharedMaterials = surfaces;
    }

    private void AddIowaParkingLots(IowaVehicleSite site, Material[] surfaces)
    {
        var asphalt = MakeMaterial(new Color32(70, 71, 75, 255));
        asphalt.SetTexture("_BaseMap", BristolPavementTexture(false));
        var stripe = MakeMaterial(new Color32(222, 221, 208, 255));
        var random = new System.Random(35165);
        AddLot("Iowa north garage-court parking", IowaLayoutPoint(297f, 219.5f, 0f),
            Vector3.right, Vector3.forward, 32.7f, 10.2f, false);
        AddLot("Iowa west infield parking", IowaLayoutPoint(207.5f, 264.5f, 0f),
            Vector3.forward, Vector3.right, 31.8f, 14.4f, true);

        void AddLot(string name, Vector3 center, Vector3 along, Vector3 across, float halfLength,
            float halfWidth, bool west)
        {
            const float stallPitch = 3.32f;
            const float rowCenter = 6.30f;
            const float markingDepth = 2.68f;
            Quaternion orientation = Quaternion.LookRotation(across, Vector3.up);
            var footprint = new IowaParkingSpace(center, orientation, halfLength, halfWidth);
            if (!site.FitsBuiltGeometry(footprint))
                return;

            if (west)
            {
                // The central driveway meets the existing broad garage yard; the
                // eastern parking row leaves a two-way entry there.
                Vector3 outsideEdgeNorth = YardEdge(264.5f - 4.35f / IowaMetersPerPixel);
                Vector3 outsideEdgeSouth = YardEdge(264.5f + 4.35f / IowaMetersPerPixel);
                AddRoadMesh(name + " asphalt", asphalt, new[]
                {
                    center - along * halfLength - across * halfWidth,
                    center + along * halfLength - across * halfWidth,
                    center + along * halfLength + across * halfWidth,
                    center + along * 4.35f + across * halfWidth,
                    outsideEdgeNorth,
                    outsideEdgeSouth,
                    center - along * 4.35f + across * halfWidth,
                    center - along * halfLength + across * halfWidth
                }, new[] { 0, 1, 2, 0, 2, 3, 0, 3, 6, 0, 6, 7, 6, 3, 4, 6, 4, 5 });
                Vector3 YardEdge(float y) => IowaLayoutPoint(Mathf.Lerp(220f, 224f, (288f - y) / 47f), y, 0f);
            }

            var vehicles = new IowaRvMesh();
            var marks = new List<Vector3>();
            var triangles = new List<int>();
            var bayCount = Mathf.FloorToInt((halfLength * 2f - 9f) / stallPitch);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < bayCount; i++)
                {
                    float station = (i + .5f - bayCount * .5f) * stallPitch;
                    if (west && side > 0 && Mathf.Abs(station) < 6.2f)
                        continue;
                    var stall = center + along * station + across * side * rowCenter;
                    var markedSpace = new IowaParkingSpace(stall, orientation, stallPitch * .5f, markingDepth);
                    if (!site.IsClear(markedSpace))
                        continue;
                    AddMark(stall - along * stallPitch * .5f);
                    AddMark(stall + along * stallPitch * .5f);
                    if (random.Next(6) == 0)
                        continue;
                    int style = random.Next(3);
                    float length = style == 1 ? 2.34f : 2.19f;
                    Quaternion heading = Quaternion.LookRotation(across * (random.Next(3) == 0 ? side : -side))
                        * Quaternion.Euler(0f, (float)random.NextDouble() * 4f - 2f, 0f);
                    var parked = new IowaParkingSpace(stall + Vector3.up * .01f, heading, 1.1f, length + .12f);
                    if (site.TryReserve(parked))
                        vehicles.AddParkedCar(parked.Center, heading, style, length, random.Next(8));
                }
            }
            if (marks.Count > 0)
                AddRoadMesh(name + " bay markings", stripe, marks.ToArray(), triangles.ToArray());
            AddIowaVehicleMesh(name + " cars", vehicles, surfaces);

            void AddMark(Vector3 point)
            {
                const float halfLine = .033f;
                // Adjacent bays have one shared line, with no coincident faces.
                Vector3 anchor = point + Vector3.up * .017f;
                if (marks.Exists(previous => (previous - (anchor - along * halfLine - across * markingDepth)).sqrMagnitude < .0001f))
                    return;
                int index = marks.Count;
                marks.Add(anchor - along * halfLine - across * markingDepth);
                marks.Add(anchor + along * halfLine - across * markingDepth);
                marks.Add(anchor + along * halfLine + across * markingDepth);
                marks.Add(anchor - along * halfLine + across * markingDepth);
                bool upward = Vector3.Cross(along, across).y > 0f;
                triangles.AddRange(upward
                    ? new[] { index, index + 1, index + 2, index, index + 2, index + 3 }
                    : new[] { index, index + 2, index + 1, index, index + 3, index + 2 });
            }
        }
    }

    private void AddIowaCampDetails(IowaVehicleSite site, List<IowaParkingSpace> spaces, Material[] surfaces)
    {
        var chairs = new IowaRvMesh();
        var grills = new IowaRvMesh();
        var carts = new IowaRvMesh();
        var tents = new IowaRvMesh();
        var random = new System.Random(352781);
        for (int i = 1; i < spaces.Count; i += 2)
        {
            IowaParkingSpace camper = spaces[i];
            if (random.Next(5) == 0)
                continue;
            if (TryFindSite(camper, .34f, .40f, out var chair))
                chairs.AddLawnChair(chair.Center, chair.Heading, 4 + random.Next(4));
            if (i % 3 != 1 && TryFindSite(camper, .43f, .39f, out var grill))
                grills.AddGrill(grill.Center, grill.Heading, i % 5 == 0);
            if (i % 5 == 1 && TryFindSite(camper, .71f, 1.23f, out var cart))
                carts.AddGolfCart(cart.Center, cart.Heading, random.Next(8));
            if (i % 11 == 1 && TryFindSite(camper, 1.15f, 1.38f, out var tent))
                tents.AddTent(tent.Center, tent.Heading, 4 + random.Next(4));
            if (TryFindSite(camper, .39f, .39f, out var visitor))
                QueueIowaStandingFan(visitor.Center, (camper.Center - visitor.Center).normalized, 35191 + i * 311);
            if (i % 7 == 1 && TryFindSite(camper, .44f, 1.35f, out var walker))
                QueueIowaWalkingFan(walker.Center - walker.Along * .95f, walker.Center + walker.Along * .95f, 35423 + i * 557);
        }
        AddIowaVehicleMesh("Iowa campsite lawn chairs", chairs, surfaces);
        AddIowaVehicleMesh("Iowa campsite barbecue grills", grills, surfaces);
        AddIowaVehicleMesh("Iowa campsite golf carts", carts, surfaces);
        AddIowaVehicleMesh("Iowa campsite tents", tents, surfaces);

        bool TryFindSite(IowaParkingSpace camper, float width, float length, out IowaParkingSpace placed)
        {
            float rotation = (float)random.NextDouble() * 36f - 18f;
            int firstSide = random.Next(2) == 0 ? 1 : -1;
            for (int attempt = 0; attempt < 14; attempt++)
            {
                int side = attempt % 2 == 0 ? firstSide : -firstSide;
                bool beyond = attempt >= 8;
                float lateral = beyond ? (attempt / 2 - 5f) * 1.4f * side
                    : (camper.HalfWidth + width + .52f + attempt / 6 * .65f) * side;
                float fore = beyond ? (camper.HalfLength + length + .59f) * (attempt / 4 % 2 == 0 ? 1f : -1f)
                    : (attempt / 2 - 1.5f) * camper.HalfLength * .49f;
                var position = camper.Center + camper.Across * lateral + camper.Along * fore;
                var heading = Quaternion.AngleAxis(rotation + side * 16f, Vector3.up) * camper.Heading;
                placed = new IowaParkingSpace(position, heading, width + .08f, length + .08f);
                if (site.TryReserve(placed))
                    return true;
            }
            placed = default;
            return false;
        }
    }

    private readonly struct IowaParkingSpace
    {
        internal Vector3 Center { get; }
        internal Quaternion Heading { get; }
        internal float HalfWidth { get; }
        internal float HalfLength { get; }
        internal Vector3 Across => Heading * Vector3.right;
        internal Vector3 Along => Heading * Vector3.forward;

        internal IowaParkingSpace(Vector3 center, Quaternion heading, float halfWidth, float halfLength)
        {
            Center = center;
            Heading = heading;
            HalfWidth = halfWidth;
            HalfLength = halfLength;
        }

        internal bool Intersects(IowaParkingSpace other)
        {
            Vector3 delta = Center - other.Center;
            IowaParkingSpace self = this;
            return !Separates(Across) && !Separates(Along) && !Separates(other.Across) && !Separates(other.Along);

            bool Separates(Vector3 axis) => Mathf.Abs(Vector3.Dot(delta, axis)) >= self.Radius(axis) + other.Radius(axis);
        }

        internal bool Intersects(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 right = new Vector2(Across.x, Across.z);
            Vector2 forward = new Vector2(Along.x, Along.z);
            Vector2 center = new Vector2(Center.x, Center.z);
            float halfWidth = HalfWidth, halfLength = HalfLength;
            a -= center;
            b -= center;
            c -= center;
            return !Separates(right) && !Separates(forward) && !Separates(Normal(b - a)) &&
                !Separates(Normal(c - b)) && !Separates(Normal(a - c));

            static Vector2 Normal(Vector2 edge) => new Vector2(edge.y, -edge.x);

            bool Separates(Vector2 axis)
            {
                if (axis.sqrMagnitude < 1e-10f)
                    return false;
                float first = Vector2.Dot(axis, a);
                float second = Vector2.Dot(axis, b);
                float third = Vector2.Dot(axis, c);
                float radius = Mathf.Abs(Vector2.Dot(axis, right)) * halfWidth +
                    Mathf.Abs(Vector2.Dot(axis, forward)) * halfLength;
                return Mathf.Min(first, second, third) > radius || Mathf.Max(first, second, third) < -radius;
            }
        }

        private float Radius(Vector3 axis) =>
            Mathf.Abs(Vector3.Dot(Across, axis)) * HalfWidth + Mathf.Abs(Vector3.Dot(Along, axis)) * HalfLength;
    }

    private sealed class IowaVehicleSite
    {
        private const float CellSize = 16f;
        private readonly Dictionary<Vector2Int, List<(Vector2 A, Vector2 B, Vector2 C)>> route =
            new Dictionary<Vector2Int, List<(Vector2, Vector2, Vector2)>>();
        private readonly List<IowaParkingSpace> reservations = new List<IowaParkingSpace>();

        internal IowaVehicleSite(Transform root)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                string name = filter.name;
                bool protectedSurface = name is "Iowa racing surface" or "Iowa flat apron" or
                    "Iowa apron to pit entrance" or "Iowa pit road" or "Iowa pit exit to apron" or
                    "Iowa east lot access" or "Iowa east return drive" or "Iowa infield south spur" or
                    "Iowa frontstretch infield loop road" or "Iowa infield loop access" or "Iowa pit walkway" ||
                    name.StartsWith("Iowa emergency access ", StringComparison.Ordinal);
                if (!protectedSurface && !(name.StartsWith("Iowa ", StringComparison.Ordinal) &&
                    name.Contains("wall", StringComparison.OrdinalIgnoreCase)))
                    continue;
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                    continue;
                Matrix4x4 toLocal = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var points = mesh.vertices;
                var projected = new Vector2[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 point = toLocal.MultiplyPoint3x4(points[i]);
                    projected[i] = new Vector2(point.x, point.z);
                }
                var triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = projected[triangles[i]];
                    var b = projected[triangles[i + 1]];
                    var c = projected[triangles[i + 2]];
                    var min = new Vector2(Mathf.Min(a.x, b.x, c.x), Mathf.Min(a.y, b.y, c.y));
                    var max = new Vector2(Mathf.Max(a.x, b.x, c.x), Mathf.Max(a.y, b.y, c.y));
                    for (int x = Cell(min.x); x <= Cell(max.x); x++)
                    for (int z = Cell(min.y); z <= Cell(max.y); z++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!route.TryGetValue(key, out var faces))
                            route[key] = faces = new List<(Vector2, Vector2, Vector2)>();
                        faces.Add((a, b, c));
                    }
                }
            }
            foreach (string name in new[] { "Iowa infield garage", "Iowa infield operations",
                "Iowa infield utility building", "Iowa parked haulers" })
            {
                var group = root.Find(name);
                if (group == null)
                    continue;
                foreach (var renderer in group.GetComponentsInChildren<Renderer>())
                {
                    Bounds bounds = renderer.bounds;
                    Vector3 center = root.InverseTransformPoint(bounds.center);
                    reservations.Add(new IowaParkingSpace(center, Quaternion.identity,
                        bounds.extents.x + .8f, bounds.extents.z + .8f));
                }
            }
        }

        internal bool IsClear(IowaParkingSpace space)
        {
            return FitsBuiltGeometry(space) && !reservations.Exists(other => space.Intersects(other));
        }

        internal bool TryReserve(IowaParkingSpace space)
        {
            if (!IsClear(space))
                return false;
            reservations.Add(space);
            return true;
        }

        internal bool FitsBuiltGeometry(IowaParkingSpace space)
        {
            Vector3 center = space.Center;
            Vector3 right = space.Across * space.HalfWidth;
            Vector3 forward = space.Along * space.HalfLength;
            float x = Mathf.Abs(right.x) + Mathf.Abs(forward.x);
            float z = Mathf.Abs(right.z) + Mathf.Abs(forward.z);
            for (int a = Cell(center.x - x); a <= Cell(center.x + x); a++)
            for (int b = Cell(center.z - z); b <= Cell(center.z + z); b++)
                if (route.TryGetValue(new Vector2Int(a, b), out var faces) &&
                    faces.Exists(t => space.Intersects(t.A, t.B, t.C)))
                    return false;
            return true;
        }

        private static int Cell(float coordinate) => Mathf.FloorToInt(coordinate / CellSize);
    }
}