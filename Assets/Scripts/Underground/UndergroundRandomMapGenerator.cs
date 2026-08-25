using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using Random = System.Random;

// 시드 하나로 StartPoint부터 DoorSocket을 이어가며 지하 맵을 절차적으로 생성한다.
// 같은 시드를 넣으면 항상 같은 배치가 나온다. 전역 UnityEngine.Random 대신 이 인스턴스만의
// System.Random을 쓰기 때문에, 다른 코드의 랜덤 호출과 절대 섞이지 않는다.
[RequireComponent(typeof(NavMeshSurface), typeof(NetworkObject))]
public class UndergroundRandomMapGenerator : NetworkBehaviour
{
    // RoundManager.GetRandomSeed(tag)에 넘기는 태그. 다른 시스템의 태그와 겹치지만 않으면 된다.
    private const int MapSeedTag = 100;

    // 목표 개수에 못 미치면 이 횟수까지 시드를 바꿔가며 다시 시도한다.
    private const int MaxGenerationAttempts = 5;

    // 프리팹이 아니라 이 생성기의 자식으로 미리 배치해둔 실제 StartPoint 인스턴스. 매번 새로 만들지 않고 그대로 등록해서 쓴다.
    [SerializeField] private UndergroundModule _startModule;
    [SerializeField] private UndergroundModule[] _modulePrefabs;
    [SerializeField, Min(1)] private int _targetModuleCount = 20;
    [SerializeField, Min(1)] private int _maxDepth = 10;
    [SerializeField, Range(0f, 1f)] private float _doorUseChance = 0.7f;
    [SerializeField] private int _debugSeed;

    // 호스트/클라이언트가 같은 맵을 생성하도록 서버가 뽑아 동기화하는 시드.
    private readonly NetworkVariable<int> _mapSeed =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 실제로 문으로 쓰인 소켓들의 문을 생성 순서대로 담아, 인덱스로 _doorOpenStates와 맞춘다.
    // 생성 자체는 모든 클라이언트가 같은 시드로 각자 독립적으로 돌리기 때문에, 이 순서도 항상 동일하다.
    private readonly List<UndergroundDoor> _doors = new();
    // 문 열림/닫힘 상태. 서버만 쓰고 클라이언트는 OnListChanged로 읽기만 한다.
    private readonly NetworkList<bool> _doorOpenStates = new();

    // 문 여는 소리가 들리는 거리(m). 보스 감지용이라 연출 사운드와는 별개다.
    [SerializeField, Min(0f)] private float _doorNoiseRadius = 35f;

    private NavMeshSurface _navMeshSurface;
    private Random _random;
    
    private readonly List<UndergroundModule> _placedModules = new();
    // 열린 상태로 다음 방 붙일 문들
    private readonly Queue<DoorSocket> _openSockets = new();
    // 일단 안 열릴 계획인 문들. 큐가 말랐는데 아직 목표에 못 미치면 여기서 꺼내 다시 시도한다.
    private readonly List<DoorSocket> _reserveSockets = new();

    // 생성(재생성 포함)이 끝날 때마다 알림. 지하 스폰 영역을 이 결과에 맞춰 갱신하는 쪽에서 구독한다.
    public event Action OnGenerated;

    // 생성이 끝난 뒤 배치된 조각 목록. 미니맵처럼 배치 결과를 그대로 다시 그려야 하는 쪽에서 참조한다.
    public IReadOnlyList<UndergroundModule> PlacedModules => _placedModules;

    // 플레이어가 지하로 들어오는 입구 조각. 입구에서 얼마나 떨어졌는지를 따져야 하는 쪽에서 참조한다.
    public UndergroundModule StartModule => _startModule;

    private void Awake() {
        _navMeshSurface = GetComponent<NavMeshSurface>();
    }

    // 서버는 시드를 직접 뽑아 동기화하고 바로 생성한다. 클라이언트는 지금 값으로 바로 한 번 생성하고,
    // 나중에 값이 또 바뀌는 경우(다음 라운드 등)에 대비해 구독도 해둔다. 레이트 조인 클라이언트는
    // OnNetworkSpawn 시점에 이미 동기화된 _mapSeed.Value를 그대로 읽으니 별도 처리가 필요 없다.
    public override void OnNetworkSpawn()
    {
        _mapSeed.OnValueChanged += HandleMapSeedChanged;
        _doorOpenStates.OnListChanged += HandleDoorStateChanged;

        if (IsServer) {
            // 라운드매니저 있으면 랜덤값 사용, 없으면 DebugSeed 사용
            _mapSeed.Value = RoundManager.Instance == null ? _debugSeed : RoundManager.Instance.GetRandomSeed(MapSeedTag);
            Generate(_mapSeed.Value);
            return;
        }

        Generate(_mapSeed.Value);

        // 레이트 조인 클라이언트는 생성 시점에 이미 서버가 채워둔 _doorOpenStates를 그대로 들고 있으니,
        // OnListChanged로 이후 변경분만 받는 대신 지금 값들을 한 번 직접 적용해줘야 한다.
        for (int i = 0; i < _doorOpenStates.Count && i < _doors.Count; i++)
        {
            if (_doorOpenStates[i])
            {
                _doors[i].Open();
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        _mapSeed.OnValueChanged -= HandleMapSeedChanged;
        _doorOpenStates.OnListChanged -= HandleDoorStateChanged;
    }

    // 서버가 문을 열 때마다 모든 클라이언트가 여기서 반영한다. 문은 한 번 열리면 다시 닫히지 않는다.
    private void HandleDoorStateChanged(NetworkListEvent<bool> change)
    {
        if (!change.Value || change.Index < 0 || change.Index >= _doors.Count)
        {
            return;
        }

        UndergroundDoor door = _doors[change.Index];
        door.Open();

        // 문소리는 모든 클라이언트가 문 위치에서 듣는다. 이 콜백 자체가 NetworkList 변경으로
        // 각자에게 도달하므로 소리 때문에 RPC 를 따로 보낼 필요가 없다.
        //
        // 뒤늦게 들어온 클라이언트가 이미 열려 있던 문들을 한꺼번에 반영하는 경로는
        // OnNetworkSpawn 에서 Open() 을 직접 부르므로 여기를 타지 않는다. 접속하자마자
        // 열린 문 개수만큼 문소리가 몰아서 나는 일은 없다.
        SoundManager.Instance?.PlayAt(SoundKey.Basement_Door_Open, door.transform.position);
    }

    // 문과 상호작용한 클라이언트가 이 문을 열어달라고 요청할 때 부른다. 실제 상태 변경은 서버만 할 수 있다.
    // 기본값(소유자만 호출 가능)으로는 막히므로 Everyone으로 열어둔다.
    [Rpc(SendTo.Server)]
    public void OpenDoorRpc(int doorIndex, RpcParams rpcParams = default)
    {
        if (doorIndex < 0 || doorIndex >= _doorOpenStates.Count)
        {
            return;
        }

        _doorOpenStates[doorIndex] = true;

        // 문 여는 소리는 보스를 부르는 가장 큰 소음원이다. 지하에서는 문을 반드시 지나야 하므로
        // 회피할 수 없는 긴장이 된다.
        if (doorIndex < _doors.Count && _doors[doorIndex] != null)
        {
            // 문을 연 사람의 음소거 보정을 그대로 적용한다. 소음 종류마다 보정이 빠지면
            // 음소거로 조용해지는 구멍이 생긴다.
            float multiplier = GetOpenerNoiseMultiplier(rpcParams.Receive.SenderClientId);
            NoiseSystem.Report(
                _doors[doorIndex].transform.position,
                _doorNoiseRadius * multiplier,
                multiplier > 1f ? "문 열림(음소거)" : "문 열림");
        }
    }

    // 문을 연 클라이언트의 소음 배율. 찾지 못하면 보정 없이 1을 돌려준다.
    private float GetOpenerNoiseMultiplier(ulong senderClientId)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client))
        {
            return 1f;
        }

        NetworkObject playerObject = client.PlayerObject;
        return playerObject != null && playerObject.TryGetComponent(out PlayerNoiseEmitter emitter)
            ? emitter.NoiseMultiplier
            : 1f;
    }

    // 서버는 OnNetworkSpawn에서 이미 직접 생성했으니 중복 실행하지 않는다.
    private void HandleMapSeedChanged(int previousSeed, int currentSeed)
    {
        if (IsServer) {
            return;
        }

        Generate(currentSeed);
    }

    // 인스펙터에서 우클릭 → 이 메뉴로 _debugSeed를 넣어 바로 테스트해볼 수 있다.
    [ContextMenu("Generate Random Map")]
    private void GenerateFromInspector()
    {
        Generate(_debugSeed);
    }

    // 라운드가 바뀔 때 RoundManager가 명시적으로 호출한다. 새 시드를 뽑아 동기화하고 서버에서 바로 생성하면,
    // 클라이언트는 _mapSeed.OnValueChanged(HandleMapSeedChanged)로 따라와 각자 같은 맵을 만든다.
    // 라운드가 바뀌면 이전 라운드 지하에서 난 소음이 새 맵으로 넘어오지 않게 비운다.
    public void RegenerateForNewRound()
    {
        if (!IsServer)
        {
            return;
        }

        NoiseSystem.Clear();
        _mapSeed.Value = RoundManager.Instance == null ? _debugSeed : RoundManager.Instance.GetRandomSeed(MapSeedTag);
        Generate(_mapSeed.Value);
    }

    // 이전 생성 결과를 지우고, seed로 이 인스턴스 전용 Random을 새로 만든 뒤 생성한다.
    // 호스트/클라이언트가 같은 seed로 이 함수를 부르면 항상 같은 맵이 나온다.
    // 목표 개수에 못 미치면, 서버가 새 시드를 다시 뽑아 동기화하는 대신 지금 시드에서
    // NextSeed로 다음 시도용 시드를 결정적으로 유도해 재시도한다. 호스트/클라이언트가
    // 이 유도 규칙을 각자 똑같이 따르므로 재동기화 없이도 항상 같은 맵으로 수렴한다.
    public void Generate(int seed)
    {
        for (int attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            Clear();
            _random = new Random(seed);
            GenerateInternal();

            if (_placedModules.Count >= _targetModuleCount)
            {
                break;
            }

            seed = NextSeed(seed);
        }

        _navMeshSurface.BuildNavMesh(); // 생성된 지오메트리 기준으로 NavMesh를 다시 굽는다. 런타임에도 동작한다.

        // 문은 전부 닫힌 채로 시작한다. 서버만 NetworkList를 채울 수 있고, 클라이언트는 이 값을
        // OnListChanged(또는 늦게 들어왔다면 OnNetworkSpawn의 캐치업 루프)로 받아 반영한다.
        if (IsServer)
        {
            _doorOpenStates.Clear();
            for (int i = 0; i < _doors.Count; i++)
            {
                _doorOpenStates.Add(false);
            }
        }

        OnGenerated?.Invoke();
    }

    // 이번 생성 결과로 실제 배치된 모든 모듈의 Bounds를 합친 월드 좌표 경계를 반환한다.
    // 지하 맵은 라운드마다 크기/형태가 달라지므로, 스폰 영역 Box를 고정값 대신 이 값으로 맞춰야 한다.
    public Bounds GetGeneratedBounds()
    {
        Bounds bounds = _placedModules[0].Bounds.bounds;
        for (int i = 1; i < _placedModules.Count; i++)
        {
            bounds.Encapsulate(_placedModules[i].Bounds.bounds);
        }

        return bounds;
    }

    // 지금까지 생성된 모듈을 전부 지우고 생성 관련 상태(큐, 예비 목록)를 초기화한다.
    private void Clear()
    {
        foreach (UndergroundModule placed in _placedModules)
        {
            // StartPoint는 매번 새로 만드는 게 아니라 계속 재사용하는 고정 인스턴스라 지우지 않는다.
            if (placed != null && placed != _startModule)
            {
                // SetActive(false)를 먼저 해서 NavMesh 소스 수집(활성 오브젝트만 대상)에서 즉시 제외시킨다.
                // Destroy()는 프레임이 끝나야 실제로 처리되는데, 재생성 시 이 프레임 안에서 바로
                // GenerateInternal() 다음 BuildNavMesh()가 불리기 때문에, 비활성화 없이는 이전 라운드
                // 모듈이 새 라운드 모듈과 함께 NavMesh에 같이 구워진다.
                placed.gameObject.SetActive(false);
                Destroy(placed.gameObject);
            }
        }

        // 재사용되는 StartPoint는 지워지지 않으니, 이전 라운드에 열렸던 문 상태를 직접 초기화해줘야 한다.
        _startModule.ResetState();

        _placedModules.Clear();
        _openSockets.Clear();
        _reserveSockets.Clear();
        _doors.Clear();
    }

    // 미리 배치해둔 StartPoint를 등록하고, 목표 개수에 도달하거나 더 이을 문이 없을 때까지
    // 큐에서 열린 소켓을 하나씩 꺼내 모듈을 이어붙인다.
    private void GenerateInternal()
    {
        UndergroundModule start = _startModule;
        start.Depth = 0;
        _placedModules.Add(start);
        EnqueueOpenSockets(start);
        Debug.Log($"[생성 시작] start={start.name}, doorSockets={start.DoorSockets.Count}, openQueue={_openSockets.Count}, reserve={_reserveSockets.Count}");

        while (_placedModules.Count < _targetModuleCount)
        {
            if (!TryTakeNextSocket(out DoorSocket openSocket))
            {
                Debug.Log($"[중단] 큐도 예비도 다 떨어짐. placed={_placedModules.Count}/{_targetModuleCount}");
                break; // 큐도 예비도 다 떨어짐 - 진짜로 더 이을 자리가 없다.
            }

            if (openSocket.IsConnected)
            {
                continue;
            }

            // 이 소켓 말고 큐에도 예비에도 아무것도 안 남아있으면, 이번이 진짜 마지막 기회다.
            bool isLastChance = _openSockets.Count == 0 && _reserveSockets.Count == 0;
            TryConnectNextModule(openSocket, isLastChance);
            // 붙일 후보가 하나도 없으면 이 소켓은 그냥 막다른 채로 남는다(백트래킹 없음).
        }

        // 목표 개수를 채우고 끝나면 큐에 남아있던 소켓들은 시도조차 안 된 상태라, 전부 벽으로 마감한다.
        foreach (DoorSocket leftover in _openSockets)
        {
            if (!leftover.IsConnected)
            {
                leftover.ShowAsWall();
            }
        }

        if (_placedModules.Count < _targetModuleCount)
        {
            Debug.LogWarning($"[UndergroundRandomMapGenerator] 길이 막혀 목표 개수({_targetModuleCount})에 못 미치고 {_placedModules.Count}개로 끝났습니다.", this);
        }
        else
        {
            Debug.Log($"[UndergroundRandomMapGenerator] 목표 개수({_targetModuleCount}) 채워서 생성 완료.", this);
        }
    }

    // 재시도용 다음 시드를 이번 시드에서 결정적으로 뽑아낸다. 같은 seed는 항상 같은 다음 seed로
    // 이어지므로, 호스트/클라이언트가 각자 이 함수를 불러도 재시도 시퀀스가 항상 일치한다.
    private static int NextSeed(int seed) => new Random(seed).Next();

    // 큐에서 하나 꺼내고, 큐가 비어있으면 예비 목록에서 하나 무작위로 뽑아온다.
    private bool TryTakeNextSocket(out DoorSocket socket)
    {
        if (_openSockets.Count > 0)
        {
            socket = _openSockets.Dequeue();
            return true;
        }

        if (_reserveSockets.Count > 0)
        {
            int index = _random.Next(_reserveSockets.Count);
            socket = _reserveSockets[index];
            _reserveSockets.RemoveAt(index);
            return true;
        }

        socket = null;
        return false;
    }

    // openSocket에 이어붙일 모듈을 찾는다. 평소엔 문 개수 상관없이 아무 후보나 나올 수 있다.
    // 깊이 제한에 걸리면 막다른 모듈만, 이번이 마지막 기회면(큐/예비 다 비었으면) 막다른 모듈은 제외한다.
    private void TryConnectNextModule(DoorSocket openSocket, bool isLastChance)
    {
        UndergroundModule parentModule = openSocket.GetComponentInParent<UndergroundModule>();
        int newDepth = parentModule.Depth + 1;
        bool mustBeDeadEnd = newDepth >= _maxDepth;
        bool mustAvoidDeadEnd = !mustBeDeadEnd && isLastChance;

        foreach (UndergroundModule candidatePrefab in Shuffled(_modulePrefabs)) {
            bool isDeadEnd = candidatePrefab.DoorSockets.Count == 1;

            if (mustBeDeadEnd && !isDeadEnd) {
                continue; // 깊이 제한에 걸리면 막다른 모듈만 써야 한다
            }

            if (mustAvoidDeadEnd && isDeadEnd) {
                continue; // 마지막 기회인데 막다른 모듈을 쓰면 여기서 생성이 끝나버린다
            }

            if (candidatePrefab == parentModule.SourcePrefab) {
                continue; // 같은 모듈이 연속으로 나오지 않게 한다
            }
            
            UndergroundModule candidate = Instantiate(candidatePrefab, transform);
            candidate.SourcePrefab = candidatePrefab;

            foreach (DoorSocket candidateSocket in Shuffled(candidate.DoorSockets))
            {
                AlignToSocket(candidate, candidateSocket, openSocket);
                Physics.SyncTransforms(); // 방금 옮긴 Transform을 Collider.bounds에 즉시 반영시킨다 (안 하면 겹침 검사가 옛날 위치로 이뤄진다).

                if (Overlaps(candidate, parentModule))
                {
                    continue;
                }

                openSocket.IsConnected = true;
                candidateSocket.IsConnected = true;
                // 문은 한쪽만 보여준다. candidateSocket은 기본값(둘 다 꺼짐)으로 둔다.
                // 인덱스가 _doorOpenStates와 대응해야 하니, 등록될 자리(_doors.Count)를 그대로 넘긴다.
                openSocket.ShowAsDoor(this, _doors.Count);
                _doors.Add(openSocket.Door);
                candidate.Depth = newDepth;
                _placedModules.Add(candidate);
                EnqueueOpenSockets(candidate);
                return;
            }

            // SetActive(false)를 먼저 해서 NavMesh 소스 수집(활성 오브젝트만 대상)에서 즉시 제외시킨다.
            // Destroy()는 프레임이 끝나야 실제로 처리되는데, 이 프레임 안에서 바로 BuildNavMesh()가
            // 불리기 때문에 비활성화 없이는 탈락한 후보가 유령 지오메트리로 구워질 수 있다.
            candidate.gameObject.SetActive(false);
            Destroy(candidate.gameObject);
        }

        openSocket.ShowAsWall(); // 어떤 후보도 못 붙였으니 확실히 벽으로 마감한다.
    }

    // 문마다 독립적으로 지금 열지, 예비로 남겨둘지 정한다. 예비로 간 문은 나중에 큐가 말랐을 때
    // (TryTakeNextSocket) 다시 꺼내 쓰므로 여기서 버려지는 게 아니다.
    private void EnqueueOpenSockets(UndergroundModule module)
    {
        foreach (DoorSocket socket in module.DoorSockets)
        {
            if (socket.IsConnected) {
                continue;
            }

            // 일정 확률로 이번 문을 사용할지 말지, 사용하면 _openSocket에, 안하면 _reverseSocekt에 저장
            if (_random.NextDouble() <= _doorUseChance) {
                _openSockets.Enqueue(socket);
            }
            else {
                socket.ShowAsWall(); // 일단 안 쓸 문이니 막힌 걸로 보여둔다. 나중에 예비에서 꺼내져 실제로 이어지면 ShowAsDoor가 뒤집는다.
                _reserveSockets.Add(socket);
            }
        }
    }

    // moduleSocket이 targetSocket과 마주보게(forward가 서로 반대) moduleToPlace 전체를 옮기고 돌린다.
    private static void AlignToSocket(UndergroundModule moduleToPlace, DoorSocket moduleSocket, DoorSocket targetSocket)
    {
        float angle = Vector3.SignedAngle(moduleSocket.transform.forward, -targetSocket.transform.forward, Vector3.up);
        moduleToPlace.transform.RotateAround(moduleSocket.transform.position, Vector3.up, angle);

        moduleToPlace.transform.position += targetSocket.transform.position - moduleSocket.transform.position;
    }

    // 문 있는 자리에서 부모 모듈과 살짝 겹치는 건 정상이라, 부모 모듈은 겹침 검사에서 제외한다.
    private bool Overlaps(UndergroundModule candidate, UndergroundModule ignore)
    {
        foreach (UndergroundModule placed in _placedModules)
        {
            if (placed == ignore)
            {
                continue;
            }

            if (candidate.Bounds.bounds.Intersects(placed.Bounds.bounds))
            {
                return true;
            }
        }

        return false;
    }

    // source 전체를 무작위 순서로, 겹치지도 빠지지도 않게 한 번씩 돈다(Fisher-Yates).
    private IEnumerable<T> Shuffled<T>(IReadOnlyList<T> source)
    {
        int[] indices = new int[source.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        for (int i = indices.Length - 1; i > 0; i--)
        {
            int swapIndex = _random.Next(i + 1);
            (indices[i], indices[swapIndex]) = (indices[swapIndex], indices[i]);
        }

        foreach (int index in indices)
        {
            yield return source[index];
        }
    }
}
