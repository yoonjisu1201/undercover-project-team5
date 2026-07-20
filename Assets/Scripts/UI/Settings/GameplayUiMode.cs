using UnityEngine;


// 커서 표시와 플레이어 이동 제한을 관리하는 클래스
[RequireComponent(typeof(SceneCursorSettings))]
public class GameplayUiMode : MonoBehaviour
{
    public static GameplayUiMode Instance { get; private set; }
    public static bool IsActive { get; private set; }
    public static bool IsMovementBlocked { get; private set; } // 플레이어 이동을 제한하는 상태
    private SceneCursorSettings _sceneCursorSettings;
    private int _cursorActivationCount;

    private void Awake()
    {
        Instance = this;
        _sceneCursorSettings = GetComponent<SceneCursorSettings>();
        _cursorActivationCount = 0;
        IsActive = false;
        IsMovementBlocked = false;
        _sceneCursorSettings.ApplyDefaultCursorState();
    }

    private void OnDisable()
    {
        _cursorActivationCount = 0;
        IsActive = false;
        IsMovementBlocked = false;
        _sceneCursorSettings.ApplyDefaultCursorState();
    }

    public void ActivateCursor()
    {
        _cursorActivationCount++;
        IsActive = true;
        IsMovementBlocked = true;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void DeactivateCursor()
    {
        _cursorActivationCount = Mathf.Max(0, _cursorActivationCount - 1);

        if (_cursorActivationCount > 0)
        {
            IsActive = true;
            IsMovementBlocked = true;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            return;
        }

        IsActive = false;
        IsMovementBlocked = false;
        _sceneCursorSettings.ApplyDefaultCursorState();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
