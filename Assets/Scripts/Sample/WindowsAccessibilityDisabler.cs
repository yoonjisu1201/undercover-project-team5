#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
using UnityEngine;

public class WindowsAccessibilityDisabler : MonoBehaviour
{
    // ★ 씬/오브젝트와 무관하게 게임 시작 시 자동으로 자기 자신을 생성해서 실행
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("WindowsAccessibilityDisabler");
        DontDestroyOnLoad(go);
        go.AddComponent<WindowsAccessibilityDisabler>();
    }

    const uint SPI_GETSTICKYKEYS = 0x003A, SPI_SETSTICKYKEYS = 0x003B;
    const uint SPI_GETFILTERKEYS = 0x0032, SPI_SETFILTERKEYS = 0x0033;
    const uint SPI_GETTOGGLEKEYS = 0x0034, SPI_SETTOGGLEKEYS = 0x0035;
    const uint KEYSON = 0x1, HOTKEYACTIVE = 0x4, CONFIRMHOTKEY = 0x8;

    [StructLayout(LayoutKind.Sequential)] struct STICKYKEYS { public uint cbSize, dwFlags; }
    [StructLayout(LayoutKind.Sequential)] struct FILTERKEYS { public uint cbSize, dwFlags, iWait, iDelay, iRepeat, iBounce; }
    [StructLayout(LayoutKind.Sequential)] struct TOGGLEKEYS { public uint cbSize, dwFlags; }

    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint p, ref STICKYKEYS v, uint w);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint p, ref FILTERKEYS v, uint w);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint p, ref TOGGLEKEYS v, uint w);

    STICKYKEYS _sk; FILTERKEYS _fk; TOGGLEKEYS _tk;
    string _status = "init";

    void Awake()
    {
        _sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS)) };
        SystemParametersInfo(SPI_GETSTICKYKEYS, _sk.cbSize, ref _sk, 0);
        _fk = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf(typeof(FILTERKEYS)) };
        SystemParametersInfo(SPI_GETFILTERKEYS, _fk.cbSize, ref _fk, 0);
        _tk = new TOGGLEKEYS { cbSize = (uint)Marshal.SizeOf(typeof(TOGGLEKEYS)) };
        SystemParametersInfo(SPI_GETTOGGLEKEYS, _tk.cbSize, ref _tk, 0);
        Apply();
    }

    void OnApplicationFocus(bool f) { if (f) Apply(); }

    void Apply()
    {
        bool ok = true;
        if ((_sk.dwFlags & KEYSON) == 0) { var s = _sk; s.dwFlags &= ~(HOTKEYACTIVE | CONFIRMHOTKEY); ok &= SystemParametersInfo(SPI_SETSTICKYKEYS, s.cbSize, ref s, 0); }
        if ((_fk.dwFlags & KEYSON) == 0) { var f = _fk; f.dwFlags &= ~(HOTKEYACTIVE | CONFIRMHOTKEY); ok &= SystemParametersInfo(SPI_SETFILTERKEYS, f.cbSize, ref f, 0); }
        if ((_tk.dwFlags & KEYSON) == 0) { var t = _tk; t.dwFlags &= ~(HOTKEYACTIVE | CONFIRMHOTKEY); ok &= SystemParametersInfo(SPI_SETTOGGLEKEYS, t.cbSize, ref t, 0); }
        _status = $"Applied ok={ok}";
        Debug.Log($"[Accessibility] {_status}");
    }

    void OnApplicationQuit()
    {
        SystemParametersInfo(SPI_SETSTICKYKEYS, _sk.cbSize, ref _sk, 0);
        SystemParametersInfo(SPI_SETFILTERKEYS, _fk.cbSize, ref _fk, 0);
        SystemParametersInfo(SPI_SETTOGGLEKEYS, _tk.cbSize, ref _tk, 0);
    }
}
#endif
