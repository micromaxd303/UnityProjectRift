using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

public class SaveLoadSystem : ISaveLoadSystem
{

    private static readonly JsonSerializerSettings Settings = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        Formatting = Formatting.Indented,
        Converters = { new Vector2IntConverter() }
    };
    
    public void Save<T>(T file, string fileName)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));
        
        if(fileName == null)
            throw new ArgumentNullException(nameof(fileName));
        
        string path = Path.Combine(Defaults.SavePath, fileName);
        
        string dir = Path.GetDirectoryName(path);
        if(!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        
        string json = JsonConvert.SerializeObject(file, Settings);
        File.WriteAllText(path, json);
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
            string json = File.ReadAllText(path);
            result = JsonConvert.DeserializeObject<T>(json, Settings);
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
