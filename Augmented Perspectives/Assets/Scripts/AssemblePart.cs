using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

public class AssemblePart : MonoBehaviour
{
    [Header("Matching reference")]
    [SerializeField] private Transform referencePart;
    [SerializeField] private Transform referenceArea;
    [SerializeField] private Transform assemblyArea;

    [Header("Scoring tolerances")]
    [Min(0.001f)]
    [SerializeField] private float maximumPositionError = 0.5f;

    [Range(0f, 180f)]
    [SerializeField] private float maximumRotationError = 90f;

    [Min(0f)]
    [SerializeField] private float positionWeight = 0.6f;

    [Min(0f)]
    [SerializeField] private float rotationWeight = 0.4f;

    [Header("Snap on release")]
    [SerializeField] private bool snapOnRelease = true;
    [SerializeField, Min(0f)] private float snapDistance = 0.4f;

    [Header("Optional submission locking")]
    [SerializeField] private Rigidbody partRigidbody;

    [Tooltip("Assign Meta Grabbable/Grab Interactable behaviours here if they should be disabled after submission.")]
    [SerializeField] private Behaviour[] grabBehaviours = new Behaviour[0];

    private Vector3 startingPosition;
    private Quaternion startingRotation;
    private bool startingKinematic;
    private RigidbodyConstraints startingConstraints;
    private Grabbable grabbable;
    private bool[] startingGrabEnabled;
    private ReferencePartVisibility referenceVisibility;
    private bool pendingSnap;
    private Vector3 releasedPosition;

    public bool IsFrozenForSubmission { get; private set; }
    public bool IsSnapped { get; private set; }
    public bool IsLocked => IsFrozenForSubmission || IsSnapped;

    public float PositionError => Vector3.Distance(transform.position, TargetWorldPosition);

    public float RotationError => Quaternion.Angle(transform.rotation, TargetWorldRotation);

    public float Score01
    {
        get
        {
            float positionScore = 1f - Mathf.Clamp01(PositionError / maximumPositionError);
            float rotationLimit = Mathf.Max(0.001f, maximumRotationError);
            float rotationScore = 1f - Mathf.Clamp01(RotationError / rotationLimit);

            float totalWeight = positionWeight + rotationWeight;
            if (totalWeight <= 0f)
                return 0f;

            return (positionScore * positionWeight + rotationScore * rotationWeight) / totalWeight;
        }
    }

    private Vector3 TargetWorldPosition
    {
        get
        {
            if (referencePart == null)
                return transform.position;

            if (referenceArea == null || assemblyArea == null)
                return referencePart.position;

            Vector3 positionInsideReferenceArea =
                referenceArea.InverseTransformPoint(referencePart.position);

            return assemblyArea.TransformPoint(positionInsideReferenceArea);
        }
    }

    private Quaternion TargetWorldRotation
    {
        get
        {
            if (referencePart == null)
                return transform.rotation;

            if (referenceArea == null || assemblyArea == null)
                return referencePart.rotation;

            Quaternion rotationInsideReferenceArea =
                Quaternion.Inverse(referenceArea.rotation) * referencePart.rotation;

            return assemblyArea.rotation * rotationInsideReferenceArea;
        }
    }

    private void Awake()
    {
        startingPosition = transform.position;
        startingRotation = transform.rotation;
        grabbable = GetComponent<Grabbable>();
        if (referencePart != null)
            referenceVisibility = referencePart.GetComponentInParent<ReferencePartVisibility>(true);
        if (partRigidbody == null)
            partRigidbody = GetComponent<Rigidbody>();
        if (partRigidbody != null)
        {
            startingKinematic = partRigidbody.isKinematic;
            startingConstraints = partRigidbody.constraints;
        }

        // Use the existing Meta hand/controller interactables when no custom list was assigned.
        if (grabBehaviours == null || grabBehaviours.Length == 0)
        {
            var behaviours = new List<Behaviour>();
            foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is Grabbable || behaviour is GrabInteractable || behaviour is HandGrabInteractable)
                    behaviours.Add(behaviour);
            }
            grabBehaviours = behaviours.ToArray();
        }
        startingGrabEnabled = new bool[grabBehaviours.Length];
        for (int i = 0; i < grabBehaviours.Length; i++)
            startingGrabEnabled[i] = grabBehaviours[i] != null && grabBehaviours[i].enabled;
    }

    private void OnEnable()
    {
        if (grabbable != null)
            grabbable.WhenPointerEventRaised += HandleGrabEvent;
    }

    private void OnDisable()
    {
        if (grabbable != null)
            grabbable.WhenPointerEventRaised -= HandleGrabEvent;
        pendingSnap = false;
    }

    // Visibility and grab permission use the same host/join audience rules.
    // Keep offline interaction available until a multiplayer role is established.
    public bool CanClientGrab(ulong clientId) => referenceVisibility == null ||
        !referenceVisibility.IsVisibleToRole(clientId == Unity.Netcode.NetworkManager.ServerClientId);

    private bool CanLocalPlayerGrab
    {
        get
        {
            var manager = Unity.Netcode.NetworkManager.Singleton;
            if (manager == null || !manager.IsListening ||
                (!manager.IsHost && !manager.IsConnectedClient))
                return true;
            return CanClientGrab(manager.LocalClientId);
        }
    }

    private void Update()
    {
        ApplyGrabPermissions();
    }

    private void ApplyGrabPermissions()
    {
        bool canGrab = !IsLocked && CanLocalPlayerGrab;
        if (!canGrab)
            pendingSnap = false;
        for (int i = 0; i < grabBehaviours.Length; i++)
        {
            if (grabBehaviours[i] != null)
            {
                bool enable = canGrab && startingGrabEnabled[i];
                if (grabBehaviours[i].enabled != enable)
                    grabBehaviours[i].enabled = enable;
            }
        }
    }

    private void HandleGrabEvent(PointerEvent pointerEvent)
    {
        if (pointerEvent.Type == PointerEventType.Select)
            pendingSnap = false;
        if (pointerEvent.Type != PointerEventType.Unselect || grabbable.SelectingPointsCount != 0)
            return;

        // Check the released pose, before gravity or the SDK's throw can move the part.
        releasedPosition = transform.position;
        pendingSnap = !IsLocked && CanLocalPlayerGrab && IsWithinSnapDistance(releasedPosition);
        Debug.Log($"[AssemblySnap] {name} released: distance={Vector3.Distance(releasedPosition, TargetWorldPosition):F3}m, " +
                  $"limit={snapDistance:F3}m, enabled={snapOnRelease}, referenceAssigned={referencePart != null}, " +
                  $"locked={IsLocked}, snapRequested={pendingSnap}, target={TargetWorldPosition:F3}", this);
    }

    private void LateUpdate()
    {
        if (!pendingSnap)
            return;
        pendingSnap = false;
        if (IsLocked || grabbable == null || grabbable.SelectingPointsCount != 0)
            return;

        var ownership = GetComponent<NetworkGrabOwnership>();
        if (ownership != null && ownership.IsSpawned)
        {
            ownership.RequestSnap(releasedPosition);
            return;
        }
        SnapToIdealPose();
    }

    public bool IsWithinSnapDistance(Vector3 position) => snapOnRelease && referencePart != null &&
        Vector3.Distance(position, TargetWorldPosition) <= snapDistance;

    public void ApplyNetworkSnapState(bool snapped)
    {
        if (snapped)
            SnapToIdealPose();
        else
        {
            IsSnapped = false;
            ApplyLockState();
        }
    }

    private void SnapToIdealPose()
    {
        // Finish Meta's release processing before changing the pose and disabling interaction.
        Vector3 targetPosition = TargetWorldPosition;
        Quaternion targetRotation = TargetWorldRotation;
        IsSnapped = true;
        ApplyLockState();
        transform.SetPositionAndRotation(targetPosition, targetRotation);
        if (partRigidbody != null)
        {
            partRigidbody.position = targetPosition;
            partRigidbody.rotation = targetRotation;
        }
        TeleportNetworkTransform();
        Debug.Log($"[AssemblySnap] {name} snapped to its ideal pose and locked.");
    }

    public void FreezeForSubmission()
    {
        IsFrozenForSubmission = true;
        pendingSnap = false;
        ApplyLockState();
    }

    public void UnfreezeAfterSubmission()
    {
        IsFrozenForSubmission = false;
        ApplyLockState(); // A snapped part stays locked until Reset.
    }

    private void ApplyLockState()
    {
        ApplyGrabPermissions();
        if (partRigidbody == null)
            return;
        ClearVelocity();
        partRigidbody.constraints = IsLocked ? RigidbodyConstraints.FreezeAll : startingConstraints;
        partRigidbody.isKinematic = IsLocked || startingKinematic;
    }

    private void ClearVelocity()
    {
        if (partRigidbody != null && !partRigidbody.isKinematic)
        {
            partRigidbody.linearVelocity = Vector3.zero;
            partRigidbody.angularVelocity = Vector3.zero;
        }
    }

    public void ResetPart()
    {
        IsFrozenForSubmission = true;
        ApplyLockState(); // Cancel any active grab before restoring the starting pose.
        pendingSnap = false;
        IsSnapped = false;
        IsFrozenForSubmission = false;
        GetComponent<NetworkGrabOwnership>()?.ResetNetworkSnap();
        ApplyLockState();
        transform.SetPositionAndRotation(startingPosition, startingRotation);
        if (partRigidbody != null)
        {
            partRigidbody.position = startingPosition;
            partRigidbody.rotation = startingRotation;
        }
        TeleportNetworkTransform();
    }

    private void TeleportNetworkTransform()
    {
        var networkTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (networkTransform != null && networkTransform.IsSpawned && networkTransform.IsOwner)
            networkTransform.Teleport(transform.position, transform.rotation, transform.localScale);
    }
}
