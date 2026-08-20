using Unity.Netcode;
using UnityEngine;

// 범인으로 판정된 NPC의 위장을 해제해 시민 외형을 감추고 외계인 본모습을 드러낸다.
// 본모습은 순수 시각 요소라 네트워크 오브젝트로 만들지 않는다. 동기화된 종류 인덱스를 보고
// 각 클라이언트가 로컬에서 생성하며, 이렇게 해야 NPC마다 5종을 미리 붙여두지 않아도 된다.
public class CriminalAlienReveal : NetworkBehaviour
{
    [SerializeField] private GameObject _humanForm;  // 평소 보이는 시민 본체(모델+아웃핏 전체를 담은 루트)
    // 배열 순서는 GroundAlienCloneSpawner/UndergroundAlienCloneSpawner의 분신 프리팹 배열과 반드시 같아야 범인과 분신이 같은 종류로 나온다.
    [SerializeField] private GameObject[] _alienModelPrefabs;

    // 촬영 때 잠시 감추려면 생성해둔 본모습 인스턴스를 들고 있어야 한다.
    private GameObject _alienForm;

    // 촬영 때 시민 외형의 포즈를 되돌리는 데 쓴다.
    private Animator _humanAnimator;

    // 본모습은 드러난 뒤 계속 달리기만 하므로 파라미터를 한 번만 켠다.
    // 이름은 분신이 쓰는 AlienAnimator 컨트롤러와 같아야 한다.
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");

    private Animator _alienAnimator;

    private readonly NetworkVariable<bool> _isRevealed = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        _isRevealed.OnValueChanged += HandleRevealedChanged;
        ApplyRevealState(_isRevealed.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isRevealed.OnValueChanged -= HandleRevealedChanged;
    }

    // 검거 판정에서 진짜 범인으로 확정됐을 때 서버가 호출한다.
    public void Reveal()
    {
        if (!IsServer) return;
        _isRevealed.Value = true;
    }

    private void HandleRevealedChanged(bool previous, bool current)
    {
        ApplyRevealState(current);
    }

    private void ApplyRevealState(bool revealed)
    {
        // 위장 해제는 되돌아가지 않으므로, 아직 해제 전이면 손댈 것이 없다.
        if (!revealed) return;

        // 생성에 실패하면 시민 외형을 그대로 둔다.
        _alienForm = CreateAlienForm();
        if (_alienForm == null) return;

        _alienAnimator = _alienForm.GetComponent<Animator>();
        _alienAnimator.SetBool(IsRunningHash, true);

        if (_humanForm != null)
        {
            _humanAnimator = _humanForm.GetComponentInChildren<Animator>(true);
            SetRenderersEnabled(_humanForm, false);
        }
    }

    // 결과 패널 촬영처럼 한 프레임만 위장한 모습이 필요할 때 쓴다.
    // 메인 카메라가 그리기 전에 EndHumanFormCapture로 반드시 되돌려야 한다.
    public void BeginHumanFormCapture()
    {
        if (!_isRevealed.Value || _alienForm == null || _humanForm == null) return;

        SetRenderersEnabled(_alienForm, false);
        SetRenderersEnabled(_humanForm, true);

        // 위장이 풀린 뒤에도 시민 Animator는 NPC 상태 머신이 계속 몰고 있어, 추격 중이면 달리는 포즈로 찍힌다.
        // 이 프레임만 기본 Idle 첫 프레임을 직접 재생해 서 있는 모습으로 남긴다.
        // 파라미터는 건드리지 않으므로 다음 프레임에 컨트롤러가 알아서 원래 상태로 돌아간다.
        if (_humanAnimator != null)
        {
            _humanAnimator.Play(AnimatorHashes.IdleState, 0, 0f);
            _humanAnimator.Update(0f); // 여기까지 해야 이번 프레임 본 포즈에 반영된다
        }
    }

    public void EndHumanFormCapture()
    {
        if (!_isRevealed.Value || _alienForm == null || _humanForm == null) return;

        SetRenderersEnabled(_humanForm, false);
        SetRenderersEnabled(_alienForm, true);
    }

    // 시민 외형 루트에는 Animator와 NpcAnimationEvents가 함께 붙어 있어, 오브젝트를 통째로 끄면
    // 애니메이션 이벤트가 끊겨 NPC 상태 머신이 Phone 종료를 영영 기다린다. 그래서 보이는 것만 끈다.
    private static void SetRenderersEnabled(GameObject form, bool enabled)
    {
        foreach (Renderer renderer in form.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = enabled;
        }
    }

    // 이번 라운드에 뽑힌 종류의 본모습 모델을 로컬에서 만들어 NPC 아래에 붙인다.
    // 설정이 어긋나면 원인을 로그로 남기고 null을 반환한다.
    private GameObject CreateAlienForm()
    {
        // 이 스크립트는 NPC 프리팹에 붙어 있어 씬 오브젝트를 인스펙터로 참조할 수 없다.
        // 게임당 검거 판정 1회에만 호출되므로 탐색 비용은 문제되지 않는다.
        CriminalNpcManager criminalNpcManager = FindFirstObjectByType<CriminalNpcManager>();
        if (criminalNpcManager == null)
        {
            Debug.LogError("[CriminalAlienReveal] CriminalNpcManager를 찾지 못해 외계인 종류를 알 수 없습니다.", this);
            return null;
        }

        int typeIndex = criminalNpcManager.RoundAlienTypeIndex;
        if (typeIndex < 0 || typeIndex >= _alienModelPrefabs.Length)
        {
            Debug.LogError(
                $"[CriminalAlienReveal] 이번 라운드 외계인 종류({typeIndex})에 해당하는 모델이 없습니다. " +
                $"등록된 모델 수: {_alienModelPrefabs.Length}",
                this);
            return null;
        }

        GameObject prefab = _alienModelPrefabs[typeIndex];
        if (prefab == null)
        {
            Debug.LogError($"[CriminalAlienReveal] {typeIndex}번 외계인 모델 슬롯이 비어 있습니다.", this);
            return null;
        }

        // 크기·오프셋·레이어는 각 모델 프리팹에 저장돼 있으므로 프리팹의 로컬 트랜스폼을 그대로 살린다.
        return Instantiate(prefab, transform, false);
    }
}
