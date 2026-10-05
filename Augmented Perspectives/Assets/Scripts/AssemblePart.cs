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

    [Header("Optional submission locking")]
    [SerializeField] private Rigidbody partRigidbody;

    [Tooltip("Assign Meta Grabbable/Grab Interactable behaviours here if they should be disabled after submission.")]
    [SerializeField] private Behaviour[] grabBehaviours = new Behaviour[0];

    private Vector3 startingPosition;
    private Quaternion startingRotation;
    private bool startingKinematic;

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

        if (partRigidbody == null)
            partRigidbody = GetComponent<Rigidbody>();

        if (partRigidbody != null)
            startingKinematic = partRigidbody.isKinematic;
    }

    public void FreezeForSubmission()
    {
        foreach (Behaviour behaviour in grabBehaviours)
        {
            if (behaviour != null)
                behaviour.enabled = false;
        }

        if (partRigidbody != null)
        {
            partRigidbody.linearVelocity = Vector3.zero;
            partRigidbody.angularVelocity = Vector3.zero;
            partRigidbody.isKinematic = true;
        }
    }

    public void UnfreezeAfterSubmission()
    {
        foreach (Behaviour behaviour in grabBehaviours)
        {
            if (behaviour != null)
                behaviour.enabled = true;
        }

        if (partRigidbody != null)
        {
            partRigidbody.linearVelocity = Vector3.zero;
            partRigidbody.angularVelocity = Vector3.zero;
            partRigidbody.isKinematic = startingKinematic;
        }
    }

    public void ResetPart()
    {
        if (partRigidbody != null)
        {
            partRigidbody.linearVelocity = Vector3.zero;
            partRigidbody.angularVelocity = Vector3.zero;
            partRigidbody.position = startingPosition;
            partRigidbody.rotation = startingRotation;
        }
        else
        {
            transform.SetPositionAndRotation(startingPosition, startingRotation);
        }

        UnfreezeAfterSubmission();
    }
}