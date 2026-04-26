using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Audio;

// Plays a one-shot axe thwack exactly once per discrete chop hit. The
// sim increments Tree.HitCount every time a colonist deals damage; this
// node diffs that counter against the last seen value and fires one play
// per increment. Pitch + volume are randomized so a forest of choppers
// doesn't sound like a metronome.
public partial class ChopAudio : Node3D
{
    private const float MinPitch = 0.85f;
    private const float MaxPitch = 1.18f;
    private const float MaxDistance = 4000f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private AudioStream? _stream;
    private readonly Dictionary<int, int> _lastHitCount = new();
    private readonly List<AudioStreamPlayer3D> _pool = new();
    private RandomNumberGenerator _rng = new();

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _rng.Randomize();

        var path = ProjectSettings.GlobalizePath("res://assets/audio/axe_chop_hit.wav");
        if (System.IO.File.Exists(path))
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            _stream = LoadWav(bytes);
        }
        else
        {
            GD.PushWarning($"ChopAudio: missing {path}");
        }
    }

    public override void _Process(double delta)
    {
        if (_stream is null) return;

        var snap = _publisher.Current;
        var trees = snap.Trees;
        var seenIds = new HashSet<int>(trees.Count);

        for (var i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            seenIds.Add(t.EntityId);
            if (!_lastHitCount.TryGetValue(t.EntityId, out var last))
            {
                _lastHitCount[t.EntityId] = t.HitCount;
                continue;
            }
            if (t.HitCount > last)
            {
                PlayAt(t);
                _lastHitCount[t.EntityId] = t.HitCount;
            }
            else if (t.HitCount < last)
            {
                // Friflo recycles entity ids; a new tree on a reused id starts
                // at HitCount=0, so resync without firing on the rollback.
                _lastHitCount[t.EntityId] = t.HitCount;
            }
        }

        if (_lastHitCount.Count != seenIds.Count)
        {
            var stale = new List<int>();
            foreach (var key in _lastHitCount.Keys)
                if (!seenIds.Contains(key)) stale.Add(key);
            foreach (var key in stale) _lastHitCount.Remove(key);
        }
    }

    private void PlayAt(TreeView t)
    {
        var metersX = (t.TileX + 0.5f) * SimConstants.MetersPerTile;
        var metersY = (t.TileY + 0.5f) * SimConstants.MetersPerTile;
        var x = metersX * _unitsPerMeter;
        var z = metersY * _unitsPerMeter;
        var y = _heightfield.SurfaceMetresAt(metersX / SimConstants.MetersPerTile,
                                             metersY / SimConstants.MetersPerTile)
                * _unitsPerMeter;

        var player = Acquire();
        player.Position = new Vector3(x, y, z);
        player.Stream = _stream;
        player.PitchScale = _rng.RandfRange(MinPitch, MaxPitch);
        player.VolumeDb = _rng.RandfRange(-10f, -6f);
        player.MaxDistance = MaxDistance;
        player.UnitSize = 30f;
        player.Play();
    }

    private AudioStreamPlayer3D Acquire()
    {
        for (var i = 0; i < _pool.Count; i++)
        {
            if (!_pool[i].Playing) return _pool[i];
        }
        var p = new AudioStreamPlayer3D { Bus = "Master" };
        _pool.Add(p);
        AddChild(p);
        return p;
    }

    private static AudioStream? LoadWav(byte[] bytes)
    {
        var stream = new AudioStreamWav { Data = bytes };
        ParseWavHeader(bytes, stream);
        return stream;
    }

    private static void ParseWavHeader(byte[] bytes, AudioStreamWav stream)
    {
        // Minimal WAV header parse: locate fmt chunk for sample rate + format,
        // and data chunk so AudioStreamWav.Data points at PCM samples only.
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
