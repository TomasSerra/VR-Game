using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class WheelNutGunAnimation : MonoBehaviour
{
    public enum PlaybackDirection
    {
        Remove,
        Install
    }

    [Header("References")]
    [SerializeField] GameObject gunPrefab;
    [SerializeField] Transform gunSpawnPoint;
    [SerializeField] Transform runtimeGunParent;
    [SerializeField] WheelAttachPoint wheelAttachPoint;
    [SerializeField] Tuerca nut;
    [SerializeField] string gunTipName = "Tip Transform";

    [Header("Spawn Fallback")]
    [SerializeField] Vector3 fallbackSpawnLocalPosition = new(0f, -0.45f, 0.1f);
    [SerializeField] Vector3 fallbackSpawnLocalEulerAngles = new(0f, 0f, 0f);
    [SerializeField] bool alignGunTipHeightToTarget = true;
    [SerializeField] Vector3 verticalWorldDirection = Vector3.up;

    [Header("Movement")]
    [SerializeField, Min(0f)] float stopDistanceFromTarget = 0.015f;
    [SerializeField, Min(0.01f)] float approachDuration = 0.6f;
    [SerializeField, Min(0.01f)] float retreatDuration = 0.6f;

    [Header("Wait Time")]
    [SerializeField, Min(0)] int waitSecondsMin = 1;
    [SerializeField, Min(0)] int waitSecondsMax = 2;

    [Header("Fade")]
    [SerializeField, Min(0.01f)] float fadeInDuration = 0.35f;
    [SerializeField, Min(0.01f)] float fadeOutDuration = 0.35f;
    [SerializeField] bool fadeInWhileApproaching = true;
    [SerializeField] bool fadeOutWhileRetreating = true;
    [SerializeField] bool disableGunWhenHidden = true;

    GameObject runtimeGun;
    Transform runtimeGunTransform;
    Transform runtimeGunTip;
    Renderer[] runtimeGunRenderers;
    Collider[] runtimeGunColliders;
    Rigidbody[] runtimeGunRigidbodies;
    RuntimeMaterialState[] runtimeGunMaterials;
    RuntimeMaterialState[] nutMaterials;

    Coroutine animationRoutine;
    bool nutFollowsGunFade;

    public bool IsPlaying => animationRoutine != null;

    void Awake()
    {
        if (wheelAttachPoint == null)
            wheelAttachPoint = GetComponentInChildren<WheelAttachPoint>(true);
        if (nut == null)
            nut = GetComponentInChildren<Tuerca>(true);
        if (nut != null)
            nutMaterials = CreateMaterialStates(nut.GetComponentsInChildren<Renderer>(true));
    }

    [ContextMenu("Play Nut Removal")]
    public void PlayRemoval()
    {
        Play(PlaybackDirection.Remove);
    }

    [ContextMenu("Play Nut Installation")]
    public void PlayInstallation()
    {
        Play(PlaybackDirection.Install);
    }

    [ContextMenu("Reset Nut Gun")]
    public void ResetAnimationState()
    {
        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }

        if (nut != null && wheelAttachPoint != null)
            nut.SnapToWheel(wheelAttachPoint);
        nutFollowsGunFade = false;

        EnsureRuntimeGun();
        if (runtimeGunTransform == null)
            return;

        MoveGunToSpawn();
        SetAnimatedAlpha(0f);
        SetGunVisible(false);
        SetNutAlpha(1f);

    }

    public void Play(PlaybackDirection direction)
    {
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        animationRoutine = StartCoroutine(PlayRoutine(direction));
    }

    IEnumerator PlayRoutine(PlaybackDirection direction)
    {
        EnsureRuntimeGun();
        if (runtimeGunTransform == null || runtimeGunTip == null || nut == null || wheelAttachPoint == null)
        {
            Debug.LogWarning($"[{nameof(WheelNutGunAnimation)}] Missing references on {name}. Check gun prefab, tip, nut, and wheel attach point.", this);
            animationRoutine = null;
            yield break;
        }

        if (direction == PlaybackDirection.Remove)
            PrepareNutOnWheel();
        else
            PrepareNutOnGun();

        Vector3 contactPoint = direction == PlaybackDirection.Remove
            ? nut.transform.position
            : wheelAttachPoint.transform.position;

        MoveGunToSpawn(contactPoint);
        SetGunVisible(true);
        SetAnimatedAlpha(fadeInWhileApproaching ? 0f : 1f);

        Vector3 targetPosition = GetApproachTargetPosition(contactPoint);

        float approachFadeDuration = fadeInWhileApproaching ? Mathf.Min(fadeInDuration, approachDuration) : 0f;
        float approachStartAlpha = fadeInWhileApproaching ? 0f : 1f;
        yield return MoveGun(runtimeGunTransform.position, targetPosition, approachDuration, approachFadeDuration, approachStartAlpha, 1f);

        yield return new WaitForSeconds(GetRandomWaitDuration());

        if (direction == PlaybackDirection.Remove)
        {
            nut.AttachToGun(runtimeGunTip);
            nutFollowsGunFade = true;
        }
        else
        {
            nut.SnapToWheel(wheelAttachPoint);
            nutFollowsGunFade = false;
        }

        float retreatFadeDuration = fadeOutWhileRetreating ? Mathf.Min(fadeOutDuration, retreatDuration) : 0f;
        Vector3 visibleRetreatTarget = GetSpawnPositionAlignedToTargetHeight(runtimeGunTip.position);
        yield return MoveGun(runtimeGunTransform.position, visibleRetreatTarget, retreatDuration, retreatFadeDuration, 1f, 0f);

        if (fadeOutWhileRetreating)
            SetAnimatedAlpha(0f);
        else
            SetAnimatedAlpha(1f);

        MoveGunToSpawn();
        SetGunVisible(false);
        animationRoutine = null;
    }

    void PrepareNutOnWheel()
    {
        if (nut == null || wheelAttachPoint == null)
            return;

        if (!nut.transform.IsChildOf(wheelAttachPoint.transform))
            nut.SnapToWheel(wheelAttachPoint);
        nutFollowsGunFade = false;
        SetNutAlpha(1f);
    }

    void PrepareNutOnGun()
    {
        if (nut == null || runtimeGunTip == null)
            return;

        if (!nut.transform.IsChildOf(runtimeGunTip))
            nut.AttachToGun(runtimeGunTip);
        nutFollowsGunFade = true;
        SetNutAlpha(1f);
    }

    void EnsureRuntimeGun()
    {
        if (runtimeGun != null && runtimeGunTip != null)
            return;

        if (gunPrefab == null)
        {
            Debug.LogWarning($"[{nameof(WheelNutGunAnimation)}] Gun prefab is not assigned on {name}.", this);
            return;
        }

        runtimeGun = Instantiate(gunPrefab, GetSpawnPosition(), GetSpawnRotation(), runtimeGunParent);
        runtimeGun.name = gunPrefab.name + " (Wheel Animation)";
        runtimeGunTransform = runtimeGun.transform;
        runtimeGunTip = FindChildRecursive(runtimeGunTransform, gunTipName);
        if (runtimeGunTip == null)
            Debug.LogWarning($"[{nameof(WheelNutGunAnimation)}] Could not find gun tip '{gunTipName}' inside spawned gun on {name}.", this);

        runtimeGunRenderers = runtimeGun.GetComponentsInChildren<Renderer>(true);
        runtimeGunColliders = runtimeGun.GetComponentsInChildren<Collider>(true);
        runtimeGunRigidbodies = runtimeGun.GetComponentsInChildren<Rigidbody>(true);
        runtimeGunMaterials = CreateMaterialStates(runtimeGunRenderers);

        PrepareGunPhysics();
        SetAnimatedAlpha(0f);
        SetGunVisible(false);
    }

    void PrepareGunPhysics()
    {
        if (runtimeGunRigidbodies != null)
        {
            for (int i = 0; i < runtimeGunRigidbodies.Length; i++)
            {
                Rigidbody body = runtimeGunRigidbodies[i];
                if (body == null)
                    continue;

                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                body.useGravity = false;
            }
        }

        if (runtimeGunColliders != null)
        {
            for (int i = 0; i < runtimeGunColliders.Length; i++)
            {
                if (runtimeGunColliders[i] != null)
                    runtimeGunColliders[i].enabled = false;
            }
        }
    }

    IEnumerator MoveGun(Vector3 from, Vector3 to, float duration, float fadeBlendDuration, float alphaFrom, float alphaTo)
    {
        if (runtimeGunTransform == null)
            yield break;

        if (duration <= 0f)
        {
            runtimeGunTransform.position = to;
            SetAnimatedAlpha(alphaTo);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float moveT = Mathf.Clamp01(elapsed / duration);
            runtimeGunTransform.position = Vector3.LerpUnclamped(from, to, moveT);

            if (fadeBlendDuration > 0f)
            {
                float fadeT = Mathf.Clamp01(elapsed / fadeBlendDuration);
                SetAnimatedAlpha(Mathf.Lerp(alphaFrom, alphaTo, fadeT));
            }
            else
            {
                SetAnimatedAlpha(alphaTo);
            }

            yield return null;
        }

        runtimeGunTransform.position = to;
        SetAnimatedAlpha(alphaTo);
    }

    Vector3 GetApproachTargetPosition(Vector3 targetPoint)
    {
        if (runtimeGunTransform == null || runtimeGunTip == null)
            return targetPoint;

        Vector3 fromTip = targetPoint - runtimeGunTip.position;
        Vector3 direction = fromTip.sqrMagnitude > 0.0001f ? fromTip.normalized : GetSpawnRotation() * Vector3.forward;
        Vector3 tipTarget = targetPoint - direction * stopDistanceFromTarget;

        Vector3 tipLocalOffset = runtimeGunTransform.InverseTransformPoint(runtimeGunTip.position);
        Vector3 rootToTip = runtimeGunTransform.TransformVector(tipLocalOffset);
        return tipTarget - rootToTip;
    }

    Vector3 GetSpawnPosition()
    {
        return gunSpawnPoint != null
            ? gunSpawnPoint.position
            : transform.TransformPoint(fallbackSpawnLocalPosition);
    }

    Quaternion GetSpawnRotation()
    {
        return gunSpawnPoint != null
            ? gunSpawnPoint.rotation
            : transform.rotation * Quaternion.Euler(fallbackSpawnLocalEulerAngles);
    }

    void MoveGunToSpawn()
    {
        MoveGunToSpawn(Vector3.zero, alignToTarget: false);
    }

    void MoveGunToSpawn(Vector3 targetPoint, bool alignToTarget = true)
    {
        if (runtimeGunTransform == null)
            return;

        runtimeGunTransform.SetPositionAndRotation(GetSpawnPosition(), GetSpawnRotation());
        if (alignToTarget)
            AlignGunTipHeightToTarget(targetPoint);
        PrepareGunPhysics();
    }

    Vector3 GetSpawnPositionAlignedToTargetHeight(Vector3 targetPoint)
    {
        if (runtimeGunTransform == null)
            return GetSpawnPosition();

        Vector3 savedPosition = runtimeGunTransform.position;
        Quaternion savedRotation = runtimeGunTransform.rotation;

        runtimeGunTransform.SetPositionAndRotation(GetSpawnPosition(), GetSpawnRotation());
        AlignGunTipHeightToTarget(targetPoint);
        Vector3 alignedPosition = runtimeGunTransform.position;

        runtimeGunTransform.SetPositionAndRotation(savedPosition, savedRotation);
        return alignedPosition;
    }

    void AlignGunTipHeightToTarget(Vector3 targetPoint)
    {
        if (!alignGunTipHeightToTarget || runtimeGunTip == null)
            return;

        Vector3 vertical = verticalWorldDirection.sqrMagnitude > 0.0001f
            ? verticalWorldDirection.normalized
            : Vector3.up;

        Vector3 tipPosition = runtimeGunTip.position;
        Vector3 offset = Vector3.Project(targetPoint - tipPosition, vertical);
        runtimeGunTransform.position += offset;
    }

    int GetRandomWaitDuration()
    {
        int minValue = Mathf.Min(waitSecondsMin, waitSecondsMax);
        int maxValue = Mathf.Max(waitSecondsMin, waitSecondsMax);
        return Random.Range(minValue, maxValue + 1);
    }

    void SetGunVisible(bool visible)
    {
        if (runtimeGun == null)
            return;

        if (disableGunWhenHidden)
        {
            runtimeGun.SetActive(visible);
            if (visible)
                PrepareGunPhysics();
            return;
        }

        if (runtimeGunRenderers == null)
            return;

        for (int i = 0; i < runtimeGunRenderers.Length; i++)
        {
            if (runtimeGunRenderers[i] != null)
                runtimeGunRenderers[i].enabled = visible;
        }
    }

    void SetAnimatedAlpha(float normalizedAlpha)
    {
        float alpha = Mathf.Clamp01(normalizedAlpha);
        SetMaterialStatesAlpha(runtimeGunMaterials, alpha);

        if (nutFollowsGunFade)
            SetMaterialStatesAlpha(nutMaterials, alpha);
    }

    void SetNutAlpha(float normalizedAlpha)
    {
        SetMaterialStatesAlpha(nutMaterials, Mathf.Clamp01(normalizedAlpha));
    }

    static void SetMaterialStatesAlpha(RuntimeMaterialState[] materialStates, float alpha)
    {
        if (materialStates == null)
            return;

        for (int i = 0; i < materialStates.Length; i++)
            materialStates[i].SetAlpha(alpha);
    }

    static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindChildRecursive(root.GetChild(i), childName);
            if (result != null)
                return result;
        }

        return null;
    }

    static RuntimeMaterialState[] CreateMaterialStates(Renderer[] renderers)
    {
        var states = new List<RuntimeMaterialState>();
        var seenMaterials = new HashSet<Material>();

        if (renderers == null)
            return states.ToArray();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Material[] materials = renderer.materials;
            for (int j = 0; j < materials.Length; j++)
            {
                Material material = materials[j];
                if (material == null || !seenMaterials.Add(material))
                    continue;

                states.Add(new RuntimeMaterialState(material));
            }
        }

        return states.ToArray();
    }

    enum MaterialMode
    {
        Unsupported,
        UniversalRenderPipeline,
        Standard
    }

    sealed class RuntimeMaterialState
    {
        public Material Material { get; }
        readonly string colorProperty;
        readonly Color originalColor;
        readonly MaterialMode mode;

        public RuntimeMaterialState(Material material)
        {
            Material = material;
            colorProperty = ResolveColorProperty(material);
            originalColor = colorProperty != null ? material.GetColor(colorProperty) : Color.white;

            if (material.HasFloat("_Surface"))
                mode = MaterialMode.UniversalRenderPipeline;
            else if (material.HasFloat("_Mode"))
                mode = MaterialMode.Standard;
            else
                mode = MaterialMode.Unsupported;

            PrepareTransparentMaterial();
        }

        public void SetAlpha(float normalizedAlpha)
        {
            if (colorProperty == null)
                return;

            Color color = originalColor;
            color.a = originalColor.a * Mathf.Clamp01(normalizedAlpha);
            Material.SetColor(colorProperty, color);
        }

        void PrepareTransparentMaterial()
        {
            if (mode == MaterialMode.UniversalRenderPipeline)
            {
                Material.SetOverrideTag("RenderType", "Transparent");
                Material.SetFloat("_Surface", 1f);
                if (Material.HasFloat("_Blend"))
                    Material.SetFloat("_Blend", 0f);
                if (Material.HasFloat("_AlphaClip"))
                    Material.SetFloat("_AlphaClip", 0f);
                if (Material.HasFloat("_SrcBlend"))
                    Material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (Material.HasFloat("_DstBlend"))
                    Material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (Material.HasFloat("_ZWrite"))
                    Material.SetFloat("_ZWrite", 0f);
                Material.DisableKeyword("_ALPHATEST_ON");
                Material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                Material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                Material.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (mode == MaterialMode.Standard)
            {
                Material.SetOverrideTag("RenderType", "Transparent");
                Material.SetFloat("_Mode", 3f);
                Material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                Material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                Material.SetInt("_ZWrite", 0);
                Material.DisableKeyword("_ALPHATEST_ON");
                Material.EnableKeyword("_ALPHABLEND_ON");
                Material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                Material.renderQueue = (int)RenderQueue.Transparent;
            }
        }

        static string ResolveColorProperty(Material material)
        {
            if (material.HasProperty("_BaseColor"))
                return "_BaseColor";
            if (material.HasProperty("_Color"))
                return "_Color";
            return null;
        }
    }
}
