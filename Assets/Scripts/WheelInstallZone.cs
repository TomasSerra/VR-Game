using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Zona de instalación (trigger en el cubo del auto). Detecta la rueda nueva (tag NewWheel)
// cuando entra en la zona y, si el jugador aprieta el gatillo con ella cerca, la "instala":
// la esconde y avisa por OnWheelInstalled. El efecto visual (la rueda original reaparece
// instalada + la animación de la tuerca) lo maneja WheelGameModeController al escuchar el evento.
[RequireComponent(typeof(Collider))]
public class WheelInstallZone : MonoBehaviour
{
    public const string NewWheelTag = "NewWheel";

    [SerializeField] string newWheelTag = NewWheelTag;
    [Tooltip("Una vez instalada una rueda, el spot queda ocupado y no acepta otra (hasta un reset).")]
    [SerializeField] bool oneShot = true;

    [Header("Install Effect")]
    [Tooltip("Desactiva la rueda que trae el jugador al colocarla, para que 'desaparezca'.")]
    [SerializeField] bool hideCarriedWheel = true;

    [Header("Tutorial")]
    [Tooltip("Si está activo, el spot se libera al cambiar de modo (main menu) para poder repetir la instalación.")]
    [SerializeField] bool resetOnModeChange;

    public UnityEvent OnWheelInstalled;

    bool installed;
    readonly List<XRGrabInteractable> candidates = new();
    bool subscribedToModeChanged;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    void OnEnable()
    {
        if (resetOnModeChange && !subscribedToModeChanged)
        {
            GameModeManager.OnModeChanged += HandleModeChanged;
            subscribedToModeChanged = true;
        }
    }

    void OnDisable()
    {
        if (subscribedToModeChanged)
        {
            GameModeManager.OnModeChanged -= HandleModeChanged;
            subscribedToModeChanged = false;
        }
        ClearCandidates();
    }

    // Registra las ruedas nuevas que entran en la zona (cerca del spot) y escucha su gatillo.
    void OnTriggerEnter(Collider other)
    {
        if (installed && oneShot) return;

        var grab = other.GetComponentInParent<XRGrabInteractable>();
        if (grab == null) return;
        // El tag va en la raíz de la rueda nueva (donde está el grab); la rueda montada del
        // auto no lo tiene, así que nunca se registra a sí misma como candidata.
        if (string.IsNullOrEmpty(newWheelTag) || !grab.CompareTag(newWheelTag)) return;

        if (!candidates.Contains(grab))
        {
            candidates.Add(grab);
            grab.activated.AddListener(OnCandidateActivated);
        }
    }

    void OnTriggerExit(Collider other)
    {
        var grab = other.GetComponentInParent<XRGrabInteractable>();
        if (grab != null && candidates.Remove(grab))
            grab.activated.RemoveListener(OnCandidateActivated);
    }

    // Se instala cuando el jugador aprieta el gatillo (activate) con la rueda cerca del spot.
    void OnCandidateActivated(ActivateEventArgs args)
    {
        if (installed && oneShot) return;
        // Instalar sólo tiene sentido en el modo de colocar rueda.
        if (GameModeManager.SelectedMode != GameModeManager.GameMode.InstallWheel) return;

        var grab = args.interactableObject as XRGrabInteractable;
        if (grab == null || !candidates.Contains(grab)) return;

        InstallWheel(grab);
    }

    void InstallWheel(XRGrabInteractable grab)
    {
        installed = true;
        ClearCandidates();

        // La rueda que trae el jugador desaparece.
        if (hideCarriedWheel)
            grab.gameObject.SetActive(false);

        OnWheelInstalled?.Invoke();
    }

    // En el tutorial: al cambiar de modo, liberar el spot. La reactivación/reposición de las
    // ruedas la maneja WheelGameModeController al re-ejecutar el modo.
    void HandleModeChanged(GameModeManager.GameMode mode)
    {
        installed = false;
        ClearCandidates();
    }

    void ClearCandidates()
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] != null)
                candidates[i].activated.RemoveListener(OnCandidateActivated);
        }
        candidates.Clear();
    }
}
