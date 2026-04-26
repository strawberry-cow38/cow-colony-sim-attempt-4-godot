namespace CowColonySim.Sim.Snapshots;

// Per-colonist row in the snapshot. Ground-plane metres only — vertical
// (Y in Godot) is sampled from the heightfield by the renderer.
public readonly record struct ColonistView(float MetersX, float MetersY);
