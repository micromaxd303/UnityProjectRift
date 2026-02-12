public interface ISaveLoadSystem
{
    public void Save<T>(T file, string fileName);
    public bool Load<T>(string fileName, out T file);
}
