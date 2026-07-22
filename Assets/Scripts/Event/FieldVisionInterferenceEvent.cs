using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;


// 시야 방해 이벤트의 경고, 활성화, 종료 신호를 Unity 이벤트로 전달합니다.

public sealed class FieldVisionInterferenceEvent : MonoBehaviour, IInterferenceEvent
{
    // 시야 방해가 시작되기 전 경고 단계에서 호출됩니다.
    [Header("FieldVision 이벤트 동작")]
    [SerializeField] private UnityEvent _onWarningStarted = new UnityEvent();

    // 시야 방해 경고가 취소될 때 호출됩니다.
    [SerializeField] private UnityEvent _onWarningCanceled = new UnityEvent();

    // 시야 방해가 활성화될 때 호출됩니다.
    [SerializeField] private UnityEvent _onInterferenceStarted = new UnityEvent();

    // 시야 방해가 종료될 때 호출됩니다.
    [SerializeField] private UnityEvent _onInterferenceEnded = new UnityEvent();

    // 이벤트 등록과 시작 요청을 처리하는 관리자입니다.
    private InterferenceEventManager _eventManager;

    // 시야 방해 이벤트 식별자를 가져옵니다.
    public InterferenceEventId Id => InterferenceEventId.FieldVision;

    // 현재 경고가 표시 중인지 나타냅니다.
    public bool IsWarningActive { get; private set; }

    // 현재 시야 방해 효과가 활성화됐는지 나타냅니다.
    public bool IsActive { get; private set; }


    // 이벤트 관리자를 찾아 현재 이벤트 구현체를 등록합니다.

    private void Start()
    {
        _eventManager = InterferenceEventManager.Instance;

        if (_eventManager == null)
        {
            Debug.LogError("[Interference] InterferenceEventManager를 찾을 수 없습니다.", this);
            return;
        }

        _eventManager.RegisterEvent(this);
    }


    // 오브젝트가 제거될 때 이벤트 관리자에서 현재 구현체를 해제합니다.

    private void OnDestroy()
    {
        if (_eventManager == null)
        {
            return;
        }

        _eventManager.UnregisterEvent(this);
    }


    // 개발 확인용 F2 입력을 감지해 시야 방해 이벤트 시작을 요청합니다.

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            StartEvent();
        }
    }


    // 이벤트 관리자에 시야 방해 이벤트 시작을 요청합니다.

    // 반환값: 서버가 시작 요청을 수락하면 true, 그렇지 않으면 false입니다.
    public bool StartEvent()
    {
        Debug.Log("[Interference] FieldVision 시작", this);

        return _eventManager.TryStartEvent(Id);
    }


    // 시야 방해 전 경고 신호를 구독 중인 Unity 이벤트에 전달합니다.

    public void ShowWarning()
    {
        if (IsWarningActive || IsActive)
        {
            return;
        }

        IsWarningActive = true;

        Debug.Log("[Interference] FieldVision 경고", this);

        _onWarningStarted.Invoke();
    }


    // 표시 중인 시야 방해 경고를 취소합니다.

    public void CancelWarning()
    {
        if (!IsWarningActive)
        {
            return;
        }

        IsWarningActive = false;

        Debug.Log("[Interference] FieldVision 경고 취소", this);

        _onWarningCanceled.Invoke();
    }


    // 시야 방해 시작 신호를 구독 중인 Unity 이벤트에 전달합니다.

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        CancelWarning();
        IsActive = true;

        Debug.Log("[Interference] FieldVision 활성화", this);

        _onInterferenceStarted.Invoke();
    }


    // 시야 방해 종료 신호를 구독 중인 Unity 이벤트에 전달합니다.

    // reason: 시야 방해 효과를 종료하는 사유입니다.
    public void Deactivate(InterferenceEndReason reason)
    {
        CancelWarning();

        if (!IsActive)
        {
            return;
        }

        IsActive = false;

        Debug.Log($"[Interference] FieldVision 종료: {reason}", this);

        _onInterferenceEnded.Invoke();
    }
}
