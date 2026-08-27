// ESC(또는 닫기 입력)로 닫을 수 있는 UI. GameplayUiMode의 열린 UI 스택에 등록해 사용한다.
public interface IClosableUi
{
    void Close();

    // 닫을 때 낼 소리. 대부분은 팝업 공통음이면 되므로 기본값을 두고,
    // 자기 소리가 따로 있는 UI 만 덮어쓴다.
    SoundKey CloseSound => SoundKey.Ui_PopupClose;
}
