using System.Linq;
using Unity.Netcode;
using UnityEngine;

public sealed class FieldVisionInterferenceEffect : InterferenceEffectBase
{
    // 맵 위 특정 지점에 생성할 안개 파티클 프리팹입니다.
    [SerializeField] private GameObject _fogPrefab;

    // 안개를 배치할 위치를 계산할 때 사용하는 맵 구역 컨트롤러입니다.
    [SerializeField] private MapRegionController _regionController;

    // 한 번에 배치할 안개 개수입니다.
    private const int FogCount = 3;

    // 매 발동마다 재사용할 로컬 안개 오브젝트입니다. 네트워크로 스폰되지 않는 순수 로컬 시각 효과입니다.
    private readonly GameObject[] _fogs = new GameObject[FogCount];

    public override InterferenceEffectType _type => InterferenceEffectType.FieldVision;

    private void Awake()
    {
        _duration = 10f;

        for (int i = 0; i < FogCount; i++)
        {
            _fogs[i] = Instantiate(_fogPrefab);
            _fogs[i].SetActive(false);
        }
    }

    // 임시 구현: 현재 연결된 모든 클라이언트를 대상으로 반환합니다.
    public override ulong[] GetTargetClientsList()
    {
        return NetworkManager.ConnectedClientsIds.ToArray();
    }

    // 서버에서 호출합니다. 안개를 배치할 랜덤 위치 3곳을 계산해 대상 클라이언트에게 먼저 전달한 뒤,
    // base.Activate()로 평소와 같은 시작 RPC를 보냅니다.
    public override void Activate()
    {
        if (!TryGetRandomPositions(out Vector3[] positions))
        {
            Debug.LogError("[FieldVisionInterferenceEffect] 안개를 배치할 위치를 찾지 못했습니다.", this);
            return;
        }

        SetFogPositionsRpc(positions, RpcTarget.Group(GetTargetClientsList(), RpcTargetUse.Temp));

        base.Activate();
    }

    protected override void OnActivateEffect()
    {
        foreach (GameObject fog in _fogs)
        {
            fog.SetActive(true);
        }
    }

    protected override void OnDeactivateEffect(InterferenceEndReason reason)
    {
        foreach (GameObject fog in _fogs)
        {
            fog.SetActive(false);
        }
    }

    // 현재 해금된 구역 안에서 랜덤 위치 3곳을 계산합니다.
    private bool TryGetRandomPositions(out Vector3[] positions)
    {
        positions = new Vector3[FogCount];

        if (_regionController == null || !_regionController.RefreshSpawnAreas())
        {
            return false;
        }

        for (int i = 0; i < FogCount; i++)
        {
            if (!_regionController.TryGetRandomSpawnPoint(out _, out Vector3 position))
            {
                return false;
            }

            positions[i] = position;
        }

        return true;
    }

    // 대상 클라이언트에서 안개 3개를 서버가 계산한 위치로 옮깁니다. (활성화는 base의 ActivateRpc가 따로 담당)
    [Rpc(SendTo.SpecifiedInParams)]
    private void SetFogPositionsRpc(Vector3[] positions, RpcParams rpcParams = default)
    {
        for (int i = 0; i < FogCount && i < positions.Length; i++)
        {
            _fogs[i].transform.position = positions[i];
        }
    }
}
