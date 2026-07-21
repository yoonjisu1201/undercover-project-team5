using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

/// <summary>
/// 시야 방해 이벤트의 경고, 활성화, 종료 신호를 Unity 이벤트로 전달합니다.
/// </summary>
public sealed class FieldVisionInterferenceEvent : MonoBehaviour, IInterferenceEvent
{
    /// <summary>시야 방해가 시작되기 전 경고 단계에서 호출됩니다.</summary>
    [Header("FieldVision 이벤트 동작")]
    [SerializeField] private UnityEvent _onWarningStarted = new UnityEvent();

    /// <summary>시야 방해가 활성화될 때 호출됩니다.</summary>
    [SerializeField] private UnityEvent _onInterferenceStarted = new UnityEvent();

    /// <summary>시야 방해가 종료될 때 호출됩니다.</summary>
    [SerializeField] private UnityEvent _onInterferenceEnded = new UnityEvent();

    /// <summary>이벤트 등록과 시작 요청을 처리하는 관리자입니다.</summary>
    private InterferenceEventManager _eventManager;

    /// <summary>시야 방해 이벤트 식별자를 가져옵니다.</summary>
    public InterferenceEventId Id => InterferenceEventId.FieldVision;

    /// <summary>
    /// 이벤트 관리자를 찾아 현재 이벤트 구현체를 등록합니다.
    /// </summary>
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

    /// <summary>
    /// 오브젝트가 제거될 때 이벤트 관리자에서 현재 구현체를 해제합니다.
    /// </summary>
    private void OnDestroy()
    {
        if (_eventManager == null)
        {
            return;
        }

        _eventManager.UnregisterEvent(this);
    }

    /// <summary>
    /// 개발 확인용 F2 입력을 감지해 시야 방해 이벤트 시작을 요청합니다.
    /// </summary>
    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            StartEvent();
        }
    }

    /// <summary>
    /// 이벤트 관리자에 시야 방해 이벤트 시작을 요청합니다.
    /// </summary>
    /// <returns>서버가 시작 요청을 수락하면 <see langword="true"/>, 그렇지 않으면 <see langword="false"/>입니다.</returns>
    public bool StartEvent()
    {
        Debug.Log("[Interference] FieldVision 시작", this);

        return _eventManager.TryStartEvent(Id);
    }

    /// <summary>
    /// 시야 방해 전 경고 신호를 구독 중인 Unity 이벤트에 전달합니다.
    /// </summary>
    public void ShowWarning()
    {
        Debug.Log("[Interference] FieldVision 경고", this);

        _onWarningStarted.Invoke();
    }

    /// <summary>
    /// 시야 방해 시작 신호를 구독 중인 Unity 이벤트에 전달합니다.
    /// </summary>
    public void Activate()
    {
        Debug.Log("[Interference] FieldVision 활성화", this);

        _onInterferenceStarted.Invoke();
    }

    /// <summary>
    /// 시야 방해 종료 신호를 구독 중인 Unity 이벤트에 전달합니다.
    /// </summary>
    public void Deactivate()
    {
        Debug.Log("[Interference] FieldVision 종료", this);

        _onInterferenceEnded.Invoke();
    }
}
