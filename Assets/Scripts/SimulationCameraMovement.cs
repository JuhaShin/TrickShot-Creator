using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SimulationCameraMovement : MonoBehaviour
{
    [SerializeField] private Transform ball;
    private Transform positionTarget;
    [SerializeField, Range(1f, 5f)] private float maxZoom = 5f;
    private float launchViewDistance;
    private Camera trackingCamera;
    private float initialFieldOfView;
    private float initialHalfFovTangent;
    private bool launchViewPrepared;
    private Vector3 launchCameraPosition;

    private void Awake()
    {
        InitializeCamera();
    }

    private void InitializeCamera()
    {
        if (trackingCamera != null) return;
        if (ball == null)
        {
            PingPongBallController controller = FindFirstObjectByType<PingPongBallController>();
            if (controller != null)
                ball = controller.transform;
        }

        trackingCamera = GetComponent<Camera>();
        initialFieldOfView = trackingCamera.fieldOfView;
        initialHalfFovTangent = Mathf.Tan(initialFieldOfView * Mathf.Deg2Rad * 0.5f);
    }

    private void LateUpdate()
    {
        if (launchViewPrepared) LookAtBall();
    }

    public void PrepareLaunchView()
    {
        InitializeCamera();
        if (ball == null)
            return;

        FindGuide();
        launchCameraPosition = positionTarget != null ? positionTarget.position : transform.position;
        if (positionTarget != null)
            positionTarget.gameObject.SetActive(false);
        launchViewPrepared = true;
        RestoreLaunchView();
    }

    private void FindGuide()
    {
        if (positionTarget == null && ball != null)
        {
            foreach (Transform child in ball.GetComponentsInChildren<Transform>(true))
                if (child.name == "PersonDemo2")
                {
                    positionTarget = child;
                    break;
                }
        }
    }

    public void RestoreBuildGuide()
    {
        FindGuide();
        if (positionTarget == null) return;
        positionTarget.gameObject.SetActive(true);
        launchViewPrepared = false;
    }

    public void RestoreLaunchView()
    {
        transform.position = launchCameraPosition;
        if (ball != null)
            launchViewDistance = Vector3.Distance(trackingCamera.transform.position, ball.position);
        LookAtBall();
    }

    public void LookAtBall()
    {
        if (ball == null)
            return;

        // 공 위치에서 카메라 위치를 빼면 카메라에서 공으로 향하는 방향이 된다.
        Vector3 direction = ball.position - transform.position;
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        float distance = direction.magnitude;
        if (distance <= launchViewDistance)
        {
            trackingCamera.fieldOfView = initialFieldOfView;
            return;
        }

        // 발사 구도보다 카메라에서 3유닛 멀어질 때마다 1배씩 확대한다.
        float zoom = Mathf.Min(1f + (distance - launchViewDistance) / 3f, maxZoom);
        trackingCamera.fieldOfView = 2f * Mathf.Atan(initialHalfFovTangent / zoom) * Mathf.Rad2Deg;
    }
}
