using System.Collections;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class PitAlertController : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] GameObject alertTextRoot;
    [SerializeField, Min(0f)] float autoHideDelay = 4f;

    [Header("Countdown (optional)")]
    [SerializeField] TMP_Text countdownText;
    [SerializeField] string countdownFormat = "{0:0.0}s";
    [SerializeField] string countdownArrivedText = "0.0s";

    [Header("Blink on arrival")]
    [SerializeField, Min(0f)] float arrivedBlinkDuration = 1.5f;
    [SerializeField, Min(0.02f)] float arrivedBlinkInterval = 0.15f;

    [Header("Audio")]
    [SerializeField] AudioSource alertAudioSource;
    [SerializeField] AudioClip alertClip;

    Coroutine activeRoutine;

    void Awake()
    {
        HideAlertImmediate();
    }

    public void ShowAlert()
    {
        ShowAlert(0f);
    }

    public void ShowAlert(float countdownSeconds)
    {
        if (alertTextRoot != null)
            alertTextRoot.SetActive(true);

        if (alertAudioSource != null && alertClip != null)
            alertAudioSource.PlayOneShot(alertClip);

        if (activeRoutine != null)
            StopCoroutine(activeRoutine);

        if (countdownSeconds > 0f)
        {
            activeRoutine = StartCoroutine(CountdownRoutine(countdownSeconds));
        }
        else
        {
            if (countdownText != null)
                countdownText.text = string.Empty;
            if (autoHideDelay > 0f && alertTextRoot != null)
                activeRoutine = StartCoroutine(HideAfterDelay(autoHideDelay));
        }
    }

    public void HideAlert()
    {
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }
        HideAlertImmediate();
    }

    void HideAlertImmediate()
    {
        if (alertTextRoot != null)
            alertTextRoot.SetActive(false);
        if (countdownText != null)
            countdownText.text = string.Empty;
    }

    IEnumerator CountdownRoutine(float seconds)
    {
        float remaining = seconds;
        while (remaining > 0f)
        {
            if (countdownText != null)
                countdownText.text = string.Format(countdownFormat, remaining);
            remaining -= Time.deltaTime;
            yield return null;
        }

        if (countdownText != null)
            countdownText.text = countdownArrivedText;

        // Parpadeo: el cartel queda visible pero pestañea durante arrivedBlinkDuration
        // segundos para reforzar visualmente que el auto llegó. Después se oculta.
        if (arrivedBlinkDuration > 0f && alertTextRoot != null)
        {
            float blinkElapsed = 0f;
            bool visible = true;
            while (blinkElapsed < arrivedBlinkDuration)
            {
                visible = !visible;
                alertTextRoot.SetActive(visible);
                float wait = Mathf.Min(arrivedBlinkInterval, arrivedBlinkDuration - blinkElapsed);
                yield return new WaitForSeconds(wait);
                blinkElapsed += wait;
            }
        }

        HideAlertImmediate();
        activeRoutine = null;
    }

    IEnumerator HideAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideAlertImmediate();
        activeRoutine = null;
    }
}
