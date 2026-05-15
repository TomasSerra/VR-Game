using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Collider))]
public class WheelInstallZone : MonoBehaviour
{
    [SerializeField] string newWheelTag = "NewWheel";
    [SerializeField] bool oneShot = true;
    [SerializeField] bool requireTwoHandGrab = true;
    public UnityEvent OnWheelInstalled;

    bool fired;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (oneShot && fired) return;
        if (!string.IsNullOrEmpty(newWheelTag) && !other.CompareTag(newWheelTag)) return;

        if (requireTwoHandGrab)
        {
            var grab = other.GetComponentInParent<XRGrabInteractable>();
            if (grab == null || grab.interactorsSelecting.Count < 2) return;
        }

        fired = true;
        OnWheelInstalled?.Invoke();
    }
}
