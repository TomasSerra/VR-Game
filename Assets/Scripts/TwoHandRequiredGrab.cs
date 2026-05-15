using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public class TwoHandRequiredGrab : MonoBehaviour
{
    public UnityEvent OnReleasedAfterCarry;

    XRGrabInteractable grab;
    Rigidbody rb;
    Transform anchorParent;
    Vector3 anchorLocalPos;
    Quaternion anchorLocalRot;
    bool wasCarried;
    bool wasCarrying;

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

    void Start()
    {
        anchorParent = transform.parent;
        anchorLocalPos = transform.localPosition;
        anchorLocalRot = transform.localRotation;
        SetAnchoredPhysics();
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

        if (wasCarrying)
        {
            wasCarrying = false;
            OnReleasedAfterCarry?.Invoke();
        }

        if (!wasCarried)
        {
            SetAnchoredPhysics();
            if (transform.parent != anchorParent)
                transform.SetParent(anchorParent, false);
            transform.localPosition = anchorLocalPos;
            transform.localRotation = anchorLocalRot;
            return;
        }

        rb.isKinematic = false;
        rb.useGravity = true;
    }

    public void ResetToAnchor()
    {
        transform.SetParent(anchorParent, false);
        transform.localPosition = anchorLocalPos;
        transform.localRotation = anchorLocalRot;
        wasCarried = false;
        wasCarrying = false;
        SetAnchoredPhysics();
    }

    void SetAnchoredPhysics()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;
    }
}
