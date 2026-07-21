/// <summary>
/// 방해 이벤트가 따라야 하는 경고, 시작, 종료 수명 주기를 정의합니다.
/// </summary>
public interface IInterferenceEvent
{
    /// <summary>이벤트를 식별하는 값을 가져옵니다.</summary>
    InterferenceEventId Id { get; }

    /// <summary>이벤트가 시작되기 전에 경고를 표시합니다.</summary>
    void ShowWarning();

    /// <summary>이벤트 효과를 시작합니다.</summary>
    void Activate();

    /// <summary>이벤트 효과를 종료합니다.</summary>
    void Deactivate();
}
