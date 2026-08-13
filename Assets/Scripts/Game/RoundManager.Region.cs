using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 라운드마다 플레이할 맵 구역을 서버가 추첨해 전 클라이언트에 같은 값으로 적용한다.
public partial class RoundManager
{
    // 현재 라운드의 활성 구역. 늦게 접속한 클라이언트도 동기화된 이 값으로 같은 구역을 본다.
    private readonly NetworkVariable<RegionId> _activeRegionId =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("라운드마다 활성 구역을 바꿀 맵 구역 컨트롤러")]
    [SerializeField] private MapRegionController _regionController;

    // 라운드 1의 NPC·단서는 씬 로드 완료 이벤트로 스폰되므로, 그보다 앞선 이 시점에 구역을 확정해야 한다.
    // 서버는 직접 추첨해 적용하고, 클라이언트는 스폰 페이로드로 이미 받은 값을 그대로 적용한다.
    private void BeginRegionFlow()
    {
        _activeRegionId.OnValueChanged += HandleActiveRegionChanged;

        if (IsServer)
        {
            SelectRegionForRound(avoidCurrent: false);
            return;
        }

        ApplyActiveRegion(_activeRegionId.Value);
    }

    private void EndRegionFlow()
    {
        _activeRegionId.OnValueChanged -= HandleActiveRegionChanged;
    }

    // 서버는 SelectRegionForRound에서 이미 적용했으므로 중복 적용하지 않는다.
    private void HandleActiveRegionChanged(RegionId previous, RegionId current)
    {
        if (IsServer) return;

        ApplyActiveRegion(current);
    }

    // 서버가 씬에 배치된 구역 중 하나를 뽑아 동기화하고 자기 화면에도 적용한다.
    private void SelectRegionForRound(bool avoidCurrent)
    {
        if (_regionController == null || _regionController.Regions == null)
        {
            Debug.LogError("[RoundManager] MapRegionController 참조가 비어 있어 맵 구역을 추첨할 수 없습니다.", this);
            return;
        }

        List<RegionId> candidates = new();
        foreach (MapRegion region in _regionController.Regions)
        {
            // SpawnArea가 없는 구역은 RefreshSpawnAreas()에서 제외되어 NPC를 배치할 수 없다.
            if (region != null && region.SpawnArea != null)
            {
                candidates.Add(region.RegionId);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogError("[RoundManager] 사용할 수 있는 맵 구역이 없습니다.", this);
            return;
        }

        // 구역이 하나뿐이면 연속 회피가 불가능하므로 같은 구역을 그대로 다시 쓴다.
        if (avoidCurrent && candidates.Count > 1)
        {
            candidates.Remove(_activeRegionId.Value);
        }

        RegionId picked = candidates[Random.Range(0, candidates.Count)];
        _activeRegionId.Value = picked;
        ApplyActiveRegion(picked);
    }

    // SetActiveRegion() 안에서 구역 잠금 전환, CCTV 전환, RefreshSpawnAreas()까지 함께 끝난다.
    private void ApplyActiveRegion(RegionId regionId)
    {
        if (_regionController == null || !_regionController.SetActiveRegion(regionId))
        {
            Debug.LogError($"[RoundManager] 맵 구역 {regionId}를 적용하지 못했습니다.", this);
            return;
        }

        Debug.Log($"[RoundManager] 맵 구역 {regionId}를 적용했습니다.", this);
    }
}
