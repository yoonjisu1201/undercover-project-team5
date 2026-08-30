using UnityEngine;

// 대기방 전시물 안내 영상 한 편을, 프레임을 격자로 이어붙인 아틀라스로 들고 있다.
// 영상 파일 대신 텍스처를 쓰는 이유는 이 프로젝트 환경에서 영상 디코더가 첫 프레임을 내놓기까지
// 몇 초씩 걸리거나 아예 멈추는 일이 잦았기 때문이다(빌드에서도 같았다).
//
// 텍스처 한 변이 8192 를 넘으면 Unity 가 축소해 격자가 어긋나므로, 한 장에 못 담으면 여러 장으로 나눈다.
// 장마다 격자는 같고 마지막 장에만 빈 칸이 남는다 — Tools/make_tutorial_flipbook.py 가 그렇게 굽는다.
[CreateAssetMenu(menuName = "WaitingRoom/Tutorial Flipbook", fileName = "NewTutorialFlipbook")]
public sealed class TutorialFlipbook : ScriptableObject
{
    [Tooltip("프레임을 왼쪽 위부터 행 방향으로 채운 아틀라스. 여러 장이면 재생 순서대로 넣는다.")]
    [SerializeField] private Texture2D[] _atlases;
    [SerializeField, Min(1)] private int _columns = 1;
    [SerializeField, Min(1)] private int _rows = 1;

    // 격자 칸 수와 다를 수 있다. 마지막 장에는 대개 빈 칸이 남고, 그 검은 칸까지 재생하면 안 된다.
    [SerializeField, Min(1)] private int _frameCount = 1;
    [SerializeField, Min(1f)] private float _frameRate = 15f;

    public bool HasAtlas => _atlases != null && _atlases.Length > 0 && _atlases[0] != null;

    // 재생 시간에 해당하는 칸 번호. 끝까지 가면 처음으로 돌아온다.
    public int GetFrameIndex(float elapsedSeconds)
    {
        int index = Mathf.FloorToInt(elapsedSeconds * _frameRate);
        return ((index % _frameCount) + _frameCount) % _frameCount;
    }

    // 그 칸이 들어 있는 아틀라스.
    public Texture2D GetAtlas(int frameIndex)
    {
        return _atlases[frameIndex / (_columns * _rows)];
    }

    // 그 칸이 자기 아틀라스에서 차지하는 영역. RawImage.uvRect 에 그대로 넣는다.
    public Rect GetUvRect(int frameIndex)
    {
        int indexInAtlas = frameIndex % (_columns * _rows);
        int column = indexInAtlas % _columns;
        int row = indexInAtlas / _columns;
        float width = 1f / _columns;
        float height = 1f / _rows;

        // 아틀라스는 위에서 아래로 채웠는데 UV 는 아래가 원점이라 행을 뒤집는다.
        return new Rect(column * width, 1f - (row + 1) * height, width, height);
    }
}
