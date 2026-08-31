using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 로컬 플레이어 탐색, 이동속도와 동적 플레이어 버튼을 관리합니다.
public sealed partial class DebugMenuController
{
    private const float FastWalkMultiplier = 3f;
    private static readonly FieldInfo MoveSpeedField =
        typeof(PlayerMoveSample).GetField("_moveSpeed", BindingFlags.Instance | BindingFlags.NonPublic);

    [Header("Dynamic Player Buttons")]
    [SerializeField] private Button[] _otherPlayerButtons;
    [SerializeField] private TMP_Text[] _otherPlayerButtonTexts;

    private readonly Dictionary<PlayerMoveSample, float> _originalMoveSpeeds = new();
    private bool _fastWalkEnabled;

    // 로컬 플레이어의 걷기 속도를 3배로 토글하고 원래 값을 보존합니다.
    public void OnToggleFastWalkClick()
    {
        if (_fastWalkEnabled)
        {
            RestoreWalkSpeed();
            ShowStatus("빨리 걷기를 취소했습니다.");
            return;
        }

        Player localPlayer = GetLocalPlayer();
        if (localPlayer == null || localPlayer.PlayerMove == null || MoveSpeedField == null)
        {
            ShowStatus("로컬 플레이어 이동 컴포넌트를 찾지 못했습니다.");
            return;
        }

        PlayerMoveSample move = localPlayer.PlayerMove;
        float originalSpeed = (float)MoveSpeedField.GetValue(move);
        _originalMoveSpeeds[move] = originalSpeed;
        MoveSpeedField.SetValue(move, originalSpeed * FastWalkMultiplier);
        _fastWalkEnabled = true;
        RefreshButtonLabels();
        ShowStatus("걷기 속도를 3배로 변경했습니다.");
    }

    // 변경한 모든 이동속도를 저장된 원래 값으로 복구합니다.
    // 오브젝트 파괴/씬 언로드 중에는 GameObject.Find가 안전하지 않으므로 라벨 갱신을 건너뜁니다.
    private void RestoreWalkSpeed(bool refreshLabels = true)
    {
        foreach (KeyValuePair<PlayerMoveSample, float> entry in _originalMoveSpeeds)
        {
            if (entry.Key != null && MoveSpeedField != null) MoveSpeedField.SetValue(entry.Key, entry.Value);
        }

        _originalMoveSpeeds.Clear();
        _fastWalkEnabled = false;
        if (refreshLabels) RefreshButtonLabels();
    }

    // 현재 디버그 상태에 맞춰 동적 버튼 문구를 갱신합니다.
    private void RefreshButtonLabels()
    {
        if (_fastWalkButtonText != null)
        {
            _fastWalkButtonText.text = _fastWalkEnabled ? "빨리 걷기 X3 취소" : "빨리 걷기 X3";
        }

        RefreshHqFieldButtonLabel();
        RefreshDebugLightButtonLabel();
    }

    // 본부 이동 버튼 문구를 설정합니다.
    private void RefreshHqFieldButtonLabel()
    {
        if (_hqFieldButtonText == null) return;

        _hqFieldButtonText.text = "본부로 이동";
    }

    // 자신을 제외한 최대 세 명의 이름과 버튼 표시 여부를 갱신합니다.
    private void RefreshOtherPlayerButtons()
    {
        Player localPlayer = GetLocalPlayer();
        Player[] otherPlayers = FindObjectsByType<Player>(FindObjectsSortMode.None)
            .Where(player => player != localPlayer)
            .OrderBy(player => player.OwnerClientId)
            .Take(3)
            .ToArray();

        int buttonCount = Mathf.Min(_otherPlayerButtons?.Length ?? 0, _otherPlayerButtonTexts?.Length ?? 0);
        for (int index = 0; index < buttonCount; index++)
        {
            bool hasPlayer = index < otherPlayers.Length;
            if (_otherPlayerButtons[index] != null) _otherPlayerButtons[index].gameObject.SetActive(hasPlayer);
            if (hasPlayer && _otherPlayerButtonTexts[index] != null)
            {
                _otherPlayerButtonTexts[index].text = GetPlayerDisplayName(otherPlayers[index]);
            }
        }
    }

    // 네트워크 플레이어 이름이 아직 준비되지 않았으면 임시 이름을 반환합니다.
    private static string GetPlayerDisplayName(Player player)
    {
        try
        {
            return player.PlayerName;
        }
        catch (InvalidOperationException)
        {
            return $"Player {player.OwnerClientId + 1}";
        }
    }

    // 현재 클라이언트가 소유한 로컬 플레이어를 찾습니다.
    private static Player GetLocalPlayer()
    {
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out Player networkPlayer))
        {
            return networkPlayer;
        }

        return FindObjectsByType<Player>(FindObjectsSortMode.None).FirstOrDefault(player => player.IsOwner);
    }
}
