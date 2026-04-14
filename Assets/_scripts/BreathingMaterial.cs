using UnityEngine;
using System.Collections.Generic;

public class BreathingMaterial : MonoBehaviour
{
    [Header("Breathing (Hz)")]
    [Tooltip("Frecuencia de respiración en ciclos por segundo.")]
    [Range(0.01f, 2f)]
    public float speed = 0.25f;

    [Tooltip("Intensidad global del efecto.")]
    [Range(0f, 1f)]
    public float intensity = 0.08f;

    [Range(0f, 1f)]
    public float organicShape = 0.5f;

    [Header("Modulation")]
    public bool modulateColor = true;
    public bool modulateSmoothness = true;
    public bool modulateEmission = false;

    [Header("Amounts")]
    [Range(0f, 0.2f)]
    public float colorAmount = 0.03f;

    [Range(0f, 0.3f)]
    public float smoothnessAmount = 0.06f;

    [Range(0f, 1f)]
    public float emissionAmount = 0.08f;

    public Color emissionColor = new Color(0.85f, 0.9f, 1f, 1f);

    List<Renderer> renderers = new();
    List<MaterialPropertyBlock> blocks = new();
    List<Color> baseColors = new();
    List<float> baseGlossiness = new();
    List<float> phases = new();

    static readonly int ColorID = Shader.PropertyToID("_Color");
    static readonly int GlossinessID = Shader.PropertyToID("_Glossiness");
    static readonly int EmissionID = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        var rends = GetComponentsInChildren<Renderer>(true);

        foreach (var r in rends)
        {
            if (r.sharedMaterial == null) continue;

            renderers.Add(r);
            blocks.Add(new MaterialPropertyBlock());

            var mat = r.sharedMaterial;

            baseColors.Add(mat.HasProperty(ColorID) ? mat.GetColor(ColorID) : Color.white);
            baseGlossiness.Add(mat.HasProperty(GlossinessID) ? mat.GetFloat(GlossinessID) : 0.25f);
            phases.Add(Random.Range(0f, Mathf.PI * 2f));
        }
    }

    void Update()
    {
        float t = Time.time * speed * Mathf.PI * 2f;

        for (int i = 0; i < renderers.Count; i++)
        {
            float s = Mathf.Sin(t + phases[i]) * 0.5f + 0.5f;

            float eased = s * s * (3f - 2f * s);
            float breath = Mathf.Lerp(s, eased, organicShape);

            float centered = (breath - 0.5f) * 2f;

            var rend = renderers[i];
            var mpb = blocks[i];

            rend.GetPropertyBlock(mpb);

            if (modulateColor)
            {
                float c = 1f + centered * (colorAmount * intensity);
                var bc = baseColors[i];
                mpb.SetColor(ColorID, new Color(bc.r * c, bc.g * c, bc.b * c, bc.a));
            }

            if (modulateSmoothness)
            {
                float g = Mathf.Clamp01(baseGlossiness[i] + centered * (smoothnessAmount * intensity));
                mpb.SetFloat(GlossinessID, g);
            }

            if (modulateEmission)
            {
                float e = Mathf.Max(0f, breath * (emissionAmount * intensity));
                mpb.SetColor(EmissionID, emissionColor * e);
            }

            rend.SetPropertyBlock(mpb);
        }
    }

    void OnDisable()
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            var mpb = blocks[i];
            mpb.Clear();
            renderers[i].SetPropertyBlock(mpb);
        }
    }
}
