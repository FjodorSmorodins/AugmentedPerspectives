using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

public class NetworkPlayerMarkers : NetworkBehaviour
{
    [SerializeField] private Material hostMaterial;
    [SerializeField] private Material clientMaterial;
    [SerializeField] private float headDiameter = 0.09f;
    [SerializeField] private float handDiameter = 0.07f;
    [SerializeField] private float headOffset = 0.18f;

    public struct MarkerPose : INetworkSerializable, IEquatable<MarkerPose>
    {
        public Vector3 Head, Left, Right;
        public bool HeadTracked, LeftTracked, RightTracked;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Head); serializer.SerializeValue(ref Left); serializer.SerializeValue(ref Right);
            serializer.SerializeValue(ref HeadTracked); serializer.SerializeValue(ref LeftTracked); serializer.SerializeValue(ref RightTracked);
        }
        public bool Equals(MarkerPose other) => Head.Equals(other.Head) && Left.Equals(other.Left) && Right.Equals(other.Right)
            && HeadTracked == other.HeadTracked && LeftTracked == other.LeftTracked && RightTracked == other.RightTracked;
    }

    private readonly NetworkVariable<MarkerPose> pose = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private OVRCameraRig localRig;
    private Transform[] spheres;
    private Mesh sphereMesh;
    private float nextSend;
    private bool headVisible, leftVisible, rightVisible;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            localRig = FindFirstObjectByType<OVRCameraRig>();
            return; // The wearer never renders their own markers.
        }
        sphereMesh = CreateSphereMesh();
        Material material = OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId ? hostMaterial : clientMaterial;
        spheres = new[] { CreateMarker("Head Marker", headDiameter, material),
                          CreateMarker("Left Marker", handDiameter, material),
                          CreateMarker("Right Marker", handDiameter, material) };
    }

    private void LateUpdate()
    {
        if (!IsSpawned)
            return;
        if (IsOwner)
        {
            if (localRig == null)
                localRig = FindFirstObjectByType<OVRCameraRig>();
            if (localRig == null || localRig.centerEyeAnchor == null || Time.unscaledTime < nextSend)
                return;
            nextSend = Time.unscaledTime + 1f / 30f;
            bool headTracked = OVRManager.isHmdPresent && OVRManager.hasVrFocus;
            pose.Value = new MarkerPose
            {
                Head = localRig.centerEyeAnchor.position - Vector3.up * headOffset,
                Left = localRig.leftHandAnchor.position,
                Right = localRig.rightHandAnchor.position,
                HeadTracked = headTracked,
                LeftTracked = headTracked && (OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch)
                    || OVRInput.GetControllerPositionTracked(OVRInput.Controller.LHand)),
                RightTracked = headTracked && (OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch)
                    || OVRInput.GetControllerPositionTracked(OVRInput.Controller.RHand))
            };
            return;
        }
        if (spheres == null)
            return;
        MarkerPose current = pose.Value;
        UpdateMarker(spheres[0], current.Head, current.HeadTracked, ref headVisible);
        UpdateMarker(spheres[1], current.Left, current.LeftTracked, ref leftVisible);
        UpdateMarker(spheres[2], current.Right, current.RightTracked, ref rightVisible);
    }

    private static void UpdateMarker(Transform sphere, Vector3 position, bool tracked, ref bool wasVisible)
    {
        sphere.gameObject.SetActive(tracked);
        if (tracked)
            sphere.position = !wasVisible || Vector3.Distance(sphere.position, position) > 0.5f
                ? position : Vector3.Lerp(sphere.position, position, 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime));
        wasVisible = tracked;
    }

    private Transform CreateMarker(string markerName, float diameter, Material material)
    {
        var marker = new GameObject(markerName);
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = Vector3.one * diameter;
        marker.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
        var renderer = marker.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        marker.SetActive(false);
        // Mesh only: no collider, rigidbody or interactable component.
        return marker.transform;
    }

    private static Mesh CreateSphereMesh()
    {
        const int rings = 8, segments = 12;
        var vertices = new Vector3[(rings + 1) * (segments + 1)];
        var triangles = new int[rings * segments * 6];
        for (int y = 0; y <= rings; y++)
        for (int x = 0; x <= segments; x++)
        {
            float latitude = Mathf.PI * y / rings, longitude = 2f * Mathf.PI * x / segments;
            vertices[y * (segments + 1) + x] = 0.5f * new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude),
                Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
        }
        int t = 0;
        for (int y = 0; y < rings; y++)
        for (int x = 0; x < segments; x++)
        {
            int a = y * (segments + 1) + x, b = a + segments + 1;
            triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
            triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
        }
        var mesh = new Mesh { name = "Ghost Player Sphere", vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    public override void OnDestroy()
    {
        if (sphereMesh != null)
            Destroy(sphereMesh);
        base.OnDestroy();
    }
}
