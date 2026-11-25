using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CinematicShot : MonoBehaviour
{
    [SerializeField] private Canvas cinematicCanvas;
    [SerializeField] private RectTransform topBar;
    [SerializeField] private RectTransform bottomBar;
    [SerializeField, Range(0f, 0.45f)] private float barHeight = 0.12f;
    [SerializeField, Min(0.01f)] private float radius = 1f;
    [SerializeField, Min(0.01f)] private float shotInterval = 2f;
    [SerializeField, Min(0.01f)] private float cameraClearance = 0.15f;

    private Camera view;
    private float shotFov, elapsed, shotAge, fromFov, bars, fromBars;
    private bool returning;
    private bool firstShot;
    private float blendDuration;
    private Vector3 fromPosition, destination;
    private Quaternion fromRotation;
    private const float RetryInterval = 0.2f;
    private float retryDelay;
    private RaycastHit[] sightHits = new RaycastHit[8];

    public void Begin(Camera source)
    {
        view = GetComponent<Camera>();
        shotFov = view.fieldOfView;
        transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        view.fieldOfView = source.fieldOfView;
        source.gameObject.SetActive(false);
        gameObject.SetActive(true);
        SetBars(0f);
        cinematicCanvas.gameObject.SetActive(true);
        returning = false;
        firstShot = true;
        retryDelay = 0f;
        shotAge = shotInterval; // 첫 프레임에도 촬영점을 고른다.
    }

    // true이면 원래 카메라와 같은 구도가 되어 화면을 넘길 수 있다.
    public bool Step(Transform ball, Collider goal, Camera simulationView, float duration)
    {
        float dt = Time.unscaledDeltaTime;
        bool exit = goal == null || ball == null;
        shotAge += dt;
        retryDelay -= dt;
        bool changeMode = exit != returning || firstShot;
        bool needPosition = !exit && (changeMode || (retryDelay <= 0f &&
            (shotAge >= shotInterval || !HasSight(destination, ball))));
        bool foundPosition = false;
        Vector3 candidate = transform.position;
        if (needPosition)
        {
            foundPosition = TryPickPosition(ball, goal, out candidate);
            retryDelay = RetryInterval;
            shotAge = foundPosition ? 0f : shotInterval;
            if (!foundPosition)
                fromPosition = destination = transform.position;
        }
        if (changeMode || foundPosition)
        {
            blendDuration = !exit && (firstShot || returning) ? duration / 3f : duration;
            firstShot = false;
            returning = exit;
            fromPosition = transform.position;
            fromRotation = transform.rotation;
            fromFov = view.fieldOfView;
            fromBars = bars;
            elapsed = 0f;
            destination = exit ? simulationView.transform.position : candidate;
        }
        float t = GameManager.CameraEase(elapsed, blendDuration);
        Vector3 end = returning ? simulationView.transform.position : destination;
        Vector3 next = Vector3.Lerp(fromPosition, end, t);
        // 후보를 정한 뒤 물체가 경로로 들어온 경우에도 다음 샘플까지 대기한다.
        if (!returning && next != transform.position && !ClearPath(transform.position, next))
        {
            fromPosition = destination = next = transform.position;
            shotAge = shotInterval;
        }
        Vector3 aim = returning ? Vector3.zero : ball.position - next;
        Quaternion facing = returning ? simulationView.transform.rotation :
            (aim.sqrMagnitude > 0.000001f ? Quaternion.LookRotation(aim, Vector3.up) : transform.rotation);
        transform.SetPositionAndRotation(next, Quaternion.Slerp(fromRotation, facing, t));
        view.fieldOfView = Mathf.Lerp(fromFov, returning ? simulationView.fieldOfView : shotFov, t);
        SetBars(Mathf.Lerp(fromBars, returning ? 0f : barHeight, t));
        elapsed += dt;
        return returning && t >= 1f;
    }

    private bool TryPickPosition(Transform ball, Collider goal, out Vector3 position)
    {
        position = transform.position;
        Vector3 center = ball.position;
        Vector3 away = center - goal.bounds.center;
        if (away.sqrMagnitude < 0.000001f) return false;
        away.Normalize();
        for (int i = 0; i < 32; i++)
        {
            Vector3 u = Random.onUnitSphere;
            if (Vector3.Dot(u, away) < 0f) u = -u;
            Vector3 candidate = center + u * radius;
            if (ClearPath(transform.position, candidate) && HasSight(candidate, ball))
            {
                position = candidate;
                return true;
            }
        }
        return false;
    }

    private bool HasSight(Vector3 eye, Transform ball)
    {
        Vector3 direction = ball.position - eye;
        float distance = direction.magnitude;
        if (distance < 0.0001f) return true;
        while (true)
        {
            int count = Physics.RaycastNonAlloc(eye, direction / distance, sightHits, distance,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!sightHits[i].collider.transform.IsChildOf(ball)) return false;
            if (count < sightHits.Length) return true;
            System.Array.Resize(ref sightHits, sightHits.Length * 2);
        }
    }

    private bool ClearPath(Vector3 start, Vector3 end)
    {
        return !Physics.CheckCapsule(start, end, cameraClearance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
    }

    private void SetBars(float height)
    {
        bars = height;
        topBar.anchorMin = new Vector2(0f, 1f - height);
        bottomBar.anchorMax = new Vector2(1f, height);
    }

    public void StopShot()
    {
        cinematicCanvas.gameObject.SetActive(false);
        gameObject.SetActive(false);
        view.fieldOfView = shotFov;
    }
}
