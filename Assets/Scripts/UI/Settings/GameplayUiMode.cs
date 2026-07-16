using UnityEngine;
using UnityEngine.InputSystem;


// 커서 표시와 플레이어 입력 제한을 관리하는 클래스
[RequireComponent(typeof(SceneCursorSettings))]
public class GameplayUiMode : MonoBehaviour
{
    public static GameplayUiMode Instance { get; private set; }
    public static bool IsActive { get; private set; } // 화면 회전을 제한하는 상태
    public static bool IsMovementBlocked { get; private set; } // 플레이어 이동을 제한하는 상태
    CustomInputActions _actions;
    private SceneCursorSettings _sceneCursorSettings;
    private int _clicksUntilCursorLock;
    private int _cursorActivationCount;

    private void Awake()
    {
        Instance = this;
        EnsureInitialized();
        IsActive = false;
        IsMovementBlocked = false;
        _sceneCursorSettings.ApplyDefaultCursorState();
    }

    private void OnEnable()
    {
        EnsureInitialized();
    }

    private void OnDisable()
    {
        if (_actions != null)
        {
            _actions.Disable();
        }

        _clicksUntilCursorLock = 0;
        _cursorActivationCount = 0;
        IsActive = false;
        IsMovementBlocked = false;
    }

    private void Update()
    {
        if (_clicksUntilCursorLock <= 0 || Mouse.current == null)   // 마우스 입력이 없거나 클릭 횟수가 0 이하이면 아무 작업도 수행하지 않음
        {
            return;
        }

        if (!Mouse.current.leftButton.wasReleasedThisFrame) //  마우스 왼쪽 버튼이 이번 프레임에 해제되지 않았으면 아무 작업도 수행하지 않음
        {
            return;
        }

        _clicksUntilCursorLock--;

        if (_clicksUntilCursorLock == 0)    // 클릭 횟수가 0이 되면 게임 플레이 UI 모드를 비활성화하고 플레이어 입력을 활성화하며 커서 상태를 기본 상태로 적용
        {
            IsActive = false;
            IsMovementBlocked = false;
            _actions.Player.Enable();
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    public void ActivateCursor()
    {
        EnsureInitialized();
        _cursorActivationCount++;
        IsActive = true;
        IsMovementBlocked = true;
        _clicksUntilCursorLock = 0;
        _actions.System.Enable();
        _actions.Player.Disable();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void DeactivateCursor()
    {
        EnsureInitialized();

        if (_cursorActivationCount > 0)
        {
            _cursorActivationCount--;
        }

        if (_cursorActivationCount > 0)
        {
            IsActive = true;
            IsMovementBlocked = true;
            _clicksUntilCursorLock = 0;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            return;
        }

        IsActive = true;
        IsMovementBlocked = false;
        _clicksUntilCursorLock = 2;
        _actions.System.Enable();
        _actions.Player.Enable();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void EnsureInitialized()
    {
        _actions ??= new CustomInputActions();
        _sceneCursorSettings ??= GetComponent<SceneCursorSettings>();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
