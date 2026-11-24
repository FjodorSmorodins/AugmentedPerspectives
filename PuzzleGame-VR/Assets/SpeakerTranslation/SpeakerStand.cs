using System;
using System.Collections;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public sealed class SpeakerStand : MonoBehaviour
{
    [SerializeField] private Text caption;
    [SerializeField] private Text status;
    [SerializeField] private AudioSource speaker;
    [SerializeField] private string endpoint = "http://10.89.8.131:8765/translate";

    public string Caption { get; private set; }
    public string Status { get; private set; }

    private const string Ready = "Touch the stand or hold the sphere to speak";
    private string readyMessage = Ready;
    private string shortRecordingMessage = "Hold the sphere while you speak.";
    private const int MaxRecordingSeconds = 12;
    private static readonly HttpClient Bridge = new HttpClient(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(150)
    };
    private static SpeakerStand activeStand;
    private static SpeakerStand playingStand;
    private static event Action<string> TranslationReceived;

    private AudioClip recording;
    private AudioClip synthesizedClip;
    private string microphone;
    private float startedAt;
    private bool recordingByHand;
    private int recordedFrames = -1;
    private Coroutine pendingTranslation;
    private CancellationTokenSource cancellation;
    private Renderer buttonRenderer;
    private MaterialPropertyBlock originalButtonProperties;
    private MaterialPropertyBlock highlightedButtonProperties;

    private void OnEnable()
    {
        TranslationReceived += ShowCaption;
        SetStatus(readyMessage);
    }

    internal void ConfigureDesktop(string bridgeEndpoint, AudioSource audioSource)
    {
        endpoint = bridgeEndpoint;
        speaker = audioSource;
        readyMessage = "Hold ` / ~ to speak. Release to translate.";
        shortRecordingMessage = "Hold ` / ~ while you speak.";
        SetStatus(readyMessage);
    }

    private void Update()
    {
        if (recording == null || recordedFrames >= 0
            || Time.realtimeSinceStartup - startedAt < MaxRecordingSeconds)
            return;

        StopCapture();
        SetStatus(recordingByHand
            ? "Recording limit reached. Move your hand away to translate."
            : "Recording limit reached. Release to translate.");
    }

    public void BeginRecording()
    {
        TryBeginRecording();
    }

    public bool TryBeginRecording(bool fromHand = false)
    {
        if (!isActiveAndEnabled || activeStand != null)
            return false;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            SetStatus("Allow microphone access, then try again.");
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            return false;
        }
#endif

        string[] devices = Microphone.devices;
        if (devices.Length == 0)
        {
            SetStatus("No microphone available. Check app microphone access.");
            return false;
        }

        if (playingStand != null)
            playingStand.StopPlayback();

        microphone = devices[0];
        Microphone.GetDeviceCaps(microphone, out int minimumRate, out int maximumRate);
        int rate = Mathf.Clamp(16000, Mathf.Max(1, minimumRate), maximumRate > 0 ? maximumRate : 48000);
        recording = Microphone.Start(microphone, true, MaxRecordingSeconds + 2, rate);
        if (recording == null)
        {
            SetStatus("Could not open the microphone.");
            return false;
        }

        activeStand = this;
        recordingByHand = fromHand;
        recordedFrames = -1;
        startedAt = Time.realtimeSinceStartup;
        SetStatus(fromHand ? "Listening… move your hand away to translate" : "Listening… release to translate");
        return true;
    }

    public void SetAimedAt(bool aimedAt)
    {
        if (highlightedButtonProperties == null)
        {
            buttonRenderer = GetComponent<Renderer>();
            if (buttonRenderer == null)
                return;

            originalButtonProperties = new MaterialPropertyBlock();
            buttonRenderer.GetPropertyBlock(originalButtonProperties);
            highlightedButtonProperties = new MaterialPropertyBlock();
            buttonRenderer.GetPropertyBlock(highlightedButtonProperties);
            Color highlight = new Color(0.15f, 0.9f, 1f);
            highlightedButtonProperties.SetColor("_BaseColor", highlight);
            highlightedButtonProperties.SetColor("_Color", highlight);
        }

        buttonRenderer.SetPropertyBlock(aimedAt ? highlightedButtonProperties : originalButtonProperties);
    }

    public void EndRecording()
    {
        if (recording == null)
            return;

        StopCapture();
        int frames = Mathf.Clamp(recordedFrames, 0,
            Mathf.Min(recording.samples, recording.frequency * MaxRecordingSeconds));
        float[] samples = new float[frames * recording.channels];
        bool captured = frames >= recording.frequency / 5 && recording.GetData(samples, 0);
        byte[] wave = captured
            ? SpeakerWave.Encode(samples, recording.channels, recording.frequency)
            : null;
        Destroy(recording);
        recording = null;
        microphone = null;

        if (wave == null)
        {
            activeStand = null;
            SetStatus(recordingByHand ? "Keep your hand near the stand while you speak." : shortRecordingMessage);
            return;
        }

        SetStatus("Translating into English…");
        pendingTranslation = StartCoroutine(Translate(wave));
    }

    public void CancelRecording()
    {
        if (recording == null)
            return;

        StopCapture();
        Destroy(recording);
        recording = null;
        microphone = null;
        activeStand = null;
        SetStatus(readyMessage);
    }

    private void StopCapture()
    {
        if (recordedFrames >= 0)
            return;
        recordedFrames = Mathf.Max(0, Microphone.GetPosition(microphone));
        Microphone.End(microphone);
    }

    private async Task<string> RequestTranslation(byte[] wave, CancellationToken token)
    {
        using var request = new ByteArrayContent(wave);
        request.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using HttpResponseMessage response = await Bridge.PostAsync(endpoint, request, token).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private IEnumerator Translate(byte[] wave)
    {
        cancellation = new CancellationTokenSource();
        Task<string> translation = RequestTranslation(wave, cancellation.Token);
        yield return new WaitUntil(() => translation.IsCompleted);

        cancellation.Dispose();
        cancellation = null;
        pendingTranslation = null;
        activeStand = null;
        if (translation.IsCanceled)
        {
            SetStatus("Speech request timed out. Please try again.");
            yield break;
        }
        if (translation.IsFaulted)
            Debug.LogWarning(translation.Exception.GetBaseException().Message, this);
        Response response = translation.Status == TaskStatus.RanToCompletion
            ? ReadResponse(translation.Result)
            : null;
        if (string.IsNullOrWhiteSpace(response?.translation))
        {
            SetStatus(response?.error ?? "Speech bridge unavailable. Check the bridge console.");
            yield break;
        }

        TranslationReceived?.Invoke(response.translation);
        if (string.IsNullOrEmpty(response.audio) || speaker == null)
        {
            SetStatus(response.error ?? "English audio unavailable.");
            yield break;
        }

        try
        {
            synthesizedClip = SpeakerWave.Decode(Convert.FromBase64String(response.audio));
            speaker.clip = synthesizedClip;
            speaker.Play();
            playingStand = this;
        }
        catch (Exception exception) when (exception is FormatException
            || exception is System.IO.InvalidDataException || exception is System.IO.EndOfStreamException)
        {
            SetStatus("English audio could not be played.");
        }
    }

    private static Response ReadResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try { return JsonUtility.FromJson<Response>(json); }
        catch (ArgumentException) { return null; }
    }

    private void ShowCaption(string english)
    {
        Caption = english;
        if (caption != null)
            caption.text = english;
        SetStatus(readyMessage);
    }

    private void SetStatus(string message)
    {
        Status = message;
        if (status != null)
            status.text = message;
    }

    private void StopPlayback()
    {
        if (speaker != null)
        {
            speaker.Stop();
            speaker.clip = null;
        }
        if (synthesizedClip != null)
            Destroy(synthesizedClip);
        synthesizedClip = null;
        if (playingStand == this)
            playingStand = null;
    }

    private void OnDisable()
    {
        TranslationReceived -= ShowCaption;
        CancelRecording();
        if (pendingTranslation != null)
            StopCoroutine(pendingTranslation);
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        pendingTranslation = null;
        if (activeStand == this)
            activeStand = null;
        StopPlayback();
    }

    [Serializable]
    private sealed class Response
    {
        public string translation;
        public string audio;
        public string error;
    }
}
