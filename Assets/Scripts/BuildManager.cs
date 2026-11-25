using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

public class BuildManager : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;
    [SerializeField] private GameObject fryingPanPrefab;
    [SerializeField] private GameObject potPrefab;
    [SerializeField, Min(0)] private int maxFryingPanCount = 10;
    [SerializeField, Min(0)] private int maxPotCount = 10;
    [SerializeField] private Sprite FryingPanSprite;
    [SerializeField] private Sprite PotSprite;
    [System.Serializable]
    private struct ToolSlot
    {
        public UnityEngine.UI.Image icon;
        public TextMeshProUGUI remainingText;
        [System.NonSerialized] public GameObject prefab;
    }
    [SerializeField] private ToolSlot[] toolSlots;
    private readonly HashSet<GameObject> placedFryingPans = new();
    private readonly HashSet<GameObject> placedPots = new();

    private int RemainingFryingPans => Mathf.Max(0, maxFryingPanCount - placedFryingPans.Count);
    private int RemainingPots => Mathf.Max(0, maxPotCount - placedPots.Count);
    [Tooltip("표면에 붙인 뒤 추가할 월드 Y 보정값. 음수이면 아래로 이동합니다.")]
    [SerializeField] private float fryingPanHeightOffset = -0.18f;
    [SerializeField] private float potHeightOffset = -0.12f;

    [SerializeField, Min(0f)] private float rotationSpeed = 90f;
    private int rotationAxis;
    private int ballRotationAxis;

    private const float DefaultDistance = 5f;
    [SerializeField, Min(0.01f)] private float distanceSmoothTime = 0.2f;
    [SerializeField, Min(0.01f)] private float wheelSensitivity = 1f;
    private float placementDistance;
    private float targetDistance;
    private float distanceVelocity;
    private bool wheelAdjusted;
    private Vector3 editStartCameraPosition;
    private Quaternion editStartCameraRotation;
    private Vector3 editLocalDirection;
    [SerializeField, Range(0f, 1f)] private float previewAlpha = 0.45f;
    [Tooltip("설치물 사이에 확보할 월드 단위 여유 거리. 바닥과 벽에는 적용하지 않습니다.")]
    [SerializeField, Min(0f)] private float placementMargin = 0.1f;
    private bool editing;
    private SphereCollider ballSphere;
    private bool editingGoal;
    private Vector3 goalGrabPoint;
    private readonly List<Collider> roundGoalParts = new();
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private readonly Dictionary<Renderer, Material[]> materials = new();
    private readonly Dictionary<Renderer, ShadowCastingMode> shadows = new();
    private readonly List<Collider> enabledColliders = new();
    private const float Skin = 0.001f;
    private GameObject selectedPrefab;
    private GameObject preview;
    private Material previewMaterial;
    private PlacementShape[] shapes;
    private readonly List<BoxCollider> placementParts = new List<BoxCollider>();

    private void Start()
    {
        string[] order = MainSceneGameManager.TakeToolOrder();
        for (int i = 0; i < toolSlots.Length; i++)
        {
            bool pan = order[i] == MainSceneGameManager.FryingPanKey;
            toolSlots[i].prefab = pan ? fryingPanPrefab : potPrefab;
            toolSlots[i].icon.sprite = pan ? FryingPanSprite : PotSprite;
        }
        foreach (GameObject tool in GameObject.FindGameObjectsWithTag("TrickTool"))
            PlacedTools(FindToolPrefab(tool))?.Add(tool);
        if (GameManager.LastFieldTools(gameObject.scene.path) is { } savedTools)
        {
            foreach (var group in new[] { placedFryingPans, placedPots })
            {
                foreach (GameObject tool in group)
                {
                    tool.SetActive(false);
                    Destroy(tool);
                }
                group.Clear();
            }
            foreach (var tool in savedTools)
            {
                var prefab = tool.pan ? fryingPanPrefab : potPrefab;
                var instance = Instantiate(prefab, tool.pose.position, tool.pose.rotation);
                PlacedTools(prefab).Add(instance);
            }
        }
        UpdateRemainingText();
    }

    internal SavedTool[] CaptureTools()
    {
        var tools = new List<SavedTool>();
        foreach (var group in new[] { placedFryingPans, placedPots })
            foreach (GameObject tool in group)
                if (tool != null)
                    tools.Add(new SavedTool { pan = group == placedFryingPans, pose = new Pose(tool.transform.position, tool.transform.rotation) });
        return tools.ToArray();
    }

    private HashSet<GameObject> PlacedTools(GameObject prefab)
    {
        if (prefab == null) return null;
        if (prefab == fryingPanPrefab) return placedFryingPans;
        if (prefab == potPrefab) return placedPots;
        return null;
    }

    private void UpdateRemainingText()
    {
        foreach (ToolSlot slot in toolSlots)
        {
            int remaining = slot.prefab == fryingPanPrefab ? RemainingFryingPans : RemainingPots;
            slot.remainingText.text = $"x {remaining}";
        }
    }

    private void Awake()
    {
        if (gameManager == null)
            gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null) gameManager.LeavingBuildMode += ClearPreview;
        shapes = Resources.LoadAll<PlacementShape>("PlacementShapes");
        Shader shader = Resources.Load<Shader>("BuildPlacementPreview");
        if (shader == null)
        {
            Debug.LogError("BuildPlacementPreview 셰이더를 찾을 수 없습니다.", this);
            enabled = false;
            return;
        }
        previewMaterial = new Material(shader);
    }

    private void LateUpdate()
    {
        if (SettingsWindow.BlocksInput) return;
        if (gameManager == null || gameManager.IsTransitioning || gameManager.BuildCamera == null ||
            !gameManager.BuildCamera.activeInHierarchy)
        {
            ClearPreview();
            return;
        }

        if (preview != null && Input.GetKeyDown(KeyCode.Escape))
        {
            ClearPreview();
            return;
        }
        int slot = ReadSlot();
        GameObject requested = slot >= 1 && slot <= toolSlots.Length ? toolSlots[slot - 1].prefab : null;
        if (slot >= 0)
        {
            bool sameSelection = preview != null && requested == selectedPrefab;
            FinishPlacement(PlacementEnd.Discard);
            if (sameSelection || requested == null) return;
            if ((requested == fryingPanPrefab && RemainingFryingPans == 0) ||
                (requested == potPrefab && RemainingPots == 0)) return;
            BeginPlacement(requested, null);
        }
        Transform cameraTransform = gameManager.BuildCamera.transform;
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (preview == null)
        {
            if (Input.GetMouseButtonDown(0)) SelectAtRay(ray);
            return;
        }
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
        {
            targetDistance = editing && !wheelAdjusted && placementDistance < 3f ? 3f :
                Mathf.Clamp(targetDistance + scroll * wheelSensitivity, 3f, 10f);
            wheelAdjusted = true;
        }
        if (wheelAdjusted)
            placementDistance = Mathf.Clamp(Mathf.SmoothDamp(placementDistance, targetDistance,
                ref distanceVelocity, distanceSmoothTime), 3f, 10f);
        HandleRotationInput();
        if (editing && !wheelAdjusted && cameraTransform.position == editStartCameraPosition &&
            cameraTransform.rotation == editStartCameraRotation)
            preview.transform.position = originalPosition;
        else
        {
            if (editing) ray.direction = cameraTransform.TransformDirection(editLocalDirection);
            UpdatePlacementPosition(ray);
        }

        bool allowed = CanPlace();
        previewMaterial.color = allowed ? new Color(0f, 1f, 0f, previewAlpha) : new Color(1f, 0f, 0f, previewAlpha);
        if (requested == null && allowed && Input.GetMouseButtonDown(0))
        {
            if (!editing)
            {
                GameObject installed = Instantiate(selectedPrefab, preview.transform.position, preview.transform.rotation);
                PlacedTools(selectedPrefab)?.Add(installed);
                UpdateRemainingText();
            }
            FinishPlacement(PlacementEnd.Confirm);
            gameManager.SaveBuildField();
        }
    }

    private void UpdatePlacementPosition(Ray ray)
    {
        preview.transform.position = ray.GetPoint(placementDistance);
        if (editingGoal)
            preview.transform.position -= preview.transform.TransformVector(goalGrabPoint);
        bool hitSurface = Physics.Raycast(ray, out RaycastHit hit, placementDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (selectedPrefab == fryingPanPrefab && ballSphere == null &&
            (!hitSurface || IsHorizontalFloor(hit)))
        {
            ClampPanToFloor(ray.origin.y);
        }
        else if (hitSurface)
        {
            preview.transform.position = hit.point;
            if (editingGoal)
                preview.transform.position -= preview.transform.TransformVector(goalGrabPoint);
            float closest = ballSphere != null ? -BallRadius : float.PositiveInfinity;
            foreach (Collider part in roundGoalParts)
            {
                GetRoundSegment(part, out var a, out var b, out float radius);
                closest = Mathf.Min(closest, Mathf.Min(Vector3.Dot(a - hit.point, hit.normal),
                    Vector3.Dot(b - hit.point, hit.normal)) - radius);
            }
            foreach (BoxCollider part in placementParts)
            {
                Vector3 center = part.transform.TransformPoint(part.center);
                Vector3 extent = Vector3.Scale(part.size * 0.5f, Abs(part.transform.lossyScale));
                float radius = Mathf.Abs(Vector3.Dot(hit.normal, part.transform.right)) * extent.x
                    + Mathf.Abs(Vector3.Dot(hit.normal, part.transform.up)) * extent.y
                    + Mathf.Abs(Vector3.Dot(hit.normal, part.transform.forward)) * extent.z;
                closest = Mathf.Min(closest, Vector3.Dot(center - hit.point, hit.normal) - radius);
            }
            // 표면 법선 쪽으로 옮겨 바닥이나 벽에 겉면이 닿게 한다.
            preview.transform.position += hit.normal * (Skin - closest);

            // 보정으로 파묻힌 경우에는 아래의 CanPlace가 설치를 차단한다.
            float heightOffset = selectedPrefab == fryingPanPrefab ? fryingPanHeightOffset : potHeightOffset;
            if (selectedPrefab != null) preview.transform.position += Vector3.up * heightOffset;
        }

    }

    private static bool IsHorizontalFloor(RaycastHit hit)
    {
        return hit.normal.y > 0.999f && !IsPlacementObject(hit.collider);
    }

    private void ClampPanToFloor(float cameraY)
    {
        Vector3 target = preview.transform.position;
        float lowestY = float.PositiveInfinity;
        foreach (BoxCollider part in placementParts)
        {
            Transform t = part.transform;
            Vector3 extent = Vector3.Scale(part.size * 0.5f, Abs(t.lossyScale));
            float halfHeight = Mathf.Abs(t.right.y) * extent.x + Mathf.Abs(t.up.y) * extent.y
                + Mathf.Abs(t.forward.y) * extent.z;
            lowestY = Mathf.Min(lowestY, t.TransformPoint(part.center).y - halfHeight);
        }

        // 시선이 바닥을 통과해도 목표 X/Z를 유지하고, 밑면이 내려간 높이만 보정한다.
        float probeY = Mathf.Max(cameraY, target.y, lowestY) + Skin;
        Vector3 origin = new Vector3(target.x, probeY, target.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit floor, probeY - lowestY + Skin,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && IsHorizontalFloor(floor))
            preview.transform.position = target + Vector3.up * Mathf.Max(0f, floor.point.y + Skin - lowestY);
    }

    private static int ReadSlot()
    {
        for (int digit = 0; digit <= 9; digit++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + digit)) ||
                Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad0 + digit)))
                return digit;
        return -1;
    }

    private void SelectAtRay(Ray ray)
    {
        // 일반 트리거는 조준을 가리지 않고 골대 트리거만 선택할 수 있다.
        Transform nearest = null;
        Vector3 point = default;
        float distance = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide))
        {
            if (hit.distance >= distance) continue;
            if (hit.collider.isTrigger)
            {
                Transform root = hit.collider.transform;
                while (root != null && !root.CompareTag("Goal")) root = root.parent;
                if (root == null) continue;
            }
            nearest = hit.collider.transform;
            point = hit.point;
            distance = hit.distance;
        }
        if (nearest != null) TryEdit(nearest, point);
    }

    // 구는 길이가 0인 캡슐로 표현한다. 두 끝점은 둥근 뚜껑의 중심이다.
    private static void GetRoundSegment(Collider part, out Vector3 a, out Vector3 b, out float radius)
    {
        Transform t = part.transform;
        Vector3 scale = Abs(t.lossyScale);
        if (part is SphereCollider sphere)
        {
            a = b = t.TransformPoint(sphere.center);
            radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            return;
        }
        var capsule = (CapsuleCollider)part;
        int axis = capsule.direction;
        radius = capsule.radius * Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
        Vector3 offset = Vector3.zero;
        offset[axis] = Mathf.Max(0f, capsule.height * scale[axis] * 0.5f - radius);
        offset = t.rotation * offset;
        Vector3 center = t.TransformPoint(capsule.center);
        a = center - offset;
        b = center + offset;
    }

    private void TryEdit(Transform hit, Vector3 hitPoint)
    {
        for (Transform t = hit; t != null; t = t.parent)
        {
            if (t.CompareTag("Goal"))
            {
                foreach (Collider part in t.GetComponentsInChildren<Collider>())
                {
                    if (!part.enabled || part.isTrigger) continue;
                    if (part is BoxCollider box) placementParts.Add(box);
                    else if (part is SphereCollider || part is CapsuleCollider) roundGoalParts.Add(part);
                    else if (part is MeshCollider mesh)
                    {
                        PlacementShape baked = System.Array.Find(shapes, s => s.sourceMesh != null && s.sourceMesh == mesh.sharedMesh);
                        if (baked != null)
                        {
                            AddPlacementBoxes(mesh.gameObject, baked);
                            continue;
                        }
                        Debug.LogWarning("Bake the goal mesh placement shape first.", mesh);
                        ClearGeneratedBoxes();
                        placementParts.Clear();
                        roundGoalParts.Clear();
                        return;
                    }
                }
                if (placementParts.Count == 0 && roundGoalParts.Count == 0) return;
                editingGoal = true;
                goalGrabPoint = t.InverseTransformPoint(hitPoint);
                BeginPlacement(null, t.gameObject);
                return;
            }
            if (t.TryGetComponent<PingPongBallController>(out _) && t.TryGetComponent<SphereCollider>(out var sphere))
            {
                ballSphere = sphere;
                ballRotationAxis = 1; // 공을 집으면 월드 Y축부터 시작한다.
                BeginPlacement(null, t.gameObject);
                return;
            }
            if (!t.CompareTag("TrickTool")) continue;
            GameObject prefab = FindToolPrefab(t.gameObject);
            if (prefab == null) continue;
            BeginPlacement(prefab, t.gameObject);
            return;
        }
    }

    public string GetToolTypeName(GameObject instance)
    {
        GameObject prefab = FindToolPrefab(instance);
        if (prefab == fryingPanPrefab && prefab != null) return "Frying Pan";
        if (prefab == potPrefab && prefab != null) return "Pot";
        return prefab != null ? prefab.name : instance.name.Replace("(Clone)", "").Trim();
    }

    private GameObject FindToolPrefab(GameObject instance)
    {
        if (!instance.TryGetComponent<MeshCollider>(out var tool)) return null;
        foreach (PlacementShape shape in shapes)
            if (shape.sourcePrefab != null && shape.sourcePrefab.TryGetComponent<MeshCollider>(out var source)
                && source.sharedMesh == tool.sharedMesh)
                return shape.sourcePrefab;
        return null;
    }

    private float BallRadius => ballSphere.radius * Mathf.Max(Abs(ballSphere.transform.lossyScale).x,
        Mathf.Max(Abs(ballSphere.transform.lossyScale).y, Abs(ballSphere.transform.lossyScale).z));

    private void HandleRotationInput()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (ballSphere != null) ballRotationAxis = (ballRotationAxis + 1) % 3;
            else rotationAxis = (rotationAxis + 1) % 3;
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            if (ballSphere != null)
            {
                ballRotationAxis = 1;
                preview.transform.rotation = originalRotation;
            }
            else
            {
                preview.transform.rotation = editingGoal ? originalRotation : selectedPrefab.transform.rotation;
            }
            return;
        }

        float direction = 0f;
        if (Input.GetKey(KeyCode.Q))
            direction += 1f;
        if (Input.GetKey(KeyCode.E))
            direction -= 1f;

        Vector3 axis = Vector3.right;
        int selectedAxis = ballSphere != null ? ballRotationAxis : rotationAxis;
        if (selectedAxis == 1)
            axis = Vector3.up;
        else if (selectedAxis == 2)
            axis = Vector3.forward;

        // 초당 회전 각도로 계산해서 프레임 수에 따라 속도가 달라지지 않게 한다.
        preview.transform.Rotate(axis, direction * rotationSpeed * Time.deltaTime, Space.World);
    }

    private bool CanPlace()
    {
        Bounds area = gameManager.BuildArea;
        foreach (Collider part in roundGoalParts)
        {
            GetRoundSegment(part, out var a, out var b, out float radius);
            Vector3 extent = Vector3.one * radius;
            if (!area.Contains(Vector3.Min(a, b) - extent) || !area.Contains(Vector3.Max(a, b) + extent))
                return false;
            foreach (Collider other in Physics.OverlapCapsule(a, b, radius + placementMargin,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (other.transform.IsChildOf(preview.transform)) continue;
                if ((placementMargin > 0f && IsPlacementObject(other)) ||
                    (Physics.ComputePenetration(part, part.transform.position, part.transform.rotation,
                        other, other.transform.position, other.transform.rotation, out _, out float depth) && depth > Skin))
                    return false;
            }
        }
        if (ballSphere != null)
        {
            Vector3 center = ballSphere.transform.TransformPoint(ballSphere.center);
            float radius = BallRadius;
            Vector3 extent = Vector3.one * radius;
            if (!area.Contains(center - extent) || !area.Contains(center + extent)
                || Physics.CheckSphere(center, Mathf.Max(0f, radius - Skin),
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            if (placementMargin > 0f)
                foreach (Collider other in Physics.OverlapSphere(center, radius + placementMargin,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (!other.transform.IsChildOf(preview.transform) && IsPlacementObject(other))
                        return false;
            return true;
        }
        foreach (BoxCollider part in placementParts)
        {
            Transform t = part.transform;
            Vector3 center = t.TransformPoint(part.center);
            Vector3 extent = Vector3.Scale(part.size * 0.5f, Abs(t.lossyScale));
            // 범위 검사는 회전된 콜라이더의 여덟 모서리로 한다.
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                if (!area.Contains(center + t.rotation * Vector3.Scale(extent, new Vector3(x, y, z))))
                    return false;

            // 주변 후보를 찾은 다음 개별 콜라이더의 실제 관통 깊이를 검사한다.
            Collider[] nearby = Physics.OverlapBox(center, extent + Vector3.one * placementMargin, t.rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (Collider other in nearby)
            {
                if (other.transform.IsChildOf(preview.transform))
                    continue;
                // 설치물에만 확장 박스를 적용하고, 일반 표면은 원래 박스로 관통 깊이를 검사한다.
                if (placementMargin > 0f && IsPlacementObject(other))
                    return false;
                if (Physics.ComputePenetration(part, t.position, t.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out _, out float depth) && depth > Skin)
                    return false;
            }
        }
        return true;
    }

    private static bool IsPlacementObject(Collider collider)
    {
        for (Transform t = collider.transform; t != null; t = t.parent)
            if (t.CompareTag("TrickTool") || t.CompareTag("Goal") || t.TryGetComponent<PingPongBallController>(out _))
                return true;
        return false;
    }

    private readonly List<BoxCollider> generatedBoxes = new();

    private void AddPlacementBoxes(GameObject owner, PlacementShape shape)
    {
        foreach (Bounds bounds in shape.parts)
        {
            BoxCollider part = owner.AddComponent<BoxCollider>();
            part.enabled = false;
            part.center = bounds.center;
            part.size = bounds.size;
            placementParts.Add(part);
            generatedBoxes.Add(part);
        }
    }

    private void ClearGeneratedBoxes()
    {
        foreach (BoxCollider part in generatedBoxes) Destroy(part);
        generatedBoxes.Clear();
    }

    private void BeginPlacement(GameObject prefab, GameObject existing)
    {
        PlacementShape shape = null;
        foreach (PlacementShape candidate in shapes)
            if (prefab != null && candidate.sourcePrefab == prefab) { shape = candidate; break; }
        if (!editingGoal && ballSphere == null && (shape == null || shape.parts == null || shape.parts.Length == 0))
        {
            Debug.LogWarning("프리팹을 선택하고 Tools/TrickShot/Rebuild Selected Placement Shapes를 실행해 주세요.", this);
            return;
        }

        selectedPrefab = prefab;
        placementDistance = targetDistance = DefaultDistance;
        distanceVelocity = 0f;
        editing = existing != null;
        preview = editing ? existing : Instantiate(prefab, Vector3.zero, prefab.transform.rotation);
        originalPosition = preview.transform.position;
        originalRotation = preview.transform.rotation;
        wheelAdjusted = false;
        if (editing)
        {
            Transform camera = gameManager.BuildCamera.transform;
            editStartCameraPosition = camera.position;
            editStartCameraRotation = camera.rotation;
            Vector3 anchor = editingGoal ? preview.transform.TransformPoint(goalGrabPoint) : originalPosition;
            Vector3 offset = anchor - camera.position;
            placementDistance = targetDistance = offset.magnitude;
            editLocalDirection = placementDistance > 0f ?
                camera.InverseTransformDirection(offset / placementDistance) : Vector3.forward;
        }
        foreach (Collider collider in preview.GetComponentsInChildren<Collider>(true))
            if (collider.enabled)
            {
                enabledColliders.Add(collider);
                collider.enabled = false;
            }
        if (!editing)
            foreach (Rigidbody body in preview.GetComponentsInChildren<Rigidbody>(true))
                body.isKinematic = true;
        if (shape != null) AddPlacementBoxes(preview, shape);

        var ball = ballSphere != null ? ballSphere.GetComponent<PingPongBallController>() : null;
        foreach (Renderer renderer in preview.GetComponentsInChildren<Renderer>())
        {
            if (ball != null && !ball.UsesPreviewMaterial(renderer)) continue;
            materials.Add(renderer, renderer.sharedMaterials);
            shadows.Add(renderer, renderer.shadowCastingMode);
            Material[] slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
                slots[i] = previewMaterial;
            renderer.sharedMaterials = slots;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private enum PlacementEnd { Confirm, Restore, Discard }

    private void ClearPreview() => FinishPlacement(PlacementEnd.Restore);

    private void FinishPlacement(PlacementEnd end)
    {
        if (editingGoal && end == PlacementEnd.Discard)
            end = PlacementEnd.Restore;

        if (preview != null && editing && end != PlacementEnd.Discard)
        {
            if (end == PlacementEnd.Restore) preview.transform.SetPositionAndRotation(originalPosition, originalRotation);
            if (ballSphere != null && preview.TryGetComponent<Rigidbody>(out var body))
            {
                body.position = preview.transform.position;
                body.rotation = preview.transform.rotation;
            }
            foreach (var entry in materials)
            {
                entry.Key.sharedMaterials = entry.Value;
                entry.Key.shadowCastingMode = shadows[entry.Key];
            }
            foreach (Collider collider in enabledColliders) collider.enabled = true;

        }
        else if (preview != null)
        {
            if (editing && PlacedTools(selectedPrefab)?.Remove(preview) == true)
                UpdateRemainingText();
            if (editing && selectedPrefab != null) gameManager.SaveBuildField();
            preview.SetActive(false);
            Destroy(preview);
        }
        ClearGeneratedBoxes();
        placementParts.Clear();
        roundGoalParts.Clear();
        editingGoal = false;
        enabledColliders.Clear();
        materials.Clear();
        shadows.Clear();
        preview = null;
        selectedPrefab = null;
        ballSphere = null;
        editing = false;
    }

    private void OnDisable() => ClearPreview();
    private void OnDestroy()
    {
        if (gameManager != null) gameManager.LeavingBuildMode -= ClearPreview;
        Destroy(previewMaterial);
    }
}
