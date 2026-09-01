using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


// 커서 표시와 플레이어 이동 제한을 관리하는 클래스
[RequireComponent(typeof(SceneCursorSettings))]
public class GameplayUiMode : MonoBehaviour
{
    public static GameplayUiMode Instance { get; private set; }
    public static bool IsActive { get; private set; }
    public static bool IsMovementBlocked { get; private set; } // 플레이어 이동을 제한하는 상태

    // 글자 입력 중인지. 단축키를 읽는 쪽에서 먼저 확인한다. isFocused 까지 봐야
    // 포커스가 빠진 뒤에도 단축키가 막히지 않는다.
    public static bool IsTypingText
    {
        get
        {
            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == null)
            {
                return false;
            }

            if (selected.TryGetComponent(out TMP_InputField tmp))
            {
                return tmp.isFocused;
            }

            return selected.TryGetComponent(out InputField legacy) && legacy.isFocused;
        }
    }

    private SceneCursorSettings _sceneCursorSettings;
    private int _cursorActivationCount;

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
                SoundManager.Instance?.Play(ui.CloseSound);
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
        IsActive = false;
        IsMovementBlocked = false;
        _sceneCursorSettings.ApplyDefaultCursorState();
    }

    private void OnDisable()
    {
        _cursorActivationCount = 0;
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

    // 커서를 씬 기본 상태로 되돌린다. 대기방·로비는 기본값이 '커서 보임'이라
    // 원래부터 커서가 보여야 하는 화면에서도 그대로 쓸 수 있다.
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
