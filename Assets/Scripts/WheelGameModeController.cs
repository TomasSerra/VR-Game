using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class WheelGameModeController : MonoBehaviour
{
    [Header("Wheel References")]
    [SerializeField] WheelRemovalAnimation wheelAnimation;
    [SerializeField] Tuerca[] attachedNuts;
    [SerializeField] XRGrabInteractable wheelGrabInteractable;

    [Header("Auto-detach Timing")]
    [SerializeField, Min(0f)] float initialDelay = 1.5f;
    [SerializeField, Min(0f)] float perNutDelay = 0.3f;
    [SerializeField, Min(0f)] float delayBeforeWheelRemoval = 0.8f;

    [Header("Auto-detach Impulse")]
    [SerializeField] float nutOutwardImpulse = 0.05f;
    [SerializeField] Transform wheelCenterForImpulse;

    [Header("Per-mode Objects")]
    [SerializeField] GameObject gunRoot;
    [SerializeField] GameObject newWheelPickupRoot;

    IEnumerator Start()
    {
        GameModeManager.GameMode mode = GameModeManager.SelectedMode;

        if (gunRoot != null)
            gunRoot.SetActive(mode == GameModeManager.GameMode.Manual);
        if (newWheelPickupRoot != null)
            newWheelPickupRoot.SetActive(mode == GameModeManager.GameMode.InstallWheel);
        if (wheelGrabInteractable != null)
            wheelGrabInteractable.enabled = mode == GameModeManager.GameMode.RemoveWheel;

        switch (mode)
        {
            case GameModeManager.GameMode.Manual:
                yield break;

            case GameModeManager.GameMode.RemoveWheel:
                yield return AutoDetachNutsCoroutine();
                yield break;

            case GameModeManager.GameMode.InstallWheel:
                yield return AutoDetachNutsCoroutine();
                yield return new WaitForSeconds(delayBeforeWheelRemoval);
                if (wheelAnimation != null)
                    wheelAnimation.PlayRemoval();
                yield break;
        }
    }

    IEnumerator AutoDetachNutsCoroutine()
    {
        yield return new WaitForSeconds(initialDelay);
        for (int i = 0; i < attachedNuts.Length; i++)
        {
            Tuerca nut = attachedNuts[i];
            if (nut != null)
                nut.AutoDetach(GetOutwardImpulse(nut.transform));
            yield return new WaitForSeconds(perNutDelay);
        }
    }

    Vector3 GetOutwardImpulse(Transform nut)
    {
        if (nutOutwardImpulse <= 0f) return default;
        Transform center = wheelCenterForImpulse != null ? wheelCenterForImpulse : transform;
        Vector3 outward = nut.position - center.position;
        if (outward.sqrMagnitude < 0.0001f) return default;
        return outward.normalized * nutOutwardImpulse;
    }

    public void RequestWheelRemoval()
    {
        if (GameModeManager.SelectedMode != GameModeManager.GameMode.RemoveWheel) return;
        if (wheelAnimation != null)
            wheelAnimation.PlayRemoval();
    }

    public void RequestWheelInstallation()
    {
        if (GameModeManager.SelectedMode != GameModeManager.GameMode.InstallWheel) return;
        if (wheelAnimation != null)
            wheelAnimation.PlayInstallation();
    }
}
