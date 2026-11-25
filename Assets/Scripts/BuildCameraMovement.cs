using UnityEngine;
using UnityEngine.InputSystem;

public class BuildCameraMovement : MonoBehaviour
{
    [SerializeField] private CameraMovementSettings movementSettings;
    private const float MinimumCameraHeight = 1f;
    [SerializeField] private GameManager gameManager;
    private Vector3 velocity;
    private float yaw;
    private float pitch;

    private void OnEnable()
    {
        // 카메라가 씬에서 바라보던 방향부터 회전을 시작한다.
        if (gameManager == null)
            gameManager = FindFirstObjectByType<GameManager>();
        velocity = Vector3.zero;
        ApplyMovementLimits(transform.position);
        yaw = transform.eulerAngles.y;
        pitch = Mathf.DeltaAngle(0f, transform.eulerAngles.x);
        SetCursorLocked(Application.isFocused);
    }

    private void OnDisable() => SetCursorLocked(false);

    private void OnApplicationFocus(bool focused)
    {
        if (isActiveAndEnabled && !SettingsWindow.BlocksInput) SetCursorLocked(focused);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void Update()
    {
        if (SettingsWindow.BlocksInput) { velocity = Vector3.zero; return; }
        Vector2 delta = movementSettings.ReadLookDelta();
        if (delta != Vector2.zero)
        {
            yaw += delta.x;
            // 마우스를 위로 움직이면 위를 보고, 고개를 끝까지 들어도 뒤집히지 않게 한다.
            pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        Keyboard keyboard = Keyboard.current;
        float vertical = keyboard == null ? 0f : keyboard.spaceKey.ReadValue() - keyboard.shiftKey.ReadValue();

        // 좌우 회전만 적용해서 위나 아래를 보더라도 WASD는 수평으로 이동한다.
        Vector3 input = movementSettings.ReadMoveInput(vertical);
        velocity = movementSettings.UpdateVelocity(velocity, input, yaw, Time.deltaTime);

        ApplyMovementLimits(transform.position + velocity * Time.deltaTime);
    }

    private void ApplyMovementLimits(Vector3 nextPosition)
    {
        if (gameManager == null)
            return;
        Bounds area = gameManager.BuildArea;
        // 경계를 넘는 축만 멈춰서 다른 방향으로는 계속 이동할 수 있다.
        if (nextPosition.x < area.min.x)
        {
            nextPosition.x = area.min.x;
            velocity.x = 0f;
        }
        else if (nextPosition.x > area.max.x)
        {
            nextPosition.x = area.max.x;
            velocity.x = 0f;
        }

        // 카메라는 설치 범위 안에서도 최소 높이 1을 유지한다.
        float minimumY = Mathf.Max(area.min.y, MinimumCameraHeight);
        if (nextPosition.y < minimumY)
        {
            nextPosition.y = minimumY;
            velocity.y = 0f;
        }
        else if (nextPosition.y > area.max.y)
        {
            nextPosition.y = area.max.y;
            velocity.y = 0f;
        }

        if (nextPosition.z < area.min.z)
        {
            nextPosition.z = area.min.z;
            velocity.z = 0f;
        }
        else if (nextPosition.z > area.max.z)
        {
            nextPosition.z = area.max.z;
            velocity.z = 0f;
        }

        transform.position = nextPosition;
    }
}
