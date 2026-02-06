using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns (hides) a key prefab at one of this GameObject's child transforms when Play starts.
///
/// Usage:
/// - Create an empty GameObject (e.g., "KeyHider") in the scene.
/// - Create children under it as spawn points (their positions/rotations are used).
/// - Assign a Key prefab (with KeyPickup + trigger collider) to Key Prefab.
/// - Press Play: one key is instantiated at a random child position.
/// </summary>
[DisallowMultipleComponent]
public class KeyHider : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject keyPrefab;

    [Header("Spawn")]
    [Tooltip("If true, uses the child transform rotation for the spawned key.")]
    [SerializeField] private bool useSpawnRotation = true;
    [Tooltip("If true, parents the spawned key under the chosen spawn point (keeps hierarchy tidy).")]
    [SerializeField] private bool parentToSpawnPoint = false;

    [Header("Random")]
    [Tooltip("If >= 0, uses a fixed seed (deterministic per play). If < 0, uses Unity Random state.")]
    [SerializeField] private int fixedSeed = -1;

    [Header("Debug")]
    [SerializeField] private bool logSpawn = false;
    [Tooltip("If true, logs warnings when misconfigured (missing prefab / no child spawn points).")]
    [SerializeField] private bool logWarnings = false;
    [SerializeField] private bool drawGizmos = true;

    private GameObject _spawnedInstance;

    private void Start()
    {
        SpawnKey();
    }

    [ContextMenu("Spawn Key Now")]
    public void SpawnKey()
    {
        if (!Application.isPlaying) return;

        if (keyPrefab == null)
        {
            if (logWarnings) Debug.LogWarning("[KeyHider] No Key Prefab assigned.", this);
            return;
        }

        List<Transform> points = GetSpawnPoints();
        if (points.Count == 0)
        {
            if (logWarnings) Debug.LogWarning("[KeyHider] No child spawn points found. Add child transforms under this object.", this);
            return;
        }

        // Clean up previous instance if any (e.g., if user calls Spawn Key Now).
        if (_spawnedInstance != null)
        {
            Destroy(_spawnedInstance);
            _spawnedInstance = null;
        }

        int index = PickIndex(points.Count);
        Transform p = points[index];

        Vector3 pos = p.position;
        Quaternion rot = useSpawnRotation ? p.rotation : Quaternion.identity;
        Transform parent = parentToSpawnPoint ? p : null;

        _spawnedInstance = Instantiate(keyPrefab, pos, rot, parent);
        _spawnedInstance.name = keyPrefab.name; // nicer name in hierarchy

        if (logSpawn)
        {
            Debug.Log($"[KeyHider] Spawned '{keyPrefab.name}' at point '{p.name}' (index {index}).", this);
        }
    }

    private int PickIndex(int count)
    {
        if (count <= 1) return 0;

        if (fixedSeed >= 0)
        {
            // Deterministic per play session.
            var r = new System.Random(fixedSeed);
            return r.Next(0, count);
        }

        return Random.Range(0, count);
    }

    private List<Transform> GetSpawnPoints()
    {
        var points = new List<Transform>(32);
        foreach (Transform c in transform)
        {
            if (c == null) continue;
            if (!c.gameObject.activeInHierarchy) continue;
            points.Add(c);
        }
        return points;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Gizmos.color = new Color(0f, 1f, 0.85f, 0.7f);
        foreach (Transform c in transform)
        {
            if (c == null) continue;
            Gizmos.DrawWireSphere(c.position, 0.15f);
            Gizmos.DrawLine(c.position, c.position + c.forward * 0.4f);
        }
    }
#endif
}

