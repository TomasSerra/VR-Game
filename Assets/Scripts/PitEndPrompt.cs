using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Cartel de fin de pit stop (escena Game): cuando el auto se va, ofrece repetir el modo
// (recarga la escena con el mismo SelectedMode) o volver al main menu. La UI se construye
// en runtime como canvas world-space frente al jugador, clickeable con los rays de XR.
public class PitEndPrompt : MonoBehaviour
{
    [Header("Escenas")]
    [Tooltip("Build index de la escena de main menu.")]
    [SerializeField] int mainMenuSceneIndex = 0;

    [Header("Ubicación")]
    [Tooltip("Punto donde aparece el cartel. Si queda vacío, aparece frente a la cámara del jugador.")]
    [SerializeField] Transform placementAnchor;
    [SerializeField, Min(0.5f)] float distanceFromPlayer = 1.8f;
    [SerializeField] float heightOffset = -0.15f;

    [Header("Textos")]
    [SerializeField] string titleText = "¡Pit stop completado!";
    [SerializeField] string timeFormat = "Tiempo: {0:00}:{1:00}.{2:00}";
    [SerializeField] string repeatText = "Repetir";
    [SerializeField] string menuText = "Menú principal";

    GameObject canvasRoot;
    TMP_Text timeLabel;

    public void Show(float elapsedSeconds)
    {
        if (canvasRoot == null)
            BuildUi();

        if (timeLabel != null)
        {
            if (elapsedSeconds >= 0f)
            {
                int minutes = Mathf.FloorToInt(elapsedSeconds / 60f);
                int seconds = Mathf.FloorToInt(elapsedSeconds % 60f);
                int centiseconds = Mathf.FloorToInt((elapsedSeconds * 100f) % 100f);
                timeLabel.text = string.Format(timeFormat, minutes, seconds, centiseconds);
            }
            else
            {
                timeLabel.text = string.Empty;
            }
        }

        PlaceInFrontOfPlayer();
        canvasRoot.SetActive(true);
    }

    public void Hide()
    {
        if (canvasRoot != null)
            canvasRoot.SetActive(false);
    }

    void PlaceInFrontOfPlayer()
    {
        if (placementAnchor != null)
        {
            canvasRoot.transform.SetPositionAndRotation(placementAnchor.position, placementAnchor.rotation);
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

        Vector3 position = cam.transform.position + forward * distanceFromPlayer + Vector3.up * heightOffset;
        canvasRoot.transform.position = position;
        // El canvas se lee desde el lado desde el que su forward apunta en dirección contraria
        // al jugador: mirar "hacia afuera" deja el texto de frente y sin espejar.
        canvasRoot.transform.rotation = Quaternion.LookRotation(position - cam.transform.position);
    }

    void BuildUi()
    {
        canvasRoot = new GameObject("Pit End Prompt Canvas",
            typeof(Canvas), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
        canvasRoot.transform.SetParent(transform, false);

        var canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var canvasRect = canvasRoot.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(700f, 420f);
        canvasRect.localScale = Vector3.one * 0.0015f;

        Image panel = CreateImage(canvasRect, "Panel", new Color(0.07f, 0.09f, 0.13f, 0.95f));
        Stretch(panel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        TMP_Text title = CreateLabel(canvasRect, "Title", titleText, 58f, FontStyles.Bold);
        Stretch(title.rectTransform, new Vector2(0f, 0.72f), new Vector2(1f, 1f), new Vector2(20f, 0f), new Vector2(-20f, -20f));

        timeLabel = CreateLabel(canvasRect, "Time", string.Empty, 44f, FontStyles.Normal);
        Stretch(timeLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.72f), new Vector2(20f, 0f), new Vector2(-20f, 0f));

        Button repeat = CreateButton(canvasRect, "Repeat Button", repeatText, new Color(0.13f, 0.55f, 0.24f, 1f));
        Stretch(((RectTransform)repeat.transform), new Vector2(0.07f, 0.12f), new Vector2(0.48f, 0.4f), Vector2.zero, Vector2.zero);
        repeat.onClick.AddListener(RepeatMode);

        Button menu = CreateButton(canvasRect, "Menu Button", menuText, new Color(0.32f, 0.34f, 0.42f, 1f));
        Stretch(((RectTransform)menu.transform), new Vector2(0.52f, 0.12f), new Vector2(0.93f, 0.4f), Vector2.zero, Vector2.zero);
        menu.onClick.AddListener(GoToMainMenu);

        canvasRoot.SetActive(false);
    }

    void RepeatMode()
    {
        // SelectedMode es estático y sobrevive la recarga: se repite el mismo modo.
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void GoToMainMenu()
    {
        SceneManager.LoadScene(mainMenuSceneIndex);
    }

    static Image CreateImage(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    static TMP_Text CreateLabel(RectTransform parent, string name, string text, float fontSize, FontStyles style)
    {
        var go = new GameObject(name, typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        return label;
    }

    static Button CreateButton(RectTransform parent, string name, string text, Color color)
    {
        var go = new GameObject(name, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.color = color;

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.15f * color.r, 1.15f * color.g, 1.15f * color.b, 1f);
        colors.pressedColor = new Color(0.8f * color.r, 0.8f * color.g, 0.8f * color.b, 1f);
        button.colors = colors;

        TMP_Text label = CreateLabel((RectTransform)go.transform, "Label", text, 40f, FontStyles.Bold);
        Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -10f));

        return button;
    }

    static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
