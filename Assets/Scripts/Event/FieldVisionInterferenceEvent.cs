// 플레이어의 시야를 방해하는 효과를 식별합니다.
public sealed class FieldVisionInterferenceEvent : InterferenceEffectBase
{
    // 시야 방해 효과 식별자를 가져옵니다.
    public override InterferenceEffectId Id => InterferenceEffectId.FieldVision;
}
