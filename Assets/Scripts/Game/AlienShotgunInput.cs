using Unity.Netcode;
using UnityEngine;

// 외계인 전용 단발 총("AlienShotgun" 아이템, AlienCaptureGun_Visual을 복제해 만든 별도 아이템).
// 실제 NetworkObject로 스폰되는 탄알(AlienBulletProjectile)이 서버 권위로 이동하며,
// 도중에 맞은 대상에게 도착 시점에 데미지를 준다(히트스캔 아님, 투사체 이동 시간 있음).
public class AlienShotgunInput : NetworkBehaviour
{
    public const string ItemId = "AlienShotgun";

    [SerializeField] private Camera _playerCamera;
    [SerializeField] private Transform _muzzlePoint;
    [SerializeField] private GameObject _bulletProjectilePrefab;
    [SerializeField] private GameObject _handToolVisual;
    [SerializeField, Min(0f)] private float _range = 30f;
    [SerializeField, Min(0f)] private float _damage = 10f;

    // 이 도구가 선택되어 있는지 여부. 손에 든 비주얼을 켜고 끄는 데 쓰며, 다른 클라이언트도 볼 수 있게 동기화한다.
    private readonly NetworkVariable<bool> _isToolSelected =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // 검거도구(PlayerArrestInput)와 같은 상체 총 자세/조준 IK를 공유해서 쓰는 파라미터라 이름은 그대로 재사용한다.
    private static readonly int IsUsingArrestToolHash = Animator.StringToHash("IsUsingArrestTool");

    private CustomInputActions _actions;
    private PlayerInventory _inventory;
    private Animator _animator;

    // 입력 액션 인스턴스를 만들고 같은 오브젝트의 인벤토리를 캐싱한다.
    private void Awake()
    {
        _actions = new CustomInputActions();
        _inventory = GetComponent<PlayerInventory>();
        _animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();
    }

    private void OnDisable()
    {
        _actions?.Disable();
    }

    // 내 캐릭터가 아니면(다른 플레이어를 보고 있는 인스턴스) 입력을 받지 않는다.
    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            _actions.Disable();
        }

        _isToolSelected.OnValueChanged += HandleToolSelectedChanged;
        ApplyVisual(_isToolSelected.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isToolSelected.OnValueChanged -= HandleToolSelectedChanged;
    }

    // 도구가 선택된 상태에서 클릭 한 번(홀드 아님)마다 발사를 시도한다.
    private void Update()
    {
        if (!IsOwner) return;

        _isToolSelected.Value = IsToolSelected();

        if (_playerCamera == null || _muzzlePoint == null) return;
        if (GameplayUiMode.IsActive) return;
        if (!_isToolSelected.Value) return;
        if (!_actions.Player.ArrestTool.WasPressedThisFrame()) return;

        TryFire();
    }

    // 도구 선택 여부를 확인한다. AlienShotgun이 선택되어 있지 않으면 클릭을 무시한다.
    private bool IsToolSelected()
    {
        return _inventory != null
            && _inventory.TryGetSelectedItem(out string itemId)
            && itemId == ItemId;
    }

    private void HandleToolSelectedChanged(bool previousValue, bool currentValue)
    {
        ApplyVisual(currentValue);
    }

    // 도구 선택 상태에 따라 손에 든 비주얼 오브젝트를 켜고 끈다.
    private void ApplyVisual(bool isSelected)
    {
        _animator?.SetBool(IsUsingArrestToolHash, isSelected);

        if (_handToolVisual != null)
        {
            _handToolVisual.SetActive(isSelected);
        }
    }

    // 총구 위치/조준 방향을 서버에 전달해 실제 탄알 스폰을 요청한다.
    private void TryFire()
    {
        RequestFireRpc(_muzzlePoint.position, _playerCamera.transform.forward);
    }

    // 서버가 실제 탄알 NetworkObject를 스폰한다. 이후 판정은 AlienBulletProjectile이 이동하며 직접 수행한다.
    [Rpc(SendTo.Server)]
    private void RequestFireRpc(Vector3 origin, Vector3 direction)
    {
        if (_bulletProjectilePrefab == null) return;

        GameObject bulletObject = Instantiate(_bulletProjectilePrefab, origin, Quaternion.LookRotation(direction));

        if (!bulletObject.TryGetComponent(out NetworkObject networkObject) ||
            !bulletObject.TryGetComponent(out AlienBulletProjectile projectile))
        {
            Debug.LogError("[AlienShotgunInput] 탄알 프리팹에 NetworkObject 또는 AlienBulletProjectile이 없습니다.", this);
            Destroy(bulletObject);
            return;
        }

        projectile.Initialize(direction, _range, _damage);
        networkObject.Spawn(destroyWithScene: true);
    }
}
