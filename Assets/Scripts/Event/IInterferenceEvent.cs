
// 방해 이벤트 종류를 식별하는 값을 정의합니다.

public enum InterferenceEventId
{
    // 활성화된 방해 이벤트가 없음을 나타냅니다.
    None,

    // 플레이어의 시야를 방해하는 이벤트를 나타냅니다.
    FieldVision,
}

public enum InterferenceEndReason
{
    Normal,
    RoundEnded,
    Replaced,
    Despawned,
}


// 방해 이벤트가 따라야 하는 경고, 시작, 종료 수명 주기를 정의합니다.

public interface IInterferenceEvent
{
    InterferenceEventId Id { get; }

    bool IsWarningActive { get; }
    bool IsActive { get; }

    void ShowWarning();
    void CancelWarning();
    void Activate();
    void Deactivate(InterferenceEndReason reason);
}
