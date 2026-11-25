using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    [SerializeField] private GameObject buildCamera;
    [SerializeField] private GameObject buildCanvas;
    [SerializeField] private GameObject simulationCanvas;
    [SerializeField] private GameObject simulationCamera;
    [SerializeField] private SimulationManager simulationManager;
    [SerializeField] private CinematicShot cinematicShot;
    [SerializeField] private BuildManager buildManager;
    [SerializeField] private GameObject successCanvas;
    [SerializeField] private UnityEngine.UI.Button uploadButton;
    private readonly Queue<ShotSegment> pendingSegments = new(ShotSegmentHistory.Capacity);
    private string lastToolType;
    private float? lastToolTime;
    private readonly List<string> usedTrickTools = new();
    private readonly HashSet<GameObject> hitObjects = new();
    private int similarVideoCount;

    public void SetSimilarVideoCount(int count)
    {
        similarVideoCount = Mathf.Clamp(count, 0, MainSceneGameManager.MaxSimilarVideos);
    }

    private bool showingSuccess;
    private bool inSimulation;
    private bool showingShot;
    private Camera simulationView;
    private SimulationCameraMovement simulationMovement;
    [SerializeField, Min(0f)] private float transitionDuration = 0.5f;
    private bool transitioning;
    public bool IsTransitioning => transitioning;

    // 월드 중심 (0, 50, 0), 크기 (50, 100, 50): X/Z -25~25, Y 0~100.
    [SerializeField] private Bounds buildArea = new Bounds(new Vector3(0f, 50f, 0f), new Vector3(50f, 100f, 50f));
    public Bounds BuildArea => buildArea;
    public GameObject BuildCamera => buildCamera;

    private PingPongBallController ball;
    private Pose savedBallPose;
    public event System.Action LeavingBuildMode;

    private static StudioField lastUpload;
    private StudioField simulationField;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ResetFieldHistory() => lastUpload = null;

    private static StudioField CurrentField => ProgressSave.Data?.draft is StudioField draft &&
        !string.IsNullOrEmpty(draft.scenePath) ? draft : ProgressSave.Data?.uploaded ?? lastUpload;

    internal static SavedTool[] LastFieldTools(string scenePath) =>
        CurrentField?.scenePath == scenePath ? CurrentField.tools : null;

    private void Start()
    {
        Application.targetFrameRate = 60;

        if (buildCamera == null || simulationCamera == null || buildCamera == simulationCamera)
        {
            Debug.LogWarning("Inspector에서 서로 다른 두 카메라를 연결해 주세요.", this);
            enabled = false;
            return;
        }

        if (simulationManager == null)
            simulationManager = FindFirstObjectByType<SimulationManager>();

        if (simulationManager != null)
            ball = simulationManager.Ball;
        simulationView = simulationCamera.GetComponent<Camera>();
        simulationMovement = simulationCamera.GetComponent<SimulationCameraMovement>();

        if (ball != null)
        {
            ball.DeadZoneEntered += RespawnBall;
            ball.GoalBodyHit += OnGoalBodyHit;
            ball.GoalScored += ShowSuccess;
            ball.TrickToolHit += RecordTool;
        }
        simulationManager.LaunchRecorded += ClearUsedTools;
        successCanvas.SetActive(false);

        if (CurrentField?.scenePath == gameObject.scene.path)
        {
            if (ball != null) ball.ResetToPose(CurrentField.ballPose);
            var goal = GameObject.FindWithTag("Goal");
            if (goal != null && CurrentField.hasGoal)
                goal.transform.SetPositionAndRotation(CurrentField.goalPose.position, CurrentField.goalPose.rotation);
        }
        SetBuildMode(true);
    }

    private StudioField CaptureField()
    {
        var goal = GameObject.FindWithTag("Goal");
        return new StudioField {
            scenePath = gameObject.scene.path, ballPose = new Pose(ball.transform.position, ball.transform.rotation),
            hasGoal = goal != null, goalPose = goal ? new Pose(goal.transform.position, goal.transform.rotation) : Pose.identity,
            tools = buildManager.CaptureTools()
        };
    }

    internal void SaveBuildField()
    {
        if (ProgressSave.Data == null || !ball) return;
        ProgressSave.Data.draft = CaptureField();
        ProgressSave.Enter("StudioScene");
    }

    private void SaveBallPose()
    {
        savedBallPose = new Pose(ball.transform.position, ball.transform.rotation);
    }

    public void OpenSettings()
    {
        if (!showingSuccess && !transitioning && !inSimulation) SettingsWindow.Open();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (SettingsWindow.BlocksInput || showingSuccess || transitioning || keyboard == null)
            return;
        if (!inSimulation && keyboard.f1Key.wasPressedThisFrame) { OpenSettings(); return; }
        if (!keyboard.pKey.wasPressedThisFrame)
        {
            if (keyboard.rKey.wasPressedThisFrame && inSimulation &&
                simulationManager != null && simulationManager.HasLaunched)
                RespawnBall();
            return;
        }

        if (!inSimulation)
        {
            if (ball == null)
            {
                Debug.LogWarning("위치를 저장할 PingPongBall을 찾을 수 없습니다.", this);
                return;
            }

            LeavingBuildMode?.Invoke();
            SaveBallPose();
            simulationField = CaptureField();
            StartCoroutine(TransitionTo(false));
        }
        else
        {
            ReturnToBuildMode();
        }
    }

    private void ReturnToBuildMode()
    {
        // P를 누르면 공을 복원한 뒤 빌드 모드로 전환한다.
        // 복귀 후 들어오는 추가 충돌 알림은 무시한다.
        if (!inSimulation)
            return;

        if (ball != null)
            ball.ResetToPose(savedBallPose);
        StartCoroutine(TransitionTo(true));
    }

    private IEnumerator TransitionTo(bool toBuild)
    {
        transitioning = true;
        Camera source = (toBuild ? simulationCamera : buildCamera).GetComponent<Camera>();
        Camera target = (toBuild ? buildCamera : simulationCamera).GetComponent<Camera>();
        Vector3 fromPosition = source.transform.position;
        Quaternion fromRotation = source.transform.rotation;
        float fromFov = source.fieldOfView;
        if (showingShot)
        {
            Camera shotView = cinematicShot.GetComponent<Camera>();
            fromPosition = shotView.transform.position;
            fromRotation = shotView.transform.rotation;
            fromFov = shotView.fieldOfView;
            EndShot();
        }
        Vector3 originalPosition = source.transform.position;
        Quaternion originalRotation = source.transform.rotation;
        float originalFov = source.fieldOfView;

        // 비활성 상태에서 발사 구도를 준비해 도착 후 카메라가 다시 움직이지 않게 한다.
        if (!toBuild && simulationCamera.TryGetComponent<SimulationCameraMovement>(out var movement))
            movement.PrepareLaunchView();

        if (transitionDuration > 0f)
        {
            Behaviour controls = toBuild ? (Behaviour)source.GetComponent<SimulationCameraMovement>()
                : source.GetComponent<BuildCameraMovement>();
            bool restoreControls = controls != null && controls.enabled;
            if (restoreControls) controls.enabled = false;
            if (simulationManager != null) simulationManager.enabled = false;
            UpdateModeCanvases(false);
            if (ball != null) ball.ShowTrajectory(false);
            float startTime = Time.unscaledTime;
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                float t = CameraEase(Time.unscaledTime - startTime, transitionDuration);
                source.transform.SetPositionAndRotation(
                    Vector3.Lerp(fromPosition, target.transform.position, t),
                    Quaternion.Slerp(fromRotation, target.transform.rotation, t));
                source.fieldOfView = Mathf.Lerp(fromFov, target.fieldOfView, t);
                if (Time.unscaledTime - startTime >= transitionDuration) break;
                yield return null;
            }
            // 마지막 연출 프레임의 클릭이 설치나 발사 입력으로 전달되지 않게 한다.
            yield return endOfFrame;
            source.gameObject.SetActive(false);
            // 다음 복귀 때 사용할 원래 시점을 화면에 보이지 않는 상태에서 복원한다.
            source.transform.SetPositionAndRotation(originalPosition, originalRotation);
            source.fieldOfView = originalFov;
            if (restoreControls) controls.enabled = true;
        }
        SetBuildMode(toBuild);
        transitioning = false;
    }

    private void RespawnBall()
    {
        if (showingSuccess || transitioning || !inSimulation || ball == null)
            return;

        if (showingShot) EndShot();
        // 시뮬레이션 진입 때 저장한 좌표로 부활하고 다음 발사를 준비한다.
        ball.ResetToPose(savedBallPose);
        if (simulationManager != null)
            simulationManager.ResetLaunchInput();
        if (simulationCamera.TryGetComponent(out SimulationCameraMovement movement))
            movement.RestoreLaunchView();
    }

    private void OnDestroy()
    {
        if (ball != null)
        {
            ball.DeadZoneEntered -= RespawnBall;
            ball.GoalBodyHit -= OnGoalBodyHit;
            ball.GoalScored -= ShowSuccess;
            ball.TrickToolHit -= RecordTool;
        }
        if (simulationManager != null) simulationManager.LaunchRecorded -= ClearUsedTools;
    }

    private void SetBuildMode(bool buildMode)
    {
        inSimulation = !buildMode;
        if (buildMode && simulationCamera.TryGetComponent<SimulationCameraMovement>(out var movement))
            movement.RestoreBuildGuide();
        if (ball != null) ball.ShowTrajectory(buildMode);
        buildCamera.SetActive(buildMode);
        simulationCamera.SetActive(!buildMode);
        UpdateModeCanvases(true);

        // 빌드 화면에서 클릭해도 공의 발사 기회를 소모하지 않게 한다.
        if (simulationManager != null)
            simulationManager.enabled = !buildMode;
    }

    internal static float CameraEase(float elapsed, float duration)
    {
        return duration <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, elapsed / duration);
    }

    private void LateUpdate()
    {
        if (showingSuccess || !inSimulation || transitioning || cinematicShot == null || simulationManager == null) return;
        Collider goal = simulationManager.SlowMotionGoal;
        if (simulationManager.GoalPassExitDue) goal = null;
        if (!showingShot)
        {
            if (goal == null || ball == null) return;
            cinematicShot.Begin(simulationView);
            simulationManager.WatchCinematicPass(true);
            showingShot = true;
            UpdateModeCanvases(false);
        }
        // 비활성 시뮬레이션 카메라의 복귀 구도도 현재 공 위치에 맞춘다.
        if (simulationMovement != null) simulationMovement.LookAtBall();
        if (cinematicShot.Step(ball != null ? ball.transform : null, goal, simulationView, transitionDuration))
            EndShot();
    }

    private void OnGoalBodyHit()
    {
        if (showingSuccess || !inSimulation || transitioning) return;
        simulationManager.InterruptGoalSlowdown();
    }

    private void ClearUsedTools()
    {
        hitObjects.Clear();
        similarVideoCount = 0;
        usedTrickTools.Clear();
        pendingSegments.Clear();
        lastToolTime = null;
    }

    private void FinishSegment(string target)
    {
        if (!lastToolTime.HasValue) return;
        float duration = ball.ElapsedFlightTime - lastToolTime.Value;
        if (duration > 0.01f)
            ShotSegmentHistory.Append(pendingSegments, new ShotSegment(lastToolType, target, duration));
    }

    private void RecordTool(GameObject tool)
    {
        hitObjects.Add(tool);
        string type = buildManager.GetToolTypeName(tool);
        FinishSegment(type);
        lastToolType = type;
        lastToolTime = ball.ElapsedFlightTime;
        if (!usedTrickTools.Contains(type)) usedTrickTools.Add(type);
    }

    private void ShowSuccess()
    {
        if (showingSuccess || !inSimulation) return;
        showingSuccess = true;
        FinishSegment("GoalZone");
        lastToolTime = null;
        simulationManager.enabled = false;
        if (showingShot) EndShot();
        if (simulationMovement != null) simulationMovement.LookAtBall();
        simulationCamera.SetActive(true);
        UpdateModeCanvases(false);
        successCanvas.SetActive(true);
    }

    public void UploadShot()
    {
        if (!showingSuccess || !uploadButton.interactable) return;
        MainSceneGameManager.QueueUpload(hitObjects.Count, ball.ElapsedFlightTime,
            usedTrickTools.Count, ball.BounceCount, similarVideoCount);
        var data = ProgressSave.Data;
        if (data != null)
        {
            data.uploaded = data.draft = simulationField;
            var history = new Queue<ShotSegment>(ShotSegmentHistory.GetRecentSegments());
            foreach (var segment in pendingSegments) ShotSegmentHistory.Append(history, segment);
            data.segments = history.ToArray();
            if (!ProgressSave.Enter("MainScene")) return;
        }
        lastUpload = simulationField;
        ShotSegmentHistory.Save(pendingSegments);
        pendingSegments.Clear();
        uploadButton.interactable = false;
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainScene");
    }

    private void EndShot()
    {
        cinematicShot.StopShot();
        simulationManager.WatchCinematicPass(false);
        showingShot = false;
        simulationCamera.SetActive(true);
        UpdateModeCanvases(true);
    }

    private void UpdateModeCanvases(bool showUI)
    {
        showUI &= !showingSuccess;
        if (buildCanvas != null)
            buildCanvas.SetActive(showUI && buildCamera.activeInHierarchy);
        if (simulationCanvas != null)
            simulationCanvas.SetActive(showUI && simulationCamera.activeInHierarchy);
    }
}
