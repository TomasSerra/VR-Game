using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class WheelGameModeController : MonoBehaviour
{
    [Header("Wheel References")]
    [SerializeField] WheelRemovalAnimation wheelAnimation;
    [SerializeField] Tuerca[] attachedNuts;
    [SerializeField] WheelNutGunAnimation nutGunAnimation;
    [SerializeField] XRGrabInteractable wheelGrabInteractable;
    [SerializeField] TwoHandRequiredGrab wheelTwoHandGrab;

    [Header("Auto-detach Timing")]
    [SerializeField, Min(0f)] float initialDelay = 1.5f;
    [SerializeField, Min(0f)] float perNutDelayFallback = 0.3f;
    [SerializeField, Min(0f)] float delayBeforeWheelRemoval = 0.8f;
    [SerializeField, Min(0f)] float delayAfterWheelRemovedBeforeInstall = 1.0f;

    [Header("Auto-detach Fallback Impulse (sin WheelNutGunAnimation)")]
    [SerializeField] float nutOutwardImpulse = 0.05f;
    [SerializeField] Transform wheelCenterForImpulse;

    [Header("Per-mode Objects")]
    [SerializeField] GameObject gunRoot;
    [SerializeField] GameObject newWheelPickupRoot;

    [Header("Pit Stop Flow (Manual mode)")]
    [SerializeField] F1CarPitMovement carMovement;
    [SerializeField] PitAlertController pitAlert;
    [SerializeField] PitTimerDisplay pitTimer;
    [SerializeField, Min(0f)] float timeBeforeAlert = 3f;
    [SerializeField, Min(0f)] float timeBetweenAlertAndArrival = 5f;

    bool isAnimatingChain;
    bool subscribedToTuerca;
    bool subscribedToTuercaAttach;
    bool subscribedToWheelRelease;
    bool removeWheelChainRunning;
    bool nutReinstalled;

    IEnumerator Start()
    {
        GameModeManager.GameMode mode = GameModeManager.SelectedMode;

        // En modo Manual la gun se activa recién cuando el auto frena en pits.
        if (gunRoot != null)
            gunRoot.SetActive(false);
        if (newWheelPickupRoot != null)
            newWheelPickupRoot.SetActive(mode == GameModeManager.GameMode.InstallWheel);
        if (wheelGrabInteractable != null)
            wheelGrabInteractable.enabled = mode == GameModeManager.GameMode.RemoveWheel;

        switch (mode)
        {
            case GameModeManager.GameMode.Manual:
                // En modo Manual sólo se agarra la gun: la rueda no necesita TwoHandRequiredGrab
                // y debe quedar siempre kinematic para que no interfiera físicamente con la gun
                // (evita que al final del PlayInstallation el rb se vuelva dinámico por un frame
                // y mande torques raros a la gun).
                if (wheelTwoHandGrab != null)
                    wheelTwoHandGrab.enabled = false;
                AnchorWheelKinematic();
                Tuerca.OnDetachedByGun += HandleTuercaDetached;
                subscribedToTuerca = true;
                Tuerca.OnAttachedToWheel += HandleTuercaAttachedToWheel;
                subscribedToTuercaAttach = true;
                yield return StartCoroutine(PitStopSequence());
                yield break;

            case GameModeManager.GameMode.RemoveWheel:
                yield return AutoDetachNutsCoroutine();
                if (wheelTwoHandGrab != null)
                {
                    wheelTwoHandGrab.OnReleasedAfterCarry.AddListener(OnWheelReleasedAfterCarry);
                    subscribedToWheelRelease = true;
                }
                yield break;

            case GameModeManager.GameMode.InstallWheel:
                yield return AutoDetachNutsCoroutine();
                yield return new WaitForSeconds(delayBeforeWheelRemoval);
                if (wheelAnimation != null)
                    wheelAnimation.PlayRemoval();
                yield break;
        }
    }

    void OnDestroy()
    {
        if (subscribedToTuerca)
        {
            Tuerca.OnDetachedByGun -= HandleTuercaDetached;
            subscribedToTuerca = false;
        }
        if (subscribedToTuercaAttach)
        {
            Tuerca.OnAttachedToWheel -= HandleTuercaAttachedToWheel;
            subscribedToTuercaAttach = false;
        }
        if (subscribedToWheelRelease && wheelTwoHandGrab != null)
        {
            wheelTwoHandGrab.OnReleasedAfterCarry.RemoveListener(OnWheelReleasedAfterCarry);
            subscribedToWheelRelease = false;
        }
    }

    IEnumerator PitStopSequence()
    {
        if (carMovement != null)
            carMovement.TeleportToInitial();
        if (pitAlert != null)
            pitAlert.HideAlert();
        if (pitTimer != null)
            pitTimer.ResetTimer();

        if (timeBeforeAlert > 0f)
            yield return new WaitForSeconds(timeBeforeAlert);

        float arrivalDuration = carMovement != null ? carMovement.ArrivalDuration : 0f;
        if (pitAlert != null)
            pitAlert.ShowAlert(timeBetweenAlertAndArrival + arrivalDuration);

        if (timeBetweenAlertAndArrival > 0f)
            yield return new WaitForSeconds(timeBetweenAlertAndArrival);

        if (carMovement != null)
            yield return carMovement.MoveToStop();

        if (pitTimer != null)
            pitTimer.StartTimer();
        if (gunRoot != null)
            gunRoot.SetActive(true);

        nutReinstalled = false;
        yield return new WaitUntil(() => nutReinstalled && !isAnimatingChain);

        if (pitTimer != null)
            pitTimer.StopTimer();
        if (gunRoot != null)
            gunRoot.SetActive(false);

        if (carMovement != null)
            yield return carMovement.LeaveAndFade();
    }

    void HandleTuercaAttachedToWheel(Tuerca tuerca)
    {
        if (System.Array.IndexOf(attachedNuts, tuerca) < 0) return;
        nutReinstalled = true;
    }

    void OnWheelReleasedAfterCarry()
    {
        if (removeWheelChainRunning) return;
        StartCoroutine(RemoveWheelInstallChainCoroutine());
    }

    IEnumerator RemoveWheelInstallChainCoroutine()
    {
        removeWheelChainRunning = true;

        if (wheelGrabInteractable != null)
            wheelGrabInteractable.enabled = false;

        yield return new WaitForSeconds(delayAfterWheelRemovedBeforeInstall);

        if (wheelTwoHandGrab != null)
            wheelTwoHandGrab.ResetToAnchor();

        if (wheelAnimation != null)
        {
            bool done = false;
            UnityEngine.Events.UnityAction onComplete = () => done = true;
            wheelAnimation.OnInstallationComplete.AddListener(onComplete);
            wheelAnimation.PlayInstallation();
            while (!done) yield return null;
            wheelAnimation.OnInstallationComplete.RemoveListener(onComplete);
        }

        if (nutGunAnimation != null)
        {
            nutGunAnimation.PlayInstallation();
            yield return new WaitUntil(() => !nutGunAnimation.IsPlaying);
        }

        if (wheelGrabInteractable != null)
            wheelGrabInteractable.enabled = true;

        removeWheelChainRunning = false;
    }

    void HandleTuercaDetached(Tuerca tuerca)
    {
        if (isAnimatingChain) return;
        if (wheelAnimation == null) return;
        if (System.Array.IndexOf(attachedNuts, tuerca) < 0) return;

        StartCoroutine(ManualChainCoroutine());
    }

    IEnumerator ManualChainCoroutine()
    {
        isAnimatingChain = true;

        bool removalDone = false;
        UnityEngine.Events.UnityAction onRemovalComplete = () => removalDone = true;
        wheelAnimation.OnRemovalComplete.AddListener(onRemovalComplete);
        wheelAnimation.PlayRemoval();
        while (!removalDone) yield return null;
        wheelAnimation.OnRemovalComplete.RemoveListener(onRemovalComplete);

        bool installDone = false;
        UnityEngine.Events.UnityAction onInstallComplete = () => installDone = true;
        wheelAnimation.OnInstallationComplete.AddListener(onInstallComplete);
        wheelAnimation.PlayInstallation();
        while (!installDone) yield return null;
        wheelAnimation.OnInstallationComplete.RemoveListener(onInstallComplete);

        isAnimatingChain = false;
    }

    IEnumerator AutoDetachNutsCoroutine()
    {
        yield return new WaitForSeconds(initialDelay);

        if (nutGunAnimation != null)
        {
            nutGunAnimation.PlayRemoval();
            yield return new WaitUntil(() => !nutGunAnimation.IsPlaying);
            yield break;
        }

        for (int i = 0; i < attachedNuts.Length; i++)
        {
            Tuerca nut = attachedNuts[i];
            if (nut != null)
                nut.AutoDetach(GetOutwardImpulse(nut.transform));
            yield return new WaitForSeconds(perNutDelayFallback);
        }
    }

    void AnchorWheelKinematic()
    {
        if (wheelAnimation == null) return;
        Rigidbody wheelRb = wheelAnimation.GetComponent<Rigidbody>();
        if (wheelRb == null) return;
        wheelRb.linearVelocity = Vector3.zero;
        wheelRb.angularVelocity = Vector3.zero;
        wheelRb.isKinematic = true;
        wheelRb.useGravity = false;
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
        if (wheelGrabInteractable != null && wheelGrabInteractable.interactorsSelecting.Count < 2) return;
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
