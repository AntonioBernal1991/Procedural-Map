using UnityEngine;

public class ModuleInfo
{
    public Vector3 NextModulePosition { get; set; }
    public CurrentDirection LastDirection { get; set; }
    public Vector2Int LastExit { get; set; } // Entry point for this module
    public bool IsBlocker { get; set; } // Full module without path (seals exits)

    // Runtime-only hook so PathGenerator can write metadata to the actual spawned module GameObject.
    public Transform RuntimeModuleRoot { get; set; }
    public CurrentDirection EntryDirectionRuntime { get; set; }
    public CurrentDirection ExitDirectionRuntime { get; set; }
    public bool IsTurnModuleRuntime { get; set; }

    public ModuleInfo(Vector3 nextModulePosition, CurrentDirection lastDirection, Vector2Int lastExit)
    {
        NextModulePosition = nextModulePosition;
        LastDirection = lastDirection;
        LastExit = lastExit;
        IsBlocker = false;
        RuntimeModuleRoot = null;
        EntryDirectionRuntime = lastDirection;
        ExitDirectionRuntime = lastDirection;
        IsTurnModuleRuntime = false;
    }
    
    public ModuleInfo(Vector3 nextModulePosition, CurrentDirection lastDirection, Vector2Int lastExit, bool isBlocker)
        : this(nextModulePosition, lastDirection, lastExit)
    {
        IsBlocker = isBlocker;
    }

    public override string ToString()
    {
        return $"NextModulePosition: {NextModulePosition}, LastDirection: {LastDirection}, LastExit: {LastExit}, IsBlocker: {IsBlocker}";
    }
}
