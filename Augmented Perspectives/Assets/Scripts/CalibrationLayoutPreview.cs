using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// Local, full-size wire outlines. These never move or clone the networked game objects.
public class CalibrationLayoutPreview : MonoBehaviour
{
    private Transform playArea, head;
    private FloorPlayArea floorPlayArea;
    private Transform[] areas;
    private Vector3 gameForward;
    private GameObject geometry;
    private Material boundaryMaterial;
    private readonly List<Material> ownedMaterials = new();
    private readonly List<Transform> labels = new();

    public void Initialize(Transform floor, Transform[] gameAreas, Vector3 forward, Transform viewer, Material material)
    {
        playArea = floor;
        floorPlayArea = floor.GetComponent<FloorPlayArea>();
        areas = gameAreas;
        head = viewer;
        gameForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        if (gameForward.sqrMagnitude < 0.5f) gameForward = Vector3.forward;
        boundaryMaterial = ColourMaterial(material, new Color(0.95f, 0.95f, 1f));
        ColourMaterial(material, new Color(0.25f, 0.7f, 1f));
        ColourMaterial(material, new Color(0.25f, 1f, 0.65f));
        ColourMaterial(material, new Color(1f, 0.75f, 0.2f));
    }

    public void Rebuild()
    {
        if (geometry != null)
        {
            geometry.SetActive(false);
            Destroy(geometry);
        }
        labels.Clear();
        geometry = new GameObject("Local Game Layout Preview - Full Size");
        geometry.layer = 2;
        geometry.SetActive(false);
        // Snapshot world-space bounds relative to the same origin used at confirmation.
        // The preview's yaw/translation is the inverse of the final rig correction.
        Vector3 Relative(Vector3 point) => point - playArea.position;
        if (floorPlayArea != null)
        {
            Vector3 half = floorPlayArea.Dimensions * 0.5f;
            var floor = new[] {
                new Vector3(-half.x, 0.02f, -half.z), new Vector3(-half.x, 0.02f, half.z),
                new Vector3(half.x, 0.02f, half.z), new Vector3(half.x, 0.02f, -half.z)
            };
            for (int i = 0; i < floor.Length; i++) floor[i] = Relative(playArea.TransformPoint(floor[i]));
            Line($"{half.x * 2f:g} by {half.z * 2f:g} metre space", floor, boundaryMaterial, 0.012f, true);
        }
        Vector3 tip = gameForward * 0.7f + Vector3.up * 0.035f;
        Vector3 side = Vector3.Cross(Vector3.up, gameForward) * 0.09f;
        Line("Facing arrow", new[] { Vector3.up * 0.035f, tip, tip - gameForward * 0.15f + side,
            tip, tip - gameForward * 0.15f - side }, boundaryMaterial, 0.012f);
        string[] names = { "Reference area", "Build here", "Loose pieces" };
        if (areas == null) return;
        for (int i = 0; i < areas.Length; i++)
        {
            if (areas[i] == null) continue;
            bool isReferenceArea = i == 0;
            Material colour = ownedMaterials[1 + i % 3];
            Bounds group = new Bounds();
            bool any = false;
            foreach (Renderer visual in areas[i].GetComponentsInChildren<Renderer>(true))
            {
                // Exclude labels/effects; hidden meshes still define the actual scene layout.
                if (!(visual is MeshRenderer || visual is SkinnedMeshRenderer) || visual.GetComponent<TMP_Text>() != null) continue;
                if (isReferenceArea && visual.GetComponentInParent<ReferencePartVisibility>(true) != null) continue;
                Bounds bounds = visual.bounds;
                if (bounds.size.sqrMagnitude < 0.000001f) continue;
                if (!any) { group = bounds; any = true; }
                else group.Encapsulate(bounds);
                if (isReferenceArea) continue; // Show only the area footprint, without revealing the build.
                Vector3 min = Relative(bounds.min), max = Relative(bounds.max);
                // Continuous path around all twelve edges; a few edges are retraced.
                Line("Outline - " + visual.name, new[] {
                    new Vector3(min.x,min.y,min.z), new Vector3(max.x,min.y,min.z),
                    new Vector3(max.x,min.y,max.z), new Vector3(min.x,min.y,max.z),
                    new Vector3(min.x,min.y,min.z), new Vector3(min.x,max.y,min.z),
                    new Vector3(max.x,max.y,min.z), new Vector3(max.x,min.y,min.z),
                    new Vector3(max.x,max.y,min.z), new Vector3(max.x,max.y,max.z),
                    new Vector3(max.x,min.y,max.z), new Vector3(max.x,max.y,max.z),
                    new Vector3(min.x,max.y,max.z), new Vector3(min.x,min.y,max.z),
                    new Vector3(min.x,max.y,max.z), new Vector3(min.x,max.y,min.z)
                }, colour, 0.006f);
            }
            if (!any) continue;
            Vector3 centre = Relative(group.center);
            Vector3 minFloor = Relative(group.min), maxFloor = Relative(group.max);
            Line("Floor footprint", new[] {
                new Vector3(minFloor.x,0.025f,minFloor.z), new Vector3(minFloor.x,0.025f,maxFloor.z),
                new Vector3(maxFloor.x,0.025f,maxFloor.z), new Vector3(maxFloor.x,0.025f,minFloor.z)
            }, colour, 0.009f, true);
            var labelObject = new GameObject(i < names.Length ? names[i] : "Game objects", typeof(TextMeshPro));
            labelObject.layer = 2;
            labelObject.transform.SetParent(geometry.transform, false);
            labelObject.transform.localPosition = new Vector3(centre.x, Mathf.Max(0.1f, maxFloor.y + 0.1f), centre.z);
            var text = labelObject.GetComponent<TextMeshPro>();
            text.text = labelObject.name;
            text.rectTransform.sizeDelta = new Vector2(1.4f, 0.25f);
            text.fontSize = 1.3f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = colour.HasProperty("_BaseColor") ? colour.GetColor("_BaseColor") : colour.color;
            text.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            labels.Add(labelObject.transform);
        }
    }

    public void Show(bool visible, Vector3 origin, Vector3 forward)
    {
        if (geometry == null) return;
        geometry.SetActive(visible);
        if (!visible) return;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        Quaternion yaw = forward.sqrMagnitude > 0.0001f
            ? Quaternion.AngleAxis(Vector3.SignedAngle(gameForward, forward, Vector3.up), Vector3.up)
            : Quaternion.identity;
        geometry.transform.SetPositionAndRotation(origin, yaw);
        if (head != null) foreach (Transform label in labels)
        {
            Vector3 awayFromViewer = label.position - head.position;
            if (awayFromViewer.sqrMagnitude > 0.001f)
                label.rotation = Quaternion.LookRotation(awayFromViewer, Vector3.up);
        }
    }

    public void Hide()
    {
        if (geometry != null) geometry.SetActive(false);
    }

    private Material ColourMaterial(Material source, Color colour)
    {
        var material = new Material(source) { name = "Calibration Layout Outline" };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
        if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
        ownedMaterials.Add(material);
        return material;
    }

    private void Line(string label, Vector3[] points, Material material, float width, bool loop = false)
    {
        var go = new GameObject(label, typeof(LineRenderer));
        go.layer = 2;
        go.transform.SetParent(geometry.transform, false);
        var line = go.GetComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = loop;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.startWidth = line.endWidth = width;
        line.sharedMaterial = material;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
    }

    private void OnDisable() => Hide();

    private void OnDestroy()
    {
        if (geometry != null) Destroy(geometry);
        foreach (Material material in ownedMaterials) Destroy(material);
    }
}
