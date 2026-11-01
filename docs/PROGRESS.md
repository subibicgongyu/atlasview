# PS3 Atlas Viewer 진행 및 인수인계

작성일: 2026-11-01

## 후속 작업: 조건문·반복문 중괄호 통일 (2026-11-01)

- 전체 C# 18개 파일을 검사하고 16개 파일에 블록 282개를 추가했다. `if`, `else`, `for`, `foreach`, `while`, `do`의 단일 문장 본문에도 중괄호를 적용했다. `else if` 연결은 유지하며 각 `if` 본문은 블록으로 감쌌다.
- `.editorconfig`에 `csharp_prefer_braces = true:warning`, `IDE0011` 경고 규칙을 추가했다. `dotnet format style PS3AtlasViewer.csproj --no-restore --diagnostics IDE0011`로 적용했다.
- Roslyn 정적 검사로 중괄호 없는 대상 본문이 없고 중괄호 외 토큰·문자열·주석·지시문이 변경 전과 동일함을 확인했다. 원본과 검증 도구는 `artifacts/braces/`에 보존한다.
- Debug 빌드 경고 0·오류 0, 기존 Framework 빌드 및 자동 검사 134개 통과. 수동 GUI 디버깅·게임 실행은 수행하지 않았다. 줄 번호가 바뀌었으므로 기존 중단점 위치 확인이 필요하다.
- 검증 빌드: `artifacts/braces/build/`. GUI SHA-256: `53ce041a64c1e4701a21f8e82251a6e0e3a5881f75be141d40d9da298bb3fe58`, CLI SHA-256: `db0e5bf58052bc914fcbaf0e38d12672227392debdfe28eb0591173ca18d6b83`. Debug 실행 파일도 갱신했으며 배포 `dist`는 유지했다.

## 후속 작업: 변수명 정리 (2026-11-01)

- 전체 C# 소스 18개를 확인하고 16개 파일의 모호한 지역 변수·매개변수와 일부 비공개 UI/캐시 필드 이름을 변경했다. 이미 역할이 명확한 이름과 좌표 `x`, `y`는 유지했다.
- 예: `Scanner.Scan`의 `root/deep/token/progress` → `rootDirectory/searchFileContents/cancellationToken/reportProgress`, `r/s/head` → `scanResult/source/header`. 디코더는 입력·출력 오프셋, 블록 좌표, RGB 채널과 알파 인덱스를 구분했다. RAW의 `w/h/fmt`는 `widthInput/heightInput/formatSelector`로 변경했다.
- Roslyn 의미 분석으로 실제 선언과 참조를 연결해 변경했다. 각 단계에서 전체 식별자의 참조 대상, 식별자 외 토큰, 문자열·주석·지시문 및 컴파일 오류 유무를 검증했다. 저장 모델의 공개 필드, 익명 객체의 암시적 JSON 속성 이름 및 리플렉션 조회 필드는 보존했다.
- 변경 전 소스, 분석 도구와 단계별 이름 변경 기록은 Git 제외 경로 `artifacts/naming/`에 보관한다. `renames*.json`은 중간 이름 재정리를 포함한 작업 이력이다.
- 최종 Debug 빌드는 경고 0·오류 0, 최종 Framework 빌드의 기존 자동 검사 134개는 모두 통과했다. 수동 GUI 디버깅·게임 실행은 이번 작업에서 하지 않았다. 사용자는 F5로 다시 시작해 폴더 검색과 이미지 선택을 확인할 수 있다.
- 검증 빌드: `artifacts/naming/build/`. GUI SHA-256: `702fd4d717b49c1c4b3b2a986e76bde35784e481cb8af56bd33c753acbc60214`, CLI SHA-256: `bf02ce5ee96a753e8c8526c61db0fa90f47448b5b9ed0222c0adf5779712b9f4`. 디버그 실행 파일은 `bin/Debug/net45/PS3AtlasViewer.exe`에 갱신했다. 배포 `dist`는 갱신하지 않았다.

## 후속 작업: 전체 C# 소스 서식 정리 (2026-11-01)

- `src/*.cs` 전체 18개 파일의 들여쓰기, 중괄호, 문장 및 필드 선언 줄바꿈을 정리했다. `.editorconfig`에 공백 4칸과 CRLF, 단일 줄 블록/문장 펼침 규칙을 추가했다.
- `dotnet format whitespace PS3AtlasViewer.csproj --no-restore`로 서식을 적용했다. 연속 필드 선언은 Roslyn 구문 트리에서 줄바꿈 trivia만 추가했다. 변경 전 파일과 검증 도구는 Git 제외 경로 `artifacts/formatting/`에 보관한다.
- Roslyn으로 18개 파일의 토큰 종류·원문 및 공백 외 주석/지시문이 변경 전과 동일함을 확인했다. 디버그 빌드 경고 0·오류 0, 기존 Framework 빌드 및 자동 검사 134개 통과를 확인했다.
- 검증 빌드: `artifacts/formatting/build/`. GUI SHA-256: `a10389621289cb29ea4a61042b1bbd602cdb8699349e04135deaf0a30718f9e8`, CLI SHA-256: `451135e68639ca576a83c18bd3f3b37de4ad60e8a2a81a80e4cacafd43bf7d9a`. `dist`는 갱신하지 않았다.
- Portable PDB 수정 후 사용자가 중단점 적중을 확인했다. 이번 서식 변경으로 줄 번호가 바뀌었으므로 중단점 위치 재확인이 필요하다. 이번 작업에서 GUI 수동 조작·게임 실행은 하지 않았다.

## 후속 작업: VS Code C# 소스 분석 환경 (2026-11-01)

### 디버깅 설정 추가

- 후속 수정: 중단점 미적중 보고 후 공식 C# 확장 문서에서 Framework 디버거가 x64 + Portable PDB를 요구함을 확인했다. 최초 `DebugType=full` 지정은 잘못된 설정이어서 `portable`로 수정했다. 아래 Windows PDB 생성 기록은 수정 전 이력이다. 실제 F5 중단점 적중은 사용자 확인이 필요하다.

- `.vscode/launch.json`에 .NET Framework용 `clr` 실행 구성을, `.vscode/tasks.json`에 F5 사전 빌드 및 기본 빌드 작업을 추가했다.
- SDK 프로젝트의 Debug 구성은 최적화 없이 Windows PDB를 생성한다. EXE/PDB 생성, 설정 JSON 및 작업 이름 연결, C# 확장의 디버거 파일 존재를 확인했다.
- `dotnet build PS3AtlasViewer.csproj --configuration Debug --nologo --force` 검증은 경고 0, 오류 0으로 성공했다. 최초 제한 환경의 NuGet 조회 경고는 네트워크 허용 후 재복원으로 해소했다.
- GUI 디버그 실행 및 중단점 적중은 직접 확인하지 않았다. 자동 검사는 이번 설정 변경에서 재실행하지 않았다. 디버그 사용자 데이터 위치는 `bin/Debug/net45`이며 배포 빌드 절차는 유지한다.

- C# 로그의 `No .NET SDKs were found`를 조사했다. 시스템에 런타임만 설치되어 있고 `.csproj`가 없어 `Loading.cs`가 단독 파일로 로드되고 있었다.
- Microsoft 공식 winget 패키지로 .NET SDK 10.0.403 x64를 설치하고 `dotnet --list-sdks`로 확인했다.
- 루트 `PS3AtlasViewer.csproj`를 추가했다. `src/*.cs`, .NET Framework 4.5, C# 5, x64 및 기존 Framework 참조를 지정했다. `.vscode/settings.json`에 `dotnet.preferCSharpExtension: true`를 추가하고 `bin/`, `obj/`를 Git 제외했다.
- `dotnet build PS3AtlasViewer.csproj --nologo`가 복원 및 빌드에 성공했다(경고 0, 오류 0). 결과는 `bin/Debug/net45/PS3AtlasViewer.exe`다. SDK는 개발 환경용이며 앱을 .NET 10으로 이전한 것은 아니다.
- 기존 빌드는 `build.ps1 -Test -OutputDirectory artifacts/ide-validation/legacy`로 검증해 134개 검사를 통과했다. 결과 해시는 해당 폴더의 `sha256.json`에 기록했다. `dist`의 GUI/CLI 해시는 아래 배포 기록과 동일함을 재확인했다. 기존 사용자 데이터는 수정하지 않았다.
- VS Code에서 저장소 루트 `E:/work/trans/atlasview`를 열고 재시작해야 설정과 SDK를 반영한다. VS Code 재시작 후 언어 서버의 실제 프로젝트 로드는 이번 작업에서 확인하지 않았다. 게임 실행·플레이 테스트는 수행하지 않았다.

이하 내용은 위 후속 작업 이전의 배포 인수인계 기록이다.

## 현재 상태

범용 PS3 아틀라스 뷰어의 초기 버전 v0.1.0 구현·빌드·배포를 완료했다. C# / .NET Framework WinForms 앱이며 복호화된 폴더를 읽어서 지원되는 이미지 리소스를 탐색한다. 특정 게임에만 묶인 도구는 아니지만 모든 게임의 전용 컨테이너·압축 형식을 지원하는 것은 아니다. 원본 게임 파일은 수정하지 않는다.

이 문서는 현재 작업 상태의 요약이다. 세부 구조는 DEVELOPMENT.md, 공개 검증 안내는 TESTING.md, 사용법은 상위 README.md를 참고한다.

## 완료한 기능

- 폴더 검색 및 파일 내부 리소스 검색, 비동기 이미지 읽기, 진행 표시와 취소 처리.
- PNG/JPEG/BMP/GIF, DDS, GTF, 일부 PS3 G1T 프로필, BNS/LZP2 및 수동 RAW 해석 지원.
- 왼쪽 목록은 파일 경로와 리소스 시작 오프셋을 기준으로 한 행을 구성한다. 한 리소스의 하위 이미지들은 함께 표시한다.
- 리소스 ID는 정규화된 절대 경로와 시작 오프셋으로 생성한다. 숨김·미번역 기록도 같은 단위를 사용한다. 원본을 다른 경로로 옮기면 ID가 달라질 수 있다.
- 목록 순서대로 기본 8개 리소스를 오른쪽에 모아 표시하며 묶음 크기를 1~32개로 변경할 수 있다. 하위 이미지가 있으면 화면의 이미지 수는 리소스 수보다 많다.
- 타일 클릭으로 활성 리소스를 선택하고 더블클릭으로 개별 이미지를 확대한다. 모아보기는 탐색용 배치이며 게임의 실제 아틀라스 배치를 복원한 것은 아니다.
- H 키로 활성 리소스를 숨기고 다음 항목을 선택한다. 묶음 전체 숨김·미번역 기록도 가능하며 각 리소스 기록은 별도로 관리한다.
- 숨김 목록 관리 모달에 이미지 미리보기, 검색, 체크 및 복원 기능을 제공한다.
- 숨김 목록은 200개씩 표시하고 검색 입력 지연 처리와 메모리 상태 재사용으로 대량 목록의 버벅임을 개선했다.
- 미번역 기록에 리소스 식별 정보와 원본 크기의 이미지 PNG를 저장하고 전달용 자료를 내보낼 수 있다.
- 숨김 또는 미번역으로 기록된 리소스는 검색 목록에서 제외한다. 미번역 기록을 삭제하고 관리 창을 닫으면 다시 표시한다. 별도로 숨김 처리된 항목은 계속 제외한다.
- 시작할 때 이전 폴더 경로를 기억하고 파일 내부 검색을 기본으로 켠다. 자동 재검색은 하지 않는다.

## 캐시 및 사용자 데이터

사용자 요청으로 디스크 이미지 캐시 기능과 최근 이미지 복원을 제거했다. 실행 폴더의 기존 `dist/cache`도 삭제했다. 이번 문서 작성 시 해당 폴더가 없는 것을 다시 확인했다.

메모리 캐시는 유지한다. 디코딩 이미지 128 MiB, 압축 해제 버퍼 64 MiB 한도이며 앱 재시작 후에는 원본에서 다시 읽는다. 숨김 관리 창은 별도 미리보기 로더를 사용한다.

실행 폴더 기준 사용자 데이터:

- `settings/viewer.json`: 이전 폴더 등 설정.
- `settings/hidden-images.json`: 숨김 리소스 목록.
- `untranslated/untranslated.json`: 미번역 리소스 기록.
- `untranslated/images/`: 미번역 기록에 첨부한 이미지. 삭제한 디스크 캐시와는 별개다.

리소스 단위로 전환할 당시 요청받은 초기화는 이미 완료했다. 이후 업데이트에서는 기존 숨김·미번역 기록을 보존한다. 추가 요청 없이 다시 초기화하지 않는다.

## 소스 및 빌드

- `src/Viewer.cs`: 메인 화면과 이미지 캔버스.
- `src/Core.cs`, `src/Formats.cs`: 파일 읽기, 형식 분석 및 디코딩.
- `src/Loading.cs`: 비동기 로딩과 메모리 캐시.
- `src/Resources.cs`, `src/ResourceBatch.cs`: 리소스 그룹과 묶음 표시.
- `src/HiddenImagesDialog.cs`, `src/HiddenPreview.cs`: 숨김 관리 및 미리보기.
- `build.ps1`: 빌드와 자동 검사.
- `scripts/`: 개발·진단·검증 보조 스크립트. 일반 사용에는 필요 없다.

빌드 및 검사 명령:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

GUI 결과물은 `dist/PS3AtlasViewer.exe`, 선택적으로 사용하는 CLI는 `dist/AtlasCheck.exe`다.

## 검증 결과

최근 빌드에서 자동 검사 134개가 통과했다. `artifacts/tests/tests.json`의 저장된 결과를 이번 문서 작성 시 재확인했다. 문서만 추가했으므로 이번에는 빌드나 테스트를 다시 실행하지 않았다.

검사 범위는 디코더와 입력 범위, 원본 보존, 메모리 캐시, 리소스 그룹, 숨김·미번역 개별/일괄 처리, 취소·저장 실패 처리, 목록 페이지 등이다. 이전 작업에서 숨김 목록 UI와 실제 리소스 일부의 디코딩을 확인했다. 모든 게임의 모든 리소스 또는 모든 UI 조작을 검증한 것은 아니다. 게임 실행·플레이 테스트는 수행하지 않았다.

현재 빌드의 기록된 SHA-256 (`dist/sha256.json`):

| 파일 | SHA-256 |
| --- | --- |
| PS3AtlasViewer.exe | `b500d17788f871c300fa0e5582eb42ec5d1dae3678d015774933df3b50036647` |
| AtlasCheck.exe | `60e2210ee709a0b5b647c440eef00bf5ceb155c3378ec21fce2b90aa4914115a` |

## GitHub 및 배포

- 저장소: https://github.com/subibicgongyu/atlasview
- 배포: https://github.com/subibicgongyu/atlasview/releases/tag/v0.1.0
- 배포 기준 커밋: `3b1a47fda01e4aecdf059548365bc142d3e52769` (`main`). 이번 문서 추가 전 로컬 HEAD도 동일함을 확인했다.
- ZIP: `PS3AtlasViewer-v0.1.0-win-x64.zip`, 별도 SHA-256 파일 제공.
- ZIP SHA-256: `af2695706f8454ea04be2667c1674cdcdb90d49c9c8779da92eb0d91da38f618`.
- 로컬 배포 자료: `artifacts/releases/v0.1.0/`.

이전 배포 작업에서 GitHub 업로드 파일의 해시 일치를 확인했다. 사용자 승인으로 저장소를 전체 공개로 전환했고, 당시 협업자 조회에서 소유자만 쓰기·관리 권한이 있음을 확인했다. 이번 문서 작성 중 원격 설정을 다시 조회하지는 않았다.

일반 사용자는 Release ZIP을 풀고 `PS3AtlasViewer.exe`를 실행하면 된다. 소스와 scripts는 필요 없다. Windows x64와 .NET Framework 4.5 이상이 필요하며, 탐색할 복호화된 게임 폴더는 별도로 준비한다. ZIP에는 GUI, 선택적 CLI, README.txt, SHA256SUMS.txt가 포함된다.

개인 경로·게임 원본 해시를 담은 `docs/VALIDATION.md`, `docs/FREEZE_INVESTIGATION.md`는 로컬에 보존하고 Git에서 제외했다. 사용자 설정·기록 및 게임 데이터도 배포에 포함하지 않는다.

## 취소된 요청과 남은 한계

- 이미지 속 일본어 OCR 조건 검색은 취소되었고 구현하지 않았다.
- Codex가 화면을 순회하며 일본어 유무를 분류하는 작업은 사용자 요청으로 중단했다. 자동 재개하지 않는다.
- 최대 1024 크기 모아보기 요청은 취소되었다. 현재 모아보기에는 별도의 안전 한도가 적용된다.
- 미지원 전용 포맷은 실제 샘플의 구조를 분석한 뒤 별도 지원을 추가해야 한다.
- 오래된 RAW 숨김 기록에 해석 설정이 없거나 원본 파일이 사라지면 미리보기를 복원하지 못할 수 있다.
- 미번역 일괄 저장 취소·실패 시 기존 JSON 기록은 보존하지만 이미 생성된 첨부 PNG 일부가 남을 수 있다.

현재 추가 구현 요청은 대기 중이 아니다. 사용자가 실제 자료로 탐색·숨김·미번역 기록 및 재시작 동작을 사용하면서 발견한 문제를 전달하면 이어서 수정한다. 이 진행 문서는 로컬에 추가했으며 별도 커밋·푸시는 아직 하지 않았다.
