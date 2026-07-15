using UnityEngine;
using UnityEngine.InputSystem;


//--- ESC 버튼을 눌러서 게임 플레이 UI 모드를 활성화/비활성화하는 기능을 구현하는 클래스
[RequireComponent(typeof(SceneCursorSettings))]
public class GameplayUiMode : MonoBehaviour
{
    public static GameplayUiMode Instance { get; private set; }
    public static bool IsActive { get; private set; } // 게임 플레이 UI 모드가 활성화되어 있는지 확인하는 변수
    CustomInputActions _actions;
    private SceneCursorSettings _sceneCursorSettings;
    private int _clicksUntilCursorLock;

    [SerializeField] private GameObject _settingsMenu;

    private void Awake()
    {
        Instance = this;
        _actions = new CustomInputActions();
        _sceneCursorSettings = GetComponent<SceneCursorSettings>();
        IsActive = false;
        _settingsMenu.SetActive(false);
        _sceneCursorSettings.ApplyDefaultCursorState();
    }

    private void OnEnable()
    {
        _actions.Enable();
        _actions.System.Escape.performed += OnEscape;
    }

    private void OnDisable()
    {
        _actions.System.Escape.performed -= OnEscape;
        _actions.Disable();
        _clicksUntilCursorLock = 0;
    }

    private void Update()
    {
        if (_clicksUntilCursorLock <= 0 || Mouse.current == null)   // 마우스 입력이 없거나 클릭 횟수가 0 이하이면 아무 작업도 수행하지 않음
        {
            return;
        }

        if (!Mouse.current.leftButton.wasReleasedThisFrame)
        {
            return;
        }

        _clicksUntilCursorLock--;

        if (_clicksUntilCursorLock == 0)    // 클릭 횟수가 0이 되면 게임 플레이 UI 모드를 비활성화하고 플레이어 입력을 활성화하며 커서 상태를 기본 상태로 적용
        {
            IsActive = false;
            _actions.Player.Enable();
            _sceneCursorSettings.ApplyDefaultCursorState();
        }
    }

    private void OnEscape(InputAction.CallbackContext context)
    {
        SetActive(!_settingsMenu.activeSelf);   // ESC 버튼을 눌렀을 때 설정 메뉴 활성화/비활성화
    }

    public void CloseSettingsMenu()
    {
        SetActive(false);
    }

    private void SetActive(bool active)
    {
        _settingsMenu.SetActive(active);

        // ESC는 항상 받을 수 있도록 System 맵 유지
        _actions.System.Enable();

        if (active) // 설정창 열렸을 때
        {
            IsActive = true;
            _actions.Player.Disable();
            _clicksUntilCursorLock = 0;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else if (_sceneCursorSettings.CursorVisibleByDefault)   // CursorVisibleByDefault이 true이면 설정창 닫았을 때 플레이어 입력을 활성화하고 커서 상태를 기본 상태로 적용
        {
            IsActive = false;
            _actions.Player.Enable();
            _clicksUntilCursorLock = 0; // 클릭 횟수가 0이 되면 게임 플레이 UI 모드를 비활성화하고 플레이어 입력을 활성화하며 커서 상태를 기본 상태로 적용
            _sceneCursorSettings.ApplyDefaultCursorState();
        }
        else    // 설정창 닫았을 때
        {
            IsActive = true;
            _actions.Player.Disable();
            _clicksUntilCursorLock = 2; // 남은 클릭 횟수
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
