using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// 외계인 복제체의 서버 권한 체력, 피격 표시와 사망 흐름을 관리한다.
// HP가 0이 되면 공격·이동 중단 이벤트를 먼저 발생시키고 사망 애니메이션을 실행한다.
// 실제 디스폰은 사망 Animation Event가 완료 이벤트를 발생시킨 뒤 AlienCloneManager가 처리한다.
public class AlienCloneHealth : NetworkBehaviour, IDamageable
{
    // Animator 파라미터: AlienAnimator의 Death Trigger와 이름이 일치해야 한다.
    private static readonly int DeathHash = Animator.StringToHash("Death");
    // 셰이더 프로퍼티: 외계인 Material이 사용하는 URP/Lit의 _BaseColor를 변경한다.
    private static readonly int BaseColorHash = Shader.PropertyToID("_BaseColor");

    [Header("HP 설정 (임시 기본값, 추후 밸런싱 이슈로 조정)")]
    [SerializeField] private float _maxHp = 50f;
    // Inspector 설정: 생존 상태에서 피격 색상을 유지할 시간이다.
    [SerializeField, Min(0f)] private float _damageFlashDuration = 0.15f;

    private readonly NetworkVariable<float> _currentHp =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isDowned =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 첫 번째 자식 Renderer에 피격 색상을 적용하고 NetworkAnimator로 사망 Trigger를 동기화한다.
    // MaterialPropertyBlock은 공유 Material을 직접 변경하지 않고 해당 Renderer에만 색상을 덮어쓴다.
    // Coroutine 참조는 연속 피격 시 이전 피격 표시를 중단하기 위해 보관한다.
    private Renderer _renderer;
    private NetworkAnimator _networkAnimator;
    private MaterialPropertyBlock _materialPropertyBlock;
    private Coroutine _damageFlashCoroutine;

    // 외부에서 변경 감지 구독
    public event Action<float, float> HpChanged;
    public event Action Died;
    // 사망 Animation Event가 완료되면 자신을 전달한다.
    // AlienCloneManager는 전달받은 개체를 목록에서 제거하고 네트워크 디스폰한다.
    public event Action<AlienCloneHealth> DeathAnimationCompleted;

    public float MaxHp => _maxHp;
    public float CurrentHp => _currentHp.Value;
    public bool IsDowned => _isDowned.Value;

    // Alien_main 프리팹 계층에서 자식 Renderer와 루트 NetworkAnimator를 가져온다.
    // 피격 색상 변경에 재사용할 MaterialPropertyBlock도 한 번만 생성한다.
    private void Awake()
    {
        _renderer = GetComponentInChildren<Renderer>();
        _networkAnimator = GetComponent<NetworkAnimator>();
        _materialPropertyBlock = new MaterialPropertyBlock();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _currentHp.Value = _maxHp;
        }

        _currentHp.OnValueChanged += HandleHpChanged;
    }

    public override void OnNetworkDespawn()
    {
        _currentHp.OnValueChanged -= HandleHpChanged;
    }

    // 외부(외계생체 제압기 등)에서 데미지를 입힐 때 호출하는 공개 진입점. 서버에서만 호출 가능하다.
    public void TakeDamage(float amount)
    {
        if (!IsServer)
        {
            Debug.LogError("[AlienCloneHealth] TakeDamage는 서버에서만 호출할 수 있습니다.");
            return;
        }

        if (_isDowned.Value || amount <= 0f) return;

        _currentHp.Value = Mathf.Max(0f, _currentHp.Value - amount);
        Debug.Log($"[AlienCloneHealth] {amount} 데미지 적용, 남은 HP: {_currentHp.Value}/{_maxHp}");

        if (_currentHp.Value <= 0f)
        {
            _isDowned.Value = true;
            // 공격·이동을 먼저 중단시킨 뒤 NetworkAnimator로 사망 애니메이션을 동기화한다.
            // 이 순서로 공격 HitBox가 남은 상태에서 사망 애니메이션이 시작되는 것을 방지한다.
            Died?.Invoke();
            _networkAnimator.SetTrigger(DeathHash);
        }
    }

    // NetworkVariable의 HP 감소를 각 인스턴스에서 감지해 피격 색상을 재생한다.
    // 연속 피격 시 기존 Coroutine을 취소하고 표시 시간을 처음부터 다시 계산한다.
    private void HandleHpChanged(float previousValue, float newValue)
    {
        HpChanged?.Invoke(previousValue, newValue);

        if (newValue >= previousValue)
        {
            return;
        }

        if (_damageFlashCoroutine != null)
        {
            StopCoroutine(_damageFlashCoroutine);
            _damageFlashCoroutine = null;
        }

        if (newValue <= 0f)
        {
            // HP가 0이면 피격 색상을 제거하고 사망 애니메이션 표시를 우선한다.
            ClearDamageFlash();
            return;
        }

        _damageFlashCoroutine = StartCoroutine(FlashDamage());
    }

    // Animation Event 진입점: Falling Back Death 클립의 마지막 프레임에서 문자열로 직접 호출된다.
    // C# 호출 참조가 없어 보여도 제거하거나 이름을 변경하면 안 된다.
    // 서버만 완료 이벤트를 발생시켜 실제 네트워크 디스폰을 요청한다.
    public void CompleteDeath()
    {
        if (!IsServer)
        {
            return;
        }

        DeathAnimationCompleted?.Invoke(this);
    }

    // 각 네트워크 인스턴스에서 Renderer의 _BaseColor를 빨간색으로 덮어쓴다.
    // Inspector의 _damageFlashDuration만큼 기다린 뒤 피격 표시를 제거한다.
    private IEnumerator FlashDamage()
    {
        _renderer.GetPropertyBlock(_materialPropertyBlock);
        _materialPropertyBlock.SetColor(BaseColorHash, Color.red);
        _renderer.SetPropertyBlock(_materialPropertyBlock);

        yield return new WaitForSeconds(_damageFlashDuration);

        ClearDamageFlash();
        _damageFlashCoroutine = null;
    }

    // Renderer의 MaterialPropertyBlock을 비워 원래 Material 표현으로 되돌린다.
    // 사망하거나 피격 표시 시간이 끝날 때 호출된다.
    private void ClearDamageFlash()
    {
        _materialPropertyBlock.Clear();
        _renderer.SetPropertyBlock(_materialPropertyBlock);
    }
}
