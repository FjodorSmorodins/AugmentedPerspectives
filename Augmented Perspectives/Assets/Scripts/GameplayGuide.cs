using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Local, non-blocking help on the existing stand while both players assemble.
public class GameplayGuide : MonoBehaviour
{
    private RectTransform canvas, root;
    private TextMeshProUGUI heading, body, progress, nextLabel;
    private Button back, next, reset;
    private AssemblyManager assembly;
    private int page;
    private float acceptAfter;

    public void Initialize(RectTransform parentCanvas, AssemblyManager manager)
    {
        canvas = parentCanvas;
        assembly = manager;
        root = Rect("Gameplay Guide", canvas, Vector2.zero, new Vector2(580f, 740f));
        heading = Text("Gameplay Step", root, new Vector2(0f, 330f), new Vector2(540f, 70f), 29f);
        body = Text("Gameplay Help", root, new Vector2(0f, 125f), new Vector2(520f, 310f), 25f);
        body.alignment = TextAlignmentOptions.TopLeft;
        body.enableAutoSizing = true;
        body.fontSizeMin = 23f;
        body.fontSizeMax = 25f;
        progress = Text("Assembly Progress", root, new Vector2(0f, -95f), new Vector2(520f, 100f), 23f);
        back = MakeButton("Back", new Vector2(-130f, -210f), () => Navigate(-1));
        next = MakeButton("Next", new Vector2(130f, -210f), () => Navigate(1));
        nextLabel = next.GetComponentInChildren<TextMeshProUGUI>();
        reset = MakeButton("Reset", new Vector2(0f, -280f), () => assembly?.RequestReset());
        Text("Reset Help", root, new Vector2(0f, -329f), new Vector2(520f, 42f), 19f).text =
            "Teleports loose pieces to their starting spots.\nSnapped pieces stay in place.";
        root.gameObject.SetActive(false);
    }

    public void ResetSession()
    {
        page = 0;
        acceptAfter = Time.unscaledTime + 0.5f;
    }

    public void Show(bool visible)
    {
        if (root == null) return;
        if (visible && !root.gameObject.activeSelf)
            acceptAfter = Time.unscaledTime + 0.5f;
        root.gameObject.SetActive(visible);
        if (!visible) return;

        bool complete = assembly != null && assembly.IsComplete;
        if (complete)
        {
            heading.text = "Build completed!";
            body.text = "You did it together! Every assembly part has been released, snapped into place and locked.";
        }
        else if (page == 0)
        {
            heading.text = "1 / 3 - Find your loose parts";
            body.text = "Reach for a loose part and grab it with your hand or the controller grip.\n\nYou cannot grab a loose part if you can see its matching part in the reference build. Your partner moves that part.\n\nYou can grab the parts whose reference is hidden from you.";
        }
        else if (page == 1)
        {
            heading.text = "2 / 3 - Talk to your partner";
            body.text = "Your goal is to recreate the reference build together in the assembly area.\n\nDescribe the reference parts you can see: their shape, position and orientation. Guide your partner as they move those loose parts.\n\nFor the parts you can grab, listen to your partner's instructions.";
        }
        else
        {
            heading.text = "3 / 3 - Place and release";
            body.text = "Move a loose part close to its correct position in the assembly area, then let go.\n\nIf it is close enough, it snaps to the exact pose and stays locked. If it does not snap, grab it again and adjust its position.\n\nKeep communicating until every part has snapped. Holding a part in place does not complete the build.";
        }

        int snapped = assembly != null ? assembly.SnappedPartCount : 0;
        int total = assembly != null ? assembly.TotalPartCount : 0;
        progress.text = $"Parts snapped: {snapped}/{total}\n" + (complete
            ? "<color=#55FF77>COMPLETED!</color>"
            : snapped == 0 ? "Try placing and releasing your first part."
            : "Keep going! Guide each other through the remaining parts.");
        back.gameObject.SetActive(!complete && page > 0);
        next.gameObject.SetActive(!complete);
        reset.interactable = assembly != null && snapped < total;
        nextLabel.text = page == 2 ? "Read again" : "Next";
    }

    private void Navigate(int direction)
    {
        if (!root.gameObject.activeInHierarchy || Time.unscaledTime < acceptAfter) return;
        // Meta UI may emit duplicate clicks from the same pinch.
        acceptAfter = Time.unscaledTime + 0.5f;
        page = direction > 0 ? (page + 1) % 3 : Mathf.Max(0, page - 1);
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

    private TextMeshProUGUI Text(string label, Transform parent, Vector2 position, Vector2 size, float fontSize)
    {
        var rect = Rect(label, parent, position, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private Button MakeButton(string label, Vector2 position, UnityAction action)
    {
        var rect = Rect(label, root, position, new Vector2(230f, 52f));
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.06f, 0.25f, 0.32f, 0.95f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(action);
        Text(label + " Label", rect, Vector2.zero, rect.sizeDelta, 23f).text = label;
        return button;
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }
}