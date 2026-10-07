using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

public class FloorPlayArea : MonoBehaviour
{
    [SerializeField] private Transform cameraRigRoot;
    [SerializeField] private Transform headAnchor;
    [SerializeField] private Material lineMaterial;
    [SerializeField] private Vector3 dimensions = new Vector3(2f, 2f, 2f);
    [SerializeField, Min(0.001f)] private float lineWidth = 0.01f;
    [SerializeField] private float floorOffset = 0.005f;

    private Mesh outlineMesh;
    private OVRDisplay display;

    private IEnumerator Start()
    {
        BuildOutline();
        if (cameraRigRoot == null || headAnchor == null)
        {
            Debug.LogError("[PlayArea] Assign the camera rig root and head anchor.");
            yield break;
        }

        if (Application.isEditor && !XRSettings.isDeviceActive)
        {
            CenterPlayer();
            yield break;
        }

        // A saved prefab pose is not the wearer's actual startup position.
        // Wait for headset tracking, then let the camera anchors update.
        while (true)
        {
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
                break;
            yield return null;
        }
        yield return new WaitForEndOfFrame();
        CenterPlayer();
        display = OVRManager.display;
        if (display != null)
            display.RecenteredPose += HandleRecenter;
    }

    private void OnEnable()
    {
        if (display != null)
            display.RecenteredPose += HandleRecenter;
    }

    private void OnDisable()
    {
        if (display != null)
            display.RecenteredPose -= HandleRecenter;
    }

    private void HandleRecenter()
    {
        StartCoroutine(CenterAfterTrackingUpdate());
    }

    private IEnumerator CenterAfterTrackingUpdate()
    {
        yield return new WaitForEndOfFrame();
        CenterPlayer();
    }

    [ContextMenu("Center Player In Area")]
    public void CenterPlayer()
    {
        if (cameraRigRoot == null || headAnchor == null)
            return;
        Vector3 offset = transform.position - headAnchor.position;
        offset.y = 0f; // Floor-level tracking preserves the wearer's real head height.
        cameraRigRoot.position += offset;
        Debug.Log($"[PlayArea] Player centred in {dimensions.x} x {dimensions.z} metre floor area.");
    }

    private void BuildOutline()
    {
        float x = dimensions.x * 0.5f;
        float z = dimensions.z * 0.5f;
        float width = Mathf.Min(lineWidth, Mathf.Min(dimensions.x, dimensions.z) * 0.5f);
        var vertices = new Vector3[16];
        var triangles = new int[24];
        AddStrip(vertices, triangles, 0, -x, x, -z, -z + width);
        AddStrip(vertices, triangles, 1, -x, x, z - width, z);
        AddStrip(vertices, triangles, 2, -x, -x + width, -z + width, z - width);
        AddStrip(vertices, triangles, 3, x - width, x, -z + width, z - width);
        outlineMesh = new Mesh { name = "2m Floor Outline", vertices = vertices, triangles = triangles };
        outlineMesh.RecalculateNormals();
        outlineMesh.RecalculateBounds();
        var outline = new GameObject("Red Floor Outline");
        outline.transform.SetParent(transform, false);
        outline.AddComponent<MeshFilter>().sharedMesh = outlineMesh;
        var renderer = outline.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = lineMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        // No collider: the marking cannot interfere with grabbing or locomotion.
    }

    private void AddStrip(Vector3[] vertices, int[] triangles, int strip, float left, float right, float back, float front)
    {
        int v = strip * 4;
        vertices[v] = new Vector3(left, floorOffset, back);
        vertices[v + 1] = new Vector3(left, floorOffset, front);
        vertices[v + 2] = new Vector3(right, floorOffset, front);
        vertices[v + 3] = new Vector3(right, floorOffset, back);
        int t = strip * 6;
        triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
        triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.up * dimensions.y * 0.5f, dimensions);
    }

    private void OnDestroy()
    {
        if (display != null)
            display.RecenteredPose -= HandleRecenter;
        if (outlineMesh != null)
            Destroy(outlineMesh);
    }
}
