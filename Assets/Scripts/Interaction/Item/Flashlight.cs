using Unity.Netcode;
using UnityEngine;

// 손전등 오브젝트에 부착. 몸통(메쉬)은 ItemBase와 동일하게 손 회전을 따라가되, 소켓 자체의 고정
// 어긋남은 _holdRotationOffset으로 보정한다. 실제 빛(Light)은 PlayerCameraController.LightFollowPivot
// (카메라 시야각을 오너/논오너 모두 그대로 따라가는 피벗)에 파렌팅해 raycast 없이도 시선 방향을 공유한다.
// 손 소켓(NetworkObject 아님)엔 파렌팅할 수 없어서, 몸통 위치는 매 프레임 handAnchor를 따라간다.
// Light는 NetworkObject가 아니라 실제 Transform.SetParent로 그 피벗 밑에 붙일 수 있다.
public class Flashlight : NetworkBehaviour
{
    [SerializeField] private Vector3 _holdOffset;
    [SerializeField] private Vector3 _holdRotationOffset;
    [SerializeField] private Vector3 _headLightLocalOffset;

    // On/Off 상태는 오너가 직접 토글하는 값이라 Owner 권한으로 쓴다.
    private readonly NetworkVariable<bool> _isOn =
        new(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private Transform _handAnchor;
    private Renderer[] _renderers;
    // 프리팹 안에 Spot Light, Point Light 두 개가 있어서 배열로 한꺼번에 켜고 끈다.
    private Light[] _lights;
    private bool _isVisible = true;

    public void Initialize(PlayerCameraController playerCameraController, Transform handAnchor)
    {
        _handAnchor = handAnchor;

        Transform lightAnchor = playerCameraController.LightFollowPivot;
        foreach (Light light in _lights)
        {
            light.transform.SetParent(lightAnchor, worldPositionStays: false);
            light.transform.SetLocalPositionAndRotation(_headLightLocalOffset, Quaternion.identity);
        }
    }

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _lights = GetComponentsInChildren<Light>(true);
    }

    public override void OnNetworkSpawn()
    {
        _isOn.OnValueChanged += HandleIsOnChanged;
        ApplyLightState();
    }

    public override void OnNetworkDespawn()
    {
        _isOn.OnValueChanged -= HandleIsOnChanged;
    }

    public void ToggleOnOff()
    {
        if (!IsOwner)
        {
            return;
        }

        _isOn.Value = !_isOn.Value;
    }

    private void HandleIsOnChanged(bool previousValue, bool newValue)
    {
        ApplyLightState();
    }

    // 카트를 드는 등 손이 다른 데 쓰일 때 시각적으로 숨긴다.
    // NetworkObject라 GameObject.SetActive 대신 렌더러/라이트만 끈다(ItemBase와 동일한 이유).
    public void SetVisible(bool isVisible)
    {
        _isVisible = isVisible;
        foreach (Renderer itemRenderer in _renderers)
        {
            itemRenderer.enabled = isVisible;
        }
        ApplyLightState();
    }

    private void ApplyLightState()
    {
        bool shouldBeOn = _isOn.Value && _isVisible;
        foreach (Light light in _lights)
        {
            light.enabled = shouldBeOn;
        }
    }

    private void LateUpdate()
    {
        if (_handAnchor != null)
        {
            transform.SetPositionAndRotation(
                _handAnchor.TransformPoint(_holdOffset),
                _handAnchor.rotation * Quaternion.Euler(_holdRotationOffset));
        }
    }
}
