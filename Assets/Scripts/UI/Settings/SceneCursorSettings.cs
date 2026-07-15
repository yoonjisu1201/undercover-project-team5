using UnityEngine;
using UnityEngine.SceneManagement;


//--- 씬 시작시 커서 상태를 설정하는 기능을 구현하는 클래스 ---//
public sealed class SceneCursorSettings : MonoBehaviour
{
    [SerializeField] private bool _cursorVisibleByDefault;

    public bool CursorVisibleByDefault => _cursorVisibleByDefault;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyDefaultCursorState();
    }

    public void ApplyDefaultCursorState()
    {
        Cursor.visible = _cursorVisibleByDefault;
        Cursor.lockState = _cursorVisibleByDefault ? CursorLockMode.None : CursorLockMode.Locked;
    }
}
