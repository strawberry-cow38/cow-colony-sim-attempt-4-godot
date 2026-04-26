using Godot;

namespace CowColonySim.Game;

public partial class Bootstrap : Node3D
{
    public override void _Ready()
    {
        GD.Print("Bootstrap ready.");
    }
}
