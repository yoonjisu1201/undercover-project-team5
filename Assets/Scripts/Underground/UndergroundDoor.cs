using Cysharp.Threading.Tasks;
using UnityEngine;

// 문 하나의 열림 상태를 반영한다.
public class UndergroundDoor : InteractableBase
{
    private const float OpenDuration = 1f;
    [SerializeField] private float _openDistance = 2.5f;
    private bool _isOpened = false;

    private UndergroundRandomMapGenerator _generator;
    private Vector3 _initialLocalPosition;

    // 생성기의 _doorOpenStates에서 이 문의 인덱스. 상호작용 시 생성기에 어떤 문인지 알려주는 용도.
    public int Index { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        _initialLocalPosition = transform.localPosition;
    }

    // 생성기가 이 소켓을 실제로 문으로 쓰기로 정했을 때 부른다. 문 모델을 켜고, 나중에 상호작용 시
    // 열어달라고 요청할 생성기 참조와 인덱스를 저장해둔다.
    public void Initialize(UndergroundRandomMapGenerator generator, int index)
    {
        _generator = generator;
        Index = index;
        gameObject.SetActive(true);
    }

    // 재사용되는 모듈(StartPoint 등)이 다시 생성될 때, 이전 라운드에 열렸던 상태를 지우고 처음 위치로 되돌린다.
    public void ResetState()
    {
        _isOpened = false;
        transform.localPosition = _initialLocalPosition;
    }

    public void Open()
    {
        _isOpened = true;
        OpenAsync().Forget();
    }

    private async UniTaskVoid OpenAsync()
    {
        Vector3 startPosition = transform.position;
        Vector3 targetPosition = startPosition + -transform.right * _openDistance;

        float elapsed = 0f;
        while (elapsed < OpenDuration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(startPosition, targetPosition, elapsed / OpenDuration);
            await UniTask.Yield();
        }

        transform.position = targetPosition;
        gameObject.SetActive(false);
    }

    public override string InteractionText => "문 열기";
    
    public override bool CanInteract(GameObject interactor) => !_isOpened;
    
    public override void Interact(GameObject interactor) {
        _generator.OpenDoorRpc(Index);
    }
}
