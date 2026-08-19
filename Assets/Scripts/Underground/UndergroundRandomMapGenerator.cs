using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

// 시드 하나로 StartPoint부터 DoorSocket을 이어가며 지하 맵을 절차적으로 생성한다.
// 같은 시드를 넣으면 항상 같은 배치가 나온다(Random.InitState 사용).
[RequireComponent(typeof(NavMeshSurface))]
public class UndergroundRandomMapGenerator : MonoBehaviour
{
    [SerializeField] private UndergroundModule _startModulePrefab;
    [SerializeField] private UndergroundModule[] _modulePrefabs;
    [SerializeField, Min(1)] private int _targetModuleCount = 20;
    [SerializeField, Min(1)] private int _maxDepth = 10;
    [SerializeField, Range(0f, 1f)] private float _doorUseChance = 0.7f;
    [SerializeField] private int _debugSeed;
    
    private NavMeshSurface _navMeshSurface;
    // readonly면 Unity가 아예 직렬화를 안 해서 인스펙터에 안 뜬다. 디버그용이라 readonly 뺐다.
    private readonly List<UndergroundModule> _placedModules = new();
    private readonly Queue<DoorSocket> _openSockets = new();
    // 랜덤으로 안 열고 닫아둔 문들. 큐가 말랐는데 아직 목표에 못 미치면 여기서 꺼내 다시 시도한다.
    private readonly List<DoorSocket> _reserveSockets = new();

    private void Awake() {
        _navMeshSurface = GetComponent<NavMeshSurface>();
    }

    // 인스펙터에서 우클릭 → 이 메뉴로 _debugSeed를 넣어 바로 테스트해볼 수 있다.
    [ContextMenu("Generate Random Map")]
    private void GenerateFromInspector()
    {
        Generate(_debugSeed);
    }

    // 이전 생성 결과를 지우고, seed로 Random 상태를 고정한 뒤 새로 생성한다.
    // 호스트/클라이언트가 같은 seed로 이 함수를 부르면 항상 같은 맵이 나온다.
    public void Generate(int seed)
    {
        Clear();

        Random.State previousState = Random.state;
        try
        {
            Random.InitState(seed);
            GenerateInternal();
        }
        finally
        {
            Random.state = previousState;
        }

        _navMeshSurface.BuildNavMesh(); // 생성된 지오메트리 기준으로 NavMesh를 다시 굽는다. 런타임에도 동작한다.
    }

    // 지금까지 생성된 모듈을 전부 지우고 생성 관련 상태(큐, 예비 목록)를 초기화한다.
    private void Clear()
    {
        foreach (UndergroundModule placed in _placedModules)
        {
            if (placed != null)
            {
                DestroyModule(placed.gameObject);
            }
        }

        _placedModules.Clear();
        _openSockets.Clear();
        _reserveSockets.Clear();
    }

    // StartPoint를 이 오브젝트 위치에 놓고, 목표 개수에 도달하거나 더 이을 문이 없을 때까지
    // 큐에서 열린 소켓을 하나씩 꺼내 모듈을 이어붙인다.
    private void GenerateInternal()
    {
        UndergroundModule start = Instantiate(_startModulePrefab, transform.position, transform.rotation, transform);
        start.Depth = 0;
        start.SourcePrefab = _startModulePrefab;
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
    }

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
            int index = Random.Range(0, _reserveSockets.Count);
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

        foreach (UndergroundModule candidatePrefab in Shuffled(_modulePrefabs))
        {
            bool isDeadEnd = candidatePrefab.DoorSockets.Count == 1;

            if (mustBeDeadEnd && !isDeadEnd)
            {
                continue; // 깊이 제한에 걸리면 막다른 모듈만 써야 한다
            }

            if (mustAvoidDeadEnd && isDeadEnd)
            {
                continue; // 마지막 기회인데 막다른 모듈을 쓰면 여기서 생성이 끝나버린다
            }

            if (candidatePrefab == parentModule.SourcePrefab)
            {
                continue; // 같은 모듈이 연속으로 나오지 않게 한다
            }

            UndergroundModule candidate = Instantiate(candidatePrefab, transform);
            candidate.SourcePrefab = candidatePrefab;

            foreach (DoorSocket candidateSocket in Shuffled(candidate.DoorSockets))
            {
                UndergroundSocketAligner.AlignToSocket(candidate, candidateSocket, openSocket);
                Physics.SyncTransforms(); // 방금 옮긴 Transform을 Collider.bounds에 즉시 반영시킨다 (안 하면 겹침 검사가 옛날 위치로 이뤄진다).

                if (Overlaps(candidate, parentModule))
                {
                    continue;
                }

                openSocket.IsConnected = true;
                candidateSocket.IsConnected = true;
                openSocket.ShowAsDoor(); // 문은 한쪽만 보여준다. candidateSocket은 기본값(둘 다 꺼짐)으로 둔다.
                candidate.Depth = newDepth;
                _placedModules.Add(candidate);
                EnqueueOpenSockets(candidate);
                return;
            }

            DestroyModule(candidate.gameObject);
        }

        openSocket.ShowAsWall(); // 어떤 후보도 못 붙였으니 확실히 벽으로 마감한다.
    }

    // Destroy는 Play 모드에서만 되고, Edit 모드(ContextMenu 테스트)에서는 DestroyImmediate를 써야 한다.
    private static void DestroyModule(GameObject target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
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
            if (Random.value <= _doorUseChance) {
                _openSockets.Enqueue(socket);
            }
            else {
                socket.ShowAsWall(); // 일단 안 쓸 문이니 막힌 걸로 보여둔다. 나중에 예비에서 꺼내져 실제로 이어지면 ShowAsDoor가 뒤집는다.
                _reserveSockets.Add(socket);
            }
        }
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
    private static IEnumerable<T> Shuffled<T>(IReadOnlyList<T> source)
    {
        int[] indices = new int[source.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        for (int i = indices.Length - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            (indices[i], indices[swapIndex]) = (indices[swapIndex], indices[i]);
        }

        foreach (int index in indices)
        {
            yield return source[index];
        }
    }
}
