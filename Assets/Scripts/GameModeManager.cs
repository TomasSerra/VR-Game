using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameModeManager : MonoBehaviour
{
    public enum GameMode
    {
        Manual,
        RemoveWheel,
        InstallWheel
    }

    public static GameMode SelectedMode { get; private set; } = GameMode.Manual;

    [Header("Scene")]
    [SerializeField] string gameSceneName = "Game";

    [Header("Tutorial Roots")]
    [SerializeField] GameObject manualTutorialRoot;
    [SerializeField] GameObject removeWheelTutorialRoot;
    [SerializeField] GameObject installWheelTutorialRoot;

    [Header("Mode Buttons")]
    [SerializeField] Button manualButton;
    [SerializeField] Button removeWheelButton;
    [SerializeField] Button installWheelButton;

    [Header("Mode Button Colors")]
    [SerializeField] Color selectedButtonColor = Color.green;
    [SerializeField] Color unselectedButtonColor = Color.white;

    static GameModeManager instance;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    void Start()
    {
        SelectManual();
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public void SelectManual()
    {
        SelectMode(GameMode.Manual);
    }

    public void SelectRemoveWheel()
    {
        SelectMode(GameMode.RemoveWheel);
    }

    public void SelectInstallWheel()
    {
        SelectMode(GameMode.InstallWheel);
    }

    public void StartSelectedMode()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    public void StartManual()
    {
        SelectManual();
    }

    public void StartRemoveWheel()
    {
        SelectRemoveWheel();
    }

    public void StartInstallWheel()
    {
        SelectInstallWheel();
    }

    void SelectMode(GameMode mode)
    {
        SelectedMode = mode;
        ApplyTutorialVisibility(mode);
        ApplyButtonColors(mode);
    }

    void ApplyTutorialVisibility(GameMode selectedMode)
    {
        SetActiveIfAssigned(manualTutorialRoot, selectedMode == GameMode.Manual);
        SetActiveIfAssigned(removeWheelTutorialRoot, selectedMode == GameMode.RemoveWheel);
        SetActiveIfAssigned(installWheelTutorialRoot, selectedMode == GameMode.InstallWheel);
    }

    void ApplyButtonColors(GameMode selectedMode)
    {
        ApplyButtonColor(manualButton, selectedMode == GameMode.Manual);
        ApplyButtonColor(removeWheelButton, selectedMode == GameMode.RemoveWheel);
        ApplyButtonColor(installWheelButton, selectedMode == GameMode.InstallWheel);
    }

    void ApplyButtonColor(Button button, bool isSelected)
    {
        if (button == null)
            return;

        Color buttonColor = isSelected ? selectedButtonColor : unselectedButtonColor;
        ColorBlock colors = button.colors;
        colors.normalColor = buttonColor;
        colors.selectedColor = buttonColor;
        colors.highlightedColor = buttonColor;
        button.colors = colors;

        if (button.targetGraphic != null)
            button.targetGraphic.color = buttonColor;
    }

    static void SetActiveIfAssigned(GameObject target, bool isActive)
    {
        if (target != null)
            target.SetActive(isActive);
    }
}
