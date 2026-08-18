using System.Linq;
using System.Reflection;
using UnityEngine;

public sealed partial class DebugMenuController
{
    private static readonly FieldInfo HqSpawnPoint =
        typeof(HqEntrance).GetField("_hqSpawnPoint", BindingFlags.Instance | BindingFlags.NonPublic);

    // 목록의 다른 플레이어에게 이동합니다.
    public void OnTeleportPlayer1Click() => TeleportToOtherPlayer(0);
    public void OnTeleportPlayer2Click() => TeleportToOtherPlayer(1);
    public void OnTeleportPlayer3Click() => TeleportToOtherPlayer(2);

    // 주파수 미션의 안테나 설치 구역으로 이동합니다.
    public void OnTeleportAntennaZoneClick()
    {
        FrequencyAntennaZone zone = FindFirstObjectByType<FrequencyAntennaZone>();
        if (zone == null)
        {
            ShowStatus("안테나 설치 구역을 찾지 못했습니다. 미션 기계가 스폰된 뒤에 사용하세요.");
            return;
        }

        Transform target = zone.transform;
        TeleportLocalPlayer(target.position - target.forward * 2f, target.rotation);
        ShowStatus("안테나 설치 구역으로 이동했습니다.");
    }

    // 현재 라운드의 범인 NPC 앞쪽으로 이동합니다.
    public void OnTeleportCriminalClick()
    {
        CriminalNpcManager manager = FindFirstObjectByType<CriminalNpcManager>();
        if (manager == null || manager.CriminalNpc == null)
        {
            ShowStatus("현재 범인 NPC를 찾지 못했습니다.");
            return;
        }

        Transform criminal = manager.CriminalNpc.transform;
        TeleportLocalPlayer(criminal.position - criminal.forward * 2f, criminal.rotation);
        ShowStatus("범인 위치로 이동했습니다.");
    }

    // CCTV 수리 기계는 지역 CCTV 수만큼 깔리므로, 누를 때마다 1번부터 차례로 돌아간다.
    private int _cctvMissionCursor;

    // 미션 종류별 기기로 이동합니다. 기기 종류는 UI 프리팹 이름으로 가린다.
    // 전력·통신은 본부에도 같은 미션 기기가 있어서 필드용(_Field)만 골라야 한다.
    public void OnTeleportCctvMissionClick() => TeleportToNextMissionOfKind("MissionUI1", "CCTV 복구", true);
    public void OnTeleportPowerMissionClick() => TeleportToNextMissionOfKind("MissionUI4_Battery_Field", "전력 복구", false);
    public void OnTeleportCommsMissionClick() => TeleportToNextMissionOfKind("MissionUI7_Knob_Field", "통신 복구", false);
    public void OnTeleportAnalysisMissionClick() => TeleportToNextMissionOfKind("MissionUI10_SampleAnalysis", "데이터 분석", false);

    // 해당 종류의 기기들을 찾아 순서대로 이동합니다. 여러 대면 누를 때마다 다음 기기로 넘어간다.
    private void TeleportToNextMissionOfKind(string uiPrefabName, string label, bool cycle)
    {
        MissionInteractable[] targets = GetOrderedMissions()
            .Where(mission => mission.UiPrefabName == uiPrefabName)
            .ToArray();

        if (targets.Length == 0)
        {
            ShowStatus($"{label} 기기를 찾지 못했습니다. 미션 기계가 스폰된 뒤에 사용하세요.");
            return;
        }

        int index = 0;
        if (cycle)
        {
            index = _cctvMissionCursor % targets.Length;
            _cctvMissionCursor = (_cctvMissionCursor + 1) % targets.Length;
        }

        Transform target = targets[index].transform;
        TeleportLocalPlayer(target.position - target.forward * 2f, target.rotation);
        ShowStatus(targets.Length > 1
            ? $"{label} {index + 1}/{targets.Length} 위치로 이동했습니다."
            : $"{label} 위치로 이동했습니다.");
    }

    // 정렬된 미션 목록의 위치로 이동합니다.
    public void OnTeleportMission1Click() => TeleportToMission(0);
    public void OnTeleportMission2Click() => TeleportToMission(1);
    public void OnTeleportMission3Click() => TeleportToMission(2);
    public void OnTeleportMission4Click() => TeleportToMission(3);
    public void OnTeleportMission5Click() => TeleportToMission(4);
    public void OnTeleportMission6Click() => TeleportToMission(5);
    public void OnTeleportMission7Click() => TeleportToMission(6);
    public void OnTeleportMission8Click() => TeleportToMission(7);

    // 로컬 플레이어를 본부로 한 번에 이동시킵니다.
    public void OnToggleHqFieldClick()
    {
        Player localPlayer = GetLocalPlayer();
        HqEntrance entrance = FindFirstObjectByType<HqEntrance>();
        if (localPlayer == null || entrance == null)
        {
            ShowStatus("본부 이동 지점을 찾지 못했습니다.");
            return;
        }

        TeleportToSafeHqPosition(localPlayer, entrance);
        ShowStatus("본부로 이동했습니다.");
    }

    // 자신을 제외하고 소유자 ID로 정렬된 플레이어 중 지정 순번으로 이동합니다.
    private void TeleportToOtherPlayer(int index)
    {
        Player localPlayer = GetLocalPlayer();
        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None)
            .Where(player => player != localPlayer)
            .OrderBy(player => player.OwnerClientId)
            .ToArray();
        if (index < 0 || index >= players.Length)
        {
            ShowStatus("해당 플레이어를 찾지 못했습니다.");
            return;
        }

        Player target = players[index];
        TeleportLocalPlayer(target.transform.position - target.transform.forward * 1.5f, target.transform.rotation);
        ShowStatus($"{GetPlayerDisplayName(target)} 위치로 이동했습니다.");
    }

    // 이름과 인스턴스 ID로 정렬된 미션 중 지정 순번 앞으로 이동합니다.
    private void TeleportToMission(int index)
    {
        MissionInteractable[] missions = GetOrderedMissions();
        if (index < 0 || index >= missions.Length)
        {
            ShowStatus($"미션 {index + 1}을 찾지 못했습니다.");
            return;
        }

        Transform target = missions[index].transform;
        TeleportLocalPlayer(target.position - target.forward * 2f, target.rotation);
        ShowStatus($"미션 {index + 1} 위치로 이동했습니다.");
    }

    // 로컬 플레이어 이동 컴포넌트를 통해 목적지보다 조금 높은 위치로 순간이동합니다.
    private void TeleportLocalPlayer(Vector3 destination, Quaternion rotation)
    {
        Player player = GetLocalPlayer();
        if (player == null || player.PlayerMove == null)
        {
            ShowStatus("로컬 플레이어를 찾지 못했습니다.");
            return;
        }

        destination.y += 0.2f;
        player.PlayerMove.TeleportToPosition(destination, rotation);
    }

    // 본부 스폰 지점 주변에서 바닥이 안정적인 위치를 찾아 플레이어를 이동합니다.
    private void TeleportToSafeHqPosition(Player player, HqEntrance entrance)
    {
        Transform hqPoint = HqSpawnPoint?.GetValue(entrance) as Transform;
        if (player == null || player.PlayerMove == null || hqPoint == null)
        {
            ShowStatus("본부 이동 지점을 찾지 못했습니다.");
            return;
        }

        Physics.SyncTransforms();
        Vector3 destination = FindSupportedGroundPosition(hqPoint.position);
        TeleportLocalPlayer(destination, Quaternion.Euler(0f, hqPoint.eulerAngles.y, 0f));
    }

    // 스폰 원점 주변을 넓혀가며 캐릭터를 온전히 받칠 수 있는 바닥을 찾습니다.
    private static Vector3 FindSupportedGroundPosition(Vector3 origin)
    {
        Vector3[] directions =
        {
            Vector3.zero, Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
            (Vector3.right + Vector3.forward).normalized,
            (Vector3.right + Vector3.back).normalized,
            (Vector3.left + Vector3.forward).normalized,
            (Vector3.left + Vector3.back).normalized
        };

        for (float radius = 0f; radius <= 2f; radius += 0.5f)
        {
            foreach (Vector3 direction in directions)
            {
                if (TryGetSupportedGround(origin + direction * radius, out Vector3 groundPosition))
                {
                    return groundPosition;
                }
            }
        }

        return origin;
    }

    // 후보 지점의 중앙과 네 방향 바닥 높이가 충분히 일정한지 검사합니다.
    private static bool TryGetSupportedGround(Vector3 candidate, out Vector3 groundPosition)
    {
        const float supportRadius = 0.55f;
        Vector3[] offsets =
        {
            Vector3.zero, Vector3.right * supportRadius, Vector3.left * supportRadius,
            Vector3.forward * supportRadius, Vector3.back * supportRadius
        };

        groundPosition = candidate;
        float centerGroundY = 0f;
        for (int index = 0; index < offsets.Length; index++)
        {
            Vector3 rayOrigin = candidate + offsets[index] + Vector3.up * 2f;
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                hit.collider.GetComponentInParent<Player>() != null)
            {
                return false;
            }

            if (index == 0)
            {
                centerGroundY = hit.point.y;
                groundPosition = hit.point;
            }
            else if (Mathf.Abs(hit.point.y - centerGroundY) > 0.25f)
            {
                return false;
            }
        }

        return true;
    }
}
