using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class F1CarPitMovement : MonoBehaviour
{
    [Header("Pit Transforms")]
    [SerializeField] Transform pitInitial;
    [SerializeField] Transform pitStop;
    [SerializeField] Transform pitFinal;

    [Header("Arrival (PitInitial -> PitStop)")]
    [SerializeField, Min(0.01f)] float arrivalDuration = 5f;
    [SerializeField] AnimationCurve arrivalCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] bool matchInitialRotation = true;
    [SerializeField] bool matchStopRotation = true;

    public float ArrivalDuration => arrivalDuration;

    [Header("Departure (PitStop -> PitFinal)")]
    [SerializeField, Min(0.01f)] float departureDuration = 5f;
    [SerializeField] AnimationCurve departureCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Fade on departure")]
    [SerializeField] Renderer[] fadeRenderers;
    [SerializeField, Min(0.01f)] float fadeDuration = 3f;
    [SerializeField, Range(0f, 1f)] float fadeStartProgress = 0.3f;

    [Header("Pit Crew NPC Animations")]
    [Tooltip("NPC animators that react to the car sequence. Leave empty to auto-find root scene objects named NPC, NPC (1), etc.")]
    [SerializeField] Animator[] pitCrewAnimators;
    [SerializeField] bool autoFindPitCrewAnimatorsByName = true;
    [SerializeField] string pitCrewNpcNamePrefix = "NPC";
    [SerializeField] string carStartsParameter = "CarStarts";
    [SerializeField] string carLeavesParameter = "CarLeaves";

    [Header("Wheel Automation")]
    [Tooltip("The one wheel the player handles manually. Every other pit wheel will animate automatically after the car stops.")]
    [SerializeField] WheelGameModeController playerInteractableWheel;
    [Tooltip("Optional explicit wheel list. Leave empty to auto-find WheelGameModeController components under this car.")]
    [SerializeField] WheelGameModeController[] pitWheels;
    [Tooltip("Starts the non-player wheel nut/wheel/nut animation sequence when the car reaches Pit Stop.")]
    [SerializeField] bool animateNonInteractableWheelsOnStop = true;
    [Tooltip("If enabled, all non-player wheels animate at the same time. If disabled, they animate one after another.")]
    [SerializeField] bool runNonInteractableWheelAnimationsInParallel = true;

    RuntimeMaterialState[] materialStates;
    Coroutine nonInteractableWheelRoutine;

    void Awake()
    {
        EnsurePitCrewAnimatorsIndexed();
        SetPitCrewAnimationState(false, false);
        EnsurePitWheelsIndexed();
        ApplyPlayerWheelSelection();
        materialStates = CreateMaterialStates();
        RestoreMaterials();
    }

    public void TeleportToInitial()
    {
        if (pitInitial == null) return;
        transform.position = pitInitial.position;
        if (matchInitialRotation)
            transform.rotation = pitInitial.rotation;
        RestoreMaterials();
        SetFade(1f);
        ShowRenderers(true);
        SetPitCrewAnimationState(false, false);
        nonInteractableWheelRoutine = null;
    }

    public IEnumerator MoveToStop()
    {
        if (pitInitial == null || pitStop == null)
            yield break;

        SetPitCrewAnimationState(true, false);

        Vector3 fromPos = pitInitial.position;
        Vector3 toPos = pitStop.position;
        Quaternion fromRot = matchInitialRotation ? pitInitial.rotation : transform.rotation;
        Quaternion toRot = matchStopRotation ? pitStop.rotation : transform.rotation;

        float elapsed = 0f;
        while (elapsed < arrivalDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / arrivalDuration);
            float curved = arrivalCurve.Evaluate(t);
            transform.position = Vector3.LerpUnclamped(fromPos, toPos, curved);
            transform.rotation = Quaternion.SlerpUnclamped(fromRot, toRot, curved);
            yield return null;
        }

        transform.position = toPos;
        transform.rotation = toRot;
        StartNonInteractableWheelAnimations();
    }

    public IEnumerator LeaveAndFade()
    {
        if (pitStop == null || pitFinal == null)
            yield break;

        SetPitCrewAnimationState(true, true);
        PrepareMaterialsForFade();

        Vector3 fromPos = transform.position;
        Vector3 toPos = pitFinal.position;

        float elapsed = 0f;
        while (elapsed < departureDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / departureDuration);
            float curved = departureCurve.Evaluate(t);
            transform.position = Vector3.LerpUnclamped(fromPos, toPos, curved);

            float fadeWindow = Mathf.Clamp01(fadeDuration / departureDuration);
            float fadeEndT = Mathf.Min(1f, fadeStartProgress + fadeWindow);
            float fadeT = t <= fadeStartProgress
                ? 0f
                : Mathf.InverseLerp(fadeStartProgress, fadeEndT, t);
            SetFade(1f - fadeT);

            yield return null;
        }

        transform.position = toPos;
        SetFade(0f);
        ShowRenderers(false);
    }

    void ShowRenderers(bool visible)
    {
        if (fadeRenderers == null) return;
        for (int i = 0; i < fadeRenderers.Length; i++)
        {
            if (fadeRenderers[i] != null)
                fadeRenderers[i].enabled = visible;
        }
    }

    void SetPitCrewAnimationState(bool carStarts, bool carLeaves)
    {
        EnsurePitCrewAnimatorsIndexed();

        if (pitCrewAnimators == null) return;
        for (int i = 0; i < pitCrewAnimators.Length; i++)
        {
            Animator animator = pitCrewAnimators[i];
            if (animator == null)
                continue;

            if (!string.IsNullOrWhiteSpace(carStartsParameter))
                animator.SetBool(carStartsParameter, carStarts);
            if (!string.IsNullOrWhiteSpace(carLeavesParameter))
                animator.SetBool(carLeavesParameter, carLeaves);
        }
    }

    void EnsurePitCrewAnimatorsIndexed()
    {
        if (pitCrewAnimators != null && pitCrewAnimators.Length > 0)
            return;

        if (!autoFindPitCrewAnimatorsByName || string.IsNullOrWhiteSpace(pitCrewNpcNamePrefix))
            return;

        var animators = new List<Animator>();
        GameObject[] sceneObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < sceneObjects.Length; i++)
        {
            GameObject sceneObject = sceneObjects[i];
            if (sceneObject == null || !sceneObject.scene.IsValid())
                continue;
            if (sceneObject.transform.parent != null)
                continue;
            if (!sceneObject.name.StartsWith(pitCrewNpcNamePrefix, System.StringComparison.Ordinal))
                continue;

            Animator animator = sceneObject.GetComponentInChildren<Animator>(true);
            if (animator != null)
                animators.Add(animator);
        }

        pitCrewAnimators = animators.ToArray();
    }

    void StartNonInteractableWheelAnimations()
    {
        if (!animateNonInteractableWheelsOnStop)
            return;

        EnsurePitWheelsIndexed();
        ApplyPlayerWheelSelection();

        if (playerInteractableWheel == null)
        {
            Debug.LogWarning($"[{nameof(F1CarPitMovement)}] Player interactable wheel is not assigned on {name}. Non-interactable wheel animations were skipped.", this);
            return;
        }

        if (nonInteractableWheelRoutine != null)
            StopCoroutine(nonInteractableWheelRoutine);

        nonInteractableWheelRoutine = StartCoroutine(PlayNonInteractableWheelAnimations());
    }

    IEnumerator PlayNonInteractableWheelAnimations()
    {
        var wheelsToAnimate = new List<WheelGameModeController>();
        if (pitWheels != null)
        {
            for (int i = 0; i < pitWheels.Length; i++)
            {
                WheelGameModeController wheel = pitWheels[i];
                if (wheel != null && wheel != playerInteractableWheel)
                    wheelsToAnimate.Add(wheel);
            }
        }

        if (runNonInteractableWheelAnimationsInParallel)
        {
            var running = new List<Coroutine>();
            for (int i = 0; i < wheelsToAnimate.Count; i++)
                running.Add(StartCoroutine(wheelsToAnimate[i].PlayAutomatedPitStopSequence()));

            for (int i = 0; i < running.Count; i++)
                yield return running[i];
        }
        else
        {
            for (int i = 0; i < wheelsToAnimate.Count; i++)
                yield return wheelsToAnimate[i].PlayAutomatedPitStopSequence();
        }

        nonInteractableWheelRoutine = null;
    }

    void EnsurePitWheelsIndexed()
    {
        if (pitWheels != null && pitWheels.Length > 0)
            return;

        pitWheels = GetComponentsInChildren<WheelGameModeController>(true);
    }

    void ApplyPlayerWheelSelection()
    {
        if (pitWheels == null || playerInteractableWheel == null)
            return;

        for (int i = 0; i < pitWheels.Length; i++)
        {
            if (pitWheels[i] != null)
                pitWheels[i].SetPlayerInteractableWheel(pitWheels[i] == playerInteractableWheel);
        }
    }

    RuntimeMaterialState[] CreateMaterialStates()
    {
        var states = new List<RuntimeMaterialState>();
        var seenMaterials = new HashSet<Material>();

        if (fadeRenderers == null)
            return states.ToArray();

        for (int i = 0; i < fadeRenderers.Length; i++)
        {
            if (fadeRenderers[i] == null)
                continue;

            Material[] rendererMaterials = fadeRenderers[i].materials;
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
        if (materialStates == null) return;
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
        if (materialStates == null) return;
        float alpha = Mathf.Clamp01(normalizedAlpha);
        for (int i = 0; i < materialStates.Length; i++)
            materialStates[i].SetAlpha(alpha);
    }

    static void SetUrpTransparent(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Surface", 1f);
        if (material.HasFloat("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasFloat("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
        if (material.HasFloat("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasFloat("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasFloat("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    static void SetUrpOpaque(Material material)
    {
        material.SetOverrideTag("RenderType", "Opaque");
        material.SetFloat("_Surface", 0f);
        if (material.HasFloat("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasFloat("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
        if (material.HasFloat("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.One);
        if (material.HasFloat("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.Zero);
        if (material.HasFloat("_ZWrite")) material.SetFloat("_ZWrite", 1f);
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
            if (!HasColorProperty) return;
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
            if (material.HasProperty("_BaseColor")) return "_BaseColor";
            if (material.HasProperty("_Color")) return "_Color";
            return null;
        }
    }
}
