using System.Collections;
using UnityEngine;

public sealed class OpeningWalkHome : MonoBehaviour
{
    private const float FilmAspect = 2.14f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.8f;
    private float fadeAlpha;
    private bool showing;
    private float departureRemaining;
    private bool departing;

    public IEnumerator WaitForDeparture(float seconds)
    {
        showing = true;
        departing = true;
        float end = Time.realtimeSinceStartup + seconds;
        while ((departureRemaining = end - Time.realtimeSinceStartup) > 0f)
            yield return null;
        departing = false;
    }
    private Material displayMaterial;

    private static Transform Anchor(GameObject model, string name)
    {
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        throw new System.InvalidOperationException(model.name + " is missing " + name);
    }

    private static void Align(Transform model, Transform origin, Vector3 forward, Vector3 up,
                              Vector3 destination, Quaternion orientation)
    {
        model.rotation = orientation * Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.rotation;
        model.position += destination - origin.position;
    }

    public IEnumerator Play(Camera view, Vector3 threshold, Vector3 outward)
    {
        var phoneAsset = Resources.Load<GameObject>("WalkHome/PhoneGrip");
        var clip = Resources.Load<Texture2D>("WalkHome/BounceShort");
        var shader = Resources.Load<Shader>("WalkHome/PhoneDisplay");
        if (!phoneAsset || !clip || !shader)
        {
            Debug.LogError("Missing OpeningScene walk-home cinematic asset.", this);
            yield break;
        }
        var eye = view.transform;
        Vector3 start = eye.position;
        Quaternion initial = eye.rotation;
        Quaternion exit = Quaternion.LookRotation(Vector3.ProjectOnPlane(outward, Vector3.up));
        Quaternion left = Quaternion.AngleAxis(-90f, Vector3.up) * exit;
        Vector3 forward = left * Vector3.forward;
        Vector3 outside = threshold + exit * Vector3.forward * 1.35f + Vector3.up * 1.58f;

        var grip = Instantiate(phoneAsset, eye);
        Transform glass = Anchor(grip, "GlassCenter");
        Vector3 top = Anchor(grip, "GlassTop").position - glass.position;
        Vector3 back = glass.position - Anchor(grip, "GlassOutside").position;
        Align(grip.transform, glass, back, top, eye.position, eye.rotation);
        Vector3 gripOffset = grip.transform.localPosition;
        var display = GameObject.CreatePrimitive(PrimitiveType.Quad);
        display.name = "TrickShot Short";
        var collider = display.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        display.transform.SetPositionAndRotation(glass.position, eye.rotation);
        display.transform.localScale = new Vector3(.074f, .13156f, 1);
        display.transform.SetParent(grip.transform, true);
        displayMaterial = new Material(shader);
        displayMaterial.mainTexture = clip;
        display.GetComponent<Renderer>().sharedMaterial = displayMaterial;
        grip.SetActive(false);

        const int phoneLayer = 30;
        foreach (Transform part in grip.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = phoneLayer;
        int oldMask = view.cullingMask;
        view.cullingMask |= 1 << phoneLayer;
        float oldNear = view.nearClipPlane;
        view.nearClipPlane = .018f;
        showing = true;
        float time = 0;
        const float exitEnd = 1.15f, turnEnd = 1.88f, raiseTime = 1.1f, end = 11f;
        while (time < end)
        {
            time = Mathf.Min(end, time + Time.unscaledDeltaTime);
            float walk = Mathf.Max(0, time - turnEnd);
            float raised = Mathf.SmoothStep(0, 1, walk / raiseTime);
            if (time < exitEnd)
            {
                float t = Mathf.SmoothStep(0, 1, time / exitEnd);
                eye.SetPositionAndRotation(Vector3.Lerp(start, outside, t), Quaternion.Slerp(initial, exit, t));
            }
            else
            {
                float turn = Mathf.SmoothStep(0, 1, (time - exitEnd) / (turnEnd - exitEnd));
                eye.position = outside + forward * (walk * .96f) + Vector3.up * (.014f * raised * Mathf.Sin(walk * 9));
                eye.rotation = Quaternion.AngleAxis(-90f * turn, Vector3.up) * exit * Quaternion.Euler(22f * raised, 0, 0);
            }
            if (walk > 0)
            {
                if (!grip.activeSelf) grip.SetActive(true);
                grip.transform.localPosition = gripOffset + new Vector3(.002f * Mathf.Sin(walk * 4),
                    Mathf.Lerp(-.29f, -.006f, raised), .255f);
                displayMaterial.SetFloat("_Frame", Mathf.Floor(Mathf.Max(0,walk-raiseTime)*10f) % 32);
            }
            fadeAlpha = fadeOutDuration > 0f
                ? Mathf.SmoothStep(0f, 1f, 1f - (end - time) / Mathf.Min(fadeOutDuration, end))
                : (time >= end ? 1f : 0f);
            yield return null;
        }
        view.cullingMask = oldMask;
        view.nearClipPlane = oldNear;
        Destroy(grip);
    }

    private void OnGUI()
    {
        if (!showing || Event.current.type != EventType.Repaint) return;
        Color old = GUI.color;
        GUI.depth = -1000;
        GUI.color = Color.black;
        float width = Screen.width, height = Screen.height;
        float bar = Mathf.Max(0, (height - width / FilmAspect) / 2);
        float side = Mathf.Max(0, (width - height * FilmAspect) / 2);
        if (bar > 0)
        {
            GUI.DrawTexture(new Rect(0, 0, width, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, height-bar, width, bar), Texture2D.whiteTexture);
        }
        else if (side > 0)
        {
            GUI.DrawTexture(new Rect(0, 0, side, height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(width-side, 0, side, height), Texture2D.whiteTexture);
        }
        if (departing)
        {
            GUI.color = Color.white;
            var style = GUI.skin.label;
            GUI.Label(new Rect(width * .5f - 78, height - Mathf.Max(bar, 40f) * .7f, 190, 28),
                $"Walking home in {Mathf.CeilToInt(departureRemaining)}s", style);
        }
        if (fadeAlpha > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, fadeAlpha);
            GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
        }
        GUI.color = old;
    }

    private void OnDestroy() { if (displayMaterial) Destroy(displayMaterial); }
}
