using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
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
    [SerializeField, Min(0f)] float timeBeforeAlert = 5f;
    [SerializeField, Min(0f)] float timeBetweenAlertAndArrival = 5f;
    [Tooltip("Segundos tras irse el auto antes de volver al main menu.")]
    [SerializeField, Min(0f)] float delayBeforeMainMenu = 10f;
    [Tooltip("Build index de la escena de main menu.")]
    [SerializeField] int mainMenuSceneIndex = 0;

    [Tooltip("Objeto libre que aparece junto al pit alert y se oculta unos segundos después de que el auto llega.")]
    [SerializeField] GameObject carArrivalObject;
    [Tooltip("Segundos tras la llegada del auto antes de ocultar el objeto.")]
    [SerializeField, Min(0f)] float carArrivalObjectHideDelay = 3f;

    bool isAnimatingChain;
    bool subscribedToTuerca;
    bool subscribedToTuercaAttach;
    bool subscribedToWheelRelease;
    bool removeWheelChainRunning;
    bool nutReinstalled;
    bool isPlayerInteractableWheel = true;

    public bool IsAutomatedPitStopRunning { get; private set; }

    public void SetPlayerInteractableWheel(bool isPlayerWheel)
    {
        isPlayerInteractableWheel = isPlayerWheel;

        if (isPlayerWheel)
            return;

        if (wheelGrabInteractable != null)
            wheelGrabInteractable.enabled = false;
        if (wheelTwoHandGrab != null)
            wheelTwoHandGrab.enabled = false;
        AnchorWheelKinematic();
    }

    IEnumerator Start()
    {
        GameModeManager.GameMode mode = GameModeManager.SelectedMode;

        // En modo Manual la gun se activa recién cuando el auto frena en pits.
        if (gunRoot != null)
            gunRoot.SetActive(false);
        // El objeto de llegada arranca oculto; aparece/desaparece con el auto en PitStopSequence.
        if (carArrivalObject != null)
            carArrivalObject.SetActive(false);
        if (newWheelPickupRoot != null)
            newWheelPickupRoot.SetActive(isPlayerInteractableWheel && mode == GameModeManager.GameMode.InstallWheel);
        if (wheelGrabInteractable != null)
            wheelGrabInteractable.enabled = isPlayerInteractableWheel && mode == GameModeManager.GameMode.RemoveWheel;

        switch (mode)
        {
            case GameModeManager.GameMode.Manual:
                if (!isPlayerInteractableWheel)
                {
                    if (wheelTwoHandGrab != null)
                        wheelTwoHandGrab.enabled = false;
                    if (wheelGrabInteractable != null)
                        wheelGrabInteractable.enabled = false;
                    AnchorWheelKinematic();
                    yield break;
                }

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
                if (!isPlayerInteractableWheel)
                {
                    AnchorWheelKinematic();
                    yield break;
                }

                yield return AutoDetachNutsCoroutine();
                if (wheelTwoHandGrab != null)
                {
                    wheelTwoHandGrab.OnReleasedAfterCarry.AddListener(OnWheelReleasedAfterCarry);
                    subscribedToWheelRelease = true;
                }
                yield break;

            case GameModeManager.GameMode.InstallWheel:
                if (!isPlayerInteractableWheel)
                {
                    AnchorWheelKinematic();
                    yield break;
                }

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

        // El objeto aparece junto con el pit alert (cuando se avisa que el auto está llegando).
        if (carArrivalObject != null)
            carArrivalObject.SetActive(true);

        if (timeBetweenAlertAndArrival > 0f)
            yield return new WaitForSeconds(timeBetweenAlertAndArrival);

        if (carMovement != null)
            yield return carMovement.MoveToStop();

        // El auto llegó: ocultar el objeto unos segundos después.
        if (carArrivalObject != null)
            StartCoroutine(HideCarArrivalObjectAfterDelay());

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

        // 10 segundos después de que se fue el auto, volver al main menu.
        yield return new WaitForSeconds(delayBeforeMainMenu);
        SceneManager.LoadScene(mainMenuSceneIndex);
    }

    IEnumerator HideCarArrivalObjectAfterDelay()
    {
        yield return new WaitForSeconds(carArrivalObjectHideDelay);
        if (carArrivalObject != null)
            carArrivalObject.SetActive(false);
    }

    void HandleTuercaAttachedToWheel(Tuerca tuerca)
    {
        if (!isPlayerInteractableWheel) return;
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
        if (!isPlayerInteractableWheel) return;
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

    public IEnumerator PlayAutomatedPitStopSequence()
    {
        if (IsAutomatedPitStopRunning)
            yield break;

        IsAutomatedPitStopRunning = true;
        SetPlayerInteractableWheel(false);

        if (nutGunAnimation != null)
        {
            nutGunAnimation.PlayRemoval();
            yield return new WaitUntil(() => !nutGunAnimation.IsPlaying);
        }
        else
        {
            yield return AutoDetachNutsCoroutine();
        }

        yield return PlayWheelRemovalAndWait();
        yield return PlayWheelInstallationAndWait();

        if (nutGunAnimation != null)
        {
            nutGunAnimation.PlayInstallation();
            yield return new WaitUntil(() => !nutGunAnimation.IsPlaying);
        }

        IsAutomatedPitStopRunning = false;
    }

    IEnumerator PlayWheelRemovalAndWait()
    {
        if (wheelAnimation == null)
            yield break;

        bool done = false;
        UnityEngine.Events.UnityAction onComplete = () => done = true;
        wheelAnimation.OnRemovalComplete.AddListener(onComplete);
        wheelAnimation.PlayRemoval();
        while (!done) yield return null;
        wheelAnimation.OnRemovalComplete.RemoveListener(onComplete);
    }

    IEnumerator PlayWheelInstallationAndWait()
    {
        if (wheelAnimation == null)
            yield break;

        bool done = false;
        UnityEngine.Events.UnityAction onComplete = () => done = true;
        wheelAnimation.OnInstallationComplete.AddListener(onComplete);
        wheelAnimation.PlayInstallation();
        while (!done) yield return null;
        wheelAnimation.OnInstallationComplete.RemoveListener(onComplete);
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
