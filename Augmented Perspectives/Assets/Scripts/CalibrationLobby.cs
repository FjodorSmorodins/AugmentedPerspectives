using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

// Shares readiness over Netcode. Calibration itself stays local: every headset maps
// the same two physical floor marks onto the same game-space origin and direction.
[DefaultExecutionOrder(-200)]
public class CalibrationLobby : NetworkBehaviour
{
    public static CalibrationLobby Instance { get; private set; }
    public static bool CanPlay => Instance == null ||
        (Instance.IsSpawned && Instance.checkpoint.Value.Progress == CalibrationGuide.Progress.Confirmed &&
         Instance.localConfirmed && Instance.layoutApplied);

    [SerializeField] private NetworkLauncher networkLauncher;
    [SerializeField] private Transform playArea;
    [SerializeField] private OVRCameraRig cameraRig;
    [SerializeField] private RectTransform connectionCanvas;
    [SerializeField] private RectTransform connectionPanel;
    [SerializeField] private GameObject assemblyControls;
    [SerializeField] private Transform[] gameVisualRoots;
    [SerializeField] private Material markerMaterial;
    [SerializeField] private Vector3 gameForward = Vector3.left;

    public struct Checkpoint : INetworkSerializable, IEquatable<Checkpoint>
    {
        public CalibrationGuide.Progress Progress;
        public uint Revision;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Progress);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(Checkpoint other) => Progress == other.Progress && Revision == other.Revision;
    }

    private readonly NetworkVariable<Checkpoint> checkpoint = new(default);
    private readonly CalibrationSession session = new();
    private bool layoutApplied;
    private const float FirstPointHoldSeconds = 3f;
    private Transform holdSource;
    private float holdStarted;
    private readonly List<Renderer> gameRenderers = new();
    private readonly List<bool> originalRendererEnabled = new();
    private NetworkManager subscribedManager;
    private OVRDisplay display;
    private readonly CalibrationGuide guide = new();
    private CalibrationInstructions guidance;
    private GameplayGuide gameplayGuide;
    private CalibrationLayoutPreview layoutPreview;
    private bool localConfirmed => guide.Confirmed;
    private bool hasOrigin => guide.HasOrigin;
    private bool hasDirection => guide.HasDirection;
    private bool selecting => guide.Selecting;
    private Vector3 layoutForward;
    private Transform originMarker, directionMarker, cursor;
    private LineRenderer pointerLine;
    private OVRHand[] hands;
    private readonly Dictionary<OVRHand, bool> previousPinches = new();
    private OVRInput.Controller preferredController = OVRInput.Controller.RTouch;
    private float acceptInputAfter;
    private Vector3 origin, directionPoint;
    private string selectionError;
    private Vector3 savedCanvasPosition;
    private Quaternion savedCanvasRotation;
    private bool canvasPlaced;
    private bool? lastPlaying;
    private Vector3 previewCursor;
    private bool previewCursorVisible;
    private bool replicatedPlacedMarkers;

    private void Awake()
    {
        Instance = this;
        foreach (Transform root in gameVisualRoots)
        {
            if (root == null) continue;
            foreach (Renderer visual in root.GetComponentsInChildren<Renderer>(true))
            {
                gameRenderers.Add(visual);
                originalRendererEnabled.Add(visual.enabled);
                visual.enabled = false;
            }
        }
        if (assemblyControls != null) assemblyControls.SetActive(false);
    }

    private IEnumerator Start()
    {
        if (cameraRig == null) cameraRig = FindFirstObjectByType<OVRCameraRig>();
        hands = FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        savedCanvasPosition = connectionCanvas.position;
        savedCanvasRotation = connectionCanvas.rotation;
        CreateInterface();
        CreatePointer();
        while (Application.isPlaying)
        {
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if ((Application.isEditor && !XRSettings.isDeviceActive) ||
                (head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)) break;
            yield return null;
        }
        yield return null;
        PlaceLobbyInterface();
    }

    public override void OnNetworkSpawn()
    {
        subscribedManager = NetworkManager;
        ResetGuide();
        gameplayGuide?.ResetSession();
        if (IsServer)
        {
            session.Reset();
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds) session.Connect(clientId);
            PublishCheckpoint();
            subscribedManager.OnClientConnectedCallback += ClientConnected;
            subscribedManager.OnClientDisconnectCallback += ClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeNetwork();
        ResetGuide();
        lastPlaying = null;
        if (guidance != null) PlaceLobbyInterface();
        SetGameVisible(false);
    }

    private void UnsubscribeNetwork()
    {
        if (subscribedManager == null) return;
        subscribedManager.OnClientConnectedCallback -= ClientConnected;
        subscribedManager.OnClientDisconnectCallback -= ClientDisconnected;
        subscribedManager = null;
    }

    private void ClientConnected(ulong clientId)
    {
        session.Connect(clientId);
        PublishCheckpoint();
    }

    private void ClientDisconnected(ulong clientId)
    {
        session.Disconnect(clientId);
        PublishCheckpoint();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetProgressRpc(CalibrationGuide.Progress progress, uint revision, RpcParams rpcParams = default)
    {
        if (session.SetProgress(rpcParams.Receive.SenderClientId, progress, revision)) PublishCheckpoint();
    }

    private void PublishCheckpoint()
    {
        checkpoint.Value = new Checkpoint { Progress = session.SharedProgress, Revision = session.Revision };
    }

    private void SyncGuide()
    {
        Checkpoint current = checkpoint.Value;
        var previous = guide.Step;
        guide.UpdateSharedProgress(current.Progress, current.Revision);
        if (guide.Step != previous)
        {
            holdSource = null;
            selectionError = null;
            acceptInputAfter = Time.unscaledTime + 0.5f;
            LogGuideStep();
        }
    }

    private void Update()
    {
        if (display == null && OVRManager.display != null)
        {
            display = OVRManager.display;
            display.RecenteredPose += RestartCalibration;
        }
        bool connected = IsSpawned && !NetworkManager.ShutdownInProgress;
        if (connected)
        {
            SyncGuide();
            if (!layoutApplied && localConfirmed && checkpoint.Value.Progress == CalibrationGuide.Progress.Confirmed)
                ApplyAlignment();
        }
        bool playing = CanPlay;
        if (lastPlaying != playing)
        {
            lastPlaying = playing;
            SetGameVisible(playing);
            if (playing)
            {
                connectionCanvas.SetPositionAndRotation(savedCanvasPosition, savedCanvasRotation);
                Debug.Log("[CalibrationLobby] Both players ready. Assembly started.");
            }
            else if (canvasPlaced) PlaceLobbyInterface();
        }
        if (guidance == null) return;
        // The gameplay guide replaces Host/Join once the session has started.
        connectionPanel.gameObject.SetActive(!connected);
        gameplayGuide.Show(playing);
        UpdatePlacementCursor(connected && !playing && selecting);
        guidance.Show(guide, connected && !playing, connected && !playing && Time.unscaledTime >= acceptInputAfter,
            selectionError);
        bool showOrigin = !replicatedPlacedMarkers && connected && !playing && hasOrigin;
        originMarker.gameObject.SetActive(showOrigin);
        directionMarker.gameObject.SetActive(showOrigin && hasDirection);
        bool followingFirstDot = guide.Step == CalibrationGuide.Stage.PlaceFirst && previewCursorVisible;
        layoutPreview.Show(connected && !playing && (hasOrigin || followingFirstDot),
            followingFirstDot ? previewCursor : origin, layoutForward);
    }

    private void SetGameVisible(bool visible)
    {
        for (int i = 0; i < gameRenderers.Count; i++)
            if (gameRenderers[i] != null)
                gameRenderers[i].enabled = visible && originalRendererEnabled[i];
        if (assemblyControls != null) assemblyControls.SetActive(visible);
    }

    public void CancelLocalCalibration()
    {
        ResetGuide();
        lastPlaying = null;
        if (guidance != null) PlaceLobbyInterface();
        SetGameVisible(false);
    }

    private void ResetGuide()
    {
        guide.Restart();
        layoutApplied = false;
        holdSource = null;
        LogGuideStep();
        previewCursorVisible = false;
        selectionError = null;
        acceptInputAfter = Time.unscaledTime + 0.5f;
        layoutPreview?.Hide();
    }

    private void DisconnectOrCancel()
    {
        if (networkLauncher == null) networkLauncher = FindFirstObjectByType<NetworkLauncher>();
        if (networkLauncher != null) networkLauncher.StopConnection();
        else Debug.LogError("[CalibrationLobby] NetworkLauncher is missing; cannot disconnect.");
    }

    public void RestartCalibration()
    {
        ResetGuide();
        if (IsSpawned && !NetworkManager.ShutdownInProgress)
            SetProgressRpc(guide.LocalProgress, checkpoint.Value.Revision);
        PlaceLobbyInterface();
        SetGameVisible(false);
    }

    private void LogGuideStep()
    {
        Debug.Log($"[CalibrationInstructions] Step={guide.Step}, first={hasOrigin}, second={hasDirection}, ready={localConfirmed}");
    }

    private bool AcceptGuideAction()
    {
        if (!IsSpawned || NetworkManager.ShutdownInProgress ||
            checkpoint.Value.Progress == CalibrationGuide.Progress.Confirmed) return false;
        SyncGuide();
        if (Time.unscaledTime < acceptInputAfter) return false;
        // One action per pinch, including duplicate Meta UI clicks and floor input.
        acceptInputAfter = Time.unscaledTime + 0.5f;
        selectionError = null;
        return true;
    }

    private void GuidePrimary()
    {
        if (!AcceptGuideAction() || !guide.Advance()) return;
        PrepareFirstDotPreview();
        if (localConfirmed) SetProgressRpc(guide.LocalProgress, checkpoint.Value.Revision);
        LogGuideStep();
    }

    private void ChangePosition()
    {
        if (!AcceptGuideAction() || !guide.ChangePosition()) return;
        layoutApplied = false;
        holdSource = null;
        previewCursorVisible = false;
        PrepareFirstDotPreview();
        SetProgressRpc(guide.LocalProgress, checkpoint.Value.Revision);
        LogGuideStep();
    }

    private void ChangeRotation()
    {
        if (!AcceptGuideAction() || !guide.ChangeRotation()) return;
        layoutApplied = false;
        SetProgressRpc(guide.LocalProgress, checkpoint.Value.Revision);
        LogGuideStep();
    }

    private void PrepareFirstDotPreview()
    {
        if (guide.Step != CalibrationGuide.Stage.PlaceFirst) return;
        Vector3 headForward = cameraRig != null && cameraRig.centerEyeAnchor != null
            ? cameraRig.centerEyeAnchor.forward : gameForward;
        layoutForward = Vector3.ProjectOnPlane(headForward, Vector3.up);
        if (layoutForward.sqrMagnitude < 0.001f)
            layoutForward = Vector3.ProjectOnPlane(gameForward, Vector3.up);
        // Capture the current scene and keep its facing steady while the player
        // slides it around. The same preview becomes fixed when dot 1 is placed.
        layoutPreview.Rebuild();
    }

    private void ApplyAlignment()
    {
        if (cameraRig == null) return;
        Vector3 desiredForward = Vector3.ProjectOnPlane(gameForward, Vector3.up);
        if (desiredForward.sqrMagnitude < 0.001f) return;
        float yaw = Vector3.SignedAngle(layoutForward, desiredForward, Vector3.up);
        Quaternion correction = Quaternion.AngleAxis(yaw, Vector3.up);
        Transform rigRoot = cameraRig.transform;
        // Map the selected origin onto the play-area centre. Transform the rig,
        // not the networked objects, so every peer retains identical game coordinates.
        Vector3 position = playArea.position + correction * (rigRoot.position - origin);
        rigRoot.SetPositionAndRotation(position, correction * rigRoot.rotation);
        directionPoint = playArea.position + correction * (directionPoint - origin);
        origin = playArea.position;
        originMarker.position = origin + Vector3.up * 0.015f;
        directionMarker.position = directionPoint + Vector3.up * 0.015f;
        Physics.SyncTransforms();
        layoutForward = correction * layoutForward;
        layoutApplied = true;
        Debug.Log($"[CalibrationLobby] Origin confirmed; yaw correction {yaw:F1} degrees.");
    }

    private void UpdatePlacementCursor(bool enabled)
    {
        previewCursorVisible = false;
        pointerLine.enabled = false;
        cursor.gameObject.SetActive(false);
        if (!enabled || cameraRig == null)
        {
            holdSource = null;
            return;
        }
        if (!TryPlacementInput(out Transform inputSource, out Ray uiRay, out bool hasUIRay, out bool pressed, out bool held))
        {
            holdSource = null;
            selectionError = "Raise your hand or controller to show the dot.";
            return;
        }

        // Follow the tracked hand/controller horizontally. Its height and aim do
        // not affect the dot: both marks stay on the same tracking-floor plane.
        Vector3 handPosition = inputSource.position;
        Vector3 point = new Vector3(handPosition.x, playArea.position.y, handPosition.z);
        Vector3 visiblePoint = point + Vector3.up * 0.015f;
        previewCursor = point;
        previewCursorVisible = true;
        pointerLine.enabled = true;
        pointerLine.SetPosition(0, handPosition);
        pointerLine.SetPosition(1, visiblePoint);
        cursor.gameObject.SetActive(true);
        cursor.position = visiblePoint;
        bool overInterface = hasUIRay && HitsInterface(uiRay);
        bool placingFirst = guide.Step == CalibrationGuide.Stage.PlaceFirst;
        Vector3 offset = Vector3.ProjectOnPlane(point - origin, Vector3.up);
        float distance = offset.magnitude;
        bool canPlace = placingFirst || distance > 0f;
        // Normalize explicitly so Unity's normalization epsilon does not erase a short offset.
        if (!placingFirst && canPlace && !overInterface)
            layoutForward = offset / distance;
        selectionError = null;
        if (Time.unscaledTime < acceptInputAfter || overInterface || !canPlace)
        {
            holdSource = null;
            return;
        }
        if (placingFirst)
        {
            if (pressed)
            {
                holdSource = inputSource;
                holdStarted = Time.unscaledTime;
            }
            if (!held || holdSource != inputSource)
            {
                holdSource = null;
                return;
            }
            float remaining = FirstPointHoldSeconds - (Time.unscaledTime - holdStarted);
            if (remaining > 0f)
            {
                selectionError = $"Keep holding ({Mathf.CeilToInt(remaining)} s)";
                return;
            }
            holdSource = null;
            origin = point;
            originMarker.position = visiblePoint;
        }
        else
        {
            if (!pressed) return;
            directionPoint = point;
            directionMarker.position = visiblePoint;
        }
        guide.PlacePoint();
        SetProgressRpc(guide.LocalProgress, checkpoint.Value.Revision);
        LogGuideStep();
        previewCursorVisible = false;
        acceptInputAfter = Time.unscaledTime + 0.5f;
        pointerLine.enabled = false;
        cursor.gameObject.SetActive(false);
    }

    // Share floor coordinates with the calibration diagnostics component.
    public CalibrationPreview.State CapturePreview()
    {
        return new CalibrationPreview.State
        {
            Present = IsSpawned,
            Origin = origin,
            Direction = directionPoint,
            Cursor = previewCursor,
            HasOrigin = hasOrigin,
            HasDirection = hasDirection,
            CursorVisible = previewCursorVisible && selecting && !localConfirmed,
            Confirmed = localConfirmed
        };
    }

    public void SetReplicatedPlacedMarkers(bool enabled)
    {
        replicatedPlacedMarkers = enabled;
        if (!enabled) return;
        if (originMarker != null) originMarker.gameObject.SetActive(false);
        if (directionMarker != null) directionMarker.gameObject.SetActive(false);
    }

    public void SetCalibrationMarkerMaterial(Material material)
    {
        markerMaterial = material;
        if (originMarker != null) originMarker.GetComponent<Renderer>().sharedMaterial = material;
        if (directionMarker != null) directionMarker.GetComponent<Renderer>().sharedMaterial = material;
        if (cursor != null) cursor.GetComponent<Renderer>().sharedMaterial = material;
        if (pointerLine != null) pointerLine.sharedMaterial = material;
    }

    private bool TryPlacementInput(out Transform inputSource, out Ray uiRay, out bool hasUIRay, out bool pressed, out bool held)
    {
        inputSource = null;
        held = false;
        uiRay = default;
        hasUIRay = false;
        pressed = false;
        bool rightPressed = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        bool leftPressed = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        if (rightPressed) preferredController = OVRInput.Controller.RTouch;
        else if (leftPressed) preferredController = OVRInput.Controller.LTouch;
        bool rightTracked = OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch);
        bool leftTracked = OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch);
        if (rightTracked || leftTracked)
        {
            OVRInput.Controller controller = preferredController;
            if (!rightTracked) controller = OVRInput.Controller.LTouch;
            else if (!leftTracked) controller = OVRInput.Controller.RTouch;
            Transform anchor = controller == OVRInput.Controller.RTouch
                ? cameraRig.rightControllerAnchor : cameraRig.leftControllerAnchor;
            if (anchor != null)
            {
                inputSource = anchor;
                uiRay = new Ray(anchor.position, anchor.forward);
                hasUIRay = true;
                pressed = controller == OVRInput.Controller.RTouch ? rightPressed : leftPressed;
                held = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, controller);
                return true;
            }
        }

        // Prefer the right hand and keep the cursor on it while it is tracked.
        // Pinching with the other hand must not teleport and confirm the cursor.
        OVRHand placementHand = null;
        if (hands != null) foreach (OVRHand hand in hands)
        {
            if (hand == null) continue;
            bool valid = hand.isActiveAndEnabled && hand.IsTracked && !hand.IsSystemGestureInProgress;
            bool pinch = valid && hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            previousPinches.TryGetValue(hand, out bool wasPinching);
            bool down = pinch && !wasPinching;
            previousPinches[hand] = pinch;
            if (valid && (placementHand == null ||
                (hand.GetHand() == OVRPlugin.Hand.HandRight && placementHand.GetHand() != OVRPlugin.Hand.HandRight)))
            {
                placementHand = hand;
                pressed = down;
                held = pinch;
            }
        }
        if (placementHand == null) return false;
        Transform handAnchor = placementHand.GetHand() == OVRPlugin.Hand.HandLeft
            ? cameraRig.leftHandAnchor : cameraRig.rightHandAnchor;
        if (handAnchor == null) return false;
        inputSource = handAnchor;
        hasUIRay = placementHand.IsPointerPoseValid;
        if (hasUIRay) uiRay = new Ray(placementHand.PointerPose.position, placementHand.PointerPose.forward);
        return true;
    }

    private bool HitsInterface(Ray ray)
    {
        // A pinch aimed anywhere at the screen is a UI gesture, not a floor dot.
        Plane plane = new Plane(connectionCanvas.forward, connectionCanvas.position);
        if (!plane.Raycast(ray, out float distance) || distance >= 4f) return false;
        Vector3 hit = ray.GetPoint(distance);
        return connectionCanvas.rect.Contains((Vector2)connectionCanvas.InverseTransformPoint(hit));
    }

    private void PlaceLobbyInterface()
    {
        if (cameraRig == null || connectionCanvas == null || cameraRig.centerEyeAnchor == null) return;
        Transform head = cameraRig.centerEyeAnchor;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
        connectionCanvas.SetPositionAndRotation(head.position + forward * 0.65f - Vector3.up * 0.2f,
            Quaternion.LookRotation(forward, Vector3.up));
        canvasPlaced = true;
    }

    private void CreateInterface()
    {
        connectionCanvas.sizeDelta = new Vector2(600f, 850f);
        connectionPanel.anchoredPosition = new Vector2(0f, 180f);
        guidance = gameObject.AddComponent<CalibrationInstructions>();
        guidance.Initialize(connectionCanvas, GuidePrimary, ChangePosition, ChangeRotation);
        gameplayGuide = gameObject.AddComponent<GameplayGuide>();
        gameplayGuide.Initialize(connectionCanvas, GetComponent<AssemblyManager>());
        layoutPreview = gameObject.AddComponent<CalibrationLayoutPreview>();
        layoutPreview.Initialize(playArea, gameVisualRoots, gameForward,
            cameraRig != null ? cameraRig.centerEyeAnchor : null, markerMaterial);
        CreateButton("Disconnect / Cancel", new Vector2(0f, -380f), DisconnectOrCancel, connectionCanvas);
    }

    private TextMeshProUGUI CreateText(Transform parent, string label, Vector2 position, Vector2 size)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = connectionCanvas.gameObject.layer;
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        text.rectTransform.anchoredPosition = position;
        text.rectTransform.sizeDelta = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateButton(string label, Vector2 position, UnityEngine.Events.UnityAction action, Transform parent = null)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.layer = connectionCanvas.gameObject.layer;
        go.transform.SetParent(parent != null ? parent : connectionCanvas, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(330f, 52f);
        var image = go.GetComponent<Image>();
        image.color = new Color(0.06f, 0.25f, 0.32f, 0.95f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        var text = CreateText(go.transform, label + " Label", Vector2.zero, rect.sizeDelta);
        text.text = label;
        text.fontSize = 23f;
        return button;
    }

    private void CreatePointer()
    {
        originMarker = CreateMarker("Calibration Origin");
        directionMarker = CreateMarker("Calibration Direction");
        cursor = CreateMarker("Hand Projected Calibration Dot");
        var line = new GameObject("Calibration Vertical Guide");
        line.transform.SetParent(transform, false);
        pointerLine = line.AddComponent<LineRenderer>();
        pointerLine.sharedMaterial = markerMaterial;
        pointerLine.positionCount = 2;
        pointerLine.startWidth = pointerLine.endWidth = 0.003f;
        pointerLine.useWorldSpace = true;
        pointerLine.enabled = false;
        pointerLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private Transform CreateMarker(string label)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = label;
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * 0.035f;
        Collider collider = go.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        go.GetComponent<Renderer>().sharedMaterial = markerMaterial;
        go.SetActive(false);
        return go.transform;
    }

    public override void OnDestroy()
    {
        UnsubscribeNetwork();
        if (display != null) display.RecenteredPose -= RestartCalibration;
        if (Instance == this) Instance = null;
        if (guidance != null) Destroy(guidance);
        if (gameplayGuide != null) Destroy(gameplayGuide);
        if (layoutPreview != null) Destroy(layoutPreview);
        base.OnDestroy();
    }
}
