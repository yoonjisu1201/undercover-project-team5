using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

// 라운드 타이머가 일정 시간(검거 투표 등으로 멈춰있는
// 동안은 제외) 줄어들 때마다 여러 마리를 한 번에 스폰한다. 라운드가 시작되면 최대 마릿수를 즉시 채우고,
// 스폰 위치는 플레이어와 무관한 맵 임의 위치로 정한다. 외계인은 배회하다 플레이어를 감지하면 추격한다.
// 스폰한 개체의 사망 애니메이션 완료 이벤트를 구독해 애니메이션이 끝나면 실제로 디스폰시킨다.
public class AlienCloneManager : MonoBehaviour
{
    [Header("스폰 설정 (임시 기본값, 추후 밸런싱 이슈로 조정)")]
    [SerializeField] private GameObject[] _alienClonePrefabs;  //외계인 5종
    [SerializeField] private CriminalNpcManager _criminalNpcManager;
    [SerializeField] private MapRegionController _mapRegionController;
    [SerializeField, Min(1)] private int _spawnCountPerCycle = 3;
    [SerializeField, Min(0.1f)] private float _spawnInterval = 60f; // 라운드 타이머가 이만큼(초) 줄어들 때마다 스폰

    private readonly List<AlienCloneHealth> _aliveClones = new();
    private bool _spawnFailedThisCycle;

    // 살아있는(사망 애니메이션 중인 개체 포함) 분신 목록. 분신끼리 서로의 위치를 참고할 때 쓴다.
    public IReadOnlyList<AlienCloneHealth> AliveClones => _aliveClones;

    // 디버그 메뉴에서 주기적인 스폰을 끄거나, 범인과 함께 분신도 정지시킬 때 사용한다. 서버에서만 의미가 있다.
    public bool SpawningEnabled { get; private set; } = true;

    // 범인이 정체를 드러내 이번 라운드 동안만 스폰을 멈춘 상태.
    // 디버그 메뉴 설정(SpawningEnabled)과 분리해야, 디버그로 꺼둔 것은 라운드가 바뀌어도 유지된다.
    private bool _spawnStoppedByReveal;
    public bool ClonesFrozen { get; private set; }

    // 지난 프레임에 읽은 라운드 잔여시간. 이번 프레임과의 차이로 "실제로 흐른 라운드 시간"을 계산하는 기준값.
    private float _lastRoundRemainingTime;
    private float _elapsedSinceLastSpawn;

    private void Start()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }
    }

    private void OnDestroy()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }
    }

    // 서버에서만 매 프레임 마릿수/라운드 타이머 경과량을 확인해 필요하면 새로 스폰한다.
    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (RoundManager.Instance == null) return;

        _aliveClones.RemoveAll(clone => clone == null);

        // InRound를 벗어난 동안에는 스폰 주기를 굴리지 않는다. RoundClear 중에 스폰되면
        // 다음 라운드 전환에서는 디스폰하지 않아 그대로 넘어간다.
        if (RoundManager.Instance.CurrentState != RoundState.InRound) return;

        // Time.deltaTime 대신 라운드 잔여시간의 감소량을 쓴다: 검거 투표 등으로 RoundManager가
        // 타이머를 멈추면 GetRemainingTime()도 같이 안 줄어들어서 elapsed가 0이 되고, 스폰도 같이 멈춘다.
        // Mathf.Max(0f, ...)는 다음 라운드 시작 때 잔여시간이 갑자기 확 늘어나는 순간(음수 elapsed)을 막기 위함.
        float currentRemaining = RoundManager.Instance.GetRemainingTime();
        float elapsed = Mathf.Max(0f, _lastRoundRemainingTime - currentRemaining);
        _lastRoundRemainingTime = currentRemaining;

        // 스폰을 막아둔 동안에는 경과 시간도 쌓지 않는다. 다시 허용한 순간 한꺼번에 몰려 나오는 것을 막기 위함.
        if (!SpawningEnabled || _spawnStoppedByReveal || _spawnFailedThisCycle) return;

        _elapsedSinceLastSpawn += elapsed;
        if (_elapsedSinceLastSpawn < _spawnInterval) return;

        // _spawnCountPerCycle은 동시에 살아있을 수 있는 최대 마릿수다. 이미 꽉 찼으면 이번 주기는 건너뛴다.
        int missingCount = _spawnCountPerCycle - _aliveClones.Count;

        if (missingCount <= 0)
        {
            _elapsedSinceLastSpawn = 0f;
            return;
        }

        // 프리팹은 재시도 루프 밖에서 한 번만 찾는다. 설정이 잘못된 채로 루프에 들어가면
        // 매 프레임 같은 에러가 수십 줄씩 쌓이므로, 실패하면 이번 라운드 자동 스폰을 멈춘다.
        GameObject clonePrefab = GetRoundClonePrefab();
        if (clonePrefab == null)
        {
            _spawnFailedThisCycle = true;
            return;
        }

        // 부족한 만큼 전부 스폰될 때까지 재시도한다(무한 루프 방지용 시도 횟수 상한 포함).
        // 전부 채웠을 때만 타이머를 리셋하고, 못 채웠으면 다음 프레임에 이어서 재시도한다.
        int spawnedCount = 0;
        int attemptLimit = _spawnCountPerCycle * 10;

        for (int attempt = 0; spawnedCount < missingCount && attempt < attemptLimit; attempt++)
        {
            if (SpawnClone(clonePrefab))
            {
                spawnedCount++;
            }
        }

        if (spawnedCount == missingCount)
        {
            _elapsedSinceLastSpawn = 0f;
        }
    }

    // 이번 라운드에 뽑힌 종류의 분신 프리팹을 찾는다. 설정이 어긋나면 원인을 로그로 남기고 null을 반환한다.
    private GameObject GetRoundClonePrefab()
    {
        if (_criminalNpcManager == null)
        {
            Debug.LogError("[AlienCloneManager] CriminalNpcManager 참조가 없어 이번 라운드의 외계인 종류를 알 수 없습니다.", this);
            return null;
        }

        // -1은 아직 추첨 전, 범위 밖이면 CriminalNpcManager의 종류 개수와 이 배열 길이가 어긋난 상태다.
        int typeIndex = _criminalNpcManager.RoundAlienTypeIndex;
        if (typeIndex < 0 || typeIndex >= _alienClonePrefabs.Length)
        {
            Debug.LogError(
                $"[AlienCloneManager] 이번 라운드 외계인 종류({typeIndex})에 해당하는 분신 프리팹이 없습니다. " +
                $"등록된 프리팹 수: {_alienClonePrefabs.Length}",
                this);
            return null;
        }

        GameObject prefab = _alienClonePrefabs[typeIndex];
        if (prefab == null)
        {
            Debug.LogError($"[AlienCloneManager] {typeIndex}번 외계인 분신 프리팹 슬롯이 비어 있습니다.", this);
        }

        return prefab;
    }

    // 스폰 위치를 찾아 외계인 복제체를 네트워크 오브젝트로 스폰한다. 성공 여부를 반환한다.
    private bool SpawnClone(GameObject clonePrefab)
    {
        if (!TryGetSpawnPosition(out Vector3 spawnPosition)) return false;

        GameObject instance = null;
        try
        {
            instance = Instantiate(clonePrefab, spawnPosition, Quaternion.identity);

            if (!instance.TryGetComponent(out NetworkObject networkObject) ||
                !instance.TryGetComponent(out AlienCloneHealth health))
            {
                Debug.LogError("[AlienCloneManager] 외계인 프리팹에 NetworkObject 또는 AlienCloneHealth가 없습니다.", this);
                Destroy(instance);
                return false;
            }

            networkObject.Spawn(destroyWithScene: true);

            _aliveClones.Add(health);
            // 정지 상태에서 새로 스폰된 분신도 곧바로 멈춘 상태로 시작한다.
            if (ClonesFrozen && instance.TryGetComponent(out AlienCloneController spawnedController))
            {
                spawnedController.SetFrozen(true);
            }

            // AlienCloneHealth.CompleteDeath가 Animation Event를 받으면 Health 자신을 인자로 전달한다.
            // 람다 캡처 없이 완료 처리 메서드를 직접 구독하며, 처리 직후 이벤트 소스도 함께 디스폰된다.
            health.DeathAnimationCompleted += HandleCloneDeathAnimationCompleted;
            return true;
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[AlienCloneManager] 외계인 스폰 중 예외가 발생해 자동 스폰을 중단합니다.\n{exception}", this);
            _spawnFailedThisCycle = true;
            if (instance != null)
            {
                Destroy(instance);
            }

            return false;
        }
    }

    // 맵 안의 임의 NavMesh 지점을 찾는다. 플레이어 근처에 생성하지 않고 맵에 흩어 놓아,
    // 배회하다 플레이어를 감지했을 때 마주치는 흐름을 만든다.
    private bool TryGetSpawnPosition(out Vector3 spawnPosition)
    {
        if (_mapRegionController != null && _mapRegionController.TryGetRandomSpawnPoint(out _, out spawnPosition))
        {
            return true;
        }

        spawnPosition = default;
        return false;
    }

    // AlienCloneHealth.CompleteDeath에서 발생한 완료 이벤트를 처리한다.
    // 관리 목록에서 먼저 제거한 뒤 해당 Health의 NetworkObject를 서버에서 디스폰한다.
    private void HandleCloneDeathAnimationCompleted(AlienCloneHealth health)
    {
        _aliveClones.Remove(health);

        NetworkObject networkObject = health.NetworkObject;

        if (networkObject != null && networkObject.IsSpawned)
        {
            networkObject.Despawn(destroy: true);
        }
    }

    // 주기적인 분신 스폰을 켜거나 끈다.
    public void SetSpawningEnabled(bool enabled)
    {
        SpawningEnabled = enabled;
    }

    // 범인이 정체를 드러내면 이번 라운드 동안 분신을 더 내보내지 않는다.
    public void StopSpawningForRevealedCriminal()
    {
        _spawnStoppedByReveal = true;
    }

    // 살아있는 분신 전체의 이동을 멈추거나 다시 풀어준다. 이후 새로 스폰되는 분신도 같은 상태를 따른다.
    public void SetClonesFrozen(bool frozen)
    {
        ClonesFrozen = frozen;

        foreach (AlienCloneHealth health in _aliveClones)
        {
            if (health != null && health.TryGetComponent(out AlienCloneController controller))
            {
                controller.SetFrozen(frozen);
            }
        }
    }

    // 살아있는 분신을 모두 디스폰하고 제거한 마릿수를 반환한다.
    public int DespawnAllClones()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return 0;

        int removedCount = 0;
        foreach (AlienCloneHealth health in _aliveClones)
        {
            if (health != null && health.TryGetComponent(out NetworkObject networkObject) && networkObject.IsSpawned)
            {
                networkObject.Despawn(destroy: true);
                removedCount++;
            }
        }

        _aliveClones.Clear();
        return removedCount;
    }

    // 라운드가 끝나면(InRound를 벗어나면) 지금까지 스폰된 외계인 복제체를 전부 강제로 디스폰시킨다.
    private void HandleRoundStateChanged(RoundState state)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        // 새 라운드는 외계인 스폰 주기를 0부터 다시 센다.
        if (state == RoundState.InRound)
        {
            _elapsedSinceLastSpawn = 0f;
            _spawnFailedThisCycle = false;
            _spawnStoppedByReveal = false; // 범인 노출로 멈춘 것만 푼다. 디버그로 꺼둔 설정은 그대로 유지한다.
            // 기준값도 새 라운드의 남은 시간으로 맞춘다. 라운드마다 지속시간이 달라서(900→750→600)
            // 이전 라운드 잔여시간을 그대로 두면 그 차이가 "흐른 시간"으로 잡혀 시작 즉시 스폰된다.
            _lastRoundRemainingTime = RoundManager.Instance.GetRemainingTime();
            FillToMaxNextFrameAsync(this.GetCancellationTokenOnDestroy()).Forget();
            return;
        }

        DespawnAllClones();
        ClonesFrozen = false;
    }

    // 라운드 시작 시 최대 마릿수를 즉시 채운다. 직접 스폰하지 않고 스폰 주기를 다 찬 상태로 만들어
    // Update의 기존 경로(프리팹 확인 / 마릿수 상한 / 실패 처리)를 그대로 태운다.
    //
    // 한 프레임 기다리는 이유: 라운드 시작 이벤트는 CriminalNpcManager도 구독하고, 이번 라운드
    // 외계인 종류는 거기서 정해진다. 구독 순서가 보장되지 않아 같은 프레임에 스폰하면
    // 지난 라운드 종류로 스폰될 수 있다.
    private async UniTaskVoid FillToMaxNextFrameAsync(CancellationToken cancellationToken)
    {
        await UniTask.NextFrame(cancellationToken);
        _elapsedSinceLastSpawn = _spawnInterval;
    }
}
