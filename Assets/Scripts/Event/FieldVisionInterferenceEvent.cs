using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

public sealed class FieldVisionInterferenceEvent : MonoBehaviour, IInterferenceEvent
{
    [Header("FieldVision 이벤트 동작")]
    [SerializeField] private UnityEvent _onWarningStarted = new UnityEvent();
    [SerializeField] private UnityEvent _onInterferenceStarted = new UnityEvent();
    [SerializeField] private UnityEvent _onInterferenceEnded = new UnityEvent();

    private InterferenceEventManager _eventManager;

    public InterferenceEventId Id => InterferenceEventId.FieldVision;

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

    private void OnDestroy()
    {
        if (_eventManager == null)
        {
            return;
        }

        _eventManager.UnregisterEvent(this);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            StartEvent();
        }
    }

    public bool StartEvent()
    {
        Debug.Log("[Interference] FieldVision 시작", this);

        return _eventManager.TryStartEvent(Id);
    }

    public void ShowWarning()
    {
        Debug.Log("[Interference] FieldVision 경고", this);

        _onWarningStarted.Invoke();
    }

    public void Activate()
    {
        Debug.Log("[Interference] FieldVision 활성화", this);

        _onInterferenceStarted.Invoke();
    }

    public void Deactivate()
    {
        Debug.Log("[Interference] FieldVision 종료", this);

        _onInterferenceEnded.Invoke();
    }
}
