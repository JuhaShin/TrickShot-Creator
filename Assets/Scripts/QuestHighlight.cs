using System.Collections.Generic;
using UnityEngine;

public sealed class QuestHighlight : MonoBehaviour
{
    [SerializeField] private Color highlightColor = new Color32(255, 139, 50, 255);
    [SerializeField, Min(0f)] private float intensity = 1f / 3f;
    [SerializeField, Min(.01f)] private float period = 2.5f;

    private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
    private readonly List<(Renderer renderer, Material[] originals)> renderers = new();
    private readonly Dictionary<Material, Material> copies = new();
    private readonly List<(Material material, Color baseline)> pulses = new();
    private GameObject current;
    private float elapsed;

    public void SetTarget(GameObject target)
    {
        if (current == target) return;
        Restore();
        current = target;
        if (!target) return;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            Material[] originals = renderer.sharedMaterials;
            Material[] slots = (Material[])originals.Clone();
            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                Material source = slots[i];
                if (!source || !source.HasProperty(Emission)) continue;
                if (!copies.TryGetValue(source, out Material copy))
                {
                    // The emission shader keyword is material-local and cannot be enabled by a property block.
                    copy = new Material(source);
                    Color baseline = source.IsKeywordEnabled("_EMISSION") ? source.GetColor(Emission) : Color.black;
                    copy.EnableKeyword("_EMISSION");
                    copy.SetColor(Emission, baseline);
                    copies.Add(source, copy);
                    pulses.Add((copy, baseline));
                }
                slots[i] = copy;
                changed = true;
            }
            if (!changed) continue;
            renderers.Add((renderer, originals));
            renderer.sharedMaterials = slots;
        }
    }

    private void Update()
    {
        if (pulses.Count == 0) return;
        float cycle = Mathf.Max(.01f, period);
        elapsed = Mathf.Repeat(elapsed + Time.deltaTime, cycle);
        float strength = (.5f - .5f * Mathf.Cos(elapsed * 2f * Mathf.PI / cycle)) * intensity;
        foreach (var pulse in pulses)
            pulse.material.SetColor(Emission, pulse.baseline + highlightColor * strength);
    }

    private void Restore()
    {
        foreach (var entry in renderers)
            if (entry.renderer) entry.renderer.sharedMaterials = entry.originals;
        foreach (Material copy in copies.Values) Destroy(copy);
        renderers.Clear();
        copies.Clear();
        pulses.Clear();
        current = null;
        elapsed = 0f;
    }

    private void OnDisable() => Restore();
}
