using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using XRDevice = UnityEngine.XR.InputDevice;
using XRUsages = UnityEngine.XR.CommonUsages;

public sealed class SpeakerStandInput : MonoBehaviour
{
    [SerializeField] private Transform trackingOrigin;
    [SerializeField] private float interactionDistance = 8f;

    private SpeakerStand mouseStand;
    private SpeakerStand leftStand;
    private SpeakerStand rightStand;
    private SpeakerStand highlightedLeft;
    private SpeakerStand highlightedRight;
    private bool leftWasPressed;
    private bool rightWasPressed;

    private void Update()
    {
        UpdateMouse();
        UpdateTrigger(XRNode.LeftHand, XRController.leftHand, ref leftStand, ref leftWasPressed, out SpeakerStand leftAim);
        UpdateTrigger(XRNode.RightHand, XRController.rightHand, ref rightStand, ref rightWasPressed, out SpeakerStand rightAim);

        SetAimHighlights(leftAim, rightAim);
    }

    private void UpdateMouse()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            Cancel(ref mouseStand);
            return;
        }
        if (mouse.leftButton.wasPressedThisFrame && Camera.main != null)
            Press(ref mouseStand, FindStand(Camera.main.ScreenPointToRay(mouse.position.ReadValue())));
        if (mouse.leftButton.wasReleasedThisFrame)
            Release(ref mouseStand);
    }

    private void UpdateTrigger(XRNode hand, XRController controller, ref SpeakerStand stand,
        ref bool wasPressed, out SpeakerStand aimedAt)
    {
        if (!TryReadController(hand, controller, wasPressed, out bool pressed, out Ray ray))
        {
            Cancel(ref stand);
            aimedAt = null;
            wasPressed = false;
            return;
        }

        if (trackingOrigin != null)
        {
            ray = new Ray(trackingOrigin.TransformPoint(ray.origin),
                trackingOrigin.TransformDirection(ray.direction));
        }

        aimedAt = FindStand(ray);
        if (pressed && !wasPressed)
            Press(ref stand, aimedAt);
        if (!pressed && wasPressed)
            Release(ref stand);

        wasPressed = pressed;
    }

    private static bool TryReadController(XRNode hand, XRController controller, bool wasPressed,
        out bool pressed, out Ray ray)
    {
        if (controller != null && controller.isTracked.isPressed
            && controller.TryGetChildControl<AxisControl>("trigger") is AxisControl trigger
            && controller.TryGetChildControl<Vector3Control>("pointerPosition") is Vector3Control pointerPosition
            && controller.TryGetChildControl<QuaternionControl>("pointerRotation") is QuaternionControl pointerRotation)
        {
            Quaternion aim = pointerRotation.ReadValue();
            if (Quaternion.Dot(aim, aim) > 0.5f)
            {
                pressed = trigger.ReadValue() >= (wasPressed ? 0.35f : 0.55f);
                ray = new Ray(pointerPosition.ReadValue(), aim * Vector3.forward);
                return true;
            }
        }

        XRDevice device = InputDevices.GetDeviceAtXRNode(hand);
        ray = default;
        if (!device.TryGetFeatureValue(XRUsages.triggerButton, out pressed)
            || !device.TryGetFeatureValue(XRUsages.devicePosition, out Vector3 position)
            || !device.TryGetFeatureValue(XRUsages.deviceRotation, out Quaternion rotation))
            return false;

        ray = new Ray(position, rotation * Vector3.forward);
        return true;
    }

    private SpeakerStand FindStand(Ray ray)
    {
        return Physics.Raycast(ray, out RaycastHit hit, interactionDistance, ~0, QueryTriggerInteraction.Collide)
            ? hit.collider.GetComponent<SpeakerStand>()
            : null;
    }

    private void SetAimHighlights(SpeakerStand left, SpeakerStand right)
    {
        if (left == highlightedLeft && right == highlightedRight)
            return;

        if (highlightedLeft != null && highlightedLeft != left && highlightedLeft != right)
            highlightedLeft.SetAimedAt(false);
        if (highlightedRight != null && highlightedRight != left && highlightedRight != right)
            highlightedRight.SetAimedAt(false);
        if (left != null)
            left.SetAimedAt(true);
        if (right != null)
            right.SetAimedAt(true);
        highlightedLeft = left;
        highlightedRight = right;
    }

    private static void Press(ref SpeakerStand stand, SpeakerStand target)
    {
        stand = target;
        if (stand != null && !stand.TryBeginRecording())
            stand = null;
    }

    private static void Release(ref SpeakerStand stand)
    {
        if (stand != null)
            stand.EndRecording();
        stand = null;
    }

    private static void Cancel(ref SpeakerStand stand)
    {
        if (stand != null)
            stand.CancelRecording();
        stand = null;
    }

    private void OnDisable()
    {
        Cancel(ref mouseStand);
        Cancel(ref leftStand);
        Cancel(ref rightStand);
        SetAimHighlights(null, null);
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
            OnDisable();
    }
}
