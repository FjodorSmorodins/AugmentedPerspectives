using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Oculus.Interaction.Surfaces;

// Shows the current calibration action and any available changes.
public class CalibrationInstructions : MonoBehaviour
{
    private RectTransform canvas, root;
    private TextMeshProUGUI heading, body, feedback, primaryLabel;
    private Button primary, changePosition, changeRotation;

    public void Initialize(RectTransform parentCanvas, UnityAction onPrimary,
        UnityAction onChangePosition, UnityAction onChangeRotation)
    {
        canvas = parentCanvas;
        canvas.sizeDelta = new Vector2(600f, 850f);
        root = Rect("Step-by-step Setup Guide", canvas, Vector2.zero, new Vector2(580f, 740f));
        heading = Text("Current Step", root, new Vector2(0f, 330f), new Vector2(540f, 70f), 29f);
        body = Text("What To Do", root, new Vector2(0f, 165f), new Vector2(520f, 230f), 32f);
        body.enableAutoSizing = true;
        body.fontSizeMin = 25f;
        body.fontSizeMax = 32f;
        feedback = Text("Placement Help", root, new Vector2(0f, -100f), new Vector2(520f, 90f), 23f);
        feedback.enableAutoSizing = true;
        feedback.fontSizeMin = 18f;
        feedback.fontSizeMax = 23f;
        primary = MakeButton("Next Step", -175f, onPrimary);
        primaryLabel = primary.GetComponentInChildren<TextMeshProUGUI>();
        changePosition = MakeButton("Change position", -245f, onChangePosition);
        changeRotation = MakeButton("Change rotation", -315f, onChangeRotation);
        root.gameObject.SetActive(false);
    }

    public void Show(CalibrationGuide guide, bool visible, bool canInteract, string feedbackText)
    {
        root.gameObject.SetActive(visible);
        if (!visible) return;
        heading.text = guide.Heading;
        body.text = guide.Body;
        feedback.text = feedbackText ?? "";
        primary.gameObject.SetActive(guide.PrimaryLabel.Length != 0);
        primaryLabel.text = guide.PrimaryLabel;
        primary.interactable = canInteract;
        changePosition.gameObject.SetActive(guide.CanChangePosition);
        changePosition.interactable = canInteract;
        changeRotation.gameObject.SetActive(guide.CanChangeRotation);
        changeRotation.interactable = canInteract;
        RefreshHitSurface();
    }

    private void LateUpdate()
    {
        if (root != null && root.gameObject.activeInHierarchy) RefreshHitSurface();
    }

    private void RefreshHitSurface()
    {
        BoundsClipper clipper = canvas.GetComponent<BoundsClipper>();
        if (clipper == null) return;
        Rect rect = canvas.rect;
        clipper.Position = new Vector3(rect.center.x, rect.center.y, 0f);
        clipper.Size = new Vector3(rect.width, rect.height, 0.01f);
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    private RectTransform Rect(string label, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(label, typeof(RectTransform));
        go.layer = canvas.gameObject.layer;
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private TextMeshProUGUI Text(string label, Transform parent, Vector2 position, Vector2 size, float sizeInPoints)
    {
        var rect = Rect(label, parent, position, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = sizeInPoints;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private Button MakeButton(string label, float y, UnityAction action)
    {
        var rect = Rect(label, root, new Vector2(0f, y), new Vector2(440f, 52f));
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.06f, 0.25f, 0.32f, 0.95f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(action);
        var text = Text(label + " Label", rect, Vector2.zero, rect.sizeDelta, 23f);
        text.text = label;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.sizeDelta = Vector2.zero;
        return button;
    }
}
