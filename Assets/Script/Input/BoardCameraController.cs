using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 为战斗棋盘提供受边界约束的平移、俯仰、缩放和一键回正视角操作。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class BoardCameraController : MonoBehaviour
{
    [Header("场景引用")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private BoardGenerator board;

    [Header("平移")]
    [SerializeField, Min(0.1f)] private float keyboardPanSpeed = 7f;
    [SerializeField, Min(0.001f)] private float mousePanSensitivity = 0.018f;
    [SerializeField, Min(0f)] private float boardBoundaryMargin = 2f;

    [Header("旋转与缩放")]
    [SerializeField, Min(0.01f)] private float pitchSensitivity = 0.18f;
    [SerializeField, Range(10f, 89f)] private float minimumPitch = 38f;
    [SerializeField, Range(10f, 89f)] private float maximumPitch = 80f;
    [SerializeField, Min(0.001f)] private float zoomSensitivity = 0.012f;
    [SerializeField, Min(0.1f)] private float minimumDistance = 5f;
    [SerializeField, Min(0.1f)] private float maximumDistance = 18f;
    [SerializeField, Min(0f)] private float movementDamping = 14f;

    private Vector3 initialFocus;
    private float initialDistance;
    private float initialPitch;
    private float initialYaw;
    private Vector3 targetFocus;
    private float targetDistance;
    private float targetPitch;
    private float targetYaw;
    private Vector3 currentFocus;
    private float currentDistance;
    private float currentPitch;
    private float currentYaw;
    private Vector2 focusXLimits;
    private Vector2 focusZLimits;
    private bool rightMousePanning;
    private bool middleMousePitching;

    /// <summary>解析摄像机和棋盘引用，记录场景设计视角并计算允许移动的棋盘边界。</summary>
    private void Awake()
    {
        targetCamera ??= GetComponent<Camera>();
        board ??= FindAnyObjectByType<BoardGenerator>();
        CaptureInitialView();
        CalculateFocusLimits();
    }

    /// <summary>读取键盘、鼠标拖拽、滚轮和回正按键，并更新受限制的目标视角。</summary>
    private void Update()
    {
        HandleResetInput();
        HandleKeyboardPan();
        HandleMouseGestures();
        HandleZoom();
        ClampTargetView();
    }

    /// <summary>在其他游戏逻辑完成后平滑应用摄像机位置和朝向，减少输入造成的画面抖动。</summary>
    private void LateUpdate()
    {
        float blend = movementDamping <= 0f ? 1f : 1f - Mathf.Exp(-movementDamping * Time.unscaledDeltaTime);
        currentFocus = Vector3.Lerp(currentFocus, targetFocus, blend);
        currentDistance = Mathf.Lerp(currentDistance, targetDistance, blend);
        currentPitch = Mathf.LerpAngle(currentPitch, targetPitch, blend);
        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, blend);
        ApplyCameraTransform();
    }

    /// <summary>把视角平滑恢复到 S_Battle 中保存的初始位置、俯仰、方向和缩放。</summary>
    public void ResetView()
    {
        targetFocus = initialFocus;
        targetDistance = initialDistance;
        targetPitch = initialPitch;
        targetYaw = initialYaw;
        rightMousePanning = false;
        middleMousePitching = false;
    }

    /// <summary>从场景摄像机朝向与棋盘平面交点推导焦点，保证回正精确还原设计视角。</summary>
    private void CaptureInitialView()
    {
        float boardHeight = board != null ? board.transform.position.y : 0f;
        Plane plane = new Plane(Vector3.up, new Vector3(0f, boardHeight, 0f));
        Ray viewRay = new Ray(transform.position, transform.forward);
        initialFocus = plane.Raycast(viewRay, out float enter)
            ? viewRay.GetPoint(enter)
            : (board != null ? board.transform.position : transform.position + transform.forward * 10f);
        initialDistance = Vector3.Distance(transform.position, initialFocus);
        initialPitch = NormalizeAngle(transform.eulerAngles.x);
        initialYaw = NormalizeAngle(transform.eulerAngles.y);
        targetFocus = currentFocus = initialFocus;
        targetDistance = currentDistance = initialDistance;
        targetPitch = currentPitch = initialPitch;
        targetYaw = currentYaw = initialYaw;
    }

    /// <summary>根据实际生成的棋盘格世界坐标计算平移边界，并在四周保留少量观察余量。</summary>
    private void CalculateFocusLimits()
    {
        Vector3 center = board != null ? board.transform.position : initialFocus;
        float minimumX = center.x - 4f;
        float maximumX = center.x + 4f;
        float minimumZ = center.z - 4f;
        float maximumZ = center.z + 4f;
        BoardCell[] cells = board != null ? board.GetComponentsInChildren<BoardCell>(true) : null;
        if (cells != null && cells.Length > 0)
        {
            minimumX = maximumX = cells[0].transform.position.x;
            minimumZ = maximumZ = cells[0].transform.position.z;
            for (int index = 1; index < cells.Length; index++)
            {
                Vector3 position = cells[index].transform.position;
                minimumX = Mathf.Min(minimumX, position.x);
                maximumX = Mathf.Max(maximumX, position.x);
                minimumZ = Mathf.Min(minimumZ, position.z);
                maximumZ = Mathf.Max(maximumZ, position.z);
            }
        }
        focusXLimits = new Vector2(minimumX - boardBoundaryMargin, maximumX + boardBoundaryMargin);
        focusZLimits = new Vector2(minimumZ - boardBoundaryMargin, maximumZ + boardBoundaryMargin);
    }

    /// <summary>使用 WASD 或方向键沿当前屏幕水平面平移焦点。</summary>
    private void HandleKeyboardPan()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        float horizontal = ReadAxis(keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed,
            keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed);
        float vertical = ReadAxis(keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed,
            keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed);
        if (Mathf.Approximately(horizontal, 0f) && Mathf.Approximately(vertical, 0f)) return;
        GetPlanarDirections(out Vector3 right, out Vector3 forward);
        Vector3 movement = right * horizontal + forward * vertical;
        if (movement.sqrMagnitude > 1f) movement.Normalize();
        targetFocus += movement * (keyboardPanSpeed * Time.unscaledDeltaTime);
    }

    /// <summary>读取右键拖拽平移和中键上下拖拽俯仰；从 UI 上按下时不启动视角操作。</summary>
    private void HandleMouseGestures()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (mouse.rightButton.wasPressedThisFrame) rightMousePanning = !pointerOverUi;
        if (mouse.rightButton.wasReleasedThisFrame) rightMousePanning = false;
        if (mouse.middleButton.wasPressedThisFrame) middleMousePitching = !pointerOverUi;
        if (mouse.middleButton.wasReleasedThisFrame) middleMousePitching = false;

        Vector2 delta = mouse.delta.ReadValue();
        if (rightMousePanning && mouse.rightButton.isPressed)
        {
            GetPlanarDirections(out Vector3 right, out Vector3 forward);
            float distanceScale = Mathf.Max(0.5f, targetDistance / Mathf.Max(0.1f, initialDistance));
            targetFocus += (-right * delta.x - forward * delta.y) * (mousePanSensitivity * distanceScale);
        }
        if (middleMousePitching && mouse.middleButton.isPressed)
            targetPitch -= delta.y * pitchSensitivity;
    }

    /// <summary>读取鼠标滚轮并改变摄像机到棋盘焦点的距离；指针位于 UI 上时忽略滚轮。</summary>
    private void HandleZoom()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        float scroll = mouse.scroll.ReadValue().y;
        if (!Mathf.Approximately(scroll, 0f)) targetDistance -= scroll * zoomSensitivity;
    }

    /// <summary>按 R 或 Home 时触发一键回正。</summary>
    private void HandleResetInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.rKey.wasPressedThisFrame || keyboard.homeKey.wasPressedThisFrame))
            ResetView();
    }

    /// <summary>把焦点、俯仰和缩放夹在策划配置的安全范围内。</summary>
    private void ClampTargetView()
    {
        targetFocus.x = Mathf.Clamp(targetFocus.x, focusXLimits.x, focusXLimits.y);
        targetFocus.z = Mathf.Clamp(targetFocus.z, focusZLimits.x, focusZLimits.y);
        targetPitch = Mathf.Clamp(targetPitch, minimumPitch, maximumPitch);
        targetDistance = Mathf.Clamp(targetDistance, minimumDistance, maximumDistance);
    }

    /// <summary>根据当前焦点、俯仰、方向与距离计算透视摄像机最终变换。</summary>
    private void ApplyCameraTransform()
    {
        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        transform.SetPositionAndRotation(currentFocus - rotation * Vector3.forward * currentDistance, rotation);
    }

    /// <summary>返回摄像机在棋盘平面上的右向与前向单位向量。</summary>
    private void GetPlanarDirections(out Vector3 right, out Vector3 forward)
    {
        right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (right.sqrMagnitude < 0.01f) right = Vector3.right;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
    }

    /// <summary>把一对负向和正向按键转换为 -1、0 或 1 的输入轴。</summary>
    private static float ReadAxis(bool negative, bool positive)
    {
        return (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }

    /// <summary>把 Unity 的 0 至 360 度角转换为便于限制的 -180 至 180 度。</summary>
    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    /// <summary>在 Inspector 修改参数时修正互相颠倒或小于有效范围的限制值。</summary>
    private void OnValidate()
    {
        minimumPitch = Mathf.Clamp(minimumPitch, 10f, 88f);
        maximumPitch = Mathf.Clamp(maximumPitch, minimumPitch, 89f);
        minimumDistance = Mathf.Max(0.1f, minimumDistance);
        maximumDistance = Mathf.Max(minimumDistance, maximumDistance);
        boardBoundaryMargin = Mathf.Max(0f, boardBoundaryMargin);
    }
}
