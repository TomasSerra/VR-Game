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

    XRGrabInteractable grabInteractable;
    float nextHapticTime;
    bool isOn;

    Vector3 shakeOriginalLocalPos;
    Quaternion shakeOriginalLocalRot;
    bool hasShakeOriginal;

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
    }

    void Update()
    {
        int handCount = grabInteractable.interactorsSelecting.Count;
        bool grabbed = handCount > 0;
        bool pressed = activateAction.action != null && activateAction.action.IsPressed();

        bool wasOn = isOn;
        isOn = grabbed && pressed;

        if (!isOn)
        {
            if (wasOn) ResetShake();
            return;
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
