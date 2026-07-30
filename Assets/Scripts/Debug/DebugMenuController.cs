using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;


// 실제 uGUI 프리팹의 버튼 이벤트를 처리하는 개발용 컨트롤러입니다.
// UI 오브젝트는 코드에서 생성하지 않습니다.
public sealed partial class DebugMenuController : NetworkBehaviour
{
    private const string WaitingRoomSceneName = "WaitingRoom";
    private static readonly Color LockedRegionButtonColor = new(0.04f, 0.13f, 0.16f, 1f);
    private static readonly Color UnlockedRegionButtonColor = new(0.05f, 0.72f, 0.55f, 1f);
    private float _nextLocationLabelRefreshTime;

    // 메뉴를 닫힌 초기 상태로 만들고 표시 텍스트를 동기화합니다.
    private void Awake()
    {
        SetMenuVisible(false);
        SetAllSubMenusInactive();
        RefreshButtonLabels();
    }

    // 활성 씬 변경 시 디버그 상태를 정리할 수 있도록 이벤트를 구독합니다.
    private void OnEnable()
    {
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    // 이벤트 구독과 디버그 이동속도 변경을 원래 상태로 복구합니다.
    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        RestoreWalkSpeed();
    }

    // 네트워크 스폰 후 라운드 시작 이벤트를 구독해 라운드별 디버그 상태를 초기화합니다.
    public override void OnNetworkSpawn()
    {
        if (RoundManager.Instance == null)
        {
            return;
        }

        RoundManager.Instance.OnRoundStateChanged += HandleDebugRoundStateChanged;
        HandleDebugRoundStateChanged(RoundManager.Instance.CurrentState);
    }

    // 네트워크 해제 시 라운드 이벤트 구독을 정리합니다.
    public override void OnNetworkDespawn()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleDebugRoundStateChanged;
        }
    }

    // F9 토글, 위치 버튼 문구 갱신, 메뉴 바깥 클릭 닫기를 처리합니다.
    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
        {
            SetMenuVisible(_menuRoot != null && !_menuRoot.activeSelf);
        }

        if (_menuRoot != null && _menuRoot.activeSelf && Time.unscaledTime >= _nextLocationLabelRefreshTime)
        {
            _nextLocationLabelRefreshTime = Time.unscaledTime + 0.2f;
            RefreshHqFieldButtonLabel();
            RefreshRoundTimeStopButton();
        }

        if (_menuRoot != null &&
            _menuRoot.activeSelf &&
            Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame &&
            !IsPointerOverDebugUi(Mouse.current.position.ReadValue()))
        {
            SetMenuVisible(false);
        }
    }

    // 디버그 메뉴 작업 결과를 공통 접두사가 포함된 콘솔 로그로 출력합니다.
    private void ShowStatus(string message)
    {
        Debug.Log($"[DebugMenu] {message}", this);
    }

    // 현재 포인터 아래 UI가 이 디버그 메뉴에 속하는지 레이캐스트로 확인합니다.
    private bool IsPointerOverDebugUi(Vector2 screenPosition)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerData = new(EventSystem.current)
        {
            position = screenPosition
        };
        List<RaycastResult> raycastResults = new();
        EventSystem.current.RaycastAll(pointerData, raycastResults);

        return raycastResults.Any(result =>
            result.gameObject != null &&
            result.gameObject.transform.IsChildOf(transform));
    }

    // 씬 전환 시 메뉴를 닫고 일시적으로 변경한 이동속도를 복구합니다.
    private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        SetMenuVisible(false);
        RestoreWalkSpeed();
        ClearSavedFieldReturnPoses();
    }
}
