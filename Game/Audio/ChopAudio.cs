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

        _stream = WavLoader.LoadFromFile("res://assets/audio/axe_chop_hit.wav");
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

}
