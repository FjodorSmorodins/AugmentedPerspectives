$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$speechText = [Console]::In.ReadToEnd()
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$audio = New-Object System.IO.MemoryStream
try {
    $englishVoice = $synth.GetInstalledVoices() |
        Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.TwoLetterISOLanguageName -eq 'en' } |
        Select-Object -First 1
    if (-not $englishVoice) { throw 'Install an English Windows desktop speech voice.' }
    $synth.SelectVoice($englishVoice.VoiceInfo.Name)
    $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
        22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
        [System.Speech.AudioFormat.AudioChannel]::Mono
    )
    $synth.SetOutputToAudioStream($audio, $format)
    $synth.Speak($speechText)
    $synth.SetOutputToNull()
    # SetOutputToAudioStream emits raw PCM. Add a WAV header for Unity.
    $pcm = $audio.ToArray()
    $writer = New-Object System.IO.BinaryWriter([Console]::OpenStandardOutput())
    $writer.Write([byte[]][Text.Encoding]::ASCII.GetBytes('RIFF'))
    $writer.Write([int](36 + $pcm.Length))
    $writer.Write([byte[]][Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $writer.Write([int]16)
    $writer.Write([int16]1)
    $writer.Write([int16]1)
    $writer.Write([int]22050)
    $writer.Write([int]44100)
    $writer.Write([int16]2)
    $writer.Write([int16]16)
    $writer.Write([byte[]][Text.Encoding]::ASCII.GetBytes('data'))
    $writer.Write([int]$pcm.Length)
    $writer.Write($pcm)
    $writer.Flush()
}
finally {
    $synth.Dispose()
    $audio.Dispose()
}
