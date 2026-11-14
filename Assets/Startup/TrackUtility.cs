using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal enum TrackUtilityTab { Track, Apron, PitRoad, PitEntry, PitExit, RaceMarkers }

internal sealed class TrackUtility
{
    private readonly TrackCatalog catalog;
    private readonly TrackReferenceFiles references;
    private readonly DropdownField selector = new DropdownField("Track");
    private readonly Label status = new Label();
    private readonly ScrollView form = new ScrollView();
    private readonly List<Button> tabs = new List<Button>();
    private readonly List<Toggle> visibilityToggles = new List<Toggle>();
    private readonly bool[] visible = { true, true, true, true, true, true };
    private bool placingConnector;
    private TrackUtilityTab activeTab;
    private int[] selectedPoints = Array.Empty<int>();
    private readonly TrackUtilityMap map = new TrackUtilityMap();
    private readonly Toggle alignReference = new Toggle("Align Image");
    private readonly Button fitReference;
    private readonly Button clearReference;
    private TrackCatalog.Entry entry;
    private TrackDefinition draft;
    private int mainIndex;
    private int pitIndex;
    private bool placingPitPoint;

    public VisualElement Screen { get; } = new VisualElement();

    public TrackUtility(TrackCatalog catalog, Action back)
    {
        this.catalog = catalog;
        references = new TrackReferenceFiles(catalog.Folder);
        Screen.AddToClassList("screen");
        Screen.AddToClassList("tracks-editor");
        Screen.AddToClassList("track-utility");

        var heading = Row();
        heading.Add(new Label("Track Utility") { name = "utilityTitle" });
        heading.Add(Button("Save Changes", Save));
        heading.Add(Button("Back to Title", back));
        Screen.Add(heading);
        Screen.Add(selector);

        var referenceControls = Row();
        referenceControls.Add(Button("Load Reference Image…", LoadReferenceImage));
        alignReference.RegisterValueChangedCallback(evt => map.SetImageAlignment(evt.newValue));
        referenceControls.Add(alignReference);
        fitReference = Button("Fit Image", map.FitImage);
        referenceControls.Add(fitReference);
        clearReference = Button("Remove Reference Image", RemoveReferenceImage);
        referenceControls.Add(clearReference);
        Screen.Add(referenceControls);
        SetReferenceControls(false);
        selector.RegisterValueChangedCallback(_ => Select(selector.index));
        status.style.whiteSpace = WhiteSpace.Normal;
        Screen.Add(status);

        var body = Row();
        body.AddToClassList("utility-body");
        var sidebar = new VisualElement();
        sidebar.AddToClassList("utility-sidebar");
        var tabList = new VisualElement();
        tabList.AddToClassList("utility-tabs");
        string[] labels = { "Track", "Apron", "Pit Road", "Pit Entry", "Pit Exit", "Restart Zone and Start/Finish" };
        for (int i = 0; i < labels.Length; i++)
        {
            var tab = (TrackUtilityTab)i;
            var button = new Button(() =>
            {
                activeTab = tab;
                placingConnector = false;
                selectedPoints = Array.Empty<int>();
                placingPitPoint = false;
                form.scrollOffset = Vector2.zero;
                BuildForm();
            }) { text = labels[i] };
            button.AddToClassList("utility-tab");
            tabs.Add(button);
            var row = Row();
            row.AddToClassList("utility-tab-row");
            var visibility = new Toggle { value = true, tooltip = $"Show {labels[i]} on the map" };
            visibility.AddToClassList("utility-visibility");
            visibility.RegisterValueChangedCallback(evt =>
            {
                visible[(int)tab] = evt.newValue;
                RefreshMap();
            });
            visibilityToggles.Add(visibility);
            row.Add(visibility);
            row.Add(button);
            tabList.Add(row);
        }
        sidebar.Add(tabList);
        sidebar.Add(form);
        body.Add(sidebar);
        form.AddToClassList("utility-form");
        body.Add(map);
        map.AddToClassList("utility-map");
        Screen.Add(new Label("Drag to pan  /  Shift + drag to select  /  Right-click to clear  /  Ctrl + scroll to zoom  /  Middle-click to reset"));
        Screen.Add(body);
        map.PointClicked += OnMapClick;
        map.SelectionChanged += SelectPoints;
    }

    private void LoadReferenceImage()
    {
        if (draft == null)
        {
            status.text = "Select a track before loading a reference image.";
            return;
        }

        try
        {
            string path = TrackImageFilePicker.Pick();
            if (string.IsNullOrEmpty(path))
                return;

            byte[] bytes = TrackImageTrace.ReadImageBytes(path);
            var image = TrackImageTrace.LoadTexture(bytes);
            try
            {
                references.SaveImage(entry.Definition.id, bytes);
            }
            catch
            {
                UnityEngine.Object.Destroy(image);
                throw;
            }
            map.SetReferenceImage(image);
            SetReferenceControls(true);
            status.text = $"{Path.GetFileName(path)} loaded. Drag or scroll to align the image. Ctrl + drag or scroll moves the view. Turn off Align Image to edit spline points.";
        }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
        {
            status.text = ex.Message;
        }
    }

    public void CloseReferenceImage()
    {
        SaveReferenceAlignment();
        ReleaseReferenceImage();
    }

    private void RemoveReferenceImage()
    {
        try
        {
            references.Delete(entry.Definition.id);
            ReleaseReferenceImage();
            status.text = "Reference image removed.";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            status.text = ex.Message;
        }
    }

    private void ReleaseReferenceImage()
    {
        map.SetReferenceImage(null);
        SetReferenceControls(false);
    }

    private void RestoreReferenceImage()
    {
        string path = references.ImagePath(entry.Definition.id);
        if (!File.Exists(path))
            return;

        try
        {
            Vector3 alignment = references.LoadAlignment(entry.Definition.id);
            map.SetReferenceImage(TrackImageTrace.LoadTexture(path), alignment);
            SetReferenceControls(true);
            alignReference.SetValueWithoutNotify(false);
            map.SetImageAlignment(false);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
        {
            status.text = $"Could not restore the reference image. {ex.Message}";
        }
    }

    private void SaveReferenceAlignment()
    {
        if (!map.HasReferenceImage || entry == null)
            return;

        try
        {
            references.SaveAlignment(entry.Definition.id, map.ReferenceAlignment);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Debug.LogWarning($"Could not save the reference image alignment. {ex.Message}");
            status.text = ex.Message;
        }
    }

    private void SetReferenceControls(bool hasImage)
    {
        alignReference.SetEnabled(hasImage);
        alignReference.SetValueWithoutNotify(hasImage);
        map.SetImageAlignment(hasImage);
        fitReference.SetEnabled(hasImage);
        clearReference.SetEnabled(hasImage);
    }

    public void Open()
    {
        int previousId = entry?.Definition.id ?? 0;
        catalog.Refresh();
        selector.choices = catalog.Entries.Select(track => track.ToString()).ToList();
        int index = catalog.Entries.FindIndex(track => track.Definition.id == previousId);
        Select(index >= 0 ? index : 0);
    }

    private void Select(int index)
    {
        var next = index >= 0 && index < catalog.Entries.Count ? catalog.Entries[index] : null;
        if (entry?.Definition.id != next?.Definition.id)
            CloseReferenceImage();
        entry = next;
        selector.SetValueWithoutNotify(entry?.ToString() ?? string.Empty);
        draft = null;
        if (entry != null)
        {
            // Keep constructor defaults for fields omitted from older files.
            draft = new TrackDefinition();
            JsonUtility.FromJsonOverwrite(entry.OriginalJson, draft);
        }
        mainIndex = 0;
        selectedPoints = Array.Empty<int>();
        pitIndex = 0;
        placingPitPoint = false;
        placingConnector = false;
        status.text = entry == null ? "Add a track in Tracks first." : "Select a point on the map or in the list. Save to update the track JSON.";
        if (entry != null && !map.HasReferenceImage)
            RestoreReferenceImage();
        BuildForm();
    }

    private void BuildForm()
    {
        for (int i = 0; i < tabs.Count; i++)
        {
            bool active = i == (int)activeTab;
            tabs[i].EnableInClassList("utility-tab-selected", active);
            visibilityToggles[i].SetValueWithoutNotify(active || visible[i]);
            visibilityToggles[i].SetEnabled(!active);
        }
        float scrollPosition = form.scrollOffset.y;
        form.Clear();
        RefreshMap();
        if (draft == null)
            return;

        switch (activeTab)
        {
            case TrackUtilityTab.Track: BuildTrackTab(); break;
            case TrackUtilityTab.Apron: BuildApronTab(); break;
            case TrackUtilityTab.PitRoad: BuildPitRoadTab(); break;
            case TrackUtilityTab.PitEntry: BuildConnectorTab(true); break;
            case TrackUtilityTab.PitExit: BuildConnectorTab(false); break;
            case TrackUtilityTab.RaceMarkers: BuildMarkersTab(); break;
        }

        form.schedule.Execute(() => form.scrollOffset = new Vector2(0f, scrollPosition));
    }

    private void BuildTrackTab()
    {
        Section("Main track spline");
        Number("Lap length (miles)", draft.lengthMiles, value => draft.lengthMiles = value);
        var points = draft.layoutPoints ?? Array.Empty<TrackLayoutPoint>();
        if (points.Length == 0)
        {
            form.Add(new Label("This track uses a generated oval."));
            form.Add(Button("Make Oval Editable", MakeOvalEditable));
        }
        else
        {
            mainIndex = Mathf.Clamp(mainIndex, 0, points.Length - 1);
            PointPicker("Spline point", points.Length);
            var selection = Selected(points);
            PointNumber("X (layout units)", selection, point => point.x, (point, value) => point.x = value);
            PointNumber("Z (layout units)", selection, point => point.z, (point, value) => point.z = value);
            PointNumber("Banking (degrees)", selection, point => point.banking, (point, value) => point.banking = value);
            PointNumber("Track width (ft, 0 inherits)", selection, point => Feet(point.widthMeters),
                (point, value) => point.widthMeters = Meters(value));
            if (selection.Length != 1)
                return;
            mainIndex = selectedPoints[0];
            var controls = Row();
            controls.Add(Button("Insert Point After", InsertMainPoint));
            var remove = Button("Remove Point", RemoveMainPoint);
            remove.SetEnabled(points.Length > 3);
            controls.Add(remove);
            form.Add(controls);
        }
    }

    private void BuildApronTab()
    {
        Section("Apron spline");
        var help = new Label("The flat apron follows the inside edge of the track. Set its width at each spline point.");
        help.style.whiteSpace = WhiteSpace.Normal;
        form.Add(help);
        var points = draft.layoutPoints ?? Array.Empty<TrackLayoutPoint>();
        if (points.Length == 0)
        {
            form.Add(new Label("Use Make Oval Editable in the Track tab to add apron points.") { style = { whiteSpace = WhiteSpace.Normal } });
            return;
        }

        mainIndex = Mathf.Clamp(mainIndex, 0, points.Length - 1);
        PointPicker("Apron point", points.Length);
        PointNumber("Apron width (ft)", Selected(points), point => Feet(point.apronWidthMeters),
            (point, value) => point.apronWidthMeters = Meters(value));
    }

    private void BuildMarkersTab()
    {
        Section("Start/finish and restart zone");
        var markerHelp = new Label("Lap percentages are measured forward from the first spline point.");
        markerHelp.style.whiteSpace = WhiteSpace.Normal;
        form.Add(markerHelp);
        var markers = draft.raceMarkers;
        Number("Start/finish (% of lap)", draft.StartFinishFraction * 100f,
            value => SetFinish(value / 100f));
        if (selectedPoints.Length == 1 && selectedPoints[0] < (draft.layoutPoints?.Length ?? 0))
            form.Add(Button("Start/Finish at Selected Point", () =>
            {
                SetFinish(MainSpline().ControlFraction(mainIndex));
                BuildForm();
            }));
        var enabled = new Toggle("Define restart zone") { value = markers?.hasRestartZone ?? false };
        enabled.RegisterValueChangedCallback(evt => { Markers().hasRestartZone = evt.newValue; BuildForm(); });
        form.Add(enabled);
        if (enabled.value)
        {
            Number("Restart zone start (% of lap)", markers.restartZoneStart * 100f, value => Markers().restartZoneStart = value / 100f);
            Number("Restart zone end (% of lap)", markers.restartZoneEnd * 100f, value => Markers().restartZoneEnd = value / 100f);
        }
    }

    private void BuildPitRoadTab()
    {
        Section("Pit road");
        var pit = draft.pitRoad;
        if (pit?.enabled != true)
        {
            var add = Button("Add Pit Road", () =>
            {
                draft.pitRoad = new TrackPitRoad { enabled = true };
                placingPitPoint = true;
                status.text = "Click the map to place the pit-road start, then its end.";
                BuildForm();
            });
            add.SetEnabled((draft.layoutPoints?.Length ?? 0) >= 3);
            form.Add(add);
        }
        else
        {
            var pitPoints = pit.points ?? Array.Empty<TrackPitPoint>();
            var place = new Toggle("Place pit points on map") { value = placingPitPoint };
            place.RegisterValueChangedCallback(evt => placingPitPoint = evt.newValue);
            form.Add(place);
            if (pitPoints.Length > 0)
            {
                pitIndex = Mathf.Clamp(pitIndex, 0, pitPoints.Length - 1);
                PointPicker("Pit point", pitPoints.Length,
                    index => index == 0 ? "Start" : index == pitPoints.Length - 1 ? "End" : $"Point {index + 1}");
                var selection = Selected(pitPoints);
                PointNumber("Pit X (layout units)", selection, point => point.x, (point, value) => point.x = value, ClearPitDerived);
                PointNumber("Pit Z (layout units)", selection, point => point.z, (point, value) => point.z = value, ClearPitDerived);
                if (selection.Length == 1)
                {
                    pitIndex = selectedPoints[0];
                    var pointActions = Row();
                    var insert = Button("Insert Pit Point After", () =>
                    {
                        var updated = pitPoints.ToList();
                        Vector3 a = TrackPitSpline.Position(pitPoints[pitIndex]);
                        Vector3 b = TrackPitSpline.Position(pitPoints[pitIndex + 1]);
                        updated.Insert(++pitIndex, TrackPitSpline.Data((a + b) * 0.5f));
                        pit.points = updated.ToArray();
                        selectedPoints = new[] { pitIndex };
                        ClearPitDerived();
                        BuildForm();
                    });
                    insert.SetEnabled(pitIndex < pitPoints.Length - 1 && pitPoints.Length < 256);
                    pointActions.Add(insert);
                    var remove = Button("Remove Pit Point", () =>
                    {
                        pit.points = pitPoints.Where((_, index) => index != pitIndex).ToArray();
                        selectedPoints = Array.Empty<int>();
                        ClearPitDerived();
                        BuildForm();
                    });
                    remove.SetEnabled(pitPoints.Length > 2 && pitIndex > 0 && pitIndex < pitPoints.Length - 1);
                    pointActions.Add(remove);
                    form.Add(pointActions);
                }
            }

            var shape = Button("Shape Pit Road From Track", () => Generate(track =>
            {
                TrackUtilityGeometry.ShapePitRoad(track);
                ClearPitDerived();
                pitIndex = 1;
            }, "Pit road reshaped. Generate the stalls and connectors again when the shape is final."));
            shape.SetEnabled(pitPoints.Length >= 2);
            form.Add(shape);
            Number("Pit width (ft)", Feet(pit.widthMeters), value => pit.widthMeters = Meters(value));
            Number("Speed limit (mph)", pit.speedLimitMph, value => pit.speedLimitMph = value);
            Number("Speed-limit start (% of pit road)", pit.speedLimitStart * 100f,
                value => { pit.speedLimitStart = value / 100f; pit.stalls = Array.Empty<TrackPitPoint>(); });
            Number("Speed-limit end (% of pit road)", pit.speedLimitEnd * 100f,
                value => { pit.speedLimitEnd = value / 100f; pit.stalls = Array.Empty<TrackPitPoint>(); });
            var count = new IntegerField("Pit stall count") { value = draft.pitStalls };
            count.RegisterValueChangedCallback(evt =>
            {
                draft.pitStalls = evt.newValue;
                pit.stalls = Array.Empty<TrackPitPoint>();
                RefreshMap();
            });
            form.Add(count);
            form.Add(Button("Generate Pit Stalls", () => Generate(TrackUtilityGeometry.GenerateStalls, "Pit stalls generated.")));
            form.Add(Button("Remove Pit Road", () => { draft.pitRoad = null; placingPitPoint = false; BuildForm(); }));
        }
    }

    private void BuildConnectorTab(bool entrance)
    {
        var pit = draft.pitRoad;
        var generate = Button(placingConnector ? "Cancel Placement" : entrance ? "Generate Pit Entry" : "Generate Pit Exit", () =>
        {
            placingConnector = !placingConnector;
            if (placingConnector)
            {
                alignReference.SetValueWithoutNotify(false);
                map.SetImageAlignment(false);
            }
            status.text = placingConnector
                ? entrance ? "Click the main track where pit entry should start." : "Click the main track where pit exit should end."
                : "Connector placement canceled.";
            BuildForm();
        });
        generate.SetEnabled(pit?.enabled == true && pit.points?.Length >= 2);
        form.Add(generate);
        if (pit?.enabled != true)
            return;

        if (entrance)
            ConnectorFields("Pit entrance spline", pit.entrance,
                points => pit.entrance = points);
        else
            ConnectorFields("Pit exit spline", pit.exit,
                points => pit.exit = points);
    }

    private void ConnectorFields(string title, TrackPitPoint[] points,
        Action<TrackPitPoint[]> replace)
    {
        if ((points?.Length ?? 0) < 2)
            return;

        Section(title);
        PointPicker("Connector point", points.Length,
            index => index == 0 ? "Start (attached)" : index == points.Length - 1 ? "End (attached)" : $"Point {index + 1}");
        var editable = selectedPoints.Where(index => index > 0 && index < points.Length - 1)
            .Select(index => points[index]).ToArray();
        PointNumber("Connector X (layout units)", editable, point => point.x, (point, value) => point.x = value);
        PointNumber("Connector Z (layout units)", editable, point => point.z, (point, value) => point.z = value);
        if (selectedPoints.Length > 1 && editable.Length != selectedPoints.Length)
            form.Add(new Label("Attached endpoints are locked."));
        if (selectedPoints.Length != 1 || selectedPoints[0] >= points.Length)
            return;
        int selected = selectedPoints[0];
        bool endpoint = selected == 0 || selected == points.Length - 1;

        var controls = Row();
        var insert = Button("Insert Point After", () =>
        {
            var updated = points.ToList();
            Vector3 a = TrackPitSpline.Position(points[selected]);
            Vector3 b = TrackPitSpline.Position(points[selected + 1]);
            updated.Insert(selected + 1, TrackPitSpline.Data((a + b) * 0.5f));
            replace(updated.ToArray());
            SelectPoints(new[] { selected + 1 });
        });
        insert.SetEnabled(selected < points.Length - 1 && points.Length < 256);
        controls.Add(insert);
        var remove = Button("Remove Point", () =>
        {
            replace(points.Where((_, index) => index != selected).ToArray());
            SelectPoints(Array.Empty<int>());
        });
        remove.SetEnabled(!endpoint && points.Length > 3);
        controls.Add(remove);
        form.Add(controls);
    }

    private void MakeOvalEditable()
    {
        float finish = draft.StartFinishFraction;
        draft.layoutPoints = TrackUtilityGeometry.EditableOval(draft);
        SetFinish(finish);
        mainIndex = 0;
        selectedPoints = Array.Empty<int>();
        BuildForm();
    }

    private void SetFinish(float fraction)
    {
        var markers = Markers();
        markers.hasStartFinish = true;
        markers.startFinish = fraction;
    }

    private TrackRaceMarkers Markers()
    {
        if (draft.raceMarkers == null)
            draft.raceMarkers = new TrackRaceMarkers();
        return draft.raceMarkers;
    }

    private TrackLayout MainSpline() => new TrackLayout(draft.layoutPoints,
        draft.lengthMiles * TrackDefinition.MetersPerMile, draft.widthMeters);

    private void InsertMainPoint()
    {
        var points = draft.layoutPoints.ToList();
        if (points.Count >= 256)
        {
            status.text = "The main spline supports up to 256 points.";
            return;
        }
        var a = points[mainIndex];
        var b = points[(mainIndex + 1) % points.Count];
        points.Insert(++mainIndex, new TrackLayoutPoint
        {
            x = (a.x + b.x) * 0.5f,
            z = (a.z + b.z) * 0.5f,
            banking = (a.banking + b.banking) * 0.5f,
            elevationMeters = (a.elevationMeters + b.elevationMeters) * 0.5f,
            widthMeters = ((a.widthMeters > 0f ? a.widthMeters : draft.widthMeters) +
                (b.widthMeters > 0f ? b.widthMeters : draft.widthMeters)) * 0.5f,
            apronWidthMeters = (a.apronWidthMeters + b.apronWidthMeters) * 0.5f
        });
        draft.layoutPoints = points.ToArray();
        selectedPoints = new[] { mainIndex };
        BuildForm();
    }

    private void RemoveMainPoint()
    {
        if (draft.layoutPoints.Length <= 3)
            return;
        selectedPoints = Array.Empty<int>();
        draft.layoutPoints = draft.layoutPoints.Where((_, index) => index != mainIndex).ToArray();
        BuildForm();
    }

    private void OnMapClick(Vector3 sourcePoint, int point)
    {
        if (draft == null)
            return;
        if (placingConnector)
        {
            bool entrance = activeTab == TrackUtilityTab.PitEntry;
            try
            {
                TrackUtilityGeometry.GenerateConnection(draft, entrance, sourcePoint);
                placingConnector = false;
                selectedPoints = Array.Empty<int>();
                status.text = entrance ? "Pit entry generated along the apron guide." : "Pit exit generated.";
                BuildForm();
            }
            catch (ArgumentException ex)
            {
                status.text = ex.Message;
            }
            return;
        }
        if (activeTab == TrackUtilityTab.PitRoad && placingPitPoint && draft.pitRoad?.enabled == true)
        {
            var points = (draft.pitRoad.points ?? Array.Empty<TrackPitPoint>()).ToList();
            if (points.Count >= 256)
                return;
            int insert = points.Count < 2 ? points.Count : Mathf.Min(pitIndex + 1, points.Count);
            points.Insert(insert, TrackPitSpline.Data(sourcePoint));
            pitIndex = insert;
            draft.pitRoad.points = points.ToArray();
            if (points.Count == 2)
                placingPitPoint = false;
            ClearPitDerived();
        }
        else
        {
            SelectPoints(point >= 0 ? new[] { point } : Array.Empty<int>());
            return;
        }
        selectedPoints = new[] { pitIndex };
        BuildForm();
    }

    private void SelectPoints(int[] points)
    {
        placingConnector = false;
        selectedPoints = points;
        placingPitPoint = false;
        if (points.Length == 1)
        {
            int point = points[0];
            switch (activeTab)
            {
                case TrackUtilityTab.PitRoad: pitIndex = point; break;
                case TrackUtilityTab.PitEntry:
                case TrackUtilityTab.PitExit: break;
                default: mainIndex = point; break;
            }
        }
        BuildForm();
    }

    private void ClearPitDerived()
    {
        draft.pitRoad.stalls = Array.Empty<TrackPitPoint>();
    }

    private void Generate(Action<TrackDefinition> generate, string message)
    {
        try
        {
            generate(draft);
            selectedPoints = Array.Empty<int>();
            status.text = message;
            BuildForm();
        }
        catch (ArgumentException ex)
        {
            status.text = ex.Message;
        }
    }

    private void Save()
    {
        if (entry == null)
            return;
        try
        {
            catalog.Save(draft, entry);
            SaveReferenceAlignment();
            int id = draft.id;
            selector.choices = catalog.Entries.Select(track => track.ToString()).ToList();
            Select(catalog.Entries.FindIndex(track => track.Definition.id == id));
            status.text = "Track definition saved.";
        }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
        {
            status.text = ex.Message;
        }
    }

    private void RefreshMap() => map.Show(draft, selectedPoints, activeTab, visible, placingConnector);
    private static float Feet(float meters) => meters / TrackDefinition.MetersPerFoot;
    private static float Meters(float feet) => feet * TrackDefinition.MetersPerFoot;

    private void Section(string text)
    {
        var label = new Label(text);
        label.AddToClassList("utility-section");
        form.Add(label);
    }

    private void Number(string label, float value, Action<float> changed)
    {
        var field = new FloatField(label) { value = value, isDelayed = true };
        field.RegisterValueChangedCallback(evt => { changed(evt.newValue); RefreshMap(); });
        form.Add(field);
    }

    private T[] Selected<T>(T[] points)
    {
        return selectedPoints.Where(index => index >= 0 && index < points.Length)
            .Select(index => points[index]).ToArray();
    }

    private void PointNumber<T>(string label, T[] points, Func<T, float> read,
        Action<T, float> write, Action afterEdit = null)
    {
        if (points.Length == 0)
            return;

        void Apply(float value)
        {
            foreach (var point in points)
                write(point, value);
            afterEdit?.Invoke();
        }

        float first = read(points[0]);
        if (points.All(point => read(point).Equals(first)))
        {
            Number(label, first, Apply);
            return;
        }

        var field = new TextField(label + " (mixed)") { isDelayed = true };
        field.RegisterValueChangedCallback(evt =>
        {
            if (!float.TryParse(evt.newValue, out float value) || float.IsNaN(value) || float.IsInfinity(value))
            {
                status.text = "Enter a valid number to update the selected points.";
                return;
            }

            Apply(value);
            field.label = label;
            RefreshMap();
        });
        form.Add(field);
    }

    private void PointPicker(string label, int count, Func<int, string> name = null)
    {
        bool single = selectedPoints.Length == 1 && selectedPoints[0] < count;
        string placeholder = selectedPoints.Length > 1
            ? $"{selectedPoints.Length} points selected" : "Select a point";
        var choices = new[] { placeholder }.Concat(Enumerable.Range(0, count)
            .Select(i => name == null ? $"Point {i + 1}" : name(i))).ToList();
        var picker = new DropdownField(label, choices, single ? selectedPoints[0] + 1 : 0);
        picker.RegisterValueChangedCallback(_ =>
            SelectPoints(picker.index > 0 ? new[] { picker.index - 1 } : Array.Empty<int>()));
        form.Add(picker);
        form.Add(Button("Select All Points", () => SelectPoints(Enumerable.Range(0, count).ToArray())));
    }

    private static VisualElement Row()
    {
        var row = new VisualElement();
        row.AddToClassList("actions");
        return row;
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button(action) { text = text };
        button.AddToClassList("action");
        button.AddToClassList("utility-button");
        return button;
    }
}
