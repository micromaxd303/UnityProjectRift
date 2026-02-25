using JetBrains.Annotations;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Tooltip("Список всех веток")]
    public List<Branch> Branches;

    [Tooltip("Название сцены для базы")]
    public string BaseSceneName;

    public ISaveLoadSystem SaveLoadSystem;
    private const string SavePath = "TEMP Save/Sectors";

    [HideInInspector]
    public SaveData saveData;


    public List<Sector> GetSectors 
    { 
        get 
        {
            if (saveData == null) return null;
            return saveData.sectors; 
        } 
    }
    public Sector GetCurrentSector 
    { 
        get 
        {
            if (saveData == null) return null;
            if (saveData.sectors == null) return null;
            if (saveData.sectors.Count <= 0) return null;
            return saveData.sectors[saveData.sectors.Count - 1]; 
        } 
    }
    public int GetCurrentLevel
    {
        get
        {
            int level = 0;
            foreach (Sector sector in GetSectors)
            {
                level += sector.currentLevel;
            }
            return level;
        }
    }

    // singletone
    private static GameManager instance;
    public static GameManager Instance { get { return instance; } }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        SaveLoadSystem = new SaveLoadSystem();
    }

    public void NewGame()
    {
        saveData.sectors = new List<Sector>();
        saveData.sectors.Add(new Sector(Branches, 0));
    }
    public void LoadGame()
    {
        if (SaveLoadSystem != null)
        {
            if (SaveLoadSystem.Load<SaveData>(SavePath, out saveData))
                Debug.Log("Игра успешно загружена");
        }
    }
    public void SaveGame()
    {
        if (SaveLoadSystem != null)
        {
            SaveLoadSystem.Save<SaveData>(saveData, SavePath);
            Debug.Log("Игра успешно сохранена");
        }
    }

    public void LoadLevel(Level level)
    {
        if (level == null) return;
        if (level.status == Level.Status.Available)
        {
            level.status = Level.Status.Running;
            SceneManager.Instance.LoadScene(level);
        } 
    }

    public void LoadBase()
    {
        for (int i = 0; i < Branches.Count; i++)
        {
            if (GetCurrentSector.FindLevel(new Vector2Int(i, GetCurrentSector.currentLevel)).status == Level.Status.Complete)
            {
                Level Base = new(BaseSceneName);
                SceneManager.Instance.LoadScene(Base);
                break;
            }
        }
        Debug.Log("Перемещение на базу невозможно, на текущем уровне сектора нет ни одного завершенного уровня");
    }

    public void CompleteLevel(Level level)
    {
        if (GetCurrentSector == null) return;
        GetCurrentSector.CompleteLevel(level);
        if (GetCurrentSector.levelBoss.status == Level.Status.Complete)
        {
            GetSectors.Add(new Sector(Branches, Mathf.Clamp(GetSectors.Count - 1, 0, int.MaxValue)));
        }
    }
}

[System.Serializable]
public class Level
{
    public Vector2Int index;                        // Индекс уровня в секторе
    public Status status = Status.NotAvailable;     // Статус уровня

    public List<Mission> missions;                  // Список всех миссий на уровне

    public string sceneName;

    public Level(string sceneName = null)
    {
        this.sceneName = sceneName;
    }

    public enum Status { NotAvailable, Available, Running, Complete }
}

[System.Serializable]
public class Sector
{
    public List<Branch> branches;   // Список веток для сектора, используется для правильной генерации сектора
    public List<Level> levels;      // Список всех уровней
    public Level levelBoss;         // Уровень босса
    public int index;               // Индекс сектора

    public int currentLevel = 0;    // Текущий уровень сектора
    public int maxLevel;            // Максимальный уровень сектора

    public Sector(List<Branch> branches, int index)
    {
        this.index = index;
        this.branches = branches;
        InitializeSector();
        UnlockLevels();
    }

    private void InitializeSector()
    {
        maxLevel = 0;
        levels = new List<Level>();
        for (int branchIndex = 0; branchIndex < branches.Count; branchIndex++)
        {
            if (maxLevel < branches[branchIndex].levelCount) maxLevel = branches[branchIndex].levelCount;
            for (int levelIndex = 0; levelIndex < branches[branchIndex].levelCount; levelIndex++)
            {
                Level level = new Level(branches[branchIndex].sceneName);
                level.index = new Vector2Int(branchIndex, levelIndex);
                levels.Add(level);
            }
        }
        levelBoss = new("BossScene_" + (index % branches.Count).ToString());
        levelBoss.index = new Vector2Int(-1, maxLevel);
    }

    public Level FindLevel(Vector2Int index)
    {
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i].index == index) return levels[i];
        }
        if (levelBoss.index == index) return levelBoss;
        return null;
    }

    public Level FindLevel(Level.Status status)
    {
        foreach (Level level in levels)
        {
            if (level.status == status) return level;
        }
        if (levelBoss.status == status) return levelBoss;
        return null;
    }

    public Branch GetBranch(Level level)
    {
        if (level.index.x >= 0 && level.index.x < branches.Count) return branches[level.index.x];
        else return null;
    }

    public void CompleteLevel(Level level)
    {
        level.status = Level.Status.Complete;
        currentLevel = Mathf.Max(currentLevel, level.index.y + 1);
        UnlockLevels();
    }

    // За логику разблокировки всех уровней отвечает эта функция, если нужно пересмотреть логику менять именно её.
    // Кстати все эти коментарии мои (не нейронка), просто расписал всю логику чтобы не было вопросов
    public void UnlockLevels()
    {
        // Если произошел пиздец, то отмена
        if (currentLevel < 0) return;

        // Если уровень нулевой, тоесть на секторе нет ни одного пройденного уровня, то разблокируем первые уровни всех веток
        if (currentLevel == 0)
        {
            for (int i = 0; i < branches.Count; i++)
            {
                FindLevel(new Vector2Int(i, 0)).status = Level.Status.Available;
            }
            return;
        }

        // Если уровень сектора почти последний, тоесть осталься только уровень босса, то блокируем все уровни кроме уровня босса
        if (currentLevel == maxLevel)
        {
            foreach (Level level in levels)
            {
                // Если уровень был доступен, то блокирем его
                if (level.status == Level.Status.Available) 
                    level.status = Level.Status.NotAvailable;
            }
            // Если уровень босса был недоступен, то разблокирем его
            if (levelBoss.status == Level.Status.NotAvailable) levelBoss.status = Level.Status.Available;
            return;
        }


        // Если частных случаев нет, то выполняем следующий алгоритм
        for (int i = 0; i < branches.Count; i++)
        {
            // На каждой ветке блокируем все уровни кроме текущего
            for (int j = 0; j < branches[i].levelCount; j++)
            {
                Level level = FindLevel(new Vector2Int(i, j));
                if (level == null) continue;
                if (currentLevel == j) level.status = Level.Status.Available;
                else if (level.status == Level.Status.Available) level.status = Level.Status.NotAvailable;
            }

            
        }

        // Находим последний пройденный уровень
        int branchIndex = -1;
        for (int i = 0; i < branches.Count; i++)
        {
            Level level = FindLevel(new Vector2Int(i, currentLevel - 1));
            if (level == null) continue;
            if (level.status == Level.Status.Complete)
            {
                branchIndex = i;
                break;
            }
        }

        // Если нашли пройденный уровень, оставляем только соседние уровни над ним
        if (branchIndex != - 1)
        {
            for (int i = 0; i < branches.Count; i++)
            {
                Level level = FindLevel(new Vector2Int(i, currentLevel));
                if (level == null) continue;
                level.status = Mathf.Abs(branchIndex - i) <= 1 ? Level.Status.Available : Level.Status.NotAvailable;
            }
        }
    }
}

[System.Serializable]
public class SaveData
{
    public List<Sector> sectors;
}
