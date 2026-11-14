using System;
using System.IO;
using UnityEngine;

internal sealed class TrackReferenceFiles
{
    private readonly string folder;

    public TrackReferenceFiles(string tracksFolder)
    {
        folder = Path.Combine(tracksFolder, "ReferenceImages");
    }

    public string ImagePath(int id) => Path.Combine(folder, $"{id}.image");

    public void SaveImage(int id, byte[] bytes)
    {
        Write(ImagePath(id), bytes);
        File.Delete(AlignmentPath(id));
    }

    public Vector3 LoadAlignment(int id)
    {
        string path = AlignmentPath(id);
        return File.Exists(path) ? JsonUtility.FromJson<Vector3>(File.ReadAllText(path)) : Vector3.zero;
    }

    public void SaveAlignment(int id, Vector3 alignment)
    {
        if (File.Exists(ImagePath(id)))
            Write(AlignmentPath(id), System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(alignment)));
    }

    public void Delete(int id)
    {
        if (!Directory.Exists(folder))
            return;

        File.Delete(AlignmentPath(id));
        File.Delete(ImagePath(id));
    }

    private string AlignmentPath(int id) => Path.Combine(folder, $"{id}.alignment.json");

    private void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, $"{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
