using UnityEngine;
using UnityEngine.UI;

public class SimulationManager : MonoBehaviour
{
    [SerializeField] private Camera simulationCamera;
    [SerializeField] private Image launchPowerFillImage;
    [SerializeField, Min(0f)] private float goalSlowDistance = 1.5f;
    [Tooltip("컵 입구에서 로컬 위쪽으로 종료 평면을 올리는 월드 거리")]
    [SerializeField, Min(0f)] private float goalExitHeightOffset = 0.05f;
    [SerializeField, Range(0.05f, 1f)] private float goalSpeedMultiplier = 0.175f;
    [SerializeField, Min(0.001f)] private float stallRadius = 0.03f;
    [SerializeField, Min(0.05f)] private float stallSeconds = 0.6f;
    [SerializeField, Min(0.01f)] private float resumeSpeedGain = 0.15f;
    private Vector3 motionAnchor;
    private float stationaryTime, lowestNormalSpeed;
    private bool waitingForAcceleration;
    private readonly System.Collections.Generic.Dictionary<Collider, Transform> goalColliders = new();
    private Transform trackedGoal;
    private Vector3 localRimPoint;
    private bool watchingShot;
    private float previousSide;
    private double? shotExitAt;
    private double resumeGoalSlowdownAt;
    public bool GoalPassExitDue => shotExitAt.HasValue && Time.unscaledTimeAsDouble >= shotExitAt.Value;
    private Collider[] nearby = new Collider[16];
    private SphereCollider ballCollider;
    // 발사 세기에 반영할 최대 누름 시간(초).
    [Min(0f)] public float maxtime = 2f;

    private PingPongBallController ball;
    public PingPongBallController Ball => ball;
    private float? mouseDownTime;
    private bool hasLaunched;
    public bool HasLaunched => hasLaunched;
    public readonly struct LaunchRecord
    {
        public readonly Vector2 viewport;
        public readonly float impulse, gaugeFraction;

        public LaunchRecord(Vector2 viewport, float impulse, float maximum)
        {
            this.viewport = viewport;
            this.impulse = impulse;
            gaugeFraction = maximum > 0f ? Mathf.Clamp01(impulse / maximum) : 0f;
        }
    }

    public LaunchRecord? PreviousLaunch { get; private set; }
    public event System.Action LaunchRecorded;
    public Image PowerGauge => launchPowerFillImage;
    public Collider SlowMotionGoal { get; private set; }

    private void Awake()
    {
        // 시작할 때 공을 한 번 찾는다. TryGetComponent는 공에 붙은 발사 스크립트를 찾아 ball에 담는다.
        GameObject target = GameObject.FindGameObjectWithTag("Player");
        if (target == null || !target.TryGetComponent(out ball))
            Debug.LogWarning("PingPongBall needs a PingPongBallController.", this);
        if (ball != null) ballCollider = ball.GetComponent<SphereCollider>();
    }

    private void OnEnable()
    {
        ResetLaunchInput();
        goalColliders.Clear();
        foreach (GameObject goal in GameObject.FindGameObjectsWithTag("Goal"))
            foreach (Collider part in goal.GetComponentsInChildren<Collider>(true))
                if (!part.isTrigger) goalColliders[part] = goal.transform;
    }

    private void OnDisable()
    {
        if (ball != null) ball.SetFlightSpeed(1f);
        ResetLaunchInput();
    }

    private float GetGoalSpeed()
    {
        if (Time.unscaledTimeAsDouble < resumeGoalSlowdownAt) return 1f;
        Collider goal = FindNearbyGoal();
        if (goal == null || goalSpeedMultiplier >= 1f)
        {
            waitingForAcceleration = false;
            SlowMotionGoal = null;
            return 1f;
        }

        if (GoalPassExitDue) return 1f;

        Rigidbody body = ballCollider.attachedRigidbody;
        Vector3 position = body.position;
        float displacement = (position - motionAnchor).sqrMagnitude;
        if (waitingForAcceleration)
        {
            float speed = body.linearVelocity.magnitude;
            lowestNormalSpeed = Mathf.Min(lowestNormalSpeed, speed);
            if (displacement <= stallRadius * stallRadius ||
                speed - lowestNormalSpeed < resumeSpeedGain) return 1f;
            waitingForAcceleration = false;
        }
        if (SlowMotionGoal == null || displacement > stallRadius * stallRadius)
        {
            motionAnchor = position;
            stationaryTime = 0f;
        }
        else if ((stationaryTime += Time.fixedUnscaledDeltaTime) >= stallSeconds)
        {
            motionAnchor = position;
            waitingForAcceleration = true;
            SlowMotionGoal = null;
            return 1f;
        }
        SlowMotionGoal = goal;
        return goalSpeedMultiplier;
    }

    private Collider FindNearbyGoal()
    {
        if (ballCollider == null || goalColliders.Count == 0) return null;
        // 구의 반지름에 임계 거리를 더한 실제 물리 쿼리로 표면 간 거리를 검사한다.
        Bounds sphere = ballCollider.bounds;
        int count;
        do
        {
            count = Physics.OverlapSphereNonAlloc(sphere.center, sphere.extents.x + goalSlowDistance,
                nearby, Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                if (goalColliders.ContainsKey(nearby[i])) return nearby[i];
            if (count < nearby.Length) break;
            System.Array.Resize(ref nearby, nearby.Length * 2);
        } while (true);
        return null;
    }

    private void FixedUpdate()
    {
        if (!hasLaunched || ball == null) return;
        bool wasWaiting = waitingForAcceleration;
        float speed = GetGoalSpeed();
        if (SlowMotionGoal == null)
            ClearGoalPass();
        else if (watchingShot)
            SampleGoalSide();
        ball.UpdateFlightSpeed(GoalPassExitDue ? 1f : speed);
        // 배속 복구로 바뀐 속도부터 비교해야 그 변화 자체를 재가속으로 세지 않는다.
        if (!wasWaiting && waitingForAcceleration)
            lowestNormalSpeed = ballCollider.attachedRigidbody.linearVelocity.magnitude;
    }

    public void ResetLaunchInput()
    {
        resumeGoalSlowdownAt = 0;
        watchingShot = false;
        ClearGoalPass();
        mouseDownTime = null;
        hasLaunched = false;
        SlowMotionGoal = null;
        waitingForAcceleration = false;
        stationaryTime = 0f;
        if (launchPowerFillImage != null) launchPowerFillImage.fillAmount = 0f;
    }

    public void WatchCinematicPass(bool active)
    {
        watchingShot = active;
        if (active) SampleGoalSide();
        else
        {
            trackedGoal = null;
            if (!GoalPassExitDue) shotExitAt = null;
        }
        // A completed deadline suppresses re-entry until the current slowdown ends.
    }

    public void InterruptGoalSlowdown()
    {
        if (SlowMotionGoal == null) return;
        resumeGoalSlowdownAt = Time.unscaledTimeAsDouble + 0.5;
        SlowMotionGoal = null;
        ClearGoalPass();
        ball.SetFlightSpeed(1f);
    }

    private void ClearGoalPass()
    {
        trackedGoal = null;
        shotExitAt = null;
    }

    private void SampleGoalSide()
    {
        if (shotExitAt.HasValue || SlowMotionGoal == null) return;
        Transform goal = goalColliders[SlowMotionGoal];
        if (trackedGoal != goal)
        {
            // RedCupDemo의 피벗과 입구 높이가 다르므로 모델 상단을 기준으로 삼는다.
            localRimPoint = Vector3.zero;
            if (goal.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                Bounds bounds = filter.sharedMesh.bounds;
                localRimPoint = bounds.center + Vector3.up * bounds.extents.y;
            }
        }
        Vector3 up = goal.up;
        Vector3 rim = goal.TransformPoint(localRimPoint) + up * goalExitHeightOffset;
        float side = Vector3.Dot(ballCollider.attachedRigidbody.position - rim, up);
        bool crossed = trackedGoal == goal && previousSide >= 0f && side < 0f;
        trackedGoal = goal;
        previousSide = side;
        if (!crossed) return;
        foreach (GameObject tool in GameObject.FindGameObjectsWithTag("TrickTool"))
            if (Vector3.Dot(tool.transform.position - rim, up) < 0f) return;
        shotExitAt = Time.unscaledTimeAsDouble;
        ball.SetFlightSpeed(1f);
    }

    private void Update()
    {
        // 한 번 발사한 뒤에는 마우스를 다시 눌러도 입력을 처리하지 않는다.
        if (hasLaunched)
            return;

        if (ball == null)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            mouseDownTime = Time.unscaledTime;
        }

        if (!mouseDownTime.HasValue)
            return;

        float holdTime = Mathf.Min(Time.unscaledTime - mouseDownTime.Value, maxtime);
        if (!Input.GetMouseButtonUp(0))
        {
            if (!Input.GetMouseButton(0))
                ResetLaunchInput();
            else if (launchPowerFillImage != null)
                launchPowerFillImage.fillAmount = maxtime > 0f ? holdTime / maxtime : 0f;
            return;
        }
        ResetLaunchInput();
        if (holdTime <= 0f)
            return;

        // Inspector에서 지정한 카메라를 쓴다. 비어 있으면 MainCamera 태그가 붙은 카메라를 쓴다.
        Camera camera = simulationCamera != null ? simulationCamera : Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("Assign a simulation camera to launch the ball.", this);
            return;
        }

        // 화면의 마우스 위치를 지나 장면 안쪽으로 뻗는 가상의 선(Ray)을 만든다.
        LaunchFromScreen(camera, Input.mousePosition, holdTime);
    }

    private void LaunchFromScreen(Camera camera, Vector3 mousePosition, float holdTime)
    {
        Ray ray = camera.ScreenPointToRay(mousePosition);
        // 선의 방향만 꺼낸다. 공은 자신의 현재 위치에서 이 방향으로 날아간다.
        Vector3 launchDirection = ray.direction;
        float impulse = ball.LaunchBall(holdTime, launchDirection, GetGoalSpeed());
        hasLaunched = true;
        if (impulse > 0f)
        {
            PreviousLaunch = new LaunchRecord(camera.ScreenToViewportPoint(mousePosition), impulse, ball.LaunchImpulse(maxtime));
            LaunchRecorded?.Invoke();
        }
    }
}
