using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class TracksEditor
{
    [Serializable]
    private sealed class LayoutInput { public TrackLayoutPoint[] points; }

    private readonly TrackCatalog catalog;
    private readonly DropdownField selector = new DropdownField("Saved tracks");
    private readonly Label status = new Label();
    private readonly Label trackId = new Label();
    private readonly Button delete;
    private readonly TextField layout = new TextField("Layout points (JSON)") { multiline = true };
    private readonly List<Action<TrackDefinition>> load = new List<Action<TrackDefinition>>();
    private readonly List<Action<TrackDefinition>> save = new List<Action<TrackDefinition>>();
    private TrackCatalog.Entry editing;
    private bool confirmDelete;

    public VisualElement Screen { get; } = new VisualElement();

    public TracksEditor(TrackCatalog catalog, Action back)
    {
        this.catalog = catalog;
        Screen.AddToClassList("screen");
        Screen.AddToClassList("tracks-editor");
        var heading = new VisualElement();
        heading.AddToClassList("track-heading");
        var title = new Label("Tracks");
        title.AddToClassList("track-title");
        heading.Add(title);
        heading.Add(ActionButton("Back to Title", back));
        Screen.Add(heading);

        var toolbar = new VisualElement();
        toolbar.AddToClassList("actions");
        toolbar.Add(ActionButton("New Track", () => Select(-1)));
        toolbar.Add(ActionButton("Save", Save));
        delete = ActionButton("Delete", Delete);
        toolbar.Add(delete);
        Screen.Add(toolbar);
        Screen.Add(selector);

        var imageLabel = new Label("Track shape image");
        imageLabel.AddToClassList("brand");
        Screen.Add(imageLabel);
        var imageActions = new VisualElement();
        imageActions.AddToClassList("actions");
        imageActions.Add(ActionButton("Choose Image…", ImportImage));
        imageActions.Add(ActionButton("Paste Image", PasteImage));
        Screen.Add(imageActions);
        selector.RegisterValueChangedCallback(_ => Select(selector.index - 1));
        status.style.whiteSpace = WhiteSpace.Normal;
        Screen.Add(status);

        var form = new ScrollView();
        form.style.flexGrow = 1;
        Screen.Add(form);
        form.Add(trackId);
        Bind(form, new TextField("Name"), t => t.name, (t, v) => t.name = v);
        Bind(form, new TextField("City"), t => t.location.city, (t, v) => t.location.city = v);
        Bind(form, new TextField("State"), t => t.location.state, (t, v) => t.location.state = v);
        Bind(form, new FloatField("Qualifying reference (mph)"), t => t.qualifyingReferenceMph, (t, v) => t.qualifyingReferenceMph = v);
        Bind(form, new FloatField("Length (miles)"), t => t.lengthMiles, (t, v) => t.lengthMiles = v);
        var aspectRatio = new FloatField("Aspect ratio");
        aspectRatio.tooltip = "Horizontal span divided by vertical span. Changing the ratio reshapes the existing spline on Save. Lap length stays the same.";
        Bind(form, aspectRatio, t => t.aspectRatio, (t, v) => t.aspectRatio = v);
        Bind(form, new DropdownField("Major style", new List<string> { "Oval", "Road" }, 0),
            t => t.majorStyle, (t, v) => t.majorStyle = v);
        Bind(form, new DropdownField("Minor style", TrackDefinition.MinorStyles.ToList(), 0),
            t => t.minorStyle, (t, v) => t.minorStyle = v);
        Bind(form, new DropdownField("Surface", TrackDefinition.Surfaces.ToList(), 0),
            t => t.surface, (t, v) => t.surface = v);
        Bind(form, new Toggle("Restrictor plate"), t => t.restrictorPlate, (t, v) => t.restrictorPlate = v);
        Bind(form, new FloatField("Turns 1–2 (degrees)"), t => t.banking.turns12, (t, v) => t.banking.turns12 = v);
        Bind(form, new FloatField("Turns 3–4 (degrees)"), t => t.banking.turns34, (t, v) => t.banking.turns34 = v);
        Bind(form, new FloatField("Front straight (degrees)"), t => t.banking.frontStraight, (t, v) => t.banking.frontStraight = v);
        Bind(form, new FloatField("Back straight (degrees)"), t => t.banking.backStraight, (t, v) => t.banking.backStraight = v);
        Bind(form, new FloatField("Track width (feet)"), t => t.widthMeters / TrackDefinition.MetersPerFoot,
            (t, v) => t.widthMeters = v * TrackDefinition.MetersPerFoot);
        Bind(form, new IntegerField("Pit stalls"), t => t.pitStalls, (t, v) => t.pitStalls = v);
        Bind(form, new IntegerField("Qualifying laps"), t => t.NumberOfQualifyingLaps, (t, v) => t.NumberOfQualifyingLaps = v);
        Bind(form, new IntegerField("Race length (laps)"), t => t.raceLaps, (t, v) =>
        {
            if (t.raceLaps != v)
                t.raceStageLaps = null;
            t.raceLaps = v;
        });
        Bind(form, new FloatField("Surface roughness (0–1)"), t => t.weights.surfaceRoughness, (t, v) => t.weights.surfaceRoughness = v);
        Bind(form, new FloatField("Aero (0–1)") { isReadOnly = true }, t => t.weights.aerodynamics, (t, v) => t.weights.aerodynamics = v);
        Bind(form, new FloatField("Horsepower (0–1)"), t => t.weights.horsepower, (t, v) => t.weights.horsepower = v);
        Bind(form, new FloatField("Brakes (0–1)") { isReadOnly = true }, t => t.weights.brakes, (t, v) => t.weights.brakes = v);
        Bind(form, new FloatField("Handling (0–1)") { isReadOnly = true }, t => t.weights.handling, (t, v) => t.weights.handling = v);
        Bind(form, new FloatField("Tire wear (0–1)") { isReadOnly = true }, t => t.weights.tireWear, (t, v) => t.weights.tireWear = v);
        Bind(form, new FloatField("Suspension (0–1)"), t => t.weights.suspension, (t, v) => t.weights.suspension = v);
        var help = new Label("Use Choose Image or Paste Image with a dark, closed racing line on a light background. Put the front stretch at the bottom so the game can estimate start/finish and banking. The first point is start/finish. Use [] for a generated oval.");
        help.style.whiteSpace = WhiteSpace.Normal;
        form.Add(help);
        layout.style.minHeight = 95;
        form.Add(layout);
    }

    public void Open()
    {
        catalog.Refresh();
        RefreshChoices();
        Select(catalog.Entries.Count > 0 ? 0 : -1);
    }

    private void RefreshChoices()
    {
        selector.choices = new[] { "New track" }.Concat(catalog.Entries.Select(entry => entry.ToString())).ToList();
    }

    private void Select(int index)
    {
        editing = index >= 0 && index < catalog.Entries.Count ? catalog.Entries[index] : null;
        selector.SetValueWithoutNotify(selector.choices[editing == null ? 0 : index + 1]);
        trackId.text = editing == null ? "ID assigned on save" : $"ID  {editing.Definition.id}";
        var definition = editing?.Definition ?? new TrackDefinition();
        foreach (var read in load)
            read(definition);

        SetLayoutPoints(definition.layoutPoints);
        delete.SetEnabled(editing != null);
        confirmDelete = false;
        delete.text = "Delete";
        status.text = string.IsNullOrEmpty(catalog.Status) ? catalog.Folder : catalog.Status;
    }

    private void ImportImage()
    {
        try
        {
            string path = TrackImageFilePicker.Pick();
            if (string.IsNullOrEmpty(path))
                return;

            var points = TrackImageTrace.Import(path, ReadFields());
            ApplyImageLayout(points, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
        {
            status.text = ex.Message;
        }
    }

    private void PasteImage()
    {
        try
        {
            ApplyImageLayout(TrackImageClipboard.Import(ReadFields()), "the clipboard");
        }
        catch (ArgumentException ex)
        {
            status.text = ex.Message;
        }
    }

    private void ApplyImageLayout(TrackLayoutPoint[] points, string source)
    {
        SetLayoutPoints(points);
        status.text = $"Generated {points.Length} layout points from {source}. Review the estimated start/finish and banking, then save.";
    }

    private void SetLayoutPoints(TrackLayoutPoint[] points)
    {
        string wrapper = JsonUtility.ToJson(new LayoutInput { points = points ?? Array.Empty<TrackLayoutPoint>() }, true);
        int first = wrapper.IndexOf('[');
        layout.SetValueWithoutNotify(wrapper.Substring(first, wrapper.LastIndexOf(']') - first + 1));
    }

    private TrackDefinition ReadFields()
    {
        var definition = editing == null
            ? new TrackDefinition()
            : JsonUtility.FromJson<TrackDefinition>(JsonUtility.ToJson(editing.Definition));
        foreach (var write in save)
            write(definition);
        return definition;
    }

    private void Save()
    {
        try
        {
            var definition = ReadFields();
            string points = string.IsNullOrWhiteSpace(layout.value) ? "[]" : layout.value.Trim();
            if (!points.StartsWith("[", StringComparison.Ordinal) || !points.EndsWith("]", StringComparison.Ordinal))
                throw new ArgumentException("Layout points must be a JSON array.");
            definition.layoutPoints = JsonUtility.FromJson<LayoutInput>("{\"points\":" + points + "}")?.points;
            if (definition.layoutPoints == null)
                throw new ArgumentException("Enter a valid layout point array.");
            bool reshaped = editing != null && definition.aspectRatio != editing.Definition.aspectRatio
                && definition.layoutPoints.Length > 0;
            if (reshaped)
                TrackUtilityGeometry.ApplyAspectRatio(definition);

            catalog.Save(definition, editing);
            RefreshChoices();
            Select(catalog.Entries.FindIndex(entry => entry.Definition.id == definition.id));
            status.text = reshaped ? $"Saved {definition.name} with recalculated layout points." : $"Saved {definition.name}.";
        }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
        {
            status.text = ex.Message;
        }
    }

    private void Delete()
    {
        if (editing == null)
            return;
        if (!confirmDelete)
        {
            confirmDelete = true;
            delete.text = "Confirm Delete";
            status.text = $"Delete {editing.Definition.name}? Click Confirm Delete to remove its file.";
            return;
        }

        try
        {
            catalog.Delete(editing);
            RefreshChoices();
            Select(-1);
            status.text = "Track deleted.";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            status.text = ex.Message;
        }
    }

    private void Bind<T>(VisualElement form, BaseField<T> field, Func<TrackDefinition, T> read,
        Action<TrackDefinition, T> write)
    {
        form.Add(field);
        load.Add(track => field.SetValueWithoutNotify(read(track)));
        save.Add(track => write(track, field.value));
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button(action) { text = text };
        button.AddToClassList("action");
        button.style.marginRight = 8f;
        return button;
    }
}
