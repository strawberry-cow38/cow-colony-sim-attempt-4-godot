using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Audio;

// Plays tree_fall.wav once per snapshot.TreeFalls entry. The sim emits the
// list when ChopJobSystem fells a trunk; this node diffs by tick number so
// each tick's events fire exactly once even if Game._Process runs faster.
public partial class TreeFallAudio : Node3D
{
    private const float MinPitch = 0.92f;
    private const float MaxPitch = 1.08f;
    private const float MaxDistance = 8000f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private AudioStream? _stream;
    private readonly List<AudioStreamPlayer3D> _pool = new();
    private long _lastTick = -1;
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
        _stream = WavLoader.LoadFromFile("res://assets/audio/tree_fall.wav");
    }

    public override void _Process(double delta)
    {
        if (_stream is null) return;

        var snap = _publisher.Current;
        if (snap.TickNumber == _lastTick) return;
        _lastTick = snap.TickNumber;

        var falls = snap.TreeFalls;
        for (var i = 0; i < falls.Count; i++)
        {
            PlayAt(falls[i].X, falls[i].Y);
        }
    }

    private void PlayAt(int tileX, int tileY)
    {
        var metersX = (tileX + 0.5f) * SimConstants.MetersPerTile;
        var metersY = (tileY + 0.5f) * SimConstants.MetersPerTile;
        var x = metersX * _unitsPerMeter;
        var z = metersY * _unitsPerMeter;
        var y = _heightfield.SurfaceMetresAt(metersX / SimConstants.MetersPerTile,
                                             metersY / SimConstants.MetersPerTile)
                * _unitsPerMeter;

        var player = Acquire();
        player.Position = new Vector3(x, y, z);
        player.Stream = _stream;
        player.PitchScale = _rng.RandfRange(MinPitch, MaxPitch);
        player.VolumeDb = _rng.RandfRange(-4f, 0f);
        player.MaxDistance = MaxDistance;
        player.UnitSize = 80f;
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
}
