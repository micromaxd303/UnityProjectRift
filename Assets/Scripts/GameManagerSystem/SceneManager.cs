using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class SceneManager : MonoBehaviour
{
    public static SceneManager Instance { get { return instance; } }

    private static SceneManager instance;

    #region LoadingSceenUI
    [Header("UI загрузочного экрана")]

    [SerializeField, Tooltip("Объект загрузочного экрана")] 
    private GameObject LoadingScreenObject;

    [SerializeField, Tooltip("Прогресс полной загрузки уровня")] 
    private UnityEngine.UI.Image GloabalLoadingBar;

    [SerializeField, Tooltip("Прогресс загрузки конкретного шага")] 
    private UnityEngine.UI.Image LocalLoadingBar;

    [SerializeField, Tooltip("Прогресс полной загрузки уровня")]
    private TMPro.TMP_Text GloabalLoadingText;

    [SerializeField, Tooltip("Прогресс загрузки конкретного шага")]
    private TMPro.TMP_Text LocalLoadingText;

    [SerializeField, Tooltip("Название текущего шага загрузки")]
    private TMPro.TMP_Text LoadingStepName;

    [SerializeField, Tooltip("Прогресс шагов")]
    private UnityEngine.UI.Image LoadingStepBar;

    [SerializeField, Tooltip("Текст: Текущий загрузочный шаг / Всего загрузочных шагов")]
    private TMPro.TMP_Text LoadingStepText;
    #endregion

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void LoadScene(Level level)
    {
        StartCoroutine(LevelLoader(level));
    }

    private IEnumerator LevelLoader(Level level)
    {
        if (!string.IsNullOrEmpty(level.sceneName))
        {
            // Подготовливаем загрузочный экран
            if (LoadingScreenObject) LoadingScreenObject.SetActive(true);
            if (LocalLoadingBar) LocalLoadingBar.fillAmount = 0f;

            yield return new WaitForSeconds(0.1f);

            // Загружаем сцену
            AsyncOperation operation = null;
            operation = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(level.sceneName);

            // Если удалось загрузить сцену
            if (operation != null)
            {
                while (operation.progress <= 0.99f)
                {
                    if (LocalLoadingBar) LocalLoadingBar.fillAmount = operation.progress;
                    yield return null;
                }
                yield return new WaitForSeconds(0.1f);

                LevelController levelController = FindFirstObjectByType<LevelController>();

                if (levelController != null)
                {
                    // Иницализируем LevelController
                    levelController.Initialize(level);

                    // Обнуляем прошлый прогресс шагов загрузки
                    if (levelController.loadingSteps != null) for (int i = 0; i < levelController.loadingSteps.Count; i++) levelController.loadingSteps[i].progress = 0f;

                    // Запускаем дополнительные загрузочные шаги
                    if (levelController.loadingSteps != null)
                    {
                        for (int i = 0; i < levelController.loadingSteps.Count; i++)
                        {
                            if (LocalLoadingBar) LocalLoadingBar.fillAmount = 0f;
                            if (LoadingStepBar) LoadingStepBar.fillAmount = (float)(i + 1) / levelController.loadingSteps.Count;
                            if (LoadingStepText) LoadingStepText.text = (i + 1).ToString() + " / " + levelController.loadingSteps.Count.ToString();

                            levelController.loadingSteps[i].start?.Invoke(levelController.loadingSteps[i]);
                            while (levelController.loadingSteps[i].progress < 1f && levelController.loadingSteps[i].waitProgressValue)
                            {
                                if (LocalLoadingBar) LocalLoadingBar.fillAmount = levelController.loadingSteps[i].progress;
                                if (LocalLoadingText) LocalLoadingText.text = ((int)(levelController.loadingSteps[i].progress * 100)).ToString();
                                yield return null;
                            }
                        }
                    }
                }
            }
            else Debug.LogError("Не удалось загрузить сцену! Дальнейшая загрузка остановлена");

            // Выключаем загрузочный экран
            if (LoadingScreenObject) LoadingScreenObject.SetActive(false);
        }
        else Debug.LogError("Название загружаемой сцены равно NULL");
    }

    [System.Serializable]
    public class LoadingStep
    {
        [Tooltip("Название шага загрузки")]
        public string stepName;

        [HideInInspector, Tooltip("Прогресс загрузки")]
        public float progress;

        [Tooltip("Ждать завершения процесса")]
        public bool waitProgressValue = true;

        [Tooltip("Эвент активации шага")]
        public UnityEvent<LoadingStep> start;
    }
}
