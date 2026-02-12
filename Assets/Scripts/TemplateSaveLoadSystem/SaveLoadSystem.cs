using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

public class SaveLoadSystem : ISaveLoadSystem
{
    public void Save<T>(T file, string fileName)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));
        
        if(fileName == null)
            throw new ArgumentNullException(nameof(fileName));
        
        string path = Path.Combine(Defaults.SavePath, fileName);

        if (!Directory.Exists(Path.GetDirectoryName(path)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
        }
        
        BinaryFormatter formatter = new BinaryFormatter();
        using FileStream stream = new FileStream(path, FileMode.Create);
        
        formatter.Serialize(stream, file);
    }

    public bool Load<T>(string fileName, out T result)
    {
        string path = Path.Combine(Defaults.SavePath, fileName);

        if (!File.Exists(path))
        {
            result = default;
            return false;
        }
        try
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            result = (T)formatter.Deserialize(stream);
            
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to load {fileName}: {e.Message}");
            result = default;
            return false;
        }
    }
}
