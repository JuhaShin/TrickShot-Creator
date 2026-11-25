using UnityEngine;
using UnityEngine.UI;

public class LaunchAssistPanel : MonoBehaviour
{
    [SerializeField] private SimulationManager simulation;
    [SerializeField] private Image previousInputMarker;
    private RectTransform forceLine;

    private void Awake()
    {
        if (previousInputMarker != null)
            previousInputMarker.transform.SetAsLastSibling();

        if (simulation.PowerGauge == null) return;
        var line = new GameObject("PreviousForceLine", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        line.raycastTarget = false;
        forceLine = line.rectTransform;
        forceLine.SetParent(simulation.PowerGauge.rectTransform, false);
        forceLine.sizeDelta = new Vector2(0f, 3f);
    }

    private void OnEnable()
    {
        simulation.LaunchRecorded += DisplayRecord;
        DisplayRecord();
    }

    private void OnDisable() => simulation.LaunchRecorded -= DisplayRecord;

    private void DisplayRecord()
    {
        var record = simulation.PreviousLaunch;
        if (previousInputMarker != null)
        {
            previousInputMarker.gameObject.SetActive(record.HasValue);
            if (record.HasValue)
            {
                RectTransform dot = previousInputMarker.rectTransform;
                Rect area = ((RectTransform)dot.parent).rect;
                dot.anchoredPosition = Rect.NormalizedToPoint(area, record.Value.viewport) - area.center;
            }
        }

        if (forceLine == null) return;
        forceLine.gameObject.SetActive(record.HasValue);
        if (!record.HasValue) return;
        Image gauge = simulation.PowerGauge;
        Rect fill = GetFillRect(gauge);
        float fraction = record.Value.gaugeFraction;
        if (gauge.fillOrigin == (int)Image.OriginVertical.Top) fraction = 1f - fraction;
        forceLine.anchorMin = forceLine.anchorMax = gauge.rectTransform.pivot;
        forceLine.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, fill.width);
        forceLine.anchoredPosition = new Vector2(fill.center.x, Mathf.Lerp(fill.yMin, fill.yMax, fraction));
    }

    // Match the padded drawing rectangle used by uGUI's vertical Filled Image.
    private static Rect GetFillRect(Image gauge)
    {
        Rect rect = gauge.GetPixelAdjustedRect();
        Sprite sprite = gauge.overrideSprite;
        if (sprite == null) return rect;

        Vector2 size = sprite.rect.size;
        if (gauge.preserveAspect)
        {
            float scale = Mathf.Min(rect.width / size.x, rect.height / size.y);
            Vector2 fitted = size * scale;
            rect.position += Vector2.Scale(rect.size - fitted, gauge.rectTransform.pivot);
            rect.size = fitted;
        }
        Vector4 padding = UnityEngine.Sprites.DataUtility.GetPadding(sprite);
        float width = Mathf.RoundToInt(size.x);
        float height = Mathf.RoundToInt(size.y);
        return Rect.MinMaxRect(rect.xMin + rect.width * padding.x / width,
            rect.yMin + rect.height * padding.y / height,
            rect.xMax - rect.width * padding.z / width,
            rect.yMax - rect.height * padding.w / height);
    }

    private void OnRectTransformDimensionsChange()
    {
        if (isActiveAndEnabled && simulation != null && simulation.PreviousLaunch.HasValue)
            DisplayRecord();
    }
}
