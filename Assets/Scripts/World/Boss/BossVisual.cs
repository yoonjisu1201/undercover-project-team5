using Unity.Netcode;
using UnityEngine;

// 라운드마다 보스의 겉모습을 바꾼다. 능력치와 행동은 그대로다.
//
// 종류는 서버가 정해서 NetworkVariable로 알리고, 실제 모델은 각 클라이언트가 로컬에서 만든다.
// 모델은 보이는 것뿐이라 네트워크 오브젝트로 만들 이유가 없다.
//
// 외형마다 리그(Avatar)가 달라서 보스 루트의 Animator로는 1번 외형만 돌릴 수 있다. 그래서
// 각 외형 프리팹이 자기 Animator와 Avatar를 들고 있고, 애니메이션은 그 Animator에 넣는다.
// 어떤 Animator를 써야 하는지는 ActiveAnimator로 알려준다.
//
// 등록된 외형이 없으면 프리팹에 들어 있는 기본 모델을 그대로 쓴다.
public class BossVisual : NetworkBehaviour
{
    [Tooltip("프리팹에 들어 있는 기본 모델. 라운드 외형이 정해지면 이쪽을 끈다.")]
    [SerializeField] private GameObject _defaultModel;

    [Tooltip("라운드마다 골라 쓸 외형. 각 프리팹은 자기 Animator와 Avatar를 들고 있어야 한다.")]
    [SerializeField] private GameObject[] _modelPrefabs = new GameObject[0];

    private readonly NetworkVariable<int> _modelIndex = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private GameObject _spawnedModel;
    private Animator _activeAnimator;

    public int ModelCount => _modelPrefabs.Length;

    // 지금 화면에 보이는 모델의 Animator. 걷기/달리기/공격을 여기에 넣어야 한다.
    public Animator ActiveAnimator => _activeAnimator;

    private void Awake()
    {
        // 외형이 정해지기 전에도 애니메이션이 돌아야 하므로 기본 모델 기준으로 먼저 잡아둔다.
        _activeAnimator = ResolveDefaultAnimator();
    }

    public override void OnNetworkSpawn()
    {
        _modelIndex.OnValueChanged += HandleModelIndexChanged;

        // 스폰 메시지에 값이 실려 왔으면 콜백이 오지 않으므로 여기서 한 번 적용한다.
        ApplyModel(_modelIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        _modelIndex.OnValueChanged -= HandleModelIndexChanged;
    }

    // BossSpawner가 스폰 직후 이번 라운드 외형을 지정한다.
    public void SetModelIndexOnServer(int index)
    {
        if (!IsServer)
        {
            return;
        }

        _modelIndex.Value = index;
    }

    private void HandleModelIndexChanged(int previousIndex, int newIndex)
    {
        ApplyModel(newIndex);
    }

    private void ApplyModel(int index)
    {
        if (index < 0 || index >= _modelPrefabs.Length)
        {
            return;
        }

        GameObject prefab = _modelPrefabs[index];
        if (prefab == null)
        {
            Debug.LogWarning($"[보스 외형] {index}번 외형 슬롯이 비어 있어 기본 모델을 유지합니다.", this);
            return;
        }

        if (_spawnedModel != null)
        {
            Destroy(_spawnedModel);
        }

        // 크기·오프셋은 각 외형 프리팹에 저장돼 있으므로 프리팹의 로컬 트랜스폼을 그대로 살린다.
        _spawnedModel = Instantiate(prefab, transform, false);

        Animator animator = _spawnedModel.GetComponent<Animator>();
        if (animator == null)
        {
            animator = _spawnedModel.GetComponentInChildren<Animator>(true);
        }

        if (animator == null)
        {
            Debug.LogError(
                $"[보스 외형] '{prefab.name}'에 Animator가 없어 움직임이 재생되지 않습니다.", this);
        }
        else
        {
            _activeAnimator = animator;

            // 애니메이션 이벤트는 Animator가 붙은 오브젝트의 컴포넌트만 호출한다.
            // 루트의 BossAttack까지 넘겨줄 중계를 그 오브젝트에 붙인다.
            if (animator.GetComponent<BossAnimationEventRelay>() == null)
            {
                animator.gameObject.AddComponent<BossAnimationEventRelay>();
            }
        }

        if (_defaultModel != null)
        {
            _defaultModel.SetActive(false);
        }

        // 루트 Animator는 기본 모델 리그 전용이다. 외형이 바뀐 뒤에는 돌릴 대상이 없으니 끈다.
        if (TryGetComponent(out Animator rootAnimator) && rootAnimator != _activeAnimator)
        {
            rootAnimator.enabled = false;
        }
    }

    // 순간이동으로 잠깐 사라져 있는 동안 쓴다. 오브젝트를 통째로 끄면 Animator 상태와
    // 애니메이션 이벤트가 끊기므로, 보이는 것만 끄고 나머지는 그대로 돌아가게 둔다.
    public void SetVisible(bool visible)
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible;
        }
    }

    // 기본 모델을 쓸 때 걷기/달리기가 들어가는 곳은 보스 루트의 Animator다.
    // 모델 자식에도 Animator가 있지만 그건 다른 컨트롤러(Character_0N_Controller)라 쓰지 않는다.
    private Animator ResolveDefaultAnimator() => GetComponent<Animator>();
}
