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
        (Instance.IsSpawned && Instance.started.Value && Instance.localConfirmed);

    [SerializeField] private Transform playArea;
    [SerializeField] private OVRCameraRig cameraRig;
    [SerializeField] private RectTransform connectionCanvas;
    [SerializeField] private RectTransform connectionPanel;
    [SerializeField] private GameObject assemblyControls;
    [SerializeField] private Transform[] gameVisualRoots;
    [SerializeField] private Material markerMaterial;
    [SerializeField] private Vector3 gameForward = Vector3.left;
    [SerializeField, Min(0.1f)] private float minimumMarkSpacing = 0.5f;

    private readonly NetworkVariable<bool> started = new(false);
    private readonly NetworkVariable<int> readyCount = new(0);
    private readonly HashSet<ulong> readyClients = new();
    private readonly List<Renderer> gameRenderers = new();
    private readonly List<bool> originalRendererEnabled = new();
    private NetworkManager subscribedManager;
    private OVRDisplay display;
    private TextMeshProUGUI instructions;
    private Button confirmButton;
    private Button selectButton;
    private GameObject calibrationUI;
    private Button[] lobbyButtons;
    private Transform originMarker, directionMarker, cursor;
    private LineRenderer pointerLine;
    private OVRHand[] hands;
    private readonly Dictionary<OVRHand, bool> previousPinches = new();
    private OVRInput.Controller preferredController = OVRInput.Controller.RTouch;
    private bool localConfirmed, hasOrigin, hasDirection, selecting;
    private float acceptInputAfter;
    private Vector3 origin, directionPoint;
    private string selectionError;
    private Vector3 savedCanvasPosition;
    private Quaternion savedCanvasRotation;
    private bool canvasPlaced;
    private bool? lastPlaying;

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
        if (IsServer)
        {
            readyClients.Clear();
            readyCount.Value = 0;
            started.Value = false;
            subscribedManager.OnClientConnectedCallback += ClientConnected;
            subscribedManager.OnClientDisconnectCallback += ClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeNetwork();
        localConfirmed = false;
        selecting = hasOrigin = hasDirection = false;
        lastPlaying = null;
        if (calibrationUI != null) PlaceLobbyInterface();
        SetGameVisible(false);
    }

    private void UnsubscribeNetwork()
    {
        if (subscribedManager == null) return;
        subscribedManager.OnClientConnectedCallback -= ClientConnected;
        subscribedManager.OnClientDisconnectCallback -= ClientDisconnected;
        subscribedManager = null;
    }

    private void ClientConnected(ulong clientId) => RecalculateReadiness();
    private void ClientDisconnected(ulong clientId)
    {
        readyClients.Remove(clientId);
        // Pause immediately, even before Netcode removes the disconnected client.
        started.Value = false;
        readyCount.Value = readyClients.Count;
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void SetReadyRpc(bool ready, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.ContainsKey(clientId)) return;
        if (ready) readyClients.Add(clientId);
        else readyClients.Remove(clientId);
        RecalculateReadiness();
    }

    private void RecalculateReadiness()
    {
        readyCount.Value = readyClients.Count;
        bool allReady = NetworkManager.ConnectedClientsIds.Count >= 2;
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            allReady &= readyClients.Contains(clientId);
        started.Value = allReady;
    }

    private void Update()
    {
        if (display == null && OVRManager.display != null)
        {
            display = OVRManager.display;
            display.RecenteredPose += RestartCalibration;
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
        if (instructions == null) return;
        calibrationUI.SetActive(!playing);
        confirmButton.interactable = IsSpawned && hasDirection && !localConfirmed;
        selectButton.interactable = IsSpawned && !playing;
        instructions.text = StatusMessage();
        bool showOrigin = !playing && selecting && hasOrigin;
        originMarker.gameObject.SetActive(showOrigin);
        directionMarker.gameObject.SetActive(showOrigin && hasDirection);
        UpdatePlacementCursor(!playing && IsSpawned && selecting && !localConfirmed && !hasDirection);
    }

    private void SetGameVisible(bool visible)
    {
        for (int i = 0; i < gameRenderers.Count; i++)
            if (gameRenderers[i] != null)
                gameRenderers[i].enabled = visible && originalRendererEnabled[i];
        if (assemblyControls != null) assemblyControls.SetActive(visible);
    }

    public void RestartCalibration()
    {
        localConfirmed = false;
        hasOrigin = hasDirection = false;
        selecting = IsSpawned;
        selectionError = null;
        acceptInputAfter = Time.unscaledTime + 0.5f;
        if (IsSpawned) SetReadyRpc(false);
        PlaceLobbyInterface();
    }

    private void ConfirmCalibration()
    {
        if (!IsSpawned || !hasDirection || localConfirmed) return;
        Vector3 measuredForward = Vector3.ProjectOnPlane(directionPoint - origin, Vector3.up);
        Vector3 desiredForward = Vector3.ProjectOnPlane(gameForward, Vector3.up);
        if (measuredForward.magnitude < minimumMarkSpacing || desiredForward.sqrMagnitude < 0.001f) return;
        float yaw = Vector3.SignedAngle(measuredForward, desiredForward, Vector3.up);
        Quaternion correction = Quaternion.AngleAxis(yaw, Vector3.up);
        Transform rigRoot = cameraRig.transform;
        // Map the selected origin onto the play-area centre. Transform the rig,
        // not the networked objects, so every peer retains identical game coordinates.
        Vector3 position = playArea.position + correction * (rigRoot.position - origin);
        rigRoot.SetPositionAndRotation(position, correction * rigRoot.rotation);
        Physics.SyncTransforms();
        localConfirmed = true;
        selecting = false;
        connectionCanvas.SetPositionAndRotation(savedCanvasPosition, savedCanvasRotation);
        SetReadyRpc(true);
        Debug.Log($"[CalibrationLobby] Origin confirmed; yaw correction {yaw:F1} degrees.");
    }

    private void UpdatePlacementCursor(bool enabled)
    {
        pointerLine.enabled = false;
        cursor.gameObject.SetActive(false);
        if (!enabled || cameraRig == null) return;
        if (!TryPlacementInput(out Vector3 handPosition, out Ray uiRay, out bool hasUIRay, out bool pressed)) return;

        // Follow the tracked hand/controller horizontally. Its height and aim do
        // not affect the dot: both marks stay on the same tracking-floor plane.
        Vector3 point = new Vector3(handPosition.x, playArea.position.y, handPosition.z);
        Vector3 visiblePoint = point + Vector3.up * 0.015f;
        pointerLine.enabled = true;
        pointerLine.SetPosition(0, handPosition);
        pointerLine.SetPosition(1, visiblePoint);
        cursor.gameObject.SetActive(true);
        cursor.position = visiblePoint;
        if (!pressed || Time.unscaledTime < acceptInputAfter || (hasUIRay && HitsInterface(uiRay))) return;
        if (!hasOrigin)
        {
            origin = point;
            hasOrigin = true;
            originMarker.position = visiblePoint;
        }
        else if (Vector3.Distance(origin, point) < minimumMarkSpacing)
            selectionError = "Direction mark is too close. Move your hand farther away.";
        else
        {
            directionPoint = point;
            hasDirection = true;
            directionMarker.position = visiblePoint;
            selectionError = null;
        }
        acceptInputAfter = Time.unscaledTime + 0.4f;
    }

    private bool TryPlacementInput(out Vector3 position, out Ray uiRay, out bool hasUIRay, out bool pressed)
    {
        position = default;
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
                position = anchor.position;
                uiRay = new Ray(anchor.position, anchor.forward);
                hasUIRay = true;
                pressed = controller == OVRInput.Controller.RTouch ? rightPressed : leftPressed;
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
            }
        }
        if (placementHand == null) return false;
        Transform handAnchor = placementHand.GetHand() == OVRPlugin.Hand.HandLeft
            ? cameraRig.leftHandAnchor : cameraRig.rightHandAnchor;
        if (handAnchor == null) return false;
        position = handAnchor.position;
        hasUIRay = placementHand.IsPointerPoseValid;
        if (hasUIRay) uiRay = new Ray(placementHand.PointerPose.position, placementHand.PointerPose.forward);
        return true;
    }

    private bool HitsInterface(Ray ray)
    {
        // A pinch aimed at a lobby button should only activate that button.
        Plane plane = new Plane(connectionCanvas.forward, connectionCanvas.position);
        if (!plane.Raycast(ray, out float distance) || distance >= 4f || lobbyButtons == null) return false;
        Vector3 hit = ray.GetPoint(distance);
        foreach (Button button in lobbyButtons)
        {
            if (button == null || !button.isActiveAndEnabled || !button.interactable) continue;
            var rect = button.transform as RectTransform;
            if (rect != null && rect.rect.Contains((Vector2)rect.InverseTransformPoint(hit))) return true;
        }
        return false;
    }

    private string StatusMessage()
    {
        if (!IsSpawned) return "LOBBY\nChoose Host or Join above.\nBoth players must use the SAME two physical floor marks.";
        if (localConfirmed) return $"POSITION CONFIRMED\nReady: {readyCount.Value}/2 players\nWaiting for both players to confirm...";
        if (!selecting) return "CALIBRATION\nPress Set floor marks. Use the SAME origin and direction marks on both headsets.";
        if (hasDirection) return "CHECK YOUR MARKS\nBoth floor points selected.\nPress Confirm position, or Set floor marks to retry.";
        return (hasOrigin
            ? "2. SELECT DIRECTION\nMove your right hand over the second floor mark, at least 50 cm away. Pinch to place."
            : "1. SELECT ORIGIN\nMove your right hand over the shared centre mark. The dot stays at floor height. Pinch to place.") +
            (string.IsNullOrEmpty(selectionError) ? "" : "\n" + selectionError);
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
        connectionCanvas.sizeDelta = new Vector2(600f, 700f);
        connectionPanel.anchoredPosition = new Vector2(0f, 180f);
        calibrationUI = new GameObject("Calibration Controls", typeof(RectTransform));
        calibrationUI.transform.SetParent(connectionCanvas, false);
        instructions = CreateText(calibrationUI.transform, "Calibration Instructions", new Vector2(0f, -30f), new Vector2(520f, 185f));
        instructions.fontSize = 23f;
        confirmButton = CreateButton("Confirm position", new Vector2(0f, -175f), ConfirmCalibration);
        selectButton = CreateButton("Set floor marks / Retry", new Vector2(0f, -245f), RestartCalibration);
        lobbyButtons = connectionCanvas.GetComponentsInChildren<Button>(true);
    }

    private TextMeshProUGUI CreateText(Transform parent, string label, Vector2 position, Vector2 size)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = connectionCanvas.gameObject.layer;
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.rectTransform.anchoredPosition = position;
        text.rectTransform.sizeDelta = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateButton(string label, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.layer = connectionCanvas.gameObject.layer;
        go.transform.SetParent(calibrationUI.transform, false);
        var rect = go.GetComponent<RectTransform>();
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
        base.OnDestroy();
    }
}