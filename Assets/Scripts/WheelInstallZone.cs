using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider))]
public class WheelInstallZone : MonoBehaviour
{
    [SerializeField] string newWheelTag = "NewWheel";
    [SerializeField] bool oneShot = true;
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
        fired = true;
        OnWheelInstalled?.Invoke();
    }
}
