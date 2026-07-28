using System.Linq;
using Unity.Netcode;
using UnityEngine;

public sealed class FieldVisionInterferenceEffect : InterferenceEffectBase
{
    // 카메라 앞에 생성할 안개 파티클 프리팹입니다.
    [SerializeField]
    private ParticleSystem _fogPrefab;

    // 현재 로컬 카메라 앞에 생성된 안개입니다.
    private ParticleSystem _activeFog;

    public override InterferenceEffectType _type => InterferenceEffectType.FieldVision;

    private void Awake()
    {
        var player = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Player>();
        var position = player.PlayerMove.HeadPivot.transform.localPosition;

        _activeFog = Instantiate(_fogPrefab, player.PlayerMove.HeadPivot.transform);
        _activeFog.transform.localPosition = position;
        _activeFog.gameObject.SetActive(false);
    }

    // 임시 구현: 현재 연결된 모든 클라이언트를 대상으로 반환합니다.
    // 현장 역할만 대상으로 걸러내는 실제 필터링은 #293에서 구현합니다.
    public override ulong[] GetTargetClientsList()
    {
        return NetworkManager.ConnectedClientsIds.ToArray();
    }

    protected override void OnActivateEffect()
    {
        _activeFog.gameObject.SetActive(true);
    }

    protected override void OnDeactivateEffect(InterferenceEndReason reason)
    {
        _activeFog.gameObject.SetActive(false);
    }
}
