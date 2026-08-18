using System.Collections.Generic;
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
    private int _movementBlockCount;

    private readonly List<IClosableUi> _openUIs = new();
    public void RegisterUi(IClosableUi ui)  // 최근에 연 ui가 맨 위로
    {
        _openUIs.Remove(ui);
        _openUIs.Add(ui);
    }

    public void UnregisterUi(IClosableUi ui)
    {
        _openUIs.Remove(ui);
    }

    public bool CloseTopUi()
    {
        for (int i = _openUIs.Count - 1; i >= 0; i--)
        {
            IClosableUi ui = _openUIs[i];
            _openUIs.RemoveAt(i);
            if (ui != null) { ui.Close(); return true; }
        }
        return false;
    }

    private void Awake()
    {
        Instance = this;
        _sceneCursorSettings = GetComponent<SceneCursorSettings>();
        _cursorActivationCount = 0;
        _movementBlockCount = 0;
        IsActive = false;
        IsMovementBlocked = false;
        _sceneCursorSettings.ApplyDefaultCursorState();
    }

    private void OnDisable()
    {
        _cursorActivationCount = 0;
        _movementBlockCount = 0;
        _openUIs.Clear();
        IsActive = false;
        IsMovementBlocked = false;
        _sceneCursorSettings.ApplyDefaultCursorState();
    }

    // 커서를 켜기로 한 동안에는 매 프레임 상태를 지킨다.
    // 다른 UI가 짝 없이 DeactivateCursor를 불러 커서가 다시 잠기는 일을 여기서 막는다.
    private void LateUpdate()
    {
        if (_cursorActivationCount <= 0)
        {
            return;
        }

        if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
        {
            ForceUnlockCursor();
        }
    }

    // 값이 이미 None/true여도 화면에는 커서가 안 나오는 경우가 있다.
    // Locked를 한 번 거쳐 상태 전이를 만들어야 실제로 커서를 놓아준다.
    private static void ForceUnlockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ActivateCursor()
    {
        _cursorActivationCount++;
        IsActive = true;
        IsMovementBlocked = true;
        ForceUnlockCursor();
    }

    // 커서 상태는 그대로 두고 이동만 막는다.
    // 대기방 닉네임 설정창처럼 커서가 원래부터 보여야 하는 화면에서는 ActivateCursor를 쓰면
    // 닫을 때 DeactivateCursor가 씬 기본 커서 상태로 되돌려 커서가 잠겨 버린다.
    public void PushMovementBlock()
    {
        _movementBlockCount++;
        IsMovementBlocked = true;
    }

    public void PopMovementBlock()
    {
        _movementBlockCount = Mathf.Max(0, _movementBlockCount - 1);
        IsMovementBlocked = _cursorActivationCount > 0 || _movementBlockCount > 0;
    }

    public void DeactivateCursor()
    {
        _cursorActivationCount = Mathf.Max(0, _cursorActivationCount - 1);

        if (_cursorActivationCount > 0)
        {
            IsActive = true;
            IsMovementBlocked = true;
            ForceUnlockCursor();
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
