using UnityEngine;

public class ModuleInfo
{
    public Vector3 NextModulePosition { get; set; }
    public CurrentDirection LastDirection { get; set; }
    public Vector2Int LastExit { get; set; } // Entry point for this module

    public ModuleInfo(Vector3 nextModulePosition, CurrentDirection lastDirection, Vector2Int lastExit)
    {
        NextModulePosition = nextModulePosition;
        LastDirection = lastDirection;
        LastExit = lastExit;
    }

    public override string ToString()
    {
        return $"NextModulePosition: {NextModulePosition}, LastDirection: {LastDirection}, LastExit: {LastExit}";
    }
}
