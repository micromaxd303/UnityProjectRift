using JetBrains.Annotations;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GameManager : MonoBehaviour
{
    [Tooltip("Список всех веток")]
    public List<Branch> Branches;

    [Tooltip("Название сцены для базы")]
    public string BaseSceneName;

    public ISaveLoadSystem SaveLoadSystem;
    private const string SavePath = @"Saves\";

    public UnityEvent LoadBaseEvent;
    public UnityEvent<Level> LoadLevelEvent;
    public UnityEvent<Level> CompleteLevelEvent;
    public UnityEvent<Sector> CompleteSectorEvent;

    [HideInInspector]
    public SaveData saveData;

    private List<BranchData> branchDatas;


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
    public List<BranchData> GetBranches
    {
        get { return branchDatas; }
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

        branchDatas = new();
        foreach (Branch branch in Branches) branchDatas.Add(branch.data);
    }

    public void NewGame()
    {
        saveData.sectors = new List<Sector>();
        saveData.sectors.Add(new Sector(GetBranches, 0));
    }
    public void LoadGame(string SaveName = "Default")
    {
        if (SaveLoadSystem != null)
        {
            if (SaveLoadSystem.Load<SaveData>(SavePath + SaveName, out saveData))
                Debug.Log($"Сохранение {SaveName} успешно загружено");
        }
    }
    public void SaveGame(string SaveName = "Default")
    {
        if (SaveLoadSystem != null)
        {
            SaveLoadSystem.Save<SaveData>(saveData, SavePath + SaveName);
            Debug.Log($"Игра успешно сохранена: {SaveName}");
        }
    }

    public void LoadLevel(Level level)
    {
        if (level == null) return;
        if (level.status == Level.Status.Available)
        {
            level.status = Level.Status.Running;
            SceneManager.Instance.LoadScene(level);
            LoadLevelEvent.Invoke(level);
        } 
    }

    public void LoadBase()
    {
        if (GetCurrentSector.currentLevel != 0 || GetCurrentSector.FindLevel(Level.Status.Running) != null) { Debug.Log("Перемещение на базу невозможно, сектор не завершен"); return; }
        Level Base = new(BaseSceneName);
        SceneManager.Instance.LoadScene(Base);
        LoadBaseEvent.Invoke();
    }

    public void CompleteLevel(Level level)
    {
        if (GetCurrentSector == null) return;
        GetCurrentSector.CompleteLevel(level);
        CompleteLevelEvent.Invoke(level);
        if (GetCurrentSector.levelBoss.status == Level.Status.Complete)
        {
            Sector lastSector = GetCurrentSector;
            GetSectors.Add(new Sector(GetBranches, Mathf.Clamp(GetSectors.Count, 0, int.MaxValue)));
            CompleteSectorEvent.Invoke(lastSector);
        }
    }
}

[System.Serializable]
public class Level
{
    public Vector2Int index;                        // Индекс уровня в секторе
    public Status status = Status.NotAvailable;     // Статус уровня

    public List<MissionData> missions;              // Список всех миссий на уровне

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
    public List<BranchData> branches;   // Список веток для сектора, используется для правильной генерации сектора
    public List<Level> levels;          // Список всех уровней
    public Level levelBoss;             // Уровень босса
    public int index;                   // Индекс сектора

    public int currentLevel = 0;        // Текущий уровень сектора
    public int maxLevel;                // Максимальный уровень сектора

    public Sector(List<BranchData> branches, int index)
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

    public BranchData GetBranch(Level level)
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
