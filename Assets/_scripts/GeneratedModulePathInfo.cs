using UnityEngine;

/// <summary>
/// Runtime metadata attached to each generated module root (Module_1, Module_2, ...).
/// Used to know whether the module requires a turn (entry direction != exit direction).
/// </summary>
[DisallowMultipleComponent]
public class GeneratedModulePathInfo : MonoBehaviour
{
    [field: SerializeField] public int ModuleIndex { get; private set; } = -1;
    [field: SerializeField] public CurrentDirection EntryDirection { get; private set; } = CurrentDirection.DOWN;
    [field: SerializeField] public CurrentDirection ExitDirection { get; private set; } = CurrentDirection.DOWN;
    [field: SerializeField] public bool IsTurnModule { get; private set; } = false;

    public void SetEntry(int moduleIndex, CurrentDirection entry)
    {
        ModuleIndex = moduleIndex;
        EntryDirection = entry;
        Recompute();
    }

    public void SetExit(CurrentDirection exit)
    {
        ExitDirection = exit;
        Recompute();
    }

    private void Recompute()
    {
        IsTurnModule = EntryDirection != ExitDirection;
    }
}

