using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class WheelRemovalAnimation : MonoBehaviour
{
    public enum PlaybackDirection
    {
        Remove,
        Install
    }

    public enum LocalAxis
    {
        X,
        Y,
        Z
    }

    [Header("Horizontal Pull")]
    [SerializeField] Transform horizontalOrigin;
    [SerializeField] LocalAxis horizontalAxis = LocalAxis.Y;
    [SerializeField] bool invertHorizontalAxis;
    [SerializeField] Vector3 horizontalFallbackLocalDirection = Vector3.right;
    [SerializeField, Min(0f)] float horizontalDistance = 0.45f;
    [SerializeField, Min(0.01f)] float horizontalDuration = 0.18f;

    [Header("Vertical Lift")]
    [SerializeField] Vector3 verticalWorldDirection = Vector3.up;
    [SerializeField, Min(0f)] float verticalDistance = 0.7f;
    [SerializeField, Min(0.01f)] float verticalDuration = 0.35f;

    [Header("Fade")]
    [SerializeField] bool fadeDuringLift = true;
    [SerializeField, Range(0f, 1f)] float fadeStartVerticalPercentage = 0.5f;
    [SerializeField] bool hideRenderersOnComplete = true;
    [SerializeField] bool disableGameObjectOnComplete;

    [Header("Physics")]
    [SerializeField] bool makeRigidBodyKinematicWhileAnimating = true;
    [SerializeField] bool disableCollidersWhileAnimating = true;

    Renderer[] renderers;
    Collider[] colliders;
    Rigidbody rb;

    bool[] originalRendererStates;
    bool[] originalColliderStates;
    bool originalRbIsKinematic;
    bool originalRbUsesGravity;

    Vector3 initialLocalPosition;
    Quaternion initialLocalRotation;

    RuntimeMaterialState[] materialStates;
    Coroutine animationRoutine;
    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        rb = GetComponent<Rigidbody>();

        originalRendererStates = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            originalRendererStates[i] = renderers[i] != null && renderers[i].enabled;

        originalColliderStates = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
            originalColliderStates[i] = colliders[i] != null && colliders[i].enabled;

        if (rb != null)
        {
            originalRbIsKinematic = rb.isKinematic;
            originalRbUsesGravity = rb.useGravity;
        }

        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
        materialStates = CreateMaterialStates();
        RestoreMaterials();
    }

    [ContextMenu("Play Removal")]
    public void PlayRemoval()
    {
        Play(PlaybackDirection.Remove);
    }

    [ContextMenu("Play Installation")]
    public void PlayInstallation()
    {
        Play(PlaybackDirection.Install);
    }

    public void Play(PlaybackDirection direction)
    {
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        if (disableGameObjectOnComplete && !gameObject.activeSelf)
            gameObject.SetActive(true);

        ResetRemoval();
        animationRoutine = StartCoroutine(PlayRoutine(direction));
    }

    [ContextMenu("Reset Removal")]
    public void ResetRemoval()
    {
        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }

        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation;

        RestoreRenderers();
        RestoreColliders();
        RestoreRigidBody();
        RestoreMaterials();
    }

    IEnumerator PlayRoutine(PlaybackDirection direction)
    {
        PrepareForAnimation();

        Vector3 horizontalDirection = GetHorizontalLocalDirection();
        Vector3 verticalDirection = GetVerticalLocalDirection();
        Vector3 basePosition = transform.localPosition;
        Vector3 horizontalTarget = basePosition + horizontalDirection * horizontalDistance;
        Vector3 topTarget = horizontalTarget + verticalDirection * verticalDistance;

        if (direction == PlaybackDirection.Remove)
        {
            SetFade(1f);
            yield return MoveLocalPosition(basePosition, horizontalTarget, horizontalDuration, fadeMode: FadeMode.None);
            yield return MoveLocalPosition(horizontalTarget, topTarget, verticalDuration, fadeMode: FadeMode.FadeOut);

            SetFade(0f);
            if (hideRenderersOnComplete)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        renderers[i].enabled = false;
                }
            }

            if (disableGameObjectOnComplete)
                gameObject.SetActive(false);
        }
        else
        {
            transform.localPosition = topTarget;
            RestoreRenderers();
            SetFade(fadeDuringLift ? 0f : 1f);

            yield return MoveLocalPosition(topTarget, horizontalTarget, verticalDuration, fadeMode: FadeMode.FadeIn);
            yield return MoveLocalPosition(horizontalTarget, basePosition, horizontalDuration, fadeMode: FadeMode.None);

            SetFade(1f);
        }

        animationRoutine = null;
    }

    IEnumerator MoveLocalPosition(Vector3 from, Vector3 to, float duration, FadeMode fadeMode)
    {
        if (duration <= 0f)
        {
            transform.localPosition = to;
            ApplyFadeAtProgress(fadeMode, 1f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localPosition = Vector3.LerpUnclamped(from, to, t);
            ApplyFadeAtProgress(fadeMode, t);

            yield return null;
        }

        transform.localPosition = to;
        ApplyFadeAtProgress(fadeMode, 1f);
    }

    void PrepareForAnimation()
    {
        RestoreRenderers();
        RestoreMaterials();

        if (makeRigidBodyKinematicWhileAnimating && rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (disableCollidersWhileAnimating)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }
        }

        if (fadeDuringLift)
            PrepareMaterialsForFade();
        else
            SetFade(1f);
    }

    void ApplyFadeAtProgress(FadeMode fadeMode, float verticalProgress)
    {
        if (!fadeDuringLift || fadeMode == FadeMode.None)
            return;

        float alpha;
        float fadeStart = Mathf.Clamp01(fadeStartVerticalPercentage);

        if (fadeMode == FadeMode.FadeOut)
        {
            if (verticalProgress <= fadeStart)
            {
                alpha = 1f;
            }
            else
            {
                float fadeT = Mathf.InverseLerp(fadeStart, 1f, verticalProgress);
                alpha = 1f - fadeT;
            }
        }
        else
        {
            float reverseProgress = 1f - verticalProgress;
            if (reverseProgress <= fadeStart)
            {
                alpha = 1f;
            }
            else
            {
                float fadeT = Mathf.InverseLerp(fadeStart, 1f, reverseProgress);
                alpha = 1f - fadeT;
            }
        }

        SetFade(alpha);
    }

    void RestoreRenderers()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = originalRendererStates[i];
        }
    }

    void RestoreColliders()
    {
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = originalColliderStates[i];
        }
    }

    void RestoreRigidBody()
    {
        if (rb == null)
            return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = originalRbIsKinematic;
        rb.useGravity = originalRbUsesGravity;
    }

    Vector3 GetHorizontalLocalDirection()
    {
        Transform origin = horizontalOrigin != null ? horizontalOrigin : transform.parent;
        Vector3 axisDirection = AxisToVector(horizontalAxis);

        if (origin != null)
        {
            Vector3 localOffset = origin.InverseTransformPoint(transform.position);
            float axisValue = GetAxisValue(localOffset, horizontalAxis);
            float sign = Mathf.Approximately(axisValue, 0f) ? 0f : Mathf.Sign(axisValue);

            if (invertHorizontalAxis)
                sign *= -1f;

            if (!Mathf.Approximately(sign, 0f))
            {
                Vector3 worldDirection = origin.TransformDirection(axisDirection * sign).normalized;
                Transform parent = transform.parent;
                if (parent == null)
                    return worldDirection;

                Vector3 localDirection = parent.InverseTransformDirection(worldDirection);
                if (localDirection.sqrMagnitude > 0.0001f)
                    return localDirection.normalized;
            }
        }

        Vector3 fallbackDirection = horizontalFallbackLocalDirection.sqrMagnitude > 0f
            ? horizontalFallbackLocalDirection.normalized
            : Vector3.right;
        return fallbackDirection;
    }

    static Vector3 AxisToVector(LocalAxis axis)
    {
        return axis switch
        {
            LocalAxis.X => Vector3.right,
            LocalAxis.Y => Vector3.up,
            _ => Vector3.forward
        };
    }

    static float GetAxisValue(Vector3 vector, LocalAxis axis)
    {
        return axis switch
        {
            LocalAxis.X => vector.x,
            LocalAxis.Y => vector.y,
            _ => vector.z
        };
    }

    Vector3 GetVerticalLocalDirection()
    {
        Vector3 worldDirection = verticalWorldDirection.sqrMagnitude > 0f
            ? verticalWorldDirection.normalized
            : Vector3.up;

        Transform parent = transform.parent;
        if (parent == null)
            return worldDirection;

        Vector3 localDirection = parent.InverseTransformDirection(worldDirection);
        return localDirection.sqrMagnitude > 0f ? localDirection.normalized : Vector3.up;
    }

    RuntimeMaterialState[] CreateMaterialStates()
    {
        var states = new List<RuntimeMaterialState>();
        var seenMaterials = new HashSet<Material>();

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            Material[] rendererMaterials = renderers[i].materials;
            for (int j = 0; j < rendererMaterials.Length; j++)
            {
                Material material = rendererMaterials[j];
                if (material == null || !seenMaterials.Add(material))
                    continue;

                states.Add(new RuntimeMaterialState(material));
            }
        }

        return states.ToArray();
    }

    void PrepareMaterialsForFade()
    {
        for (int i = 0; i < materialStates.Length; i++)
        {
            RuntimeMaterialState state = materialStates[i];
            if (!state.HasColorProperty)
                continue;

            if (state.Mode == MaterialMode.UniversalRenderPipeline)
                SetUrpTransparent(state.Material);
            else if (state.Mode == MaterialMode.Standard)
                SetStandardTransparent(state.Material);
        }
    }

    void RestoreMaterials()
    {
        for (int i = 0; i < materialStates.Length; i++)
        {
            RuntimeMaterialState state = materialStates[i];
            state.RestoreAlpha();

            if (state.Mode == MaterialMode.UniversalRenderPipeline)
                SetUrpOpaque(state.Material);
            else if (state.Mode == MaterialMode.Standard)
                SetStandardOpaque(state.Material);
        }
    }

    void SetFade(float normalizedAlpha)
    {
        float alpha = Mathf.Clamp01(normalizedAlpha);
        for (int i = 0; i < materialStates.Length; i++)
            materialStates[i].SetAlpha(alpha);
    }

    static void SetUrpTransparent(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Surface", 1f);
        if (material.HasFloat("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasFloat("_AlphaClip"))
            material.SetFloat("_AlphaClip", 0f);
        if (material.HasFloat("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasFloat("_DstBlend"))
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasFloat("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    static void SetUrpOpaque(Material material)
    {
        material.SetOverrideTag("RenderType", "Opaque");
        material.SetFloat("_Surface", 0f);
        if (material.HasFloat("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasFloat("_AlphaClip"))
            material.SetFloat("_AlphaClip", 0f);
        if (material.HasFloat("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
        if (material.HasFloat("_DstBlend"))
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
        if (material.HasFloat("_ZWrite"))
            material.SetFloat("_ZWrite", 1f);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = -1;
    }

    static void SetStandardTransparent(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Mode", 3f);
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    static void SetStandardOpaque(Material material)
    {
        material.SetOverrideTag("RenderType", string.Empty);
        material.SetFloat("_Mode", 0f);
        material.SetInt("_SrcBlend", (int)BlendMode.One);
        material.SetInt("_DstBlend", (int)BlendMode.Zero);
        material.SetInt("_ZWrite", 1);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = -1;
    }

    enum MaterialMode
    {
        Unsupported,
        UniversalRenderPipeline,
        Standard
    }

    enum FadeMode
    {
        None,
        FadeOut,
        FadeIn
    }

    sealed class RuntimeMaterialState
    {
        public Material Material { get; }
        public MaterialMode Mode { get; }
        public bool HasColorProperty => !string.IsNullOrEmpty(colorProperty);

        readonly string colorProperty;
        readonly Color originalColor;

        public RuntimeMaterialState(Material material)
        {
            Material = material;
            colorProperty = ResolveColorProperty(material);
            originalColor = Color.white;
            if (HasColorProperty)
                originalColor = material.GetColor(colorProperty);

            if (material.HasFloat("_Surface"))
                Mode = MaterialMode.UniversalRenderPipeline;
            else if (material.HasFloat("_Mode"))
                Mode = MaterialMode.Standard;
            else
                Mode = MaterialMode.Unsupported;
        }

        public void SetAlpha(float normalizedAlpha)
        {
            if (!HasColorProperty)
                return;

            Color color = originalColor;
            color.a = originalColor.a * normalizedAlpha;
            Material.SetColor(colorProperty, color);
        }

        public void RestoreAlpha()
        {
            if (HasColorProperty)
                Material.SetColor(colorProperty, originalColor);
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
