using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Oculus.Interaction.Surfaces;

// Local instructions: each player reads independently before placing floor marks.
public class CalibrationInstructions : MonoBehaviour
{
    public bool Completed { get; private set; }
    public bool IsOpen { get; private set; }
    private RectTransform canvas, root, diagram;
    private TextMeshProUGUI heading, body, footer, pageNumber, nextLabel;
    private Button backButton;
    private Action continueAction;
    private int page;
    private float navigationAllowedAt;
    private const float NavigationDebounceSeconds = 0.4f;
    private readonly string[] titles = { "Why calibrate?", "Place the two dots", "What the dots define" };
    private readonly string[] pages =
    {
        "Calibration helps both players see the virtual objects in the same physical locations. Each headset starts with its own position and direction, so both players need to identify the same two real-world points.",
        "Agree on two physical floor marks, at least 50 cm apart.\n\nMove your right hand above the first mark—the dot follows your hand horizontally and stays at floor height. Pinch to place it.\n\nRepeat for the second mark. Both players should use the same marks in the same order.",
        "The first dot marks the centre of the play area. The direction from the first dot to the second sets which way the play area faces."
    };

    public void Initialize(RectTransform parentCanvas, Transform playArea, Transform[] areas,
        Vector3 forward, Action onContinue)
    {
        canvas = parentCanvas;
        canvas.sizeDelta = new Vector2(Mathf.Max(600f, canvas.sizeDelta.x), Mathf.Max(850f, canvas.sizeDelta.y));
        continueAction = onContinue;
        root = Rect("Calibration Introduction - Three Pages", canvas, Vector2.zero, new Vector2(580f, 740f));
        heading = Text("Heading", root, new Vector2(0f, 340f), new Vector2(540f, 55f), 30f);
        body = Text("Explanation", root, Vector2.zero, new Vector2(530f, 540f), 25f);
        body.alignment = TextAlignmentOptions.TopLeft;
        footer = Text("Confirm Instructions", root, new Vector2(0f, -195f), new Vector2(530f, 140f), 23f);
        footer.text = "Press <b>Confirm position</b> after placing both dots, or <b>Retry</b> to place them again. The game starts when both players have confirmed.";
        diagram = Rect("Top-down Scene Layout", root, new Vector2(0f, 5f), new Vector2(530f, 250f));
        BuildDiagram(playArea, areas, forward);
        backButton = Button("Back", new Vector2(-170f, -315f), Back);
        Button next = Button("Next", new Vector2(170f, -315f), Next);
        nextLabel = next.GetComponentInChildren<TextMeshProUGUI>();
        pageNumber = Text("Page Number", root, new Vector2(0f, -315f), new Vector2(90f, 50f), 22f);
        root.gameObject.SetActive(false);
    }

    public void BeginSession()
    {
        Completed = false;
        Open();
    }

    public void EndSession()
    {
        Completed = false;
        IsOpen = false;
        if (root != null) root.gameObject.SetActive(false);
    }

    public void Open()
    {
        if (root == null) return;
        IsOpen = true;
        navigationAllowedAt = Time.unscaledTime + 0.25f;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        root.gameObject.SetActive(true);
        ShowPage(0);
    }

    public void Close()
    {
        IsOpen = false;
        if (root != null) root.gameObject.SetActive(false);
    }

    private void ShowPage(int index)
    {
        page = Mathf.Clamp(index, 0, pages.Length - 1);
        heading.text = titles[page];
        body.text = pages[page];
        bool last = page == pages.Length - 1;
        body.rectTransform.anchoredPosition = last ? new Vector2(0f, 220f) : new Vector2(0f, 55f);
        body.rectTransform.sizeDelta = last ? new Vector2(530f, 150f) : new Vector2(530f, 470f);
        body.fontSize = last ? 23f : 25f;
        diagram.gameObject.SetActive(last);
        footer.gameObject.SetActive(last);
        pageNumber.text = $"{page + 1} / {pages.Length}";
        backButton.interactable = page > 0;
        nextLabel.text = last ? "Continue" : "Next";
        RefreshHitSurface();
        Debug.Log($"[CalibrationInstructions] Showing page {page + 1}/{pages.Length}");
    }

    private bool AcceptNavigation()
    {
        if (!IsOpen || Time.unscaledTime < navigationAllowedAt) return false;
        // Meta hand/controller UI can raise more than one click for one gesture.
        // Share the guard across Back, Next and Continue so no page is skipped.
        navigationAllowedAt = Time.unscaledTime + NavigationDebounceSeconds;
        return true;
    }

    private void Back()
    {
        if (!AcceptNavigation() || page <= 0) return;
        ShowPage(page - 1);
    }

    private void Next()
    {
        if (!AcceptNavigation()) return;
        Debug.Log($"[CalibrationInstructions] Next clicked on page {page + 1}");
        if (page < pages.Length - 1) { ShowPage(page + 1); return; }
        Completed = true;
        IsOpen = false;
        root.gameObject.SetActive(false);
        continueAction?.Invoke();
    }

    private void RefreshHitSurface()
    {
        Canvas.ForceUpdateCanvases();
        RefreshHitSurfaceBoundsOnly();
    }

    private void LateUpdate()
    {
        if (IsOpen) RefreshHitSurfaceBoundsOnly();
    }

    private void RefreshHitSurfaceBoundsOnly()
    {
        BoundsClipper clipper = canvas != null ? canvas.GetComponent<BoundsClipper>() : null;
        if (clipper == null) return;
        Rect rect = canvas.rect;
        clipper.Position = new Vector3(rect.center.x, rect.center.y, 0f);
        clipper.Size = new Vector3(rect.width, rect.height, 0.01f);
    }

    private void BuildDiagram(Transform playArea, Transform[] areas, Vector3 direction)
    {
        Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 origin = playArea.position;
        Vector2 Project(Vector3 point)
        {
            Vector3 delta = point - origin;
            return new Vector2(Vector3.Dot(delta, right), Vector3.Dot(delta, forward));
        }
        Bounds MapBounds(Transform area)
        {
            bool any = false;
            Bounds result = new Bounds();
            if (area != null) foreach (Renderer visual in area.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = visual.bounds;
                foreach (float x in new[] { b.min.x, b.max.x })
                foreach (float z in new[] { b.min.z, b.max.z })
                {
                    Vector2 point = Project(new Vector3(x, origin.y, z));
                    Vector3 p = new Vector3(point.x, point.y, 0f);
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            if (!any) result = new Bounds(area != null ? (Vector3)Project(area.position) : Vector3.zero, Vector3.one * 0.25f);
            return result;
        }
        var bounds = new Bounds[3];
        Bounds all = new Bounds(Vector3.zero, new Vector3(2f, 2f, 0f));
        for (int i = 0; i < bounds.Length; i++)
        {
            bounds[i] = MapBounds(areas != null && i < areas.Length ? areas[i] : null);
            all.Encapsulate(bounds[i]);
        }
        float scale = Mathf.Min(470f / Mathf.Max(2f, all.size.x), 205f / Mathf.Max(2f, all.size.y));
        Vector2 offset = -(Vector2)all.center * scale;
        Vector2 Map(Vector2 p) => offset + p * scale;
        Color red = new Color(1f, 0.3f, 0.3f);
        Line(Map(new Vector2(-1f, -1f)), Map(new Vector2(1f, -1f)), red);
        Line(Map(new Vector2(1f, -1f)), Map(new Vector2(1f, 1f)), red);
        Line(Map(new Vector2(1f, 1f)), Map(new Vector2(-1f, 1f)), red);
        Line(Map(new Vector2(-1f, 1f)), Map(new Vector2(-1f, -1f)), red);
        Text("Boundary Label", diagram, Map(new Vector2(0f, -1f)) + new Vector2(0f, -13f), new Vector2(140f, 24f), 15f).text = "2 x 2 m marking";
        string[] labels = { "Reference area", "Assembly area", "Loose parts" };
        Color[] colors = { new Color(0.25f, 0.65f, 0.9f), new Color(0.2f, 0.8f, 0.6f), new Color(0.9f, 0.65f, 0.2f) };
        for (int i = 0; i < bounds.Length; i++)
        {
            RectTransform box = Rect(labels[i], diagram, Map(bounds[i].center),
                new Vector2(Mathf.Max(75f, bounds[i].size.x * scale), Mathf.Max(30f, bounds[i].size.y * scale)));
            Image image = box.gameObject.AddComponent<Image>();
            image.color = new Color(colors[i].r, colors[i].g, colors[i].b, 0.25f);
            image.raycastTarget = false;
            Text("Area Label", box, Vector2.zero, box.sizeDelta, 15f).text = labels[i];
        }
        Vector2 a = Map(Vector2.zero), b2 = Map(new Vector2(0f, 0.75f));
        Color yellow = new Color(1f, 0.9f, 0.2f);
        Line(a, b2, yellow, 3f);
        Line(b2, b2 + new Vector2(-6f, -9f), yellow, 3f);
        Line(b2, b2 + new Vector2(6f, -9f), yellow, 3f);
        Dot("1", a, yellow);
        Dot("2", b2, yellow);
        Text("Centre Label", diagram, a + new Vector2(-62f, -15f), new Vector2(100f, 24f), 14f).text = "Centre";
        Text("Forward Label", diagram, b2 + new Vector2(-55f, 13f), new Vector2(110f, 24f), 14f).text = "Facing direction";
        Text("Diagram Caption", diagram, new Vector2(0f, -116f), new Vector2(520f, 25f), 14f).text = "Top view - current scene layout; dot 2 distance is illustrative";
    }

    private void Dot(string number, Vector2 position, Color color)
    {
        RectTransform dot = Rect("Dot " + number, diagram, position, new Vector2(20f, 20f));
        Image image = dot.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        var label = Text("Dot Number", dot, Vector2.zero, dot.sizeDelta, 16f);
        label.text = number;
        label.color = Color.black;
    }

    private void Line(Vector2 a, Vector2 b, Color color, float thickness = 2f)
    {
        Vector2 delta = b - a;
        RectTransform line = Rect("Diagram Line", diagram, (a + b) * 0.5f, new Vector2(delta.magnitude, thickness));
        line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        Image image = line.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = canvas.gameObject.layer;
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private TextMeshProUGUI Text(string name, Transform parent, Vector2 position, Vector2 size, float fontSize)
    {
        var rect = Rect(name, parent, position, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private Button Button(string label, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(label, root, position, new Vector2(190f, 52f));
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.06f, 0.25f, 0.32f, 0.95f);
        image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.interactable = true;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.4f, 1f, 1f, 1f);
        colors.pressedColor = new Color(0.2f, 0.7f, 1f, 1f);
        button.colors = colors;
        button.onClick.AddListener(action);
        Text(label + " Label", rect, Vector2.zero, rect.sizeDelta, 23f).text = label;
        return button;
    }
}