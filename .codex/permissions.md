# Codex 권한 참고

Claude의 `.claude/settings.local.json`에 있던 allow 목록을 Codex에서 참고할 수 있도록 옮긴 문서다.

Codex는 Claude의 `settings.local.json` 권한 형식을 그대로 사용하지 않는다. 외부 도구 실행, 네트워크 접근, GUI 실행, 저장소 밖 파일 접근이 필요하면 Codex의 승인 요청 흐름을 사용한다.

## Claude allow 원본 요약

- Notion MCP fetch:
  - `mcp__a7bccb2e-d668-4003-a3b1-97c850ad019e__notion-fetch`
- Claude 로컬 skill plugin docx 경로 읽기:
  - `Read(//c/Users/oi3oi3oi/AppData/Roaming/Claude/local-agent-mode-sessions/skills-plugin/3ee6a632-7478-4cde-8a7e-d0062cbb44be/537a3315-9e6e-41f5-b89b-235c2043791f/skills/docx/**)`
- GDD 원본 Word 문서 추출:
  - `extract-text "C:\\Users\\oi3oi3oi\\Downloads\\5팀_0706_기획서 (1).docx"`
  - `pandoc "C:\\Users\\oi3oi3oi\\Downloads\\5팀_0706_기획서 (1).docx" -o /tmp/gdd_source.md -t gfm`
  - `pandoc "C:\\Users\\oi3oi3oi\\Downloads\\5팀_0706_기획서 (1).docx" -o "C:\\Users\\oi3oi3oi\\AppData\\Local\\Temp\\claude\\D--Unity-Project-Undercover\\3950a0e6-64ee-4600-860d-7c2ac5a3aac1\\scratchpad\\gdd_source.md" -t gfm`
- GitHub CLI 인증 관련:
  - `gh auth *`

## Codex에서의 대응

- 저장소 내부 파일 읽기/쓰기는 현재 워크스페이스 권한을 따른다.
- GitHub 이슈 생성은 사용자 승인 후 `gh issue create`를 실행한다.
- 저장소 밖 Word 문서를 다시 읽거나 변환해야 하면 별도 승인이 필요할 수 있다.
- Notion MCP는 현재 Codex 세션에 같은 도구가 노출되어 있을 때만 사용할 수 있다.

