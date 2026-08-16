using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public sealed partial class DebugMenuController
{
    private const float BringCluesForwardOffset = 2f;
    private const float BringCluesSideSpacing = 0.65f;
    private const float BringCluesHeightOffset = 0.35f;

    public void OnBringCluesClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestBringCluesRpc();
        ShowStatus("필드 단서를 내 위치 앞으로 이동 요청했습니다.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestBringCluesRpc(RpcParams rpcParams = default)
    {
        if (!TryGetSenderPlayer(rpcParams, out Player player))
        {
            return;
        }

        ClueItem[] clues = FindObjectsByType<ClueItem>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        List<ClueItem> fieldClues = new();

        foreach (ClueItem clue in clues)
        {
            if (clue != null && !clue.IsStored && clue.NetworkObject != null && clue.NetworkObject.IsSpawned)
            {
                fieldClues.Add(clue);
            }
        }

        Vector3 basePosition = player.transform.position +
                               player.transform.forward * BringCluesForwardOffset +
                               Vector3.up * BringCluesHeightOffset;
        Quaternion rotation = Quaternion.LookRotation(player.transform.forward, Vector3.up);

        for (int index = 0; index < fieldClues.Count; index++)
        {
            float sideOffset = (index - (fieldClues.Count - 1) * 0.5f) * BringCluesSideSpacing;
            Vector3 position = basePosition + player.transform.right * sideOffset;
            MoveClueTo(fieldClues[index], position, rotation);
        }

        Debug.Log($"[DebugMenu] 필드 단서 {fieldClues.Count}개를 플레이어 앞으로 이동했습니다.", this);
    }

    private static void MoveClueTo(ClueItem clue, Vector3 position, Quaternion rotation)
    {
        clue.transform.SetPositionAndRotation(position, rotation);

        if (clue.TryGetComponent(out NetworkTransform networkTransform))
        {
            networkTransform.Teleport(position, rotation, clue.transform.localScale);
        }

        if (clue.TryGetComponent(out Rigidbody rigidbody))
        {
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.isKinematic = true;
        }

        clue.BlockInteraction(0.1f);
    }
}
