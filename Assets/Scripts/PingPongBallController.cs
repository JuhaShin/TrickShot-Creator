using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Rigidbody))]
public class PingPongBallController : MonoBehaviour
{
    [Tooltip("공 편집 시 미리보기 색을 적용할 렌더러. 비워 두면 공 루트의 렌더러만 사용합니다.")]
    [SerializeField] private Renderer[] previewRenderers;

    public bool UsesPreviewMaterial(Renderer candidate)
    {
        if (previewRenderers == null || previewRenderers.Length == 0)
            return candidate.transform == transform;
        return System.Array.IndexOf(previewRenderers, candidate) >= 0;
    }

    // Inspector에서 버튼을 1초 눌렀을 때 줄 충격량을 조절할 수 있다.
    [SerializeField, Min(0f)] private float impulsePerSecond = 1f;
    [SerializeField, Min(3)] private int maxGhosts = 384;
    [Tooltip("잔상 사이의 기본 월드 거리입니다.")]
    [SerializeField, Min(0.01f)] private float ghostSpacing = 0.05f;
    [SerializeField, Range(0f, 1f)] private float ghostAlpha = 0.25f;
    private readonly List<Vector3> samples = new();
    private Vector3 latestPosition;
    private float sampleSpacing, distanceSinceSample;
    private Vector3 previousDirection;
    private bool recording;
    private bool showTrajectory;
    private Mesh ghostMesh;
    private Material ghostMaterial;
    private Rigidbody body;
    private float flightSpeed = 1f;
    private float? originalBounceThreshold;
    public event System.Action DeadZoneEntered;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (TryGetComponent<MeshFilter>(out var filter))
            ghostMesh = filter.sharedMesh;
        Shader shader = Resources.Load<Shader>("BuildPlacementPreview");
        if (shader != null) ghostMaterial = new Material(shader);
        // 배치 중에는 충돌이나 중력이 공의 좌표를 바꾸지 않게 한다.
        body.useGravity = false;
        body.isKinematic = true;
    }

    public event System.Action GoalBodyHit;
    public event System.Action GoalScored;
    public event System.Action<GameObject> TrickToolHit;
    public int BounceCount { get; private set; }
    public float ElapsedFlightTime { get; private set; }
    public bool HasScored { get; private set; }
    private enum GoalContact { None, PendingBody, Resolved }
    private GoalContact firstContact;

    private void OnCollisionEnter(Collision collision)
    {
        if (body.isKinematic || HasScored) return;
        for (Transform part = collision.collider.transform; part != null; part = part.parent)
        {
            if (part.CompareTag("TrickTool"))
            {
                BounceCount++;
                TrickToolHit?.Invoke(part.gameObject);
                return;
            }
            if (!part.CompareTag("Goal")) continue;
            if (firstContact == GoalContact.None) firstContact = GoalContact.PendingBody;
            return;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (body.isKinematic || HasScored) return;

        if (other.CompareTag("GoalZone"))
        {
            firstContact = GoalContact.Resolved;
            HasScored = true;
            SetFlightSpeed(1f);
            //Debug.Log("골인!");
            GoalScored?.Invoke();
        }
        else if (other.CompareTag("DeadZone"))
            DeadZoneEntered?.Invoke();
    }

    public void ResetToPose(Pose pose)
    {
        HasScored = false;
        BounceCount = 0;
        ElapsedFlightTime = 0f;
        firstContact = GoalContact.None;
        if (recording) RecordSegment(body.position);
        recording = false;
        ApplyBounceThreshold(1f);
        flightSpeed = 1f;
        body.useGravity = false;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        body.position = pose.position;
        body.rotation = pose.rotation;
        transform.SetPositionAndRotation(pose.position, pose.rotation);
    }

    public float LaunchImpulse(float holdTime) => holdTime * impulsePerSecond * flightSpeed;

    public float LaunchBall(float holdTime, Vector3 direction, float speed = 1f)
    {
        float impulse = holdTime * impulsePerSecond * Mathf.Clamp(speed, 0.05f, 1f);
        return LaunchWithImpulse(impulse, direction, speed);
    }

    public float LaunchWithImpulse(float impulse, Vector3 direction, float speed)
    {
        if (impulse <= 0f || direction == Vector3.zero)
            return 0f;

        firstContact = GoalContact.None;
        samples.Clear();
        HasScored = false;
        BounceCount = 0;
        ElapsedFlightTime = 0f;
        latestPosition = body.position;
        samples.Add(latestPosition);
        sampleSpacing = Mathf.Max(0.01f, ghostSpacing);
        distanceSinceSample = 0f;
        previousDirection = Vector3.zero;
        recording = true;
        body.isKinematic = false;
        flightSpeed = Mathf.Clamp(speed, 0.05f, 1f);
        ApplyBounceThreshold(flightSpeed);
        body.useGravity = flightSpeed == 1f;
        // normalized는 방향을 유지하면서 길이만 1로 맞춘다. 누른 시간에 비례하는 충격량을 Impulse로 한 번에 준다.
        body.AddForce(direction.normalized * impulse, ForceMode.Impulse);
        return impulse;
    }

    public void SetFlightSpeed(float speed)
    {
        speed = Mathf.Clamp(speed, 0.05f, 1f);
        if (speed == flightSpeed || body.isKinematic) return;
        float ratio = speed / flightSpeed;
        body.linearVelocity *= ratio;
        body.angularVelocity *= ratio;
        flightSpeed = speed;
        ApplyBounceThreshold(speed);
        body.useGravity = speed == 1f;
    }

    // PhysX compares contact speed against a scene-wide threshold in world units per second.
    private void ApplyBounceThreshold(float speed)
    {
        if (speed < 1f)
        {
            originalBounceThreshold ??= Physics.bounceThreshold;
            Physics.bounceThreshold = originalBounceThreshold.Value * speed;
        }
        else if (originalBounceThreshold.HasValue)
        {
            Physics.bounceThreshold = originalBounceThreshold.Value;
            originalBounceThreshold = null;
        }
    }

    private void OnDisable() => ApplyBounceThreshold(1f);

    private void OnEnable()
    {
        if (flightSpeed < 1f) ApplyBounceThreshold(flightSpeed);
    }

    public void UpdateFlightSpeed(float speed)
    {
        SetFlightSpeed(speed);
        if (!body.isKinematic && !HasScored)
            ElapsedFlightTime += Time.fixedDeltaTime * flightSpeed;
        // 시간 배율의 제곱으로 중력을 줄여 느린 공도 같은 포물선을 따르게 한다.
        if (!body.isKinematic && flightSpeed < 1f)
            body.AddForce(Physics.gravity * (flightSpeed * flightSpeed), ForceMode.Acceleration);
    }
    public void SetBallVisible(bool visible)
    {
        GetComponent<Renderer>().enabled = visible;
        GetComponent<SphereCollider>().enabled = visible;
    }

    public void ShowTrajectory(bool visible) => showTrajectory = visible;

    // Flush after the contact callbacks, before the next physics step or rendered frame.
    private void ResolveGoalContact()
    {
        if (firstContact != GoalContact.PendingBody) return;
        firstContact = GoalContact.None;
        GoalBodyHit?.Invoke();
    }

    private void Update() => ResolveGoalContact();

    private void FixedUpdate()
    {
        ResolveGoalContact();
        if (recording) RecordSegment(body.position);
    }

    private void RecordSegment(Vector3 end)
    {
        Vector3 start = latestPosition;
        latestPosition = end;
        Vector3 delta = end - start;
        float length = delta.magnitude;
        if (length == 0f) return;
        Vector3 direction = delta / length;
        // 45도 이상 꺾인 지점을 남기되, 접촉 중 작은 떨림은 기록하지 않는다.
        if (previousDirection != Vector3.zero && Vector3.Dot(previousDirection, direction) < 0.7071f &&
            (start - samples[samples.Count - 1]).magnitude >= sampleSpacing * 0.25f)
        {
            AppendSample(start);
            distanceSinceSample = 0f;
        }
        previousDirection = direction;
        float walked = 0f;
        while (length - walked >= sampleSpacing - distanceSinceSample)
        {
            walked += sampleSpacing - distanceSinceSample;
            Vector3 point = start + direction * walked;
            distanceSinceSample = 0f;
            if ((point - samples[samples.Count - 1]).magnitude >= sampleSpacing * 0.5f)
                AppendSample(point);
        }
        distanceSinceSample += length - walked;
    }

    private void AppendSample(Vector3 point)
    {
        // 출발점과 표시할 마지막 위치의 자리를 확보한다.
        if (samples.Count >= Mathf.Max(3, maxGhosts) - 1)
        {
            int kept = 0;
            for (int i = 0; i < samples.Count; i += 2) samples[kept++] = samples[i];
            samples.RemoveRange(kept, samples.Count - kept);
            sampleSpacing *= 2f;
        }
        samples.Add(point);
    }

    private void LateUpdate()
    {
        if (!showTrajectory || samples.Count == 0 || ghostMesh == null || ghostMaterial == null) return;
        ghostMaterial.color = new Color(1f, 1f, 1f, ghostAlpha);
        var settings = new RenderParams(ghostMaterial)
        {
            layer = gameObject.layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false
        };
        Vector3 scale = transform.lossyScale;
        foreach (Vector3 position in samples)
            Graphics.RenderMesh(settings, ghostMesh, 0, Matrix4x4.TRS(position, Quaternion.identity, scale));
        if (latestPosition != samples[samples.Count - 1])
            Graphics.RenderMesh(settings, ghostMesh, 0, Matrix4x4.TRS(latestPosition, Quaternion.identity, scale));
    }

    private void OnDestroy() => Destroy(ghostMaterial);
}
