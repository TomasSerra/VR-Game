using UnityEngine;

public class Tuerca : MonoBehaviour
{
    Rigidbody rb;
    Collider[] colliders;
    Vector3 originalLossyScale;

    public bool IsAttachedToGun { get; private set; }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>(true);
        originalLossyScale = transform.lossyScale;
    }

    public void AttachToGun(Transform tip)
    {
        IsAttachedToGun = true;
        ReparentPreservingWorldScale(tip);
        SetPhysics(kinematic: true, collidersEnabled: false);
    }

    public void SnapToWheel(WheelAttachPoint point)
    {
        IsAttachedToGun = false;
        ReparentPreservingWorldScale(point.transform);
        SetPhysics(kinematic: true, collidersEnabled: true);
    }

    void ReparentPreservingWorldScale(Transform newParent)
    {
        transform.SetParent(newParent, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        Vector3 parentLossy = newParent.lossyScale;
        transform.localScale = new Vector3(
            SafeDivide(originalLossyScale.x, parentLossy.x),
            SafeDivide(originalLossyScale.y, parentLossy.y),
            SafeDivide(originalLossyScale.z, parentLossy.z));
    }

    static float SafeDivide(float a, float b)
    {
        return Mathf.Approximately(b, 0f) ? a : a / b;
    }

    public void Release()
    {
        IsAttachedToGun = false;
        transform.SetParent(null, true);
        SetPhysics(kinematic: false, collidersEnabled: true);
    }

    void SetPhysics(bool kinematic, bool collidersEnabled)
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = kinematic;
        }
        if (colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) colliders[i].enabled = collidersEnabled;
        }
    }
}
