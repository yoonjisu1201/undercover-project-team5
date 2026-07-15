using UnityEngine;
using UnityEngine.InputSystem;


//--- ESC 버튼을 눌러서 게임 플레이 UI 모드를 활성화/비활성화하는 기능을 구현하는 클래스
public class GameplayUiMode : MonoBehaviour
{
    public static bool IsActive { get; private set; } // 게임 플레이 UI 모드가 활성화되어 있는지 확인하는 변수
    CustomInputActions _actions;
    private SceneCursorSettings _sceneCursorSettings;

    [SerializeField] private GameObject _settingsMenu;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _sceneCursorSettings = GetComponent<SceneCursorSettings>();
        SetActive(false);
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
    }

    private void OnEscape(InputAction.CallbackContext context)
    {
        SetActive(!IsActive);   // ESC 버튼을 눌렀을 때 게임 플레이 UI 모드 활성화/비활성화
    }

    private void SetActive(bool active)
    {
        IsActive = active;
        // 게임 플레이 UI 모드 활성화/비활성화에 따른 추가적인 동작을 여기에 구현할 수 있습니다.
        _settingsMenu.SetActive(active);

        if (active)
        {
            _actions.Player.Disable();
        }
        else
        {
            _actions.Player.Enable();
        }

        // ESC는 항상 받을 수 있도록 System 맵 유지
        _actions.System.Enable();

        if (active)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            _sceneCursorSettings.ApplyDefaultCursorState();
        }
    }
}
