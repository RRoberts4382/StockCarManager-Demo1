using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

internal static class ManufacturerCatalog
{
    // These IDs are also the numeric values written by older versions.
    internal const int Chevrolet = 0, Ford = 1, Toyota = 2, Dodge = 3;

    [Serializable]
    internal sealed class Entry
    {
        public int id;
        public string name;
        public string logo;
        public bool deleted;
    }

    [Serializable]
    private sealed class Catalog
    {
        public Entry[] entries;
    }

    private static Catalog catalog;
    private static string CatalogPath => Path.Combine(Application.persistentDataPath, "Manufacturers.json");
    private static Catalog Current => catalog ??= Load();
    internal static Entry[] Active => Current.entries.Where(entry => !entry.deleted).ToArray();
    internal static int[] ActiveIds => Active.Select(entry => entry.id).ToArray();
    internal static bool Contains(int id) => Find(id) != null;
    internal static string Name(int id) => Find(id)?.name ?? $"Manufacturer #{id}";
    internal static Texture2D Logo(int id) => SeriesSettingsStore.LoadArtwork(Find(id)?.logo);
    private static Entry Find(int id) => Current.entries.FirstOrDefault(entry => entry.id == id);

    internal static int? ParseSavedFilter(string saved)
    {
        if (int.TryParse(saved, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && Contains(id))
            return id;
        return saved?.ToUpperInvariant() switch
        {
            "CHEVROLET" => Chevrolet,
            "FORD" => Ford,
            "TOYOTA" => Toyota,
            "DODGE" => Dodge,
            _ => null
        };
    }

    internal static string NameError(int? id, string name) =>
        SettingsFormStyle.TextError(name?.Trim()) ??
        (Active.Any(entry => entry.id != id && string.Equals(entry.name, name?.Trim(), StringComparison.OrdinalIgnoreCase))
            ? "A manufacturer with this name already exists." : null);

    internal static int SaveEntry(int? id, string name, string logo)
    {
        if (NameError(id, name) is string error)
            throw new ArgumentException(error);
        name = name.Trim();
        var updated = new Entry
        {
            id = id ?? checked(Current.entries.Max(entry => entry.id) + 1),
            name = name,
            logo = SeriesSettingsStore.SaveArtwork(logo)
        };
        Save(id.HasValue
            ? Current.entries.Select(entry => entry.id == id ? updated : entry).ToArray()
            : Current.entries.Append(updated).ToArray());
        return updated.id;
    }

    internal static bool CanDelete(int id) =>
        id > Dodge && Active.Length > 1 && Find(id)?.deleted == false;

    internal static void Delete(int id)
    {
        if (!CanDelete(id))
            throw new InvalidOperationException("Only user-added manufacturers can be deleted, and at least one manufacturer must remain.");
        // Keep the identity for careers, series settings and recorded results.
        Save(Current.entries.Select(entry => entry.id == id
            ? new Entry { id = id, name = entry.name, logo = entry.logo, deleted = true }
            : entry).ToArray());
    }

    private static Catalog Load()
    {
        if (File.Exists(CatalogPath))
        {
            try
            {
                var saved = JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogPath, Encoding.UTF8));
                if (saved?.entries != null && saved.entries.All(entry => entry != null && entry.id >= 0 &&
                    !string.IsNullOrWhiteSpace(entry.name)) && saved.entries.Any(entry => !entry.deleted) &&
                    saved.entries.Select(entry => entry.id).Distinct().Count() == saved.entries.Length &&
                    Enumerable.Range(Chevrolet, 4).All(id => saved.entries.Any(entry => entry.id == id)))
                    return saved;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Could not read the manufacturer catalog. {error.Message}");
            }
        }
        return new Catalog
        {
            entries = new[]
            {
                new Entry { id = Chevrolet, name = "Chevrolet" },
                new Entry { id = Ford, name = "Ford" },
                new Entry { id = Toyota, name = "Toyota" },
                new Entry { id = Dodge, name = "Dodge" }
            }
        };
    }

    private static void Save(Entry[] entries)
    {
        var updated = new Catalog { entries = entries };
        SeriesSettingsStore.AtomicWrite(CatalogPath, path =>
            File.WriteAllText(path, JsonUtility.ToJson(updated, true), new UTF8Encoding(false)));
        catalog = updated;
    }
}
