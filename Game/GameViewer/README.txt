GameViewer - MMORPG 필드 전투 C# 뷰어
==========================================

[무엇인가]
  Gateway 클라이언트 포트에 BotClient 처럼 접속(로그인 -> 존 입장)해서 서버가 보내는 전투 패킷을 받아
  숲 필드 위에 영웅/몬스터의 이동, 공격, 스킬(시전/투사체/범위), 피해 숫자, 사망/부활, 상태 효과를
  눈으로 확인하는 도구입니다. 서버와 같은 GameData JSON(Hero/Monster/Skill/Status/HeroSkill/Block/Spawn)을
  읽으므로 이름/사거리/반경/쿨다운이 서버 값과 항상 같습니다.
  서버 없이 화면만 확인하는 데모 모드(--demo)도 있습니다.

[구성]
  Core/    플랫폼 비의존 코어(netstandard2.0, UI/엔진 참조 없음) - Unity 에서 그대로 재사용 가능
    Protocol/  패킷 상수/구조체(Parse/Build), 프레이머 (서버 ZonePacket.h 와 1:1)
    Net/       GatewayClient (수신 스레드 + 큐, 소비는 호출 스레드)
    World/     WorldModel(엔티티/연출/로그), Entity(이동 보간), ViewEffect(연출 데이터)
    Data/      MiniJson, GameData(서버와 같은 JSON 로더)
    Audio/     Sfx(효과음 합성 + 연출별 소리 규칙. 오디오 장치를 모르는 순수 데이터)
    Logic/     AutoPilot(자동 전투), DemoDirector(서버 없는 로컬 시뮬레이션)
    ViewerSession.cs  접속 흐름 + 월드 + 명령(이동/공격/스킬/쿨다운)
    ViewerConfig.cs   JSON + 명령행 옵션
  Assets/  (선택) 이미지 에셋과 sprites.json - 아래 [이미지 에셋]
  App/     WinForms 뷰어 (GDI+ 더블 버퍼). Render/ 는 System.Drawing 만 쓰는 렌더러
  Tests/   콘솔 테스트 (가짜 Gateway 왕복 포함)
  Tools/   RenderSnapshot - 서버/창 없이 데모 장면을 PNG 로 저장 (렌더러 확인용)
           <GameData> <outDir> [초...]   장면 저장 (환경변수 SNAP_BOSS=1 이면 영웅이 보스를 쫓고, SNAP_CAM=x,y 로 카메라 고정)
           --sheet <outDir> [배율]        스프라이트 시트    --bench <GameData> [프레임]  부분별 그리기 시간
           --selftest <GameData>          전 스프라이트/상태/해상도/배율 그리기 점검 + 이미지 에셋 로더 점검(합성 이미지)
  GameViewer.json / GameViewer.bat / GameViewer_Demo.bat

[빌드]
  GameViewer.sln 을 Visual Studio 로 열어 빌드 (".NET 데스크톱 개발" 워크로드, .NET Framework 4.8).
  .NET 8+ 로 올리려면 App.csproj 의 TargetFramework 를 net8.0-windows 로 바꾸면 됩니다.

[실행]
  1) 서버: GatewayServer 와 ZoneServer 를 띄움
  2) GameViewer.bat 실행  (GameViewer.json 의 값 사용, 명령행 옵션이 우선)
     - GameViewer.bat 안의 DATA 를 서버가 읽는 GameData 폴더로 맞춰 주세요.
     - 서버 없이 화면만 보려면 GameViewer_Demo.bat
  3) BotClient 를 같이 돌리면 뷰어 주변에서 봇들이 몬스터와 싸우는 모습을 볼 수 있습니다.
  4) 같은 PC 에서 GameViewer.bat 을 여러 번 실행하면 창이 여러 개 뜹니다. 실행마다 빈 슬롯 번호(0, 1, 2...)를 CharId 에 더해
     (900001, 900002, ...) 서로 다른 캐릭터로 입장하고, 창 제목에 캐릭터 ID 가 표시됩니다. 창을 닫으면 그 번호는 다시 쓰입니다.
     --char-id 를 직접 주거나 --single(또는 GameViewer.json 의 MultiInstance=false)이면 번호를 더하지 않고 그 ID 를 그대로 씁니다.
     서버에 같은 캐릭터 ID 로 두 번 입장할 수 없으므로(DuplicateEntity) 직접 지정할 때는 창마다 다른 값을 주세요.
  종료 코드: 0 정상, 1 옵션 오류

[조작]
  좌클릭   땅 = 이동 / 몬스터 = 선택 + 쫓아가서 공격(스킬 포함)
  우클릭   누른 채 이동(MMO 방식), 몬스터 우클릭 = 공격
  1~9      스킬(영웅 스킬 순서). 단일 스킬은 선택한 대상(없으면 가장 가까운 몬스터), 범위 스킬은 대상 위치/커서 위치
  Q        기본 공격(대상 선택 후 접근-공격)    Tab/Shift+Tab 대상 전환    Esc 취소
  A        자동 전투(가까운 몬스터를 찾아 싸우고, 없으면 스폰 지점을 순회)
  F        시점 고정 on/off (끄면 방향키로 이동)    휠, +/- 확대/축소    Space 내 위치로
  F5 재접속   F1 도움말   L 로그 표시
  M        효과음 켜기/끄기(기본 켜짐)
  H        영웅 선택 창(↑↓/1~9/클릭으로 고르고 Enter: 그 영웅으로 다시 접속. 데모 모드는 그 영웅으로 다시 시작). 열려 있는 동안 다른 입력은 막힘
  B        보스 선택(가장 가까운 보스)    F12  스크린샷 저장(실행 파일 옆 screenshots 폴더)
  미니맵 클릭  그 위치로 이동    휠/+- 확대·축소는 부드럽게 보간

[화면]
  숲 바닥/나무/바위(Block.json 의 Circle=나무, Rect=바위), 영웅(전사/궁수/마법사 색과 무기), 몬스터(종류별 색),
  이름/체력바/시전바/상태 아이콘(몰려 있어도 겹치지 않게 위로 밀어 올리고, 시체의 표시는 자리가 없으면 숨김), 범위 스킬 예고(시전 중 붉은 원)와 폭발 링, 투사체(화살/화염구, 단일 대상은 추적),
  피해/회복/상태 문구, 무적 표시, 하단 체력/스킬바(쿨다운), 우상단 미니맵(스폰 지점/장애물/엔티티), 좌하단 로그
  (공격/스킬 거부, 위치 보정 사유가 표시되어 서버 규칙 점검에 유용).

[보스 표시]
  GameData 에 Boss/BossPhase JSON 이 있으면(없으면 보스 표시 없이 동작) 몬스터 템플릿 ID 로 보스를 알아보고 다음을 그립니다.
  상단 보스 HP 바(페이즈 눈금, 지연 체력, 페이즈 번호), 페이즈 진입 배너와 화면 흔들림, 격노 표시와 화면 가장자리 붉은 테두리,
  범위 스킬 예고(시전 시간만큼 차오르는 위험 지역)와 시전 바, 미니맵 보스 표시, 대상 프레임(상태 효과 남은 시간).
  패킷은 추가하지 않았습니다. 페이즈는 체력 비율로 추정합니다(서버 규칙과 같은 hp*100 <= HpBelowPercent*maxHp, 사망/부활 전까지 내려가지 않음).
  격노는 EnrageStatusId 상태 효과가 걸렸는지로 판단합니다. 데모 모드 보스(오우거)는 이 표시를 확인하기 위한 단순한 로컬 패턴이며 서버 로직과 다릅니다.

[그래픽]
  외부 이미지 없이 코드로 그린 음영 스프라이트입니다(사실적인 사진 수준이 아님). 영웅/몬스터 종류별 걷기/공격/시전/피격/사망 프레임,
  방향 반전, 그림자, 피격 번쩍임, 사망 시 시체. 스프라이트는 배율별로 캐시하고, 바닥은 큰 비트맵을 캐시하고, 카메라가 여유분의 절반을 넘으면 다음 비트맵을 16조각으로 나눠 프레임마다 한 조각씩 그려 끊김 없이 교체합니다.
  효과(타격 불꽃, 범위 폭발, 투사체 꼬리, 치유/상태 효과)는 시작 시각에서 계산하는 무상태 방식입니다. 프레임은 오프스크린 비트맵에 그린 뒤 복사합니다.
  스킬 모양: 서버 ESkillShape/ESkillTarget 을 그대로 따른다(SkillRow.Shape/IsAlly). 요청 값은 Core/Logic/SkillAim.cs 가 정한다:
  Single 은 대상 ID(적 스킬은 선택한 적 또는 가장 가까운 적, 아군 스킬은 선택한 아군 또는 자기 자신), Area 는 중심 좌표,
  Self 는 값 없음(시전자 위치), Cone/Line 은 방향점(시전자에서 1칸 이상). 키 입력과 자동 전투가 같은 규칙을 쓰며,
  자동 전투는 아군 스킬을 체력이 줄었을 때만, Self 스킬은 대상이 효과 반경 안일 때만 쓴다. 슬롯 아래 줄에 모양(범위/자신/부채꼴/직선/아군)과 사거리를 표시한다.
  스킬 연출: Core/Data/SkillFx.cs 의 표가 스킬 ID마다 양식(Blade 베기, Pierce 찌르기, Lance 창 찌르기/직선 관통, Whirl 회전, Arrow 화살, Volley 화살비, Fireball 불덩이,
  Blast 대폭발, Roar 함성, Quake 대지 강타, Poison 독, Heal 치유, Nova 충격파)과 두 가지 색, 지속 시간, 화면 흔들림을 정하고, 표에 없으면
  데이터(범위/투사체 속도)로 고릅니다. 그리기는 App/Render/SkillEffects.cs 가 맡습니다: 범위 폭발(바닥/공중), 베기, 투사체, 착탄 폭발,
  시전 중 몸으로 빨려드는 기(CastSkillId), 기절(머리 위 별)/보호막(방울)/지속 치유(십자) 오라. 양식이 Default 면 기본 연출을 씁니다.
  내 영웅이 쓴 스킬이거나 14 칸 안의 폭발만 화면을 흔듭니다. 스킬 연출을 한눈에 보려면 Snap.exe --fxsheet GameData out.png [진행도0~1].
  공격 몸동작: Core/Logic/AttackPose.cs 가 그림 한 장을 변형(밀기/기울이기/눌러 늘이기/뛰기)해서 만든다. WorldRenderer 가 발 밑을 축으로 적용한다.
  시전 중에는 시전 진행도에 맞춰 뒤로 젖히며 힘을 모으고(끝 15%는 웅크림, 떨림), 타격은 약 70ms 만에 내지른 뒤 잠깐 멈추고 천천히 돌아온다.
  스킬은 모은 자세에서 더 크게 튀어나오고, 원거리 발사는 반동으로 물러난다. 몬스터 스타일: 슬라임/3번은 Hop(웅크렸다 뛰어올라 덮치고 착지에서 퍼짐),
  7~10번과 보스는 Slam(치켜들었다 내려찍고 땅이 울림), 나머지는 Lunge(달려들어 묾). 보스는 더 멀리 나가고 더 느리며 피격 반동이 작다.
  피격한 쪽은 공격자 반대로 밀리며 눌린다(Entity.HitDirX/Y). 근접 기본 공격은 몸 앞에 휘두름 궤적(DrawSwipe)이 번쩍인다. 스킬의 동작은 시전/발사 알림에서
  시작하며 적중 알림(투사체는 한참 뒤)으로 다시 시작하지 않는다. 동작 모음은 Snap.exe --atksheet out.png [배율].
  영웅 무기 동작: 코드 그림(Creatures.cs)은 전사 검/궁수 시위/마법사 지팡이/창술사 창이 공격 5프레임으로 움직이고, 전사와 창술사는 칼날/창끝 잔상이 남는다.
  기본 공격 연출(App/Render/AttackEffects.cs): 무기마다 궤적이 다르다. 검 = 겹친 초승달 궤적, 창 = 대상 너머까지 꿰뚫는 찌르기 줄기+속도선+고리,
  몬스터 = 세 줄 할퀴기, 활 = 날아가는 화살, 지팡이 = 마력 구슬. 모든 타격에는 섬광, 별빛, 충격 고리, 맞은 방향으로 튀는 파편 선, 중력 불꽃이 붙고
  큰 피해(대상 최대 체력의 12% 이상)는 고리와 먼지가 한 겹 더 붙으며 내가 주면 화면이 살짝 흔들린다. 기본 공격과 창술사 스킬은 Snap.exe --atkfx GameData out.png basic|skill 로 한눈에 본다.
  이미지 에셋 영웅은 프레임 애니메이션이 없으므로 WorldRenderer 가 무기 궤적을 겹쳐 그려 대신한다: 전사 검광, 창기사 직선 찌르기, 궁수 시위 섬광, 마법사 구슬 폭발.
  영웅 4종의 프레임을 보려면 Snap.exe --herosheet out.png, 실제 장면에서 보려면 Snap.exe --heroscene GameData out.png.
  긴박감 연출(Core/Logic/Tension.cs 가 규칙, App/Render/TensionHud.cs 가 그리기): 체력 40% 이하에서 화면 가장자리가 붉게 맥동하고(5%에서 최대, 매우 위험하면
  "체력 위험!" 표시와 박동마다 화면 번쩍임), 심장 박동음이 60회/분에서 약 140회/분까지 빨라진다. 보스가 나타나거나 재출현/격노하면 붉은 경보 배너와 사이렌이 울린다.
  화면 밖 30여 칸 안에서 나를 향해 달려오는 몬스터(보스 제외)는 가장자리 화살표로 방향을 알리며 가까울수록 크고 진하다. 내가 7초 안에 연속으로 처치하면
  더블/트리플/쿼드라 킬과 5, 10, 20연속 칭호가 뜨고 이어지는 남은 시간 막대가 보인다(죽으면 끊김). 장면 확인: Snap.exe --tension GameData out.png.
  Preview/ 에 스프라이트 시트, 보스 장면, 창 캡처 예시가 있습니다.

[이미지 에셋]  (선택. 사진/3D 렌더 같은 그림을 쓰려면)
  실행 파일 옆 Assets\sprites.json 에 적은 종류만 이미지로 그리고, 나머지는 위의 코드로 그린 그림을 그대로 씁니다. 파일이 없으면 모두 코드 그림입니다.
    { "sprites": [ { "kind": "monster", "template": 9, "pattern": "demon/{0:000}.png", "angles": 18, "height": 4.6 } ] }
    kind monster|hero|tree, template = Monster/Hero 시트의 Id (tree 는 없음: Block 의 원 = 나무 전부를 이 이미지로 그림, height 는 반경 1 일 때 높이), pattern = sprites.json 기준 경로({0} = 각도(도)), angles = 방향 수(1..72),
    height = 그림 내용이 차지할 높이(단위, 0.5..12). 각도 = 번호*360/angles. 0도는 화면 아래(카메라 쪽)를 보는 모습이고
    커질수록 화면 오른쪽 -> 위 -> 왼쪽으로 돕니다. 이미지는 PNG/JPG/GIF/BMP(투명 배경은 PNG)이며 WebP 는 GDI+ 가 못 읽습니다.
  - 모든 방향의 불투명 영역을 합친 상자로 발 밑과 크기를 잡으므로 돌 때 흔들리지 않고, 클릭 판정/체력바 높이도 이 크기를 따릅니다.
  - 걷기 애니메이션(선택): 항목에 "walkFrames": 6, "walkPattern": "wolf/walk_{0:000}_{1:00}.png" 를 더하면({0}=각도, {1}=프레임 번호 0부터, 2..32장) 걸을 때 걸은 거리에 맞춰 프레임을 넘깁니다.
    서 있을 땐 pattern 그림을 씁니다. 걷기 프레임이 없는 종류는 걸을 땐 튀고 설 땐 숨 쉬듯 눌러서 흉내 내며, 피격 번쩍임/시체는 둘 다 같은 방식으로 처리합니다.
    걷기 이미지는 기본 이미지와 같은 크기여야 하고, 크기/발 밑 기준은 걷기 프레임까지 합친 범위로 잡습니다.
  - 잘못된 항목(파일 없음, 크기 불일치, 값 범위 등)은 그 항목만 건너뛰고 표준 오류에 "assets: ..." 로 적습니다.
  - 늑대(몬스터 3): Assets\wolf\ 18방향 + 걷기 6프레임(wolf\walk_*). 외부 이미지가 아니라 tools\render_wolf.py 로 직접 렌더링한 것이라 색/비율을 바꿔 다시 만들 수 있습니다.
  - 샘플: Assets\demon\ 의 악마를 오우거(몬스터 9)에, Assets\slime\ 의 점액 괴물을 슬라임(몬스터 1)에, Assets\mushroom\ 의 버섯 전사를 고블린(몬스터 2)에, Assets\warrior\, archer\, mage\, lancer\ 의 오리지널 영웅(전사/궁수/마법사/창기사(영웅 4), 걷기 8프레임 포함)을 영웅 1/2/3/4에, Assets\spruce\ 의 가문비나무를 나무에 연결해 두었습니다(각 18방향, 20도 간격).
    원본은 Pixabay 무료 3D 모델 페이지의 회전 렌더이고, WebP 였던 프레임은 PNG 로 바꾸고 400x400 으로 줄였습니다. 쓰기 전에 Pixabay 콘텐츠 라이선스를 확인하세요. 연결을 빼려면 sprites.json 의 항목을 지우면 됩니다.
  - 샘플 두 개와 코드로 그린 다른 몬스터는 화풍이 다릅니다. 통일하려면 같은 화풍의 에셋을 전 종류에 맞춰 넣어야 합니다.

[설정 JSON 키]  Host, Port, CharId, ZoneId, HeroId, DataDir, Auto, PixelsPerUnit, WindowWidth, WindowHeight,
                ConnectTimeoutMs, MoveIntervalMs  (--dump-config <file> 로 기본값 템플릿 저장)

[서버와의 약속]
  - 내 엔티티 ID = 로그인한 CharId (BotClient 와 같음). 몬스터 ID 는 최상위 비트가 켜진 값.
  - 이동 요청에는 로컬에서 보간한 내 위치를 실어 보냅니다. 서버 허용 오차를 넘으면 ZcCorrectPosNotify 가 오고
    로그에 "위치 보정: PositionMismatch" 로 표시됩니다(지연이 큰 환경에서 늘어날 수 있음).
  - 투사체는 서버에 엔티티가 없으므로 뷰어가 발사/착탄 패킷으로 연출만 합니다(Single 은 대상 추적).
  - 사망 시 상태 효과는 해제 알림 없이 사라지므로 ZcDeathNotify 에서 비웁니다.
  - 시야(AOI) 밖은 서버가 보내지 않으므로 화면에는 내 캐릭터 주변만 나옵니다.
  - 영웅 ID 0(서버 기본 영웅)이면 스폰 알림의 templateId 로 영웅을 확인합니다.

[효과음]
  외부 소리 파일 없이 Core/Audio/Sfx.cs 가 합성합니다(쿵/퍽, 베기, 휘익, 폭발, 치유, 사망, 부활, 알림). 연출(ViewEffect)이 생길 때마다 SfxRules 가 소리와 크기를 정합니다:
  내가 맞거나 때리는 소리는 거리와 무관, 남들끼리의 타격은 11 단위 안이면 또렷하고 26 단위까지 작아지며 그 밖은 조용합니다. 큰 피해(최대 체력의 12% 이상)는 묵직한 소리입니다.
  WinForms 는 SoundPlayer 로 WAV 를 틀고(종류마다 3개를 돌려 겹쳐 울림, 같은 종류는 45ms 안에 다시 안 울림), 장치가 없으면 조용히 꺼집니다.
  Unity 에서는 SfxSynth.Make 의 float 샘플로 AudioClip 을 만들면 됩니다. 단순한 자리표시 소리라 나중에 파일로 바꿀 때는 SfxKind 별로 교체하세요.

[Unity 클라이언트에서 재사용]
  Core 폴더를 Assets 아래에 그대로 복사하고(.csproj 제외) 다음 순서로 씁니다. (netstandard2.0 / C# 7.3 문법만 사용)
    var data = GameData.Load(path, zoneId);                // 폴더에서 읽기
    var data = GameData.Load(table => Resources.Load<TextAsset>(table)?.text, zoneId);   // 파일 없이(Resources/어드레서블). 표 이름을 받아 JSON 텍스트(없으면 null)를 돌려준다
    var session = new ViewerSession(cfg, data);  session.Start(nowMs);
    void Update() { session.Update(nowMs); pilot.Update(nowMs); ... }     // nowMs = Time.realtimeSinceStartup * 1000
    session.World.Entities / Effects / Log 를 읽어 GameObject, 파티클, 텍스트로 표현
    입력 -> session.MoveTo / Attack / UseSkill
  App/Render 의 그리기 코드만 Unity 용으로 바꾸면 되고, 패킷/월드/AI 는 그대로입니다.
  ZoneCommon/ZonePacket.h 가 바뀌면 Core/Protocol/Packets.cs 를 같이 고치고, Tests 의 cpp_packet_sizes.txt 를
  새 sizeof 값으로 갱신하면 크기 불일치를 테스트가 잡아 줍니다.

[검증]
  Tests: 패킷 27종 크기를 C++ sizeof 와 대조, 프레이머(분할/병합/위반), 파서, 월드 모델(이동 보간/피해/사망/부활/
  스킬/투사체/상태), 데이터 로더, 설정, 가짜 Gateway 와 TCP 왕복(로그인 실패/입장 실패/존 소실/연결 끊김/프로토콜 위반),
  자동 전투(접근 -> 스킬 -> 기본 공격 -> 순회), 데모 시뮬레이션 40초 불변식,
  보스(데이터/페이즈 추정/지연 체력/흔들림/걷기 판정/데모 보스) - 모두 290개 점검.
  샌드박스(mono)에서 전부 통과했고, WinForms 앱도 가상 디스플레이에서 실행해 화면을 확인했습니다.
  mono 소프트웨어 렌더링 기준 프레임당 약 9ms(이전 8ms). 스프라이트 모양/애니메이션은 mono 에서 맞췄습니다.
  아직 확인하지 못한 것: Visual Studio/dotnet 으로의 빌드, Windows(GDI+) 실행과 성능, 실제 Gateway/ZoneServer 와의 접속(보스 표시 포함).

[숲 배경]
  GroundRenderer  바탕은 화면 1/6 해상도로 노이즈를 계산해 부드럽게 늘려 그립니다(풀 명암, 이끼, 낙엽층, 흙길, 나무 그늘, 햇빛 얼룩,
                  월드 가장자리 어두워짐). 그 위에 고사리, 덤불(열매), 버섯, 쓰러진 통나무(이끼/나이테), 돌, 낙엽, 풀포기, 꽃을 격자 칸마다
                  해시로 놓습니다. 전부 월드 좌표로 결정되므로 카메라가 움직여도 무늬가 월드에 붙어 있습니다.
  Atmosphere      나뭇잎 사이로 비스듬히 내리는 빛줄기 5개(천천히 밝아졌다 어두워짐)와 떨어지는 낙엽/꽃가루. 시간의 함수라 상태가 없습니다.
  확인            Snap.exe --selftest 가 조각 렌더와 한 번에 렌더가 같은지(이음매 없음)를 검사합니다.

[영웅 선택 뒷배경]
  Assets/backdrop.jpg (.jpeg/.png) 가 있으면 영웅 선택 화면(H) 뒤에 화면을 덮게(비율 유지, 넘치는 부분은 잘라서) 깔립니다. 없으면 예전처럼 어둡게만 칠합니다.
  해상도는 화면(보통 1280x800 이상)보다 커야 선명합니다. Backdrop.cs 가 화면 크기가 바뀔 때만 다시 늘려 둡니다.

[오리지널 영웅]
  Assets\warrior, archer, mage, lancer  스타일라이즈드 3D 느낌(셀 셰이딩 + 외곽선)으로 직접 렌더한 영웅. 각 18방향(정지) + 방향마다 걷기 8프레임. 외부 모델/텍스처를 쓰지 않았습니다.
  걷기: 8프레임 한 주기에 두 발이 벌어진 순간(접지)과 다리가 몸 아래를 지나는 순간이 모두 들어 있고, 딛은 발이 늘 바닥에 닿도록 엉덩이 높이가 정해져 접지 때 가장 낮고 통과 때 가장 높다. 몸통 위쪽은 딛은 발 쪽으로 조금 쏠린다. 딛은 발은 땅에서 일정한 속도로 뒤로 밀리고(발 위치를 먼저 정하고 다리 각도를 역산), 든 발은 발꿈치부터 떼어 올린다. 프레임 한 장은 스프라이트 20픽셀(HeroStridePx × asset.Scale)만큼 걸을 때마다 넘어가 발이 땅에서 미끄러지지 않는다.
  무기 분리(전사 검, 궁수 활, 마법사 지팡이, 창기사 창): 몸 그림에는 무기가 없고 무기는 따로 그린다. 네 영웅 모두 공격 자세 5장(attackFrames 5, poses 6)을 3D로 팔과 무기를 함께 렌더한다(전사 위로 들기→내려베기→찌름, 궁수 시위 당김→놓음, 마법사 지팡이 올림→내지름). HeroWeapons 는 무기를 그리지 않고 검 궤적/화살 비행/구슬 빛/불꽃 같은 효과만 앵커를 따라 얹는다(SpriteFx). 무기 그림이 없는 에셋은 예전 코드 그리기로 대체된다.
  창 분리(sprites.json): "attackFrames": N, "attackPattern": "lancer/atk_{0:000}_{1}.png"({0}=각도, {1}=공격 자세 0..N-1) 와
    "weapon": { "poses": N+1, "pad": 160, "backPattern": ..._{0}_{1:000}_b.png, "frontPattern": ..._f.png, "anchors": "lancer/anchors.json" } ({0}=자세, {1}=각도).
    자세 0 은 대기/걷기, 1.. 은 공격 자세. 창 그림은 몸 그림 둘레에 pad 픽셀 여백을 둔 같은 카메라 그림이라 몸 그림 (-pad,-pad) 에 맞춰 찍으면 겹친다.
    뷰어는 창 뒤쪽 -> 몸 -> 창 앞쪽 순으로 찍고, 걸을 때는 walkGrip 으로 창을 프레임별 손 위치에 옮기며, 찌르기 잔광/불꽃/기모으기 빛은 창끝 앵커를 따라간다.
  tools/render_heroes.py  <warrior|archer|mage|lancer> <출력폴더> --walk 8   (numpy, Pillow 필요) 형태/색을 고쳐 다시 렌더할 수 있습니다. 네 영웅 모두 몸(무기 없음)과 무기를 따로 렌더하고 손/무기끝/무기꼬리 위치를 anchors.json 으로 내보냅니다. lancer 는 몸(창 없음: ###.png, walk_###_##.png, 공격 자세 atk_###_N.png)과 창(자세별 몸 뒤 spear_P_###_b.png / 몸 앞 spear_P_###_f.png)을 따로 렌더하고, 손/창끝/창꼬리 위치를 anchors.json 으로 내보냅니다.
