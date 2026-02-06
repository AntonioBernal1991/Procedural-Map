using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


//unifies the meshes of the cubes winning up to 100 fps
public class MeshCombiner
{
    public static void CombineMeshesByMaterial(GameObject parent)
    {
        MeshFilter[] meshFilters = parent.GetComponentsInChildren<MeshFilter>();
        if (meshFilters.Length == 0)
        {
            
            return;
        }

        // Combined colliders: use the combined mesh as the collider (one per material),
        // avoiding per-cube collider seams that cause bouncy contacts.
        // Note: if you rotate/move the maze physically, MeshCollider-based collision can be less stable than primitives,
        // but it removes the cube-by-cube "steps" from having individual colliders.
        // MapGenerator3D no longer controls physics optimization; keep it simple:
        // combine visuals only, keep per-cube colliders for stable camera collision.
        bool useCombinedColliders = false;
       
        Dictionary<Material, List<CombineInstance>> combineInstancesByMaterial = new Dictionary<Material, List<CombineInstance>>();
        List<GameObject> originals = useCombinedColliders ? new List<GameObject>() : null;
        // If we're combining visuals (and not using combined colliders), we will hide the original renderers
        // ONLY if we actually end up combining (eligible > 1). Otherwise we would accidentally make a module invisible.
        List<MeshRenderer> renderersToDisable = !useCombinedColliders ? new List<MeshRenderer>() : null;
        int eligible = 0;

        // Collects meshes and sort by material
        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter.sharedMesh == null) continue;
            // Don't include the invisible base collider plane in visual mesh combining (prevents z-fighting / extra geometry).
            if (meshFilter.gameObject.name == "BasePlane") continue;
            if (meshFilter.gameObject.name.Contains("_Combined")) continue;

            MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterial == null) continue;

            Material material = renderer.sharedMaterial;

            if (!combineInstancesByMaterial.ContainsKey(material))
            {
                combineInstancesByMaterial[material] = new List<CombineInstance>();
            }

          
            CombineInstance combineInstance = new CombineInstance
            {
                mesh = meshFilter.sharedMesh,
                transform = parent.transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix
            };

            combineInstancesByMaterial[material].Add(combineInstance);
            eligible++;
            if (useCombinedColliders && originals != null) originals.Add(meshFilter.gameObject);

            if (!useCombinedColliders)
            {
                // Keep colliders (if any). We'll hide originals visually only if we actually combine.
                MeshRenderer mr = meshFilter.GetComponent<MeshRenderer>();
                if (mr != null) renderersToDisable.Add(mr);
            }
        }

        // If there's 0 or 1 eligible mesh, combining would just add overhead.
        if (eligible <= 1)
        {
            return;
        }

        // Hide originals visually now that we know we are combining.
        if (!useCombinedColliders && renderersToDisable != null)
        {
            foreach (MeshRenderer mr in renderersToDisable)
            {
                if (mr != null) mr.enabled = false;
            }
        }

        // creates a mesh combine for each material
        foreach (var entry in combineInstancesByMaterial)
        {
            Material material = entry.Key;
            List<CombineInstance> combineInstances = entry.Value;

            // Creates a new gameobject for each material
            GameObject combinedObject = new GameObject($"{parent.name}_{material.name}_Combined");
            combinedObject.transform.parent = parent.transform;
            combinedObject.transform.localPosition = Vector3.zero;
            combinedObject.transform.localRotation = Quaternion.identity;

            MeshFilter combinedMeshFilter = combinedObject.AddComponent<MeshFilter>();
            MeshRenderer combinedMeshRenderer = combinedObject.AddComponent<MeshRenderer>();

            // Build combined mesh safely:
            // - Use 32-bit indices to avoid overflow when combining lots of tiles.
            // - Recalculate bounds so frustum culling doesn't pop parts in/out.
            Mesh combined = new Mesh();
            combined.indexFormat = IndexFormat.UInt32;
            combined.CombineMeshes(combineInstances.ToArray(), true, true);
            combined.RecalculateBounds();
            combinedMeshFilter.sharedMesh = combined;
            // Use sharedMaterial to avoid instantiating a unique material per combined mesh.
            combinedMeshRenderer.sharedMaterial = material;
            // Priority 1 FPS win: shadows from thousands of tiles are extremely expensive.
            combinedMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            combinedMeshRenderer.receiveShadows = false;

            if (useCombinedColliders)
            {
                // Static environment collision: non-convex mesh collider is usually correct.
                MeshCollider mc = combinedObject.AddComponent<MeshCollider>();
                mc.sharedMesh = combinedMeshFilter.mesh;
                // Only make the GROUND collider convex (flat, safe). Keep others non-convex to avoid blocking at module edges.
                mc.convex = MapGenerator3D.Instance != null && material == MapGenerator3D.Instance.GroundMaterial;
                if (MapGenerator3D.Instance != null && material == MapGenerator3D.Instance.GroundMaterial)
                {
                    mc.material = MapGenerator3D.Instance.GroundPhysicMaterial;
                }
                // Faster collision cooking for static environment meshes.
                mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning;
            }

            combinedObject.SetActive(true);
        }

        if (useCombinedColliders && originals != null)
        {
            // Disable originals completely (removes per-cube colliders).
            foreach (GameObject go in originals)
            {
                if (go != null) go.SetActive(false);
            }
        }

    }
}
