using System;
// ===== [검토표시-추가-시작] =====
using System.Collections;
// ===== [검토표시-추가-끝] =====
using Unity.Netcode;
// ===== [검토표시-추가-시작] =====
using Unity.Netcode.Components;
// ===== [검토표시-추가-끝] =====
using UnityEngine;

// ===== [검토표시-수정-시작] =====
// 외계인 복제체의 체력과 피격 표시를 관리한다. 체력이 0이 되면 사망 애니메이션을 실행하고,
// 애니메이션이 완료되면 AlienCloneManager가 실제 디스폰을 처리한다.
// ===== [검토표시-수정-끝] =====
public class AlienCloneHealth : NetworkBehaviour, IDamageable
{
    // ===== [검토표시-추가-시작] =====
    private static readonly int DeathHash = Animator.StringToHash("Death");
    private static readonly int BaseColorHash = Shader.PropertyToID("_BaseColor");
    // ===== [검토표시-추가-끝] =====

    [Header("HP 설정 (임시 기본값, 추후 밸런싱 이슈로 조정)")]
    [SerializeField] private float _maxHp = 50f;
    // ===== [검토표시-추가-시작] =====
    [SerializeField, Min(0f)] private float _damageFlashDuration = 0.15f;
    // ===== [검토표시-추가-끝] =====

    private readonly NetworkVariable<float> _currentHp =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isDowned =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ===== [검토표시-추가-시작] =====
    private Renderer _renderer;
    private NetworkAnimator _networkAnimator;
    private MaterialPropertyBlock _materialPropertyBlock;
    private Coroutine _damageFlashCoroutine;
    // ===== [검토표시-추가-끝] =====

    // 외부에서 변경 감지 구독
    public event Action<float, float> HpChanged;
    public event Action Died;
    // ===== [검토표시-추가-시작] =====
    public event Action DeathAnimationCompleted;
    // ===== [검토표시-추가-끝] =====

    public float MaxHp => _maxHp;
    public float CurrentHp => _currentHp.Value;
    public bool IsDowned => _isDowned.Value;

    // ===== [검토표시-추가-시작] =====
    private void Awake()
    {
        _renderer = GetComponentInChildren<Renderer>();
        _networkAnimator = GetComponent<NetworkAnimator>();
        _materialPropertyBlock = new MaterialPropertyBlock();
    }
    // ===== [검토표시-추가-끝] =====

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
            Died?.Invoke();
            // ===== [검토표시-추가-시작] =====
            _networkAnimator.SetTrigger(DeathHash);
            // ===== [검토표시-추가-끝] =====
        }
    }

    private void HandleHpChanged(float previousValue, float newValue)
    {
        HpChanged?.Invoke(previousValue, newValue);

        // ===== [검토표시-추가-시작] =====
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
            ClearDamageFlash();
            return;
        }

        _damageFlashCoroutine = StartCoroutine(FlashDamage());
        // ===== [검토표시-추가-끝] =====
    }

    // ===== [검토표시-추가-시작] =====
    public void CompleteDeath()
    {
        if (!IsServer)
        {
            return;
        }

        DeathAnimationCompleted?.Invoke();
    }

    private IEnumerator FlashDamage()
    {
        _renderer.GetPropertyBlock(_materialPropertyBlock);
        _materialPropertyBlock.SetColor(BaseColorHash, Color.red);
        _renderer.SetPropertyBlock(_materialPropertyBlock);

        yield return new WaitForSeconds(_damageFlashDuration);

        ClearDamageFlash();
        _damageFlashCoroutine = null;
    }

    private void ClearDamageFlash()
    {
        _materialPropertyBlock.Clear();
        _renderer.SetPropertyBlock(_materialPropertyBlock);
    }
    // ===== [검토표시-추가-끝] =====
}
