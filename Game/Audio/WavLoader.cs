using Godot;

namespace CowColonySim.Game.Audio;

// Hand-rolled WAV reader so we can pull .wav files straight from res:// at
// runtime without requiring a .import sidecar (fresh clones don't have
// those). Only handles 8-bit and 16-bit PCM; AudioGen output must be
// re-encoded to PCM16 before shipping.
internal static class WavLoader
{
    public static AudioStreamWav? LoadFromFile(string resPath)
    {
        var path = ProjectSettings.GlobalizePath(resPath);
        if (!System.IO.File.Exists(path))
        {
            GD.PushWarning($"WavLoader: missing {path}");
            return null;
        }
        var bytes = System.IO.File.ReadAllBytes(path);
        var stream = new AudioStreamWav { Data = bytes };
        Parse(bytes, stream);
        return stream;
    }

    private static void Parse(byte[] bytes, AudioStreamWav stream)
    {
        if (bytes.Length < 44) return;
        if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F') return;
        var pos = 12;
        var sampleRate = 44100;
        var channels = 1;
        var bitsPerSample = 16;
        var dataOffset = -1;
        var dataSize = 0;
        while (pos + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            var size = BitConverter.ToInt32(bytes, pos + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(bytes, pos + 10);
                sampleRate = BitConverter.ToInt32(bytes, pos + 12);
                bitsPerSample = BitConverter.ToInt16(bytes, pos + 22);
            }
            else if (id == "data")
            {
                dataOffset = pos + 8;
                dataSize = size;
                break;
            }
            pos += 8 + size + (size & 1);
        }
        stream.MixRate = sampleRate;
        stream.Stereo = channels >= 2;
        stream.Format = bitsPerSample == 8
            ? AudioStreamWav.FormatEnum.Format8Bits
            : AudioStreamWav.FormatEnum.Format16Bits;
        if (dataOffset > 0 && dataSize > 0)
        {
            var pcm = new byte[dataSize];
            Buffer.BlockCopy(bytes, dataOffset, pcm, 0, dataSize);
            stream.Data = pcm;
        }
    }
}
