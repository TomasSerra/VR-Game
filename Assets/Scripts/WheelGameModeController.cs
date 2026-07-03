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

    [Header("Tutorial (Main Menu)")]
    [Tooltip("Marca esta rueda como rueda de tutorial: re-ejecuta el flujo del modo elegido cada vez que se aprieta un botón de modo en el menú, sin secuencia de pits ni cambio de escena.")]
    [SerializeField] bool tutorialWheel;

    [Header("Auto-detach Timing")]
    [SerializeField, Min(0f)] float initialDelay = 1.5f;
    [SerializeField, Min(0f)] float perNutDelayFallback = 0.3f;
    [SerializeField, Min(0f)] float delayBeforeWheelRemoval = 0.8f;

    [Header("Auto-detach Fallback Impulse (sin WheelNutGunAnimation)")]
    [SerializeField] float nutOutwardImpulse = 0.05f;
    [SerializeField] Transform wheelCenterForImpulse;

    [Header("Per-mode Objects")]
    [SerializeField] GameObject gunRoot;
    [Tooltip("Raíz de la rueda nueva (la que se agarra en el modo colocar). Si queda vacío se busca por tag NewWheel al iniciar.")]
    [SerializeField] GameObject newWheelPickupRoot;

    [Header("Remove Wheel (modo sacar rueda)")]
    [Tooltip("Zona de instalación del auto. Si queda vacío se busca en la jerarquía del auto.")]
    [SerializeField] WheelInstallZone installZone;
    [Tooltip("Distancia (m) desde su lugar en el auto a la que hay que llevar la rueda agarrada para que cuente como sacada (dispara la instalación de la nueva + tuerca).")]
    [SerializeField, Min(0.05f)] float removalCommitDistance = 0.5f;
    [Tooltip("Segundos que tarda en desvanecerse la rueda que el jugador tiene en las manos cuando arranca la instalación de la nueva.")]
    [SerializeField, Min(0f)] float carriedWheelFadeDuration = 0.8f;

    [Header("Pit Stop Flow (escena Game)")]
    [SerializeField] F1CarPitMovement carMovement;
    [SerializeField] PitAlertController pitAlert;
    [SerializeField] PitTimerDisplay pitTimer;
    [SerializeField, Min(0f)] float timeBeforeAlert = 5f;
    [SerializeField, Min(0f)] float timeBetweenAlertAndArrival = 5f;
    [Tooltip("Fallback si no hay PitEndPrompt en la escena: segundos tras irse el auto antes de volver al main menu.")]
    [SerializeField, Min(0f)] float delayBeforeMainMenu = 10f;
    [Tooltip("Build index de la escena de main menu (fallback sin PitEndPrompt).")]
    [SerializeField] int mainMenuSceneIndex = 0;

    [Tooltip("Objeto libre que aparece junto al pit alert y se oculta unos segundos después de que el auto llega.")]
    [SerializeField] GameObject carArrivalObject;
    [Tooltip("Segundos tras la llegada del auto antes de ocultar el objeto.")]
    [SerializeField, Min(0f)] float carArrivalObjectHideDelay = 3f;

    bool isAnimatingChain;
    bool subscribedToTuerca;
    bool subscribedToTuercaAttach;
    bool subscribedToInstallZone;
    bool subscribedToModeChanged;
    bool nutReinstalled;
    bool isPlayerInteractableWheel = true;

    // Señal de fin de la tarea del jugador en los modos sacar/colocar (dispara la salida del auto).
    bool modeTaskCompleted;
    bool finishInstallRunning;
    GameObject installAnimClone;

    public bool IsAutomatedPitStopRunning { get; private set; }

    public void SetPlayerInteractableWheel(bool isPlayerWheel)
    {
        // Una rueda de tutorial nunca se desactiva desde afuera (p.ej. el F1CarPitMovement).
        if (tutorialWheel)
        {
            isPlayerInteractableWheel = true;
            return;
        }

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
        // La rueda de repuesto (tag NewWheel) usa este mismo prefab pero no corre lógica de
        // modo: es sólo un objeto agarrable que la zona de instalación sabe reconocer.
        if (gameObject.CompareTag(WheelInstallZone.NewWheelTag))
            yield break;

        CacheSceneReferences();

        if (tutorialWheel)
        {
            GameModeManager.OnModeChanged += HandleTutorialModeChanged;
            subscribedToModeChanged = true;
            yield return StartCoroutine(RunTutorialSetup(GameModeManager.SelectedMode));
        }
        else
        {
            yield return StartCoroutine(RunModeSetup(GameModeManager.SelectedMode));
        }
    }

    // Las referencias que viven en la escena (y no dentro del prefab de la rueda) se resuelven
    // en runtime para no depender del cableado por Inspector en cada escena.
    void CacheSceneReferences()
    {
        if (newWheelPickupRoot == null)
        {
            GameObject tagged = GameObject.FindWithTag(WheelInstallZone.NewWheelTag);
            if (tagged != null)
                newWheelPickupRoot = tagged;
        }

        if (installZone == null)
            installZone = transform.root.GetComponentInChildren<WheelInstallZone>(true);
    }

    // Reinicia el estado y vuelve a reproducir el flujo del modo elegido, como recién
    // spawneada ("resetear el auto"). Lo dispara OnModeChanged en las ruedas de tutorial.
    void HandleTutorialModeChanged(GameModeManager.GameMode newMode)
    {
        StopAllCoroutines();
        ResetTutorialToInitial();
        StartCoroutine(RunTutorialSetup(newMode));
    }

    // Tutorial (main menu): mismo flujo jugable que en la escena Game pero sin secuencia de
    // pits (el auto ya está presente) y re-ejecutable con cada click de botón de modo.
    IEnumerator RunTutorialSetup(GameModeManager.GameMode mode)
    {
        isPlayerInteractableWheel = true;
        modeTaskCompleted = false;
        finishInstallRunning = false;

        if (wheelGrabInteractable != null)
            wheelGrabInteractable.enabled = false;
        if (gunRoot != null)
            gunRoot.SetActive(mode == GameModeManager.GameMode.Manual);

        switch (mode)
        {
            case GameModeManager.GameMode.RemoveWheel:
                if (wheelTwoHandGrab != null)
                    wheelTwoHandGrab.enabled = true;
                yield return RemoveWheelFlow();
                break;

            case GameModeManager.GameMode.InstallWheel:
                if (wheelTwoHandGrab != null)
                    wheelTwoHandGrab.enabled = true;
                yield return InstallWheelFlow();
                break;

            case GameModeManager.GameMode.Manual:
                if (wheelTwoHandGrab != null)
                    wheelTwoHandGrab.enabled = false;
                AnchorWheelKinematic();
                SubscribeToNutEvents();
                break;
        }
    }

    IEnumerator RunModeSetup(GameModeManager.GameMode mode)
    {
        // En modo Manual la gun se activa recién cuando el auto frena en pits.
        if (gunRoot != null)
            gunRoot.SetActive(false);
        // El objeto de llegada arranca oculto; aparece/desaparece con el auto en PitStopSequence.
        if (carArrivalObject != null)
            carArrivalObject.SetActive(false);

        if (!isPlayerInteractableWheel)
        {
            AnchorWheelKinematic();
            yield break;
        }

        modeTaskCompleted = false;
        finishInstallRunning = false;

        if (wheelGrabInteractable != null)
            wheelGrabInteractable.enabled = false;

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
                SubscribeToNutEvents();
                yield return StartCoroutine(PitStopSequence(mode));
                yield break;

            case GameModeManager.GameMode.RemoveWheel:
            case GameModeManager.GameMode.InstallWheel:
                if (wheelTwoHandGrab != null)
                    wheelTwoHandGrab.enabled = true;
                yield return StartCoroutine(PitStopSequence(mode));
                yield break;
        }
    }

    void SubscribeToNutEvents()
    {
        if (!subscribedToTuerca)
        {
            Tuerca.OnDetachedByGun += HandleTuercaDetached;
            subscribedToTuerca = true;
        }
        if (!subscribedToTuercaAttach)
        {
            Tuerca.OnAttachedToWheel += HandleTuercaAttachedToWheel;
            subscribedToTuercaAttach = true;
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
        if (subscribedToInstallZone && installZone != null)
        {
            installZone.OnWheelInstalled.RemoveListener(HandleNewWheelInstalled);
            subscribedToInstallZone = false;
        }
        if (subscribedToModeChanged)
        {
            GameModeManager.OnModeChanged -= HandleTutorialModeChanged;
            subscribedToModeChanged = false;
        }
    }

    // Secuencia completa de pits (escena Game): alerta, llegada del auto, timer, la tarea del
    // modo elegido, salida del auto y el prompt de repetir / volver al menú.
    IEnumerator PitStopSequence(GameModeManager.GameMode mode)
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

        switch (mode)
        {
            case GameModeManager.GameMode.Manual:
                if (gunRoot != null)
                    gunRoot.SetActive(true);
                nutReinstalled = false;
                yield return new WaitUntil(() => nutReinstalled && !isAnimatingChain);
                if (gunRoot != null)
                    gunRoot.SetActive(false);
                break;

            case GameModeManager.GameMode.RemoveWheel:
                yield return RemoveWheelFlow();
                break;

            case GameModeManager.GameMode.InstallWheel:
                yield return InstallWheelFlow();
                break;
        }

        if (pitTimer != null)
            pitTimer.StopTimer();

        if (carMovement != null)
            yield return carMovement.LeaveAndFade();

        yield return ShowEndPromptOrReturnToMenu();
    }

    // Modo sacar rueda: la gun saca la tuerca (A) y el jugador hace la remoción (B) a mano:
    // agarra la rueda con las dos manos y la lleva lejos del auto. Al alejarse lo suficiente
    // se instala la "nueva" (C con un clon, mientras la que tiene en las manos se desvanece)
    // y la gun coloca la tuerca (D).
    IEnumerator RemoveWheelFlow()
    {
        yield return AutoDetachNutsCoroutine();

        // El clon que después reproduce C se crea YA, con la rueda todavía clavada en su
        // lugar exacto del auto (crearlo recién al sacarla hacía que C arrancara desde una
        // pose corrida). Queda invisible, esperando en el punto donde termina B.
        WheelRemovalAnimation cloneAnim = null;
        if (wheelAnimation != null)
        {
            cloneAnim = CreateInstallAnimClone();
            if (cloneAnim != null)
                cloneAnim.SetRenderersVisible(false);
        }

        if (wheelGrabInteractable != null)
            wheelGrabInteractable.enabled = true;

        if (wheelTwoHandGrab != null)
        {
            float commitSqr = removalCommitDistance * removalCommitDistance;
            yield return new WaitUntil(() =>
                wheelTwoHandGrab.IsCarrying &&
                (wheelTwoHandGrab.transform.position - wheelTwoHandGrab.AnchorWorldPosition).sqrMagnitude >= commitSqr);
        }

        yield return CommitRemovalCoroutine(cloneAnim);
        modeTaskCompleted = true;
    }

    IEnumerator CommitRemovalCoroutine(WheelRemovalAnimation cloneAnim)
    {
        // C la reproduce el clon pre-creado en el auto; la rueda real sigue en las manos del
        // jugador desvaneciéndose (así la animación no se la arranca de las manos).
        bool cloneDone = false;

        if (wheelAnimation != null)
        {
            if (cloneAnim != null)
            {
                UnityEngine.Events.UnityAction onDone = () => cloneDone = true;
                cloneAnim.OnInstallationComplete.AddListener(onDone);
                cloneAnim.PlayInstallation();
            }
            else
            {
                cloneDone = true;
            }

            yield return wheelAnimation.FadeOutRoutine(carriedWheelFadeDuration);
        }
        else
        {
            cloneDone = true;
        }

        // Soltar y esconder la rueda real (ya invisible) en su lugar del auto mientras el
        // clon termina de instalarse justo ahí.
        if (wheelGrabInteractable != null)
            wheelGrabInteractable.enabled = false;
        if (wheelTwoHandGrab != null)
            wheelTwoHandGrab.ResetToAnchor();
        if (wheelAnimation != null)
            wheelAnimation.SetRenderersVisible(false);

        while (!cloneDone)
            yield return null;

        // Swap invisible: desaparece el clon y la rueda real reaparece instalada en su lugar.
        if (installAnimClone != null)
        {
            Destroy(installAnimClone);
            installAnimClone = null;
        }
        if (wheelAnimation != null)
            wheelAnimation.ResetRemoval();

        // D: la gun coloca la tuerca (que quedó en la gun tras la animación A).
        if (nutGunAnimation != null)
        {
            nutGunAnimation.PlayInstallation();
            yield return new WaitUntil(() => !nutGunAnimation.IsPlaying);
        }
    }

    // Clona la rueda para reproducir la animación de instalación en el auto. El clon es sólo
    // visual: se le apagan comportamientos, colliders y física para que no interactúe con nada.
    WheelRemovalAnimation CreateInstallAnimClone()
    {
        GameObject source = wheelAnimation.gameObject;
        installAnimClone = Instantiate(source, source.transform.parent);
        installAnimClone.name = source.name + " (anim instalacion)";
        installAnimClone.tag = "Untagged";

        foreach (MonoBehaviour behaviour in installAnimClone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!(behaviour is WheelRemovalAnimation))
                behaviour.enabled = false;
        }
        foreach (Collider collider in installAnimClone.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (Rigidbody body in installAnimClone.GetComponentsInChildren<Rigidbody>(true))
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
        }

        WheelRemovalAnimation cloneAnim = installAnimClone.GetComponent<WheelRemovalAnimation>();
        if (cloneAnim == null)
        {
            Destroy(installAnimClone);
            installAnimClone = null;
            return null;
        }

        cloneAnim.enabled = true;
        // El clon nace en la pose de las manos del jugador: hay que pisar su pose inicial con
        // el lugar real de la rueda en el auto para que la animación termine bien ubicada.
        cloneAnim.OverrideInitialLocalPose(wheelAnimation.InitialLocalPosition, wheelAnimation.InitialLocalRotation);
        return cloneAnim;
    }

    // Modo colocar rueda: A y B automáticas, aparece la rueda nueva, el jugador la lleva a la
    // zona y aprieta el gatillo. La zona esconde la rueda del jugador y acá la original
    // reaparece instalada al instante; después la gun coloca la tuerca (D).
    IEnumerator InstallWheelFlow()
    {
        if (installZone != null && !subscribedToInstallZone)
        {
            installZone.OnWheelInstalled.AddListener(HandleNewWheelInstalled);
            subscribedToInstallZone = true;
        }

        yield return AutoDetachNutsCoroutine();
        yield return new WaitForSeconds(delayBeforeWheelRemoval);
        if (wheelAnimation != null)
            wheelAnimation.PlayRemoval();

        yield return new WaitUntil(() => modeTaskCompleted);

        if (subscribedToInstallZone && installZone != null)
        {
            installZone.OnWheelInstalled.RemoveListener(HandleNewWheelInstalled);
            subscribedToInstallZone = false;
        }
    }

    void HandleNewWheelInstalled()
    {
        if (!isPlayerInteractableWheel) return;
        if (modeTaskCompleted || finishInstallRunning) return;
        StartCoroutine(FinishInstallCoroutine());
    }

    IEnumerator FinishInstallCoroutine()
    {
        finishInstallRunning = true;

        // La original reaparece instalada de una (sin animación): como la nueva desapareció en
        // el mismo frame, parece que la nueva quedó puesta.
        if (wheelAnimation != null)
            wheelAnimation.ResetRemoval();

        if (nutGunAnimation != null)
        {
            nutGunAnimation.PlayInstallation();
            yield return new WaitUntil(() => !nutGunAnimation.IsPlaying);
        }

        finishInstallRunning = false;
        modeTaskCompleted = true;
    }

    IEnumerator ShowEndPromptOrReturnToMenu()
    {
        PitEndPrompt prompt = Object.FindFirstObjectByType<PitEndPrompt>(FindObjectsInactive.Include);
        if (prompt != null)
        {
            prompt.Show(pitTimer != null ? pitTimer.ElapsedSeconds : -1f);
            yield break;
        }

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

    // Vuelve la rueda, la gun, las tuercas y el pickup a su estado inicial, y limpia
    // suscripciones/flags para poder re-ejecutar el modo desde cero.
    void ResetTutorialToInitial()
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
        if (subscribedToInstallZone && installZone != null)
        {
            installZone.OnWheelInstalled.RemoveListener(HandleNewWheelInstalled);
            subscribedToInstallZone = false;
        }

        isAnimatingChain = false;
        nutReinstalled = false;
        modeTaskCompleted = false;
        finishInstallRunning = false;

        if (installAnimClone != null)
        {
            Destroy(installAnimClone);
            installAnimClone = null;
        }

        if (wheelAnimation != null)
            wheelAnimation.ResetRemoval();
        if (nutGunAnimation != null)
            nutGunAnimation.ResetAnimationState();
        if (wheelTwoHandGrab != null)
            wheelTwoHandGrab.ResetToAnchor();
        if (newWheelPickupRoot != null)
        {
            var newWheelGrab = newWheelPickupRoot.GetComponentInChildren<TwoHandRequiredGrab>(true);
            if (newWheelGrab != null)
            {
                // El install zone pudo haberla desactivado al colocarla; reactivar y reposicionar.
                newWheelGrab.gameObject.SetActive(true);
                newWheelGrab.ResetToAnchor();
            }
        }
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
}
