# Local speech translation

## Desktop play mode

Open **Assets/Scenes/TranslatorDesktop.unity** in Unity. Start Docker Desktop and
the Ollama container as described below. From `PuzzleGame-VR/LocalSpeech`, run:

```powershell
$env:HF_HOME = Join-Path (Get-Location).Path '.models'
./.venv/Scripts/python.exe -u server.py --host 127.0.0.1
```

Enter Play mode and click the Game view. Hold the backquote/tilde key (the
physical key below Esc on a US keyboard) while speaking, then release it. On a
German layout you can also hold AltGr and +. The
scene displays the English translation and reads it aloud using a Windows voice.
It records from the first microphone Unity detects, for up to 12 seconds.
If the local bridge is already running on all interfaces for the Quest, the
desktop scene can use that same process.

## OpenAI play mode

Set `OPENAI_API_KEY` in the PowerShell session that runs the bridge. From
`PuzzleGame-VR/LocalSpeech`, run:

```powershell
python -u server.py --openai
```

Open **Assets/Scenes/TranslatorOpenAI.unity** and use the same Game view controls.
This bridge listens on `127.0.0.1:8766` and can run alongside the local bridge.
On release, it sends the recording to OpenAI's `gpt-transcribe` model and the
transcript to `gpt-5-mini` for English translation. Windows handles spoken
playback. This mode needs internet access and an API key with billing enabled.
The bridge reads the key from the environment.

## Quest play

The Quest sends microphone audio to the Python bridge on the PC. Multilingual Whisper transcribes it, Ollama's Qwen 2.5 3B model translates it into English, and a Windows English voice generates audio. Both stands show the translation and the activated stand plays it.

Start Docker Desktop and run `docker start ollama`. The container needs the `qwen2.5:3b` model installed.

From this directory, start the bridge in PowerShell.

```powershell
$env:HF_HOME = Join-Path (Get-Location).Path '.models'
./.venv/Scripts/python.exe -u server.py
```

Keep the window open while playing. Both stands call `http://10.89.8.131:8765/translate`, the laptop's current Wi-Fi address. Connect the Quest to a network that can reach this address. The bridge listens on all IPv4 interfaces by default, so USB forwarding is no longer needed.

After rebuilding the Quest app, open `http://10.89.8.131:8765/health` in the Quest browser to check connectivity. It should show `{"status": "ready"}`. This checks the bridge connection. Whisper transcription and Ollama translation are checked by speaking at a stand.

If the laptop's Wi-Fi address changes, update the Endpoint on both stands, the default endpoint in `Assets/SpeakerTranslation/SpeakerStand.cs`, and the allowed address in `Assets/Oculus/LocalSpeechNetworkSecurity.xml`, then rebuild. Windows Firewall must allow inbound TCP 8765 on Wi-Fi from the local subnet. To restrict the bridge to USB/local-only testing, start it with `--host 127.0.0.1`.

The bridge uses Whisper `small` on the CPU and automatically detects the language. `--source-language ro` specifies Romanian for a controlled test. `--trace-transcripts` prints recognized words and English output for debugging. Profanity matches become asterisks before display and voice generation.

Near the top of `server.py`, set `SHOW_LANGUAGE_PREFIX = False` to hide the language introduction from captions and spoken audio. Set it to `True` to show and speak “The language is Dutch.” before the translation. Restart the Python bridge after changing it.

For hand tracking, adjust **Hand Touch Distance** on each stand's **Speaker Stand Hand Touch** component. The default is `0.06` metres. Move both hands away to finish recording if both are touching the same stand.
