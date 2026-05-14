using UnityEngine;
using UnityEngine.SceneManagement;

public class GameModeManager : MonoBehaviour
{
    public enum GameMode
    {
        Manual,
        RemoveWheel,
        InstallWheel
    }

    public static GameMode SelectedMode { get; private set; } = GameMode.Manual;

    [SerializeField] string gameSceneName = "Game";

    static GameModeManager instance;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void StartManual()
    {
        SelectedMode = GameMode.Manual;
        SceneManager.LoadScene(gameSceneName);
    }

    public void StartRemoveWheel()
    {
        SelectedMode = GameMode.RemoveWheel;
        SceneManager.LoadScene(gameSceneName);
    }

    public void StartInstallWheel()
    {
        SelectedMode = GameMode.InstallWheel;
        SceneManager.LoadScene(gameSceneName);
    }
}
