using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

// Agarre de rueda a DOS manos sobre la base del agarre simple: XRI estándar con dynamic
// attach (cada mano agarra en el punto de contacto, sin poses fijas), pero la rueda sólo se
// mueve cuando hay una mano en CADA manija. Cualquier mano puede ir a cualquier manija (no
// importa cuál ni en qué orden), lo único prohibido es dos manos en la MISMA manija (el
// filtro rechaza esa segunda selección). Los agarres son pegajosos: la primera mano queda
// agarrada sin mover la rueda; al entrar la segunda, XRI la lleva con las dos manos. Al
// soltar cualquiera de las dos, cae con gravedad.
[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public class TwoHandRequiredGrab : MonoBehaviour, IXRSelectFilter
{
    public UnityEvent OnReleasedAfterCarry;

    [Header("Handles")]
    [Tooltip("Manija derecha de la rueda. Si queda vacía se busca un hijo llamado HandleRight.")]
    [SerializeField] Transform handleRight;
    [Tooltip("Manija izquierda de la rueda. Si queda vacía se busca un hijo llamado HandleLeft.")]
    [SerializeField] Transform handleLeft;

    [Header("Grabbable Gating")]
    [Tooltip("Marca si ESTA rueda es agarrable. El prefab viene en true; desmarcá esto sólo en las ruedas que NO deben agarrarse (decorativas / no interactuables). Al estar en false la rueda queda inerte y anclada.")]
    [SerializeField] bool isGrabbableWheel = true;
    [Tooltip("Si está activo, la rueda sólo es agarrable en los modos de la lista de abajo.")]
    [SerializeField] bool useGameModeGate = true;
    [SerializeField] GameModeManager.GameMode[] grabbableModes =
    {
        GameModeManager.GameMode.RemoveWheel,
        GameModeManager.GameMode.InstallWheel,
        GameModeManager.GameMode.Tutorial
    };
    [Tooltip("Marca esta rueda como siempre agarrable, ignorando el modo. Útil para la rueda del tutorial del main menu.")]
    [SerializeField] bool alwaysGrabbable;

    [Header("Debug (Simulador)")]
    [Tooltip("SOLO PARA PROBAR EN EL SIMULADOR: cada control que agarra la rueda queda agarrado PARA SIEMPRE (interacción manual de XRI), así se puede agarrar con un control, cambiar al otro en el simulador y ver el agarre a dos manos. Apagalo para el comportamiento real (en el simulador el grip se suelta al cambiar de control, por eso sin esto nunca llegan a estar las dos manos a la vez).")]
    [SerializeField] bool debugPermaGrab;

    XRGrabInteractable grab;
    Rigidbody rb;
    Collider handleRightCollider;
    Collider handleLeftCollider;
    Transform anchorParent;
    Vector3 anchorLocalPos;
    Quaternion anchorLocalRot;
    Vector3 anchorLocalScale;
    bool wasCarried;
    bool wasCarrying;
    bool filterRegistered;
    readonly HashSet<IXRGrabTransformer> transformerSet = new();
    readonly List<XRBaseInteractor> permaGrabInteractors = new();

    // Estado de carga: con una mano en cada manija, XRI lleva la rueda. Se suelta una y cae.
    bool carrying;
    IXRSelectInteractor carryHandA;
    IXRSelectInteractor carryHandB;

    public bool canProcess => isActiveAndEnabled;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();

        // Misma base a prueba de balas del agarre simple: dynamic attach (agarre en el punto
        // de contacto) y movimiento kinematic. La única diferencia es que el tracking arranca
        // apagado: se prende recién cuando las dos manijas están agarradas (StartCarry), así
        // la primera mano queda pegada sin mover la rueda.
        grab.selectMode = InteractableSelectMode.Multiple;
        grab.movementType = XRBaseInteractable.MovementType.Kinematic;
        grab.useDynamicAttach = true;
        grab.matchAttachPosition = true;
        grab.matchAttachRotation = true;
        grab.snapToColliderVolume = true;
        grab.trackPosition = false;
        grab.trackRotation = false;
        grab.throwOnDetach = false;

        if (handleRight == null)
            handleRight = FindDeepChild(transform, "HandleRight");
        if (handleLeft == null)
            handleLeft = FindDeepChild(transform, "HandleLeft");
        if (handleRight != null)
            handleRightCollider = handleRight.GetComponent<Collider>();
        if (handleLeft != null)
            handleLeftCollider = handleLeft.GetComponent<Collider>();
    }

    void OnEnable()
    {
        if (grab != null && !filterRegistered)
        {
            grab.selectFilters.Add(this);
            filterRegistered = true;
        }
    }

    void OnDisable()
    {
        if (grab != null && filterRegistered)
        {
            grab.selectFilters.Remove(this);
            filterRegistered = false;
        }
        ReleasePermaGrabs();
        StopCarry();
    }

    void Start()
    {
        anchorParent = transform.parent;
        anchorLocalPos = transform.localPosition;
        anchorLocalRot = transform.localRotation;
        anchorLocalScale = transform.localScale;
        SnapToAnchor();
    }

    // Filtro de selección: gate por modo + rechazo de una segunda mano en una manija que ya
    // está agarrada (así la mano libre nunca queda "pegada" inútilmente en la misma manija).
    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        if (!wasCarried && !IsGrabbable)
            return false;

        if (HandlesConfigured)
        {
            Transform incomingHandle = GetHandleAt(GetHandPosition(interactor));
            var selecting = grab.interactorsSelecting;
            for (int i = 0; i < selecting.Count; i++)
            {
                if (!ReferenceEquals(selecting[i], interactor) &&
                    GetGrabbedHandle(selecting[i]) == incomingHandle)
                    return false;
            }
        }

        return true;
    }

    // Permite que un controlador (p.ej. una rueda de tutorial) fuerce la agarrabilidad en
    // runtime, ignorando el gate por modo. La usa WheelGameModeController.
    public void SetAlwaysGrabbable(bool value)
    {
        alwaysGrabbable = value;
    }

    // La rueda está siendo llevada en este momento con las dos manos.
    public bool IsCarrying => carrying;

    // Posición del anclaje (su lugar en el auto / soporte) en coordenadas de mundo.
    public Vector3 AnchorWorldPosition =>
        anchorParent != null ? anchorParent.TransformPoint(anchorLocalPos) : anchorLocalPos;

    bool HandlesConfigured => handleRight != null && handleLeft != null;

    bool IsGrabbable
    {
        get
        {
            if (alwaysGrabbable)
                return true;
            if (!isGrabbableWheel)
                return false;
            if (!useGameModeGate)
                return true;

            GameModeManager.GameMode mode = GameModeManager.SelectedMode;
            for (int i = 0; i < grabbableModes.Length; i++)
            {
                if (grabbableModes[i] == mode)
                    return true;
            }
            return false;
        }
    }

    void LateUpdate()
    {
        if (debugPermaGrab)
            UpdatePermaGrabLatches();

        if (carrying && !CarryPairStillSelecting())
            StopCarry();

        if (!carrying)
            TryStartCarry();

        // Llevada a dos manos: la mueve XRI con el dynamic attach de cada mano. Nada que
        // hacer acá.
        if (carrying)
            return;

        // Nunca fue llevada: queda clavada en el anchor (con o sin una mano apoyada).
        if (!wasCarried)
        {
            SnapToAnchor();
            return;
        }

        if (wasCarrying)
        {
            wasCarrying = false;
            OnReleasedAfterCarry?.Invoke();
        }

        // Fue llevada y ahora no está agarrada a DOS manos: se cae, aunque quede una mano
        // apretando (esto pisa cada frame el estado que XRI restaura al soltar).
        rb.isKinematic = false;
        rb.useGravity = true;
    }

    // La carga arranca SOLO cuando dos interactores distintos sostienen manijas distintas.
    // No importa qué mano ni en qué orden: el filtro ya impidió dos manos en la misma manija.
    void TryStartCarry()
    {
        var selecting = grab.interactorsSelecting;
        if (selecting.Count < 2)
            return;

        IXRSelectInteractor first = selecting[0];
        IXRSelectInteractor second = selecting[1];

        if (HandlesConfigured)
        {
            Transform firstHandle = GetGrabbedHandle(first);
            Transform secondHandle = GetGrabbedHandle(second);
            if (firstHandle == null || secondHandle == null || firstHandle == secondHandle)
                return;
        }

        StartCarry(first, second);
    }

    // Punto de la palma del interactor (el mismo dato que usa XRI para el dynamic attach).
    Vector3 GetHandPosition(IXRSelectInteractor hand)
    {
        Transform attach = hand.GetAttachTransform(grab);
        return attach != null ? attach.position : hand.transform.position;
    }

    // Manija que un interactor YA seleccionado está sosteniendo: se clasifica por su dynamic
    // attach (el punto de la rueda donde agarró, pegado al collider de una manija).
    Transform GetGrabbedHandle(IXRSelectInteractor hand)
    {
        Transform attach = grab.GetAttachTransform(hand);
        Vector3 grabPoint = attach != null ? attach.position : GetHandPosition(hand);
        return GetHandleAt(grabPoint);
    }

    // Manija más cercana a un punto, midiendo contra la superficie de sus colliders (robusto
    // ante escala: los únicos colliders agarrables SON las manijas, así que el punto de
    // agarre siempre está sobre una de las dos).
    Transform GetHandleAt(Vector3 worldPos)
    {
        if (!HandlesConfigured)
            return null;

        float sqrToRight = SqrDistanceToHandle(worldPos, handleRight, handleRightCollider);
        float sqrToLeft = SqrDistanceToHandle(worldPos, handleLeft, handleLeftCollider);
        return sqrToRight <= sqrToLeft ? handleRight : handleLeft;
    }

    static float SqrDistanceToHandle(Vector3 worldPos, Transform handle, Collider handleCollider)
    {
        if (handleCollider != null && handleCollider.enabled)
            return (handleCollider.ClosestPoint(worldPos) - worldPos).sqrMagnitude;
        return (handle.position - worldPos).sqrMagnitude;
    }

    // Las dos manijas están agarradas: se prende el tracking y se re-inicializan los grab
    // transformers para que capturen el agarre ACÁ (con el tracking ya prendido). Como el
    // attach es dinámico y las manos están exactamente donde agarraron, la rueda no salta:
    // arranca a seguir a las dos manos desde donde está.
    void StartCarry(IXRSelectInteractor handA, IXRSelectInteractor handB)
    {
        carrying = true;
        wasCarried = true;
        wasCarrying = true;
        carryHandA = handA;
        carryHandB = handB;

        // Movimiento kinematic de XRI: el rb va kinematic y XRI lo lleva con MovePosition.
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;

        grab.trackPosition = true;
        grab.trackRotation = true;

        ReinitializeGrabTransformers();
    }

    // Repite lo que hace XRGrabInteractable al empezar un agarre (OnGrab + OnGrabCountChanged
    // de cada transformer): el transformer captura sus offsets y el handle bar con el estado
    // ACTUAL. Sin esto quedarían los capturados cuando entró la primera mano, con el tracking
    // apagado, y al prenderlo la rueda se movería con offsets incorrectos.
    void ReinitializeGrabTransformers()
    {
        transformerSet.Clear();
        for (int i = 0; i < grab.singleGrabTransformersCount; i++)
            transformerSet.Add(grab.GetSingleGrabTransformerAt(i));
        for (int i = 0; i < grab.multipleGrabTransformersCount; i++)
            transformerSet.Add(grab.GetMultipleGrabTransformerAt(i));

        Pose currentPose = new Pose(transform.position, transform.rotation);
        Vector3 currentScale = transform.localScale;
        foreach (IXRGrabTransformer transformer in transformerSet)
        {
            transformer.OnGrab(grab);
            transformer.OnGrabCountChanged(grab, currentPose, currentScale);
        }
    }

    void StopCarry()
    {
        carrying = false;
        carryHandA = null;
        carryHandB = null;

        if (grab != null)
        {
            grab.trackPosition = false;
            grab.trackRotation = false;
        }
    }

    // Debug: convierte cada selección en una interacción manual de XRI, que mantiene la
    // selección activa aunque se suelte el grip (o el simulador cambie de control).
    void UpdatePermaGrabLatches()
    {
        var selecting = grab.interactorsSelecting;
        for (int i = 0; i < selecting.Count; i++)
        {
            if (selecting[i] is XRBaseInteractor baseInteractor &&
                !baseInteractor.isPerformingManualInteraction &&
                !permaGrabInteractors.Contains(baseInteractor))
            {
                baseInteractor.StartManualInteraction((IXRSelectInteractable)grab);
                permaGrabInteractors.Add(baseInteractor);
            }
        }

        // Si la selección de un latcheado se cortó desde afuera (p.ej. se desactivó la
        // rueda), hay que cerrar la interacción manual: si no, ese interactor quedaría con
        // isSelectActive forzado en true y agarraría solo cualquier cosa que toque.
        for (int i = permaGrabInteractors.Count - 1; i >= 0; i--)
        {
            XRBaseInteractor interactor = permaGrabInteractors[i];
            if (interactor == null)
            {
                permaGrabInteractors.RemoveAt(i);
                continue;
            }
            if (!IsStillSelecting(interactor))
            {
                if (interactor.isPerformingManualInteraction)
                    interactor.EndManualInteraction();
                permaGrabInteractors.RemoveAt(i);
            }
        }
    }

    void ReleasePermaGrabs()
    {
        for (int i = permaGrabInteractors.Count - 1; i >= 0; i--)
        {
            XRBaseInteractor interactor = permaGrabInteractors[i];
            if (interactor != null && interactor.isPerformingManualInteraction)
                interactor.EndManualInteraction();
        }
        permaGrabInteractors.Clear();
    }

    bool CarryPairStillSelecting()
    {
        return IsStillSelecting(carryHandA) && IsStillSelecting(carryHandB);
    }

    bool IsStillSelecting(IXRSelectInteractor interactor)
    {
        if (interactor == null)
            return false;

        var selecting = grab.interactorsSelecting;
        for (int i = 0; i < selecting.Count; i++)
        {
            if (ReferenceEquals(selecting[i], interactor))
                return true;
        }
        return false;
    }

    static Transform FindDeepChild(Transform root, string childName)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
                return child;
            Transform nested = FindDeepChild(child, childName);
            if (nested != null)
                return nested;
        }
        return null;
    }

    public void ResetToAnchor()
    {
        ReleasePermaGrabs();
        StopCarry();
        wasCarried = false;
        wasCarrying = false;
        SnapToAnchor();
    }

    void SnapToAnchor()
    {
        SetAnchoredPhysics();
        if (anchorParent != null && transform.parent != anchorParent)
            transform.SetParent(anchorParent, false);
        transform.localPosition = anchorLocalPos;
        transform.localRotation = anchorLocalRot;
        transform.localScale = anchorLocalScale;
        if (rb != null)
        {
            rb.position = transform.position;
            rb.rotation = transform.rotation;
        }
    }

    void SetAnchoredPhysics()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;
    }
}
