using Oculus.Interaction.Input;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SpeakerStandHandTouch : MonoBehaviour
{
    [SerializeField] private SpeakerStand speakerStand;
    [SerializeField] private Hand leftHand;
    [SerializeField] private Hand rightHand;
    [SerializeField, Min(0f), Tooltip("Maximum distance from a tracked hand joint to the stand surface, in metres.")]
    private float handTouchDistance = 0.06f;

    private const float ReleaseMargin = 0.02f;
    private const float ReleaseDelay = 0.15f;
    private Collider[] standSurfaces;
    private bool inContact;
    private bool ownsRecording;
    private float lastContactTime;
    private bool hasFocus;

    private void Awake()
    {
        standSurfaces = GetComponentsInChildren<Collider>(true);
        hasFocus = Application.isFocused;
    }

    private void LateUpdate()
    {
        if (!hasFocus || speakerStand == null)
            return;

        float distance = handTouchDistance + (inContact ? ReleaseMargin : 0f);
        float now = Time.unscaledTime;
        if (TouchesStand(leftHand, OVRInput.Hand.HandLeft, distance)
            || TouchesStand(rightHand, OVRInput.Hand.HandRight, distance))
        {
            lastContactTime = now;
            if (!inContact)
            {
                inContact = true;
                ownsRecording = speakerStand.TryBeginRecording(fromHand: true);
            }
            return;
        }

        if (!inContact || now - lastContactTime < ReleaseDelay)
            return;

        inContact = false;
        if (ownsRecording)
            speakerStand.EndRecording();
        ownsRecording = false;
    }

    private bool TouchesStand(Hand hand, OVRInput.Hand side, float distance)
    {
        if (hand == null || !hand.isActiveAndEnabled || !hand.IsTrackedDataValid || !hand.IsHighConfidence
            || OVRInput.GetControllerIsInHandState(side) == OVRInput.ControllerInHandState.ControllerInHand)
            return false;

        float maxDistanceSquared = distance * distance;
        for (HandJointId joint = HandJointId.HandStart; joint < HandJointId.HandEnd; joint++)
        {
            if (!hand.GetJointPose(joint, out Pose pose))
                continue;

            foreach (Collider surface in standSurfaces)
            {
                if (surface == null || !surface.enabled || !surface.gameObject.activeInHierarchy
                    || (surface is MeshCollider mesh && !mesh.convex))
                    continue;

                Vector3 nearest = surface.ClosestPoint(pose.position);
                if ((nearest - pose.position).sqrMagnitude <= maxDistanceSquared)
                    return true;
            }
        }
        return false;
    }

    private void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        if (!focused)
            CancelOwnedRecording();
    }

    private void OnDisable()
    {
        CancelOwnedRecording();
        inContact = false;
    }

    private void CancelOwnedRecording()
    {
        if (ownsRecording && speakerStand != null)
            speakerStand.CancelRecording();
        ownsRecording = false;
    }
}
