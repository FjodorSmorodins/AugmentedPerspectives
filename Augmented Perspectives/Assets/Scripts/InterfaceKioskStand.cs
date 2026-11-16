using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Ordinary scene objects, sized by the UI scripts. No physics or networking.
[ExecuteAlways, DefaultExecutionOrder(500)]
public class InterfaceKioskStand : MonoBehaviour
{
    [SerializeField] private RectTransform connectionCanvas;
    [SerializeField] private RectTransform assemblyCanvas;
    [SerializeField] private Transform floorOrigin;
    [SerializeField] private Material shellMaterial;
    [SerializeField] private Material trimMaterial;
    [SerializeField] private Material screenMaterial;
    [SerializeField, Range(0f, 35f)] private float screenTilt = 18f;
    [SerializeField, Min(0.01f)] private float bezelWidth = 0.028f;
    [SerializeField, Min(0.005f)] private float screenClearance = 0.025f;
    [SerializeField, Tooltip("Assembly controls centre, in connection-canvas UI units.")]
    private Vector2 assemblyOffset = new Vector2(0f, -300f);
    [SerializeField, HideInInspector] private Transform geometry;

    private sealed class Panel
    {
        public Transform root, backing, top, bottom, left, right, post, foot;
    }

    private Panel connection;
    private readonly Vector3[] corners = new Vector3[4];
    private readonly System.Collections.Generic.List<Graphic> graphics = new();

    private void LateUpdate()
    {
        // Do not generate objects while editing a prefab asset.
        if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return;
        EnsureGeometry();
        FitPanel(connection, connectionCanvas);
    }

    private void EnsureGeometry()
    {
        if (geometry == null)
        {
            geometry = transform.Find("Interface Stand Geometry");
            if (geometry == null)
            {
                geometry = new GameObject("Interface Stand Geometry").transform;
                geometry.SetParent(transform, false);
            }
        }
        if (connection == null || connection.root == null) connection = CreatePanel("Shared Interface Stand");
    }

    private Panel CreatePanel(string label)
    {
        Transform root = geometry.Find(label);
        if (root == null)
        {
            root = new GameObject(label).transform;
            root.SetParent(geometry, false);
        }
        return new Panel
        {
            root = root,
            backing = Box("Screen Backing", root, screenMaterial),
            top = Box("Top Bezel", root, shellMaterial),
            bottom = Box("Bottom Bezel", root, shellMaterial),
            left = Box("Left Bezel", root, shellMaterial),
            right = Box("Right Bezel", root, shellMaterial),
            post = Box("Rear Support", root, trimMaterial),
            foot = Box("Floor Base", root, shellMaterial)
        };
    }

    private void FitPanel(Panel panel, RectTransform canvas)
    {
        bool visible = canvas != null && canvas.gameObject.activeInHierarchy;
        panel.root.gameObject.SetActive(visible);
        if (!visible) return;

        Vector3 forward = Vector3.ProjectOnPlane(canvas.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f) return;
        canvas.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up) * Quaternion.Euler(screenTilt, 0f, 0f);
        // Keep the existing canvases and interaction components, but share one
        // display surface. The gameplay guide sits above the assembly controls;
        // Disconnect stays at the bottom.
        if (assemblyCanvas != null && assemblyCanvas != canvas)
        {
            assemblyCanvas.SetPositionAndRotation(canvas.TransformPoint(new Vector3(assemblyOffset.x, assemblyOffset.y, 0f)), canvas.rotation);
        }
        Quaternion orientation = canvas.rotation;
        Quaternion inverse = Quaternion.Inverse(orientation);
        Bounds bounds = new Bounds();
        bool first = true;
        IncludeCanvas(canvas, canvas.position, inverse, ref bounds, ref first);
        if (assemblyCanvas != null && assemblyCanvas != canvas && assemblyCanvas.gameObject.activeInHierarchy)
            IncludeCanvas(assemblyCanvas, canvas.position, inverse, ref bounds, ref first);

        float width = Mathf.Max(0.1f, bounds.size.x);
        float height = Mathf.Max(0.1f, bounds.size.y);
        float bezel = Mathf.Max(0.01f, bezelWidth);
        float depth = 0.02f;
        float rear = bounds.extents.z + Mathf.Max(0.005f, screenClearance);
        panel.root.SetPositionAndRotation(canvas.position + orientation * bounds.center, orientation);
        SetLocalBox(panel.backing, new Vector3(0f, 0f, rear + depth * 0.5f), new Vector3(width, height, depth));
        SetLocalBox(panel.top, new Vector3(0f, (height + bezel) * 0.5f, rear + depth * 0.5f), new Vector3(width + bezel * 2f, bezel, depth));
        SetLocalBox(panel.bottom, new Vector3(0f, -(height + bezel) * 0.5f, rear + depth * 0.5f), new Vector3(width + bezel * 2f, bezel, depth));
        SetLocalBox(panel.left, new Vector3(-(width + bezel) * 0.5f, 0f, rear + depth * 0.5f), new Vector3(bezel, height, depth));
        SetLocalBox(panel.right, new Vector3((width + bezel) * 0.5f, 0f, rear + depth * 0.5f), new Vector3(bezel, height, depth));

        // A slim upright behind the screen replaces the wide angled plank that
        // protruded toward the viewer across the bottom (Disconnect / Cancel) row.
        float floor = floorOrigin != null ? floorOrigin.position.y : 0f;
        float baseWidth = Mathf.Clamp(width * 0.65f, 0.2f, 0.55f);
        float baseDepth = Mathf.Clamp(width * 0.35f, 0.16f, 0.3f);
        float postDepth = 0.06f;
        Vector3 attachment = panel.root.TransformPoint(new Vector3(0f, -height * 0.5f, rear + depth + baseDepth * 0.5f));
        float postHeight = Mathf.Max(0f, attachment.y - floor - 0.04f);
        panel.post.gameObject.SetActive(postHeight > 0f);
        panel.foot.gameObject.SetActive(postHeight > 0f);
        Quaternion yaw = Quaternion.LookRotation(forward.normalized, Vector3.up);
        SetWorldBox(panel.post, new Vector3(attachment.x, floor + 0.04f + postHeight * 0.5f, attachment.z), yaw,
            new Vector3(Mathf.Clamp(width * 0.16f, 0.05f, 0.12f), postHeight, postDepth));
        SetWorldBox(panel.foot, new Vector3(attachment.x, floor + 0.02f, attachment.z), yaw,
            new Vector3(baseWidth, 0.04f, baseDepth));

        ApplyMaterial(panel.backing, screenMaterial);
        ApplyMaterial(panel.post, trimMaterial);
        ApplyMaterial(panel.foot, shellMaterial);
        ApplyMaterial(panel.top, shellMaterial);
        ApplyMaterial(panel.bottom, shellMaterial);
        ApplyMaterial(panel.left, shellMaterial);
        ApplyMaterial(panel.right, shellMaterial);
    }

    private void IncludeCanvas(RectTransform canvas, Vector3 anchor, Quaternion inverse, ref Bounds bounds, ref bool first)
    {
        IncludeRect(canvas, anchor, inverse, ref bounds, ref first);
        // Include runtime text/buttons outside the canvas, and their depth.
        canvas.GetComponentsInChildren(false, graphics);
        foreach (Graphic graphic in graphics)
            if (graphic.isActiveAndEnabled)
                IncludeRect(graphic.rectTransform, anchor, inverse, ref bounds, ref first);
    }

    private void IncludeRect(RectTransform rect, Vector3 anchor, Quaternion inverse, ref Bounds bounds, ref bool first)
    {
        rect.GetWorldCorners(corners);
        foreach (Vector3 corner in corners)
        {
            Vector3 local = inverse * (corner - anchor);
            if (first) { bounds = new Bounds(local, Vector3.zero); first = false; }
            else bounds.Encapsulate(local);
        }
    }

    private static void SetLocalBox(Transform box, Vector3 position, Vector3 size)
    {
        box.localPosition = position;
        box.localRotation = Quaternion.identity;
        box.localScale = size;
    }

    private static void SetWorldBox(Transform box, Vector3 position, Quaternion rotation, Vector3 size)
    {
        box.SetPositionAndRotation(position, rotation);
        box.localScale = size;
    }

    private static void ApplyMaterial(Transform box, Material material)
    {
        Renderer renderer = box.GetComponent<Renderer>();
        if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;
    }

    private static Transform Box(string label, Transform parent, Material material)
    {
        Transform existing = parent.Find(label);
        if (existing != null) return existing;
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = label;
        go.layer = 2; // Ignore Raycast, in addition to removing its collider.
        go.transform.SetParent(parent, false);
        Collider collider = go.GetComponent<Collider>();
        collider.enabled = false;
        if (Application.isPlaying) Destroy(collider);
        else DestroyImmediate(collider);
        Renderer renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go.transform;
    }

    private void OnEnable()
    {
        if (geometry != null) geometry.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (geometry != null) geometry.gameObject.SetActive(false);
    }
}
