using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Debug helper: prints render/batching-relevant stats for the currently selected GameObject hierarchy.
/// Useful to compare two runtime roots like "Run0" vs "Run1".
/// </summary>
public static class RenderStatsForSelection
{
    [MenuItem("Tools/Procedural/Debug/Print Render Stats (Selected)")]
    private static void PrintSelected()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("Render Stats: select a GameObject first (e.g., Run0 or Run1 root).");
            return;
        }

        foreach (GameObject go in selected)
        {
            if (go == null) continue;

            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            int enabled = 0;
            int combinedNameCount = 0;
            var uniqueSharedMats = new HashSet<Material>();
            int nonAssetMaterials = 0;
            int staticRenderers = 0;
            int shadowsOn = 0;
            int receiveShadowsOn = 0;
            int probesOn = 0;
            int instancingMats = 0;
            var instancingMatSet = new HashSet<Material>();

            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null) continue;
                if (r.enabled) enabled++;
                if (r.gameObject != null && r.gameObject.name.Contains("_Combined")) combinedNameCount++;
                if (r.gameObject != null && r.gameObject.isStatic) staticRenderers++;

                Material m = r.sharedMaterial;
                if (m != null)
                {
                    uniqueSharedMats.Add(m);
                    if (!AssetDatabase.Contains(m)) nonAssetMaterials++;
                    if (m.enableInstancing && instancingMatSet.Add(m)) instancingMats++;
                }

                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) shadowsOn++;
                if (r.receiveShadows) receiveShadowsOn++;
                if (r.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off ||
                    r.reflectionProbeUsage != UnityEngine.Rendering.ReflectionProbeUsage.Off)
                {
                    probesOn++;
                }
            }

            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>(true);
            int meshFilters = mfs != null ? mfs.Length : 0;

            // If the user accidentally selected the camera, this will be 0 and not useful.
            if (rs.Length == 0 && go.GetComponent<Camera>() != null)
            {
                Debug.LogWarning($"[RenderStats] Selected '{go.name}' is a Camera and has 0 Renderers. Select the map root (e.g., Run0/Run1) in the Hierarchy.", go);
                continue;
            }

            // One-line summary (easy to compare between Run0/Run1 without expanding Console entries).
            Debug.Log(
                $"[RenderStats] Root='{go.name}' renderers={rs.Length} enabled={enabled} staticR={staticRenderers} uniqueMats={uniqueSharedMats.Count} nonAssetMats={nonAssetMaterials} combinedObjs={combinedNameCount} shadowsOn={shadowsOn} recvShadowsOn={receiveShadowsOn} probesOn={probesOn} instancingMats={instancingMats}",
                go);

            Debug.Log(
                $"[RenderStats] Root='{go.name}'\n" +
                $"- Renderers: {rs.Length} (enabled: {enabled})\n" +
                $"- Static renderers: {staticRenderers}\n" +
                $"- MeshFilters: {meshFilters}\n" +
                $"- Unique shared materials: {uniqueSharedMats.Count}\n" +
                $"- Renderers w/ non-asset sharedMaterial (likely instances): {nonAssetMaterials}\n" +
                $"- Objects named '*_Combined*': {combinedNameCount}\n\n" +
                $"- ShadowCasting (not Off): {shadowsOn}\n" +
                $"- ReceiveShadows (true): {receiveShadowsOn}\n" +
                $"- Probes (light/reflection not Off): {probesOn}\n" +
                $"- Materials w/ GPU instancing enabled: {instancingMats}\n\n" +
                $"If batches are huge, common causes are:\n" +
                $"- thousands of enabled Renderers (not combined)\n" +
                $"- many unique/instanced materials (break batching)\n" +
                $"- shadows/probes enabled on thousands of renderers\n" +
                $"- not marked Static (no static batching)\n",
                go);
        }
    }
}

