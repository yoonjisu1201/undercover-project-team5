using UnityEngine;
using UnityEngine.UI;

// GameScene 씬의 마이크/스피커 뮤트 버튼을 VivoxManager와 연결한다.
public class GameSceneUI : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private Button _micMuteButton;
    [SerializeField] private Button _outputMuteButton;

    // 옅은 붉은색(뮤트) / 옅은 녹색(언뮤트): 투명도를 낮춰서 옅게 보이도록 한다.
    private static readonly Color MutedColor = new Color(1f, 0f, 0f, 0.5f);
    private static readonly Color UnmutedColor = new Color(0f, 1f, 0f, 0.5f);

    private void Start()
    {
        _micMuteButton.onClick.AddListener(HandleMicMuteButtonClicked);
        _outputMuteButton.onClick.AddListener(HandleOutputMuteButtonClicked);

        UpdateMicMuteButtonColor();
        UpdateOutputMuteButtonColor();
    }

    private void OnDestroy()
    {
        _micMuteButton.onClick.RemoveListener(HandleMicMuteButtonClicked);
        _outputMuteButton.onClick.RemoveListener(HandleOutputMuteButtonClicked);
    }

    private void HandleMicMuteButtonClicked()
    {
        VivoxManager.Instance.ToggleMicMute();
        UpdateMicMuteButtonColor();
    }

    private void UpdateMicMuteButtonColor()
    {
        _micMuteButton.targetGraphic.color = VivoxManager.IsMicMuted ? MutedColor : UnmutedColor;
    }

    private void HandleOutputMuteButtonClicked()
    {
        VivoxManager.Instance.ToggleOutputMute();
        UpdateOutputMuteButtonColor();
    }

    private void UpdateOutputMuteButtonColor()
    {
        _outputMuteButton.targetGraphic.color = VivoxManager.IsOutputMuted ? MutedColor : UnmutedColor;
    }
}
