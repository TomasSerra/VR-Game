using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(XRGrabInteractable))]
public class GunActivation : MonoBehaviour
{
    [SerializeField] InputActionProperty activateAction;

    [SerializeField, Range(0f, 1f)] float oneHandAmplitude = 1.0f;
    [SerializeField, Range(0f, 1f)] float twoHandAmplitude = 0.4f;
    [SerializeField] float hapticInterval = 0.1f;

    [Header("Visual Shake")]
    [SerializeField] Transform shakeTarget;
    [SerializeField] float shakePositionAmount = 0.003f;
    [SerializeField] float shakeRotationAmount = 1.5f;

    [Header("Attachment")]
    [SerializeField] Transform tipTransform;
    [SerializeField] float tipDetectRadius = 0.04f;
    [SerializeField] float wheelSnapRadius = 0.15f;
    [SerializeField] float holdToActivate = 1.0f;

    XRGrabInteractable grabInteractable;
    float nextHapticTime;
    bool isOn;

    Vector3 shakeOriginalLocalPos;
    Quaternion shakeOriginalLocalRot;
    bool hasShakeOriginal;

    Tuerca attachedNut;
    float holdElapsed;
    bool actionFiredThisPress;

    static readonly Collider[] overlapBuffer = new Collider[16];

    void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        if (shakeTarget != null)
        {
            shakeOriginalLocalPos = shakeTarget.localPosition;
            shakeOriginalLocalRot = shakeTarget.localRotation;
            hasShakeOriginal = true;
        }
    }

    void OnEnable()
    {
        activateAction.action?.Enable();
    }

    void OnDisable()
    {
        activateAction.action?.Disable();
        isOn = false;
        ResetShake();
        if (attachedNut != null)
        {
            attachedNut.Release();
            attachedNut = null;
        }
    }

    void Update()
    {
        int handCount = grabInteractable.interactorsSelecting.Count;
        bool grabbed = handCount > 0;
        bool pressed = activateAction.action != null && activateAction.action.IsPressed();

        bool wasOn = isOn;
        isOn = grabbed && pressed;

        if (!pressed)
        {
            holdElapsed = 0f;
            actionFiredThisPress = false;
        }

        if (!isOn)
        {
            if (wasOn) ResetShake();
            return;
        }

        if (!actionFiredThisPress)
        {
            holdElapsed += Time.deltaTime;
            if (holdElapsed >= holdToActivate)
            {
                TryFireAction();
                actionFiredThisPress = true;
            }
        }

        float amplitude = handCount == 1 ? oneHandAmplitude : twoHandAmplitude;

        ApplyShake(amplitude);

        if (Time.time < nextHapticTime) return;
        nextHapticTime = Time.time + hapticInterval;

        for (int i = 0; i < grabInteractable.interactorsSelecting.Count; i++)
        {
            if (grabInteractable.interactorsSelecting[i] is XRBaseInputInteractor ctrl)
                ctrl.SendHapticImpulse(amplitude, hapticInterval);
        }
    }

    void TryFireAction()
    {
        if (tipTransform == null) return;

        if (attachedNut == null)
        {
            Tuerca found = FindNutNearTip();
            if (found != null)
            {
                attachedNut = found;
                attachedNut.AttachToGun(tipTransform);
            }
            return;
        }

        WheelAttachPoint point = WheelAttachPoint.FindClosest(attachedNut.transform.position, wheelSnapRadius);
        if (point != null)
        {
            attachedNut.SnapToWheel(point);
            attachedNut = null;
        }
    }

    Tuerca FindNutNearTip()
    {
        int count = Physics.OverlapSphereNonAlloc(tipTransform.position, tipDetectRadius, overlapBuffer, ~0, QueryTriggerInteraction.Collide);
        Tuerca best = null;
        float bestSqr = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var col = overlapBuffer[i];
            if (col == null) continue;
            var nut = col.GetComponentInParent<Tuerca>();
            if (nut == null || nut.IsAttachedToGun) continue;
            float sqr = (nut.transform.position - tipTransform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = nut;
            }
        }
        return best;
    }

    void ApplyShake(float amplitude)
    {
        if (!hasShakeOriginal) return;

        Vector3 posOffset = Random.insideUnitSphere * (shakePositionAmount * amplitude);
        Quaternion rotOffset = Quaternion.Euler(
            Random.Range(-1f, 1f) * shakeRotationAmount * amplitude,
            Random.Range(-1f, 1f) * shakeRotationAmount * amplitude,
            Random.Range(-1f, 1f) * shakeRotationAmount * amplitude);

        shakeTarget.localPosition = shakeOriginalLocalPos + posOffset;
        shakeTarget.localRotation = shakeOriginalLocalRot * rotOffset;
    }

    void ResetShake()
    {
        if (!hasShakeOriginal) return;
        shakeTarget.localPosition = shakeOriginalLocalPos;
        shakeTarget.localRotation = shakeOriginalLocalRot;
    }
}
