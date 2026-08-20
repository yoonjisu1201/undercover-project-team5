using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 화면 고정 위치에 떠 있는 개인 스태미나 게이지. PlayerCameraController와 같은 방식으로,
// Canvas 자체는 모든 인스턴스에 붙어있지만 오너가 아니면 꺼둔다.
public class StaminaGaugeUI : NetworkBehaviour
{
    [SerializeField] private GameObject _canvasRoot;
    [SerializeField] private Slider _staminaSlider;

    private PlayerStamina _playerStamina;

    private void Awake()
    {
        _playerStamina = GetComponent<PlayerStamina>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            _canvasRoot.SetActive(false);
            return;
        }

        _staminaSlider.maxValue = _playerStamina.MaxStamina;
        _staminaSlider.value = _playerStamina.CurrentStamina;

        _playerStamina.StaminaChanged += HandleStaminaChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        _playerStamina.StaminaChanged -= HandleStaminaChanged;
    }

    private void HandleStaminaChanged(float previousValue, float newValue)
    {
        _staminaSlider.value = newValue;
    }
}
