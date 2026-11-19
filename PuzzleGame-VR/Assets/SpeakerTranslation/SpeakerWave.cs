using System;
using System.IO;
using UnityEngine;

internal static class SpeakerWave
{
    public static byte[] Encode(float[] samples, int channels, int rate)
    {
        int frames = samples.Length / channels;
        int outputRate = Mathf.Clamp(rate, 8000, 48000);
        int outputFrames = (int)((long)frames * outputRate / rate);
        using var stream = new MemoryStream(44 + outputFrames * 2);
        using var writer = new BinaryWriter(stream);
        writer.Write(0x46464952); // RIFF
        writer.Write(36 + outputFrames * 2);
        writer.Write(0x45564157); // WAVE
        writer.Write(0x20746D66); // fmt
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(outputRate);
        writer.Write(outputRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(0x61746164); // data
        writer.Write(outputFrames * 2);

        for (int frame = 0; frame < outputFrames; frame++)
        {
            int input = (int)((long)frame * rate / outputRate) * channels;
            float mono = 0;
            for (int channel = 0; channel < channels; channel++)
                mono += samples[input + channel];
            writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(mono / channels, -1f, 1f) * 32767));
        }
        return stream.ToArray();
    }

    public static AudioClip Decode(byte[] wave)
    {
        using var reader = new BinaryReader(new MemoryStream(wave));
        if (reader.ReadInt32() != 0x46464952)
            throw new InvalidDataException("Expected RIFF.");
        reader.ReadInt32(); // RIFF size
        if (reader.ReadInt32() != 0x45564157)
            throw new InvalidDataException("Expected WAVE.");

        int rate = 0;
        while (reader.BaseStream.Position + 8 <= wave.Length)
        {
            int chunk = reader.ReadInt32();
            int size = reader.ReadInt32();
            long end = reader.BaseStream.Position + size;
            if (size < 0 || end > wave.Length)
                break;

            if (chunk == 0x20746D66 && size >= 16)
            {
                short format = reader.ReadInt16();
                short channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.BaseStream.Position += 6;
                short bits = reader.ReadInt16();
                if (format != 1 || channels != 1 || bits != 16 || rate < 8000 || rate > 48000)
                    break;
            }
            else if (chunk == 0x61746164 && rate > 0 && size >= 2 && size % 2 == 0)
            {
                var samples = new float[size / 2];
                for (int index = 0; index < samples.Length; index++)
                    samples[index] = reader.ReadInt16() / 32768f;
                var clip = AudioClip.Create("English translation", samples.Length, 1, rate, false);
                clip.SetData(samples, 0);
                return clip;
            }

            reader.BaseStream.Position = end + (size & 1);
        }
        throw new InvalidDataException("Expected a mono PCM16 WAV clip.");
    }
}
