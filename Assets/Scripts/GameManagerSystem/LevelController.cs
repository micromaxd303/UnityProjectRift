using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

public class LevelController : MonoBehaviour
{
    [Tooltip("Список этапов загрузки уровня")]
    public List<SceneManager.LoadingStep> loadingSteps;

    [Tooltip("Эвент завершеня уровня")]
    public UnityEvent levelComplete;

    public static LevelController Instance { get { return instance; } }
    private static LevelController instance;

    public Level GetCurrentLevel { get { return level; } }
    private Level level;

    public void Initialize(Level level = null)
    {
        instance = this;
        this.level = level;
    }

    public void CompleteLevel()
    {
        GameManager.Instance.CompleteLevel(GetCurrentLevel);
        levelComplete.Invoke();
    }

    public void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
