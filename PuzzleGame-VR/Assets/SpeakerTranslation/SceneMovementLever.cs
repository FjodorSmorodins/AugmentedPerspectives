using Oculus.Interaction.Input;
using UnityEngine;

public sealed class SceneMovementLever : MonoBehaviour
{
    [SerializeField] private Transform playerCamera;
    [SerializeField] private Hand leftHand;
    [SerializeField] private Hand rightHand;
    [SerializeField] private Transform[] sceneObjects;
    [SerializeField] private Vector3 cameraOffset = new Vector3(0.25f, -0.32f, 0.5f);
    [SerializeField, Min(0.01f)] private float grabDistance = 0.09f;
    [SerializeField, Min(0.01f)] private float maximumPull = 0.14f;
    [SerializeField, Min(0f)] private float movementSpeed = 0.4f;
    [SerializeField, Min(0f)] private float deadZone = 0.015f;

    private Transform knob;
    private Transform shaft;
    private Hand grabbingHand;
    private Vector3 grabPosition;
    private Vector3 pull;
    private bool leftWasPinching;
    private bool rightWasPinching;
    private Material leverMaterial;
    private bool hasFocus;

    private void Awake()
    {
        hasFocus = Application.isFocused;
        leverMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        leverMaterial.color = new Color(0.15f, 0.75f, 0.9f);
        CreatePart("Lever Base", PrimitiveType.Cylinder, new Vector3(0.11f, 0.015f, 0.11f));
        shaft = CreatePart("Lever Shaft", PrimitiveType.Cylinder, new Vector3(0.018f, 0.07f, 0.018f));
        knob = CreatePart("Lever Handle", PrimitiveType.Sphere, Vector3.one * 0.065f);
    }

    private Transform CreatePart(string partName, PrimitiveType type, Vector3 scale)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = partName;
        part.transform.SetParent(transform, false);
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = leverMaterial;
        Destroy(part.GetComponent<Collider>());
        return part.transform;
    }

    private void LateUpdate()
    {
        if (playerCamera == null || !hasFocus)
            return;

        // Follow the player's position and heading while keeping the base level.
        transform.SetPositionAndRotation(playerCamera.TransformPoint(cameraOffset),
            Quaternion.Euler(0f, playerCamera.eulerAngles.y, 0f));

        bool leftPinching = ReadPinch(leftHand, out Vector3 leftPosition);
        bool rightPinching = ReadPinch(rightHand, out Vector3 rightPosition);
        if (grabbingHand == null)
        {
            if (leftPinching && !leftWasPinching)
                TryGrab(leftHand, leftPosition);
            if (grabbingHand == null && rightPinching && !rightWasPinching)
                TryGrab(rightHand, rightPosition);
        }

        if (grabbingHand != null)
        {
            if (!ReadPinch(grabbingHand, out Vector3 handPosition))
            {
                grabbingHand = null;
                pull = Vector3.zero;
            }
            else
            {
                pull = Vector3.ClampMagnitude(handPosition - grabPosition, maximumPull);
                float amount = Mathf.InverseLerp(deadZone, Mathf.Max(deadZone + 0.001f, maximumPull), pull.magnitude);
                Vector3 movement = pull.normalized * (amount * movementSpeed * Time.unscaledDeltaTime);
                foreach (Transform sceneObject in sceneObjects)
                {
                    if (sceneObject != null)
                        sceneObject.position += movement;
                }
            }
        }

        leftWasPinching = leftPinching;
        rightWasPinching = rightPinching;
        Vector3 handlePosition = transform.position + Vector3.up * 0.14f + pull;
        knob.position = handlePosition;
        Vector3 shaftDirection = handlePosition - transform.position;
        shaft.position = transform.position + shaftDirection * 0.5f;
        shaft.up = shaftDirection.normalized;
        shaft.localScale = new Vector3(0.018f, shaftDirection.magnitude * 0.5f, 0.018f);
    }

    private void TryGrab(Hand hand, Vector3 position)
    {
        Vector3 restPosition = transform.position + Vector3.up * 0.14f;
        if (Vector3.Distance(position, restPosition) > grabDistance)
            return;
        grabbingHand = hand;
        grabPosition = position;
    }

    private static bool ReadPinch(Hand hand, out Vector3 position)
    {
        position = default;
        if (hand == null || !hand.isActiveAndEnabled || !hand.IsTrackedDataValid
            || !hand.IsHighConfidence || !hand.GetFingerIsPinching(HandFinger.Index)
            || !hand.GetJointPose(HandJointId.HandIndexTip, out Pose index)
            || !hand.GetJointPose(HandJointId.HandThumbTip, out Pose thumb))
            return false;
        position = (index.position + thumb.position) * 0.5f;
        return true;
    }

    private void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        if (!focused)
            ResetLever();
    }

    private void OnDisable()
    {
        ResetLever();
    }

    private void ResetLever()
    {
        grabbingHand = null;
        pull = Vector3.zero;
        leftWasPinching = false;
        rightWasPinching = false;
    }

    private void OnDestroy()
    {
        if (leverMaterial != null)
            Destroy(leverMaterial);
    }
}
