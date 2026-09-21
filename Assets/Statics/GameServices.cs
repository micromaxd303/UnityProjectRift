using UnityEngine;

[DefaultExecutionOrder(-1)]
public static class GameServices
{
    public static InputManager Input { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        Input = new InputManager();
        Application.quitting += Dispose;
    }

    private static void Dispose()
    {
        Application.quitting -= Dispose;
        Input?.Dispose();
        Input = null;
    }
}