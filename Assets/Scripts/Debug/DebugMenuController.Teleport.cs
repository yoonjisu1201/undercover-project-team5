using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public sealed partial class DebugMenuController
{
    private static readonly FieldInfo HqSpawnPointField =
        typeof(HqEntrance).GetField("_hqSpawnPoint", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo FieldSpawnPointField =
        typeof(HqExit).GetField("_fieldSpawnPoint", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly Dictionary<ulong, Pose> _fieldReturnPoses = new();

    // 목록의 다른 플레이어에게 이동합니다.
    public void OnTeleportPlayer1Click() => TeleportToOtherPlayer(0);
    public void OnTeleportPlayer2Click() => TeleportToOtherPlayer(1);
    public void OnTeleportPlayer3Click() => TeleportToOtherPlayer(2);

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

    // 정렬된 미니게임 목록의 위치로 이동합니다.
    public void OnTeleportMiniGame1Click() => TeleportToMiniGame(0);
    public void OnTeleportMiniGame2Click() => TeleportToMiniGame(1);
    public void OnTeleportMiniGame3Click() => TeleportToMiniGame(2);
    public void OnTeleportMiniGame4Click() => TeleportToMiniGame(3);
    public void OnTeleportMiniGame5Click() => TeleportToMiniGame(4);
    public void OnTeleportMiniGame6Click() => TeleportToMiniGame(5);
    public void OnTeleportMiniGame7Click() => TeleportToMiniGame(6);
    public void OnTeleportMiniGame8Click() => TeleportToMiniGame(7);

    // 플레이어의 현재 위치를 판별해 본부와 필드 중 반대편으로 이동합니다.
    public void OnToggleHqFieldClick()
    {
        Player localPlayer = GetLocalPlayer();
        HqEntrance entrance = FindFirstObjectByType<HqEntrance>();
        HqExit exit = FindFirstObjectByType<HqExit>();
        if (localPlayer == null || entrance == null || exit == null)
        {
            ShowStatus("본부/필드 이동 지점을 찾지 못했습니다.");
            return;
        }

        if (IsCloserToHq(localPlayer.transform.position, entrance, exit))
        {
            TeleportFromHqToField(localPlayer, exit);
        }
        else
        {
            SaveFieldReturnPose(localPlayer);
            TeleportToSafeHqPosition(localPlayer, entrance);
            ShowStatus("본부로 이동했습니다.");
        }

        RefreshButtonLabels();
    }

    // 역할 변경 직후 현재 역할에 맞는 본부 또는 필드 위치로 이동합니다.
    private void MoveLocalPlayerToRoleArea(Role role)
    {
        Player player = GetLocalPlayer();
        HqEntrance entrance = FindFirstObjectByType<HqEntrance>();
        HqExit exit = FindFirstObjectByType<HqExit>();
        if (player == null || entrance == null || exit == null)
        {
            ShowStatus("역할에 맞는 이동 지점을 찾지 못했습니다.");
            return;
        }

        if (role == Role.Headquarter)
        {
            if (!IsCloserToHq(player.transform.position, entrance, exit))
            {
                SaveFieldReturnPose(player);
            }
            TeleportToSafeHqPosition(player, entrance);
            return;
        }

        TeleportFromHqToField(player, exit);
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

    // 이름과 인스턴스 ID로 정렬된 미니게임 중 지정 순번 앞으로 이동합니다.
    private void TeleportToMiniGame(int index)
    {
        MiniGameInteractable[] miniGames = FindObjectsByType<MiniGameInteractable>(FindObjectsSortMode.None)
            .OrderBy(miniGame => miniGame.name, StringComparer.Ordinal)
            .ThenBy(miniGame => miniGame.GetInstanceID())
            .ToArray();
        if (index < 0 || index >= miniGames.Length)
        {
            ShowStatus($"미니게임 {index + 1}을 찾지 못했습니다.");
            return;
        }

        Transform target = miniGames[index].transform;
        TeleportLocalPlayer(target.position - target.forward * 2f, target.rotation);
        ShowStatus($"미니게임 {index + 1} 위치로 이동했습니다.");
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
        Transform hqPoint = HqSpawnPointField?.GetValue(entrance) as Transform;
        if (player == null || player.PlayerMove == null || hqPoint == null)
        {
            ShowStatus("본부 이동 지점을 찾지 못했습니다.");
            return;
        }

        Physics.SyncTransforms();
        Vector3 destination = FindSupportedGroundPosition(hqPoint.position);
        TeleportLocalPlayer(destination, Quaternion.Euler(0f, hqPoint.eulerAngles.y, 0f));
    }

    // 최근 필드 위치가 있으면 복귀하고, 없으면 해방된 필드의 임의 위치로 이동합니다.
    private void TeleportFromHqToField(Player player, HqExit fallbackExit)
    {
        if (_fieldReturnPoses.TryGetValue(player.OwnerClientId, out Pose returnPose))
        {
            TeleportLocalPlayer(returnPose.position, returnPose.rotation);
            ShowStatus("최근 필드 위치로 이동했습니다.");
            return;
        }

        MapRegionController regionController = FindFirstObjectByType<MapRegionController>();
        if (regionController != null &&
            regionController.RefreshSpawnAreas() &&
            regionController.TryGetRandomSpawnPoint(out MapRegion region, out Vector3 position))
        {
            Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            SaveFieldReturnPose(player.OwnerClientId, position, rotation);
            TeleportLocalPlayer(position, rotation);
            ShowStatus($"선택된 {region.RegionId} 지역의 임의 위치로 이동했습니다.");
            return;
        }

        fallbackExit.Interact(player.gameObject);
        Transform fallbackPoint = FieldSpawnPointField?.GetValue(fallbackExit) as Transform;
        if (fallbackPoint != null)
        {
            SaveFieldReturnPose(player.OwnerClientId, fallbackPoint.position, fallbackPoint.rotation);
        }
        ShowStatus("해방된 필드 위치를 찾지 못해 기본 필드 위치로 이동했습니다.");
    }

    // 플레이어가 본부로 들어가기 직전의 필드 위치와 방향을 최근 위치로 저장합니다.
    private void SaveFieldReturnPose(Player player)
    {
        SaveFieldReturnPose(player.OwnerClientId, player.transform.position, player.transform.rotation);
    }

    // 지정한 플레이어의 최근 필드 위치와 방향을 저장합니다.
    private void SaveFieldReturnPose(ulong clientId, Vector3 position, Quaternion rotation)
    {
        _fieldReturnPoses[clientId] = new Pose(position, rotation);
    }

    // 씬이 바뀌면 이전 씬 좌표가 재사용되지 않도록 저장된 복귀 위치를 비웁니다.
    private void ClearSavedFieldReturnPoses()
    {
        _fieldReturnPoses.Clear();
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

    // 본부와 필드 스폰 지점까지의 거리를 비교해 현재 플레이어 위치를 판별합니다.
    private static bool IsCloserToHq(Vector3 playerPosition, HqEntrance entrance, HqExit exit)
    {
        Transform hqPoint = HqSpawnPointField?.GetValue(entrance) as Transform;
        Transform fieldPoint = FieldSpawnPointField?.GetValue(exit) as Transform;
        return hqPoint != null && fieldPoint != null &&
               Vector3.SqrMagnitude(playerPosition - hqPoint.position) <
               Vector3.SqrMagnitude(playerPosition - fieldPoint.position);
    }
}
