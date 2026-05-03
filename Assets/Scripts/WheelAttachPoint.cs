using System.Collections.Generic;
using UnityEngine;

public class WheelAttachPoint : MonoBehaviour
{
    static readonly List<WheelAttachPoint> all = new();
    public static IReadOnlyList<WheelAttachPoint> All => all;

    void OnEnable() { all.Add(this); }
    void OnDisable() { all.Remove(this); }

    public static WheelAttachPoint FindClosest(Vector3 position, float maxRadius)
    {
        WheelAttachPoint closest = null;
        float bestSqr = maxRadius * maxRadius;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            float sqr = (p.transform.position - position).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                closest = p;
            }
        }
        return closest;
    }
}
