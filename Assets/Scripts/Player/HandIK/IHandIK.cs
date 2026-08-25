// PlayerItemIK가 우선순위에 따라 어느 손 IK를 적용할지 판단하기 위한 공통 인터페이스.

public interface IHandIK {
    bool IsActive { get; }
    void ApplyIK(int layerIndex);
}
