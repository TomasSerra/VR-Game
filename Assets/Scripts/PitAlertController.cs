using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class PitAlertController : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] GameObject alertTextRoot;
    [SerializeField, Min(0f)] float autoHideDelay = 4f;

    [Header("Audio")]
    [SerializeField] AudioSource alertAudioSource;
    [SerializeField] AudioClip alertClip;

    Coroutine hideRoutine;

    void Awake()
    {
        HideAlertImmediate();
    }

    public void ShowAlert()
    {
        if (alertTextRoot != null)
            alertTextRoot.SetActive(true);

        if (alertAudioSource != null && alertClip != null)
            alertAudioSource.PlayOneShot(alertClip);

        if (hideRoutine != null)
            StopCoroutine(hideRoutine);

        if (autoHideDelay > 0f && alertTextRoot != null)
            hideRoutine = StartCoroutine(HideAfterDelay());
    }

    public void HideAlert()
    {
        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }
        HideAlertImmediate();
    }

    void HideAlertImmediate()
    {
        if (alertTextRoot != null)
            alertTextRoot.SetActive(false);
    }

    IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(autoHideDelay);
        HideAlertImmediate();
        hideRoutine = null;
    }
}
