using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class PitTimerDisplay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] TMP_Text timerText;
    [SerializeField] GameObject displayRoot;

    [Header("Display")]
    [SerializeField] bool hideWhenReset = true;

    float elapsed;
    bool running;

    void Awake()
    {
        if (hideWhenReset)
            ShowDisplay(false);
        UpdateText();
    }

    void Update()
    {
        if (!running) return;
        elapsed += Time.deltaTime;
        UpdateText();
    }

    public void StartTimer()
    {
        elapsed = 0f;
        running = true;
        ShowDisplay(true);
        UpdateText();
    }

    public void StopTimer()
    {
        running = false;
        UpdateText();
    }

    public void ResetTimer()
    {
        running = false;
        elapsed = 0f;
        UpdateText();
        if (hideWhenReset)
            ShowDisplay(false);
    }

    public float ElapsedSeconds => elapsed;

    void ShowDisplay(bool visible)
    {
        if (displayRoot != null)
            displayRoot.SetActive(visible);
    }

    void UpdateText()
    {
        if (timerText == null) return;
        int minutes = Mathf.FloorToInt(elapsed / 60f);
        int seconds = Mathf.FloorToInt(elapsed % 60f);
        int centiseconds = Mathf.FloorToInt((elapsed * 100f) % 100f);
        timerText.text = $"{minutes:00}:{seconds:00}.{centiseconds:00}";
    }
}
