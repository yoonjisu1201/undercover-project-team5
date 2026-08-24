using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CriminalNpcManager : NetworkBehaviour
{
    // CriminalFeature 동기화 값이 실제로 반영된 순간 서버/클라이언트 양쪽에서 발생한다.
    // ClueSpawner(서버)는 필드 단서 트림에, HQ UI(클라이언트)는 단서 진행률 표시에 쓴다.
    public event System.Action OnCriminalAssigned;

    [Header("외계인 종류")]
    [SerializeField, Min(1)] private int _alienTypeCount = 5;

    private readonly NetworkVariable<NetworkObjectReference> _criminalNpcReference = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<NpcFeature> _criminalFeature = new(
        new NpcFeature(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // 이번 라운드에 등장할 외계인 종류. 범인 본모습과 분신이 같은 종류로 나오도록 양쪽이 이 값을 참조한다.
    // -1은 아직 추첨 전이라는 뜻이며, 클라이언트는 스폰 시 서버 값을 그대로 받는다.
    private readonly NetworkVariable<int> _roundAlienTypeIndex = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkObject CriminalNpc { get; private set; }
    public NpcFeature CriminalFeature => _criminalFeature.Value;
    public int RoundAlienTypeIndex => _roundAlienTypeIndex.Value;

    public override void OnNetworkSpawn()
    {
        _criminalNpcReference.OnValueChanged += HandleCriminalNpcChanged;
        _criminalFeature.OnValueChanged += HandleCriminalFeatureChanged;
        ResolveCriminalNpc(_criminalNpcReference.Value);

        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        _criminalNpcReference.OnValueChanged -= HandleCriminalNpcChanged;
        _criminalFeature.OnValueChanged -= HandleCriminalFeatureChanged;

        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }
    }

    // 1라운드가 아닌 라운드가 시작되면 이전 라운드에서 잡힌 범인은 제외하고 새 범인을 다시 지정한다.
    private void HandleRoundStateChanged(RoundState state)
    {
        if (!IsServer) return;
        if (state != RoundState.InRound || RoundManager.Instance.CurrentRoundIndex == 0) return;

        NetworkObject previousCriminal = CriminalNpc;

        AssignRandomCriminal(previousCriminal);

        if (previousCriminal != null && previousCriminal.IsSpawned)
        {
            // 이전 범인 NPC를 제거한다. (새 라운드에서는 새로운 범인을 지정하므로 이전 범인은 제거)
            previousCriminal.Despawn(destroy: true);  
        }
    }

    private void OnEnable()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SceneManager == null)
        {
            Debug.LogError("[CriminalNpcManager] NetworkManager 또는 NetworkSceneManager가 존재하지 않습니다.", this);
            return;
        }

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton?.SceneManager == null) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!NetworkManager.Singleton.IsServer || sceneName != gameObject.scene.name)
        {
            return;
        }

        // 씬 로드가 완료되면 범인 NPC를 지정합니다.
        AssignCriminalAfterSpawnAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    // 범인 NPC를 지정하는 비동기 메서드
    private async UniTaskVoid AssignCriminalAfterSpawnAsync(CancellationToken cancellationToken)
    {
        await UniTask.NextFrame(cancellationToken); // 다음 프레임까지 대기하여 모든 NPC가 스폰될 시간을 확보합니다.

        AssignRandomCriminal(null);
    }

    // 범인 NPC를 랜덤으로 지정한다. exclude가 지정되면 해당 NPC는 후보에서 제외한다(Round2 재지정 시 이전 범인 제외용).
    private void AssignRandomCriminal(NetworkObject exclude)
    {
        SelectRoundAlienType();

        NpcStateMachine[] npcs = FindObjectsByType<NpcStateMachine>(FindObjectsSortMode.None);  // 씬에 존재하는 모든 NpcStateMachine을 찾습니다.

        if (npcs.Length == 0)
        {
            Debug.LogError("[CriminalNpcManager] 범인으로 지정할 NPC가 없습니다.", this);
            return;
        }

        // NPC가 항상 exclude 한 마리보다 훨씬 많으므로, 뽑았다가 exclude면 다시 뽑는 방식으로 처리한다.
        NpcStateMachine selectedNpc;
        do
        {
            int randomIndex = Random.Range(0, npcs.Length); // 랜덤 인덱스를 생성하여 범인 NPC를 선택합니다.
            selectedNpc = npcs[randomIndex];
        } while (exclude != null && selectedNpc.TryGetComponent(out NetworkObject excludedCheck) && excludedCheck == exclude);

        if (!selectedNpc.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError("[CriminalNpcManager] 선택된 NPC에 NetworkObject가 없습니다.", selectedNpc);
            return;
        }

        if (!networkObject.IsSpawned)
        {
            Debug.LogError("[CriminalNpcManager] 선택된 NPC의 NetworkObject가 아직 스폰되지 않았습니다.", selectedNpc);
            return;
        }

        CriminalNpc = networkObject;
        CriminalNpc.name = "Criminal_main";
        _criminalNpcReference.Value = networkObject;

        if (CriminalNpc.TryGetComponent(out NpcFeatureController featureController))
        {
            featureController.SendFeatureTo(this);
        }
        else
        {
            Debug.LogError("[CriminalNpcManager] 범인 NPC에 NpcFeatureController가 없습니다.", CriminalNpc);
        }

        Debug.Log(
            $"[CriminalNpcManager] 범인 지정 완료 | " +
            $"Name: {CriminalNpc.name}, " +
            $"NetworkObjectId: {CriminalNpc.NetworkObjectId}, " +
            $"Position: {CriminalNpc.transform.position}, " +
            $"외계인 종류: {_roundAlienTypeIndex.Value}",
            CriminalNpc);
    }

    // 이번 라운드의 외계인 종류를 뽑는다. 직전 라운드와 같은 종류가 연달아 나오지 않도록 이전 값은 후보에서 뺀다.
    private void SelectRoundAlienType()
    {
        int previousIndex = _roundAlienTypeIndex.Value;

        // 첫 추첨이거나 종류가 하나뿐이면 제외할 대상이 없다.
        if (previousIndex < 0 || _alienTypeCount <= 1)
        {
            _roundAlienTypeIndex.Value = Random.Range(0, _alienTypeCount);
            return;
        }

        // 이전 인덱스 하나를 뺀 범위에서 뽑은 뒤, 이전 인덱스 이상이면 한 칸 밀어 원래 범위로 되돌린다.
        // 재추첨 루프 없이 한 번에 균등하게 뽑기 위함.
        int selectedIndex = Random.Range(0, _alienTypeCount - 1);
        if (selectedIndex >= previousIndex)
        {
            selectedIndex++;
        }

        _roundAlienTypeIndex.Value = selectedIndex;
    }

    public bool IsCriminal(NetworkObject npc)   // 범인 NPC인지 확인하는 메서드
    {
        return npc != null && npc == CriminalNpc;
    }

    public void SetCriminalFeature(NpcFeature feature)
    {
        if (!IsServer)
        {
            return;
        }

        _criminalFeature.Value = feature;
    }

    private void HandleCriminalNpcChanged(NetworkObjectReference previous, NetworkObjectReference current)
    {
        ResolveCriminalNpc(current);
    }

    private void HandleCriminalFeatureChanged(NpcFeature previous, NpcFeature current)
    {
        OnCriminalAssigned?.Invoke();
    }

    private void ResolveCriminalNpc(NetworkObjectReference reference)
    {
        if (reference.TryGet(out NetworkObject criminalNpc))
        {
            CriminalNpc = criminalNpc;
        }
    }
}
