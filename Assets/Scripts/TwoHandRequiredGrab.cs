using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public class TwoHandRequiredGrab : MonoBehaviour, IXRSelectFilter
{
    public UnityEvent OnReleasedAfterCarry;

    XRGrabInteractable grab;
    Rigidbody rb;
    Transform anchorParent;
    Vector3 anchorLocalPos;
    Quaternion anchorLocalRot;
    bool wasCarried;
    bool wasCarrying;
    bool filterRegistered;

    public bool canProcess => isActiveAndEnabled;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();
        grab.selectMode = InteractableSelectMode.Multiple;
        grab.trackPosition = false;
        grab.trackRotation = false;
        grab.matchAttachPosition = false;
        grab.matchAttachRotation = false;
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
    }

    void Start()
    {
        anchorParent = transform.parent;
        anchorLocalPos = transform.localPosition;
        anchorLocalRot = transform.localRotation;
        SnapToAnchor();
    }

    // Bloquea cualquier intento de XR de seleccionar la rueda con una sola mano:
    // el primer selector sólo se acepta cuando ya hay 2 interactores haciendo hover,
    // y a partir de ahí se permite que entre el segundo (count >= 1).
    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        if (wasCarried)
            return true;

        if (grab.interactorsSelecting.Count >= 1)
            return true;

        return grab.interactorsHovering.Count >= 2;
    }

    void LateUpdate()
    {
        bool twoHands = grab.interactorsSelecting.Count >= 2;
        grab.trackPosition = twoHands;
        grab.trackRotation = twoHands;
        grab.matchAttachPosition = twoHands;
        grab.matchAttachRotation = twoHands;

        if (twoHands)
        {
            SetAnchoredPhysics();
            wasCarried = true;
            wasCarrying = true;
            return;
        }

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

        rb.isKinematic = false;
        rb.useGravity = true;
    }

    public void ResetToAnchor()
    {
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
