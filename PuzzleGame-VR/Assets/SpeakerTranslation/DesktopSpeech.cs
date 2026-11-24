using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(SpeakerStand), typeof(AudioSource))]
public sealed class DesktopSpeech : MonoBehaviour
{
    [SerializeField] private bool useOpenAI;

    private SpeakerStand stand;
    private bool ownsRecording;
    private bool wasHeld;
    private Vector2 scroll;

    private void Awake()
    {
        stand = GetComponent<SpeakerStand>();
        string endpoint = useOpenAI
            ? "http://127.0.0.1:8766/translate"
            : "http://127.0.0.1:8765/translate";
        AudioSource speaker = GetComponent<AudioSource>();
        speaker.playOnAwake = false;
        speaker.spatialBlend = 0;
        stand.ConfigureDesktop(endpoint, speaker);

        var view = new GameObject("Desktop view");
        view.transform.SetParent(transform, false);
        Camera camera = view.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.10f, 0.14f);
        camera.cullingMask = 0;
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        view.AddComponent<AudioListener>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (!Application.isFocused || keyboard == null)
        {
            CancelRecording();
            return;
        }

        bool held = keyboard.backquoteKey.isPressed
            || (keyboard.rightAltKey.isPressed && keyboard.rightBracketKey.isPressed
                && keyboard.rightBracketKey.displayName == "+");
        if (held && !wasHeld)
            ownsRecording = stand.TryBeginRecording();
        if (!held && wasHeld && ownsRecording)
        {
            ownsRecording = false;
            stand.EndRecording();
        }
        wasHeld = held;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
            CancelRecording();
    }

    private void OnDisable()
    {
        CancelRecording();
    }

    private void CancelRecording()
    {
        if (ownsRecording)
            stand.CancelRecording();
        ownsRecording = false;
    }

    private void OnGUI()
    {
        if (stand == null)
            return;

        var title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
        var caption = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true, richText = false };
        float width = Mathf.Min(Screen.width - 32, 680);
        GUILayout.BeginArea(new Rect((Screen.width - width) / 2, 24, width, Screen.height - 48));
        GUILayout.Label(useOpenAI ? "OpenAI speech translation" : "Local speech translation", title);
        GUILayout.Label(stand.Status, caption);
        GUILayout.Space(16);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label(stand.Caption, caption);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
