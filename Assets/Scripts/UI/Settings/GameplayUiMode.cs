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
    // 영상 UI처럼 커서는 숨긴 채 이동과 월드 상호작용만 막아야 하는 UI의 중첩 수를 관리한다.
    // bool 대신 카운터를 사용해 한 UI가 닫혀도 다른 입력 차단 UI가 열려 있으면 차단 상태를 유지한다.
    private int _inputBlockActivationCount;

    private readonly List<IClosableUi> _openUIs = new();
    public void RegisterUi(IClosableUi ui, bool playOpenSound = true)  // 최근에 연 ui가 맨 위로
    {
        bool alreadyOpen = _openUIs.Remove(ui);
        _openUIs.Add(ui);

        // 이미 열려 있던 UI를 맨 위로 올리는 경우는 새로 열린 게 아니다.
        if (!alreadyOpen && playOpenSound)
        {
            SoundManager.Instance?.Play(SoundKey.Ui_PopupOpen);
        }
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
            if (ui != null)
            {
                // 버튼으로 닫을 땐 클릭음이 대신 나므로, 클릭음이 없는 ESC 경로에서만 닫힘음을 낸다.
                SoundManager.Instance?.Play(SoundKey.Ui_PopupClose);
                ui.Close();
                return true;
            }
        }
        return false;
    }

    private void Awake()
    {
        Instance = this;
        _sceneCursorSettings = GetComponent<SceneCursorSettings>();
        _cursorActivationCount = 0;
        _inputBlockActivationCount = 0;
        IsActive = false;
        IsMovementBlocked = false;
        _sceneCursorSettings.ApplyDefaultCursorState();
    }

    private void OnDisable()
    {
        _cursorActivationCount = 0;
        _inputBlockActivationCount = 0;
        _openUIs.Clear();
        IsActive = false;
        IsMovementBlocked = false;
        _sceneCursorSettings.ApplyDefaultCursorState();
    }

    // UI가 요청한 커서 상태를 매 프레임 복원해 다른 코드의 Cursor 변경이 화면 조작 방식을 깨뜨리지 않게 한다.
    // 마우스 조작이 필요한 커서 UI를 우선하고, 없을 때는 영상 UI의 숨김·잠금 상태를 유지한다.
    private void LateUpdate()
    {
        if (_cursorActivationCount > 0)
        {
            if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
            {
                ForceUnlockCursor();
            }
            return;
        }

        if (_inputBlockActivationCount > 0 && (Cursor.visible || Cursor.lockState != CursorLockMode.Locked))
        {
            ForceLockCursor();
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

    private static void ForceLockCursor()
    {
        // 입력은 차단하지만 포인터 조작은 필요 없는 영상 UI가 게임 기본 조준 상태를 유지하게 한다.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void ActivateCursor()
    {
        _cursorActivationCount++;
        ApplyInputState();
    }

    // 커서를 씬 기본 상태로 되돌린다. 대기방·로비는 기본값이 '커서 보임'이라
    // 원래부터 커서가 보여야 하는 화면에서도 그대로 쓸 수 있다.
    public void DeactivateCursor()
    {
        _cursorActivationCount = Mathf.Max(0, _cursorActivationCount - 1);
        ApplyInputState();
    }

    public void ActivateInputBlock()
    {
        // 커서를 표시하는 ActivateCursor와 구분해 영상 UI가 포인터 없이 플레이어 조작만 막을 수 있게 한다.
        _inputBlockActivationCount++;
        ApplyInputState();
    }

    public void DeactivateInputBlock()
    {
        _inputBlockActivationCount = Mathf.Max(0, _inputBlockActivationCount - 1);
        ApplyInputState();
    }

    private void ApplyInputState()
    {
        // 커서 UI와 커서 없는 영상 UI의 활성 요청을 함께 계산해 플레이어 입력 상태를 갱신한다.
        // 두 UI는 커서 표시 방식은 다르지만 모두 이동을 막아야 하므로 별도 카운터로 관리하고,
        // 한쪽 UI가 닫혀도 다른 쪽이 열려 있으면 차단이 풀리지 않게 한다.
        bool isBlocked = _cursorActivationCount > 0 || _inputBlockActivationCount > 0;
        IsActive = isBlocked;
        IsMovementBlocked = isBlocked;

        if (_cursorActivationCount > 0)
        {
            ForceUnlockCursor();
        }
        else if (_inputBlockActivationCount > 0)
        {
            ForceLockCursor();
        }
        else
        {
            _sceneCursorSettings.ApplyDefaultCursorState();
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
