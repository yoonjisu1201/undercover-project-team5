using Unity.Netcode;
using UnityEngine;

// 범인으로 판정된 NPC의 위장을 해제해 시민 외형을 감추고 외계인 본모습을 드러낸다.
public class CriminalAlienReveal : NetworkBehaviour
{
    [SerializeField] private GameObject _humanForm;  // 평소 보이는 시민 본체(모델+아웃핏 전체를 담은 루트)
    [SerializeField] private GameObject _alienForm;  // 평소엔 비활성화된 외계인 모델 자식 오브젝트

    private readonly NetworkVariable<bool> _isRevealed = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        _isRevealed.OnValueChanged += HandleRevealedChanged;
        ApplyRevealState(_isRevealed.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isRevealed.OnValueChanged -= HandleRevealedChanged;
    }

    // 검거 판정에서 진짜 범인으로 확정됐을 때 서버가 호출한다.
    public void Reveal()
    {
        if (!IsServer) return;
        _isRevealed.Value = true;
    }

    private void HandleRevealedChanged(bool previous, bool current)
    {
        ApplyRevealState(current);
    }

    private void ApplyRevealState(bool revealed)
    {
        if (_humanForm != null)
        {
            _humanForm.SetActive(!revealed);
        }

        if (_alienForm != null)
        {
            _alienForm.SetActive(revealed);
        }
    }
}
