# TC Games 자산 도입 안내

TC Games판 카트라이더 클라이언트에서 트랙·카트·스키드마크·BGM을 가져와 이 포트에
연결한 기록이다. **경로와 절차보다 시행착오 부분이 중요하다** — 같은 함정을 다시
밟지 않도록 원인과 해결을 함께 적었다.

## 0. 대전제: TC 자산은 복구 근거가 아니다

`../KartriderDemoPhysics/Reference/TCGames/README.md`와
`analysis/TC_GAMES_EVOLUTION_NOTES.md`가 못박아 둔 원칙이다. 2004년 데모 복구의
근거는 데모 EXE와 데모 아카이브뿐이고, TC판 수치·에셋은 그 대체물이 될 수 없다.

그래서 TC 자산은 **세 겹으로 분리**해 두었다. 셋 중 하나라도 빠뜨리면 나중에
"이 값이 원본 근거인가"를 되짚을 수 없게 된다.

| 겹 | 방법 |
|---|---|
| 디스크 | 전용 하위 폴더 `TCGames/` |
| 데이터 | `TrackSpec.Source` / `KartSpec.Source` = `KartAssetSource.TCGames` |
| 화면 | `T`/`K` 목록에서 행 끝에 `[TC]` 표시 |

생성된 코스 테이블도 데모의 `kart_course_data.c`와 섞지 않고
`kart_course_data_tcgames.c`로 따로 뽑는다.

## 1. 원본 위치

```
클라이언트          C:\Program Files (x86)\TCGAME\TCGameApps\kart\Data
추출 도구           ../KartriderDemoPhysics/DeveloperTools/AssetImporters/rho-safe-index
메쉬 변환           ../KartriderDemoPhysics/DeveloperTools/AssetImporters/track-mesh-exporter
사전 추출본         ../KartriderDemoPhysics/Reference/TCGames/
아카이브 인덱스     ../KartriderDemoPhysics/Reference/TCGames/Index/{sound,track}/*.json
```

아카이브별 내용:

| 아카이브 | 내용 |
|---|---|
| `track_<트랙>.rho` | `track.1s`, `skydome.1s`, `xt_minimap/bigmap/trackCard/trackThumb.png` — **텍스처는 없다** |
| `theme_<테마>.rho` | 그 테마 트랙들이 공유하는 텍스처 (`texture/` 하위가 전부) |
| `theme_common.rho` | **모든 테마가 공유하는 텍스처 1,079개.** 부스터존·점프존처럼 테마에 속하지 않는 것이 여기 있다 (§3.6) |
| `sound_bgm_<테마>.rho` | 테마 BGM 원곡 |
| `sound_bgm_<테마>2.rho` | **리믹스** (`<곡>_re.ogg`) — 별도 아카이브다 |
| `stuff.rho` | 스키드마크, 오라, 풍선 등 꾸미기 |
| `DataPack2` (rho5) | 카트 1,451종 (`param.xml`, `model.1s`) |

## 2. 이 포트에서의 도착지

```
Assets/_Project/Art/Tracks/TCGames/<트랙>/track_<트랙>.ktrk
Assets/_Project/Art/Tracks/TCGames/<트랙>/<트랙>_minimap.png
Assets/_Project/Art/Tracks/TCGames/<트랙>/Textures/*.png + textures.json
Assets/_Project/Art/Karts/Models/TCGames/<카트>.ktrk
Assets/_Project/Art/Karts/Skins/TCGames/<카트>.png
Assets/_Project/Art/Effects/TCGames/rainbow.png
Assets/_Project/Audio/Music/TCGames/*.ogg
```

데모 자산은 각 상위 폴더에 그대로 둔다. **빌더는 데모를 먼저 찾고 없을 때만
`TCGames/`를 본다** — 같은 이름이면 데모가 이긴다.

## 3. 함정과 해결

### 3.1 빌더를 하나만 고치면 "no KTRK mesh"

`TCGames/` 하위 경로를 아는 곳이 **세 군데**다. 하나라도 빠지면 증상이 제각각으로
나온다.

| 빌더 | 함수 | 빠뜨렸을 때 |
|---|---|---|
| `TrackCatalogBuilder` | `ArtFolder(id)` | 카탈로그에 트랙이 안 생김 |
| `TrackSceneBuilder` | `KtrkPath(id)` | `Skipped: <트랙> (no KTRK mesh)` |
| `KartCatalogBuilder` | 모델 로드부 | 카트가 `K` 목록에 안 뜸 |

실제로 카탈로그만 고치고 씬 빌더를 빠뜨려서 한 번 헤맸다.

### 3.2 DDS를 그대로 넣으면 회색이 된다

Unity 텍스처 임포터는 **DDS를 읽지 않는다.** 게다가 이 자산의 상당수가 DXT3인데
Unity의 압축 포맷은 DXT1/DXT5만 덮는다. 테마 아카이브를 통째로 복사하면 `.dds`가
따라 들어와 전부 회색으로 깨진다.

데모 파이프라인은 `Tools/AssetPipeline/import_track_textures.py`가 이미 PNG로
변환해서 넣고 있었다. 같은 디코더를 쓰면 된다:

```python
import importlib.util
spec = importlib.util.spec_from_file_location(
    'itt', 'Tools/AssetPipeline/import_track_textures.py')
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
decode_dds, _ = m.load_decoders('../KartriderDemoPhysics')

width, height, pixels, has_alpha = decode_dds(open(path, 'rb').read())
m.write_png(png_path, width, height, pixels)
```

`decode_dds`는 **4개**를 돌려준다(`has_alpha` 포함). 3개로 언패킹하면 226개가 전부
`ValueError`로 실패한다. 실제로 이걸로 한 번 막혔다.

이번에 223개를 변환했다 (northeu 178 PNG, castle 227 PNG, 남은 DDS 0).

### 3.3 `textures.json`이 없으면 전부 불투명

임포터는 텍스처 폴더의 `textures.json`을 읽어 `transparent` 플래그가 붙은 것만
알파 컷아웃으로 만든다. 매니페스트가 없으면 **모든 머티리얼이 불투명**이 되고,
투명해야 할 면이 노란 판때기로 보인다.

DDS를 손으로 변환하면 이 파일이 안 생기므로 직접 써야 한다. 알파 유무는 PNG의
알파 채널을 스캔해 판정하는 게 가장 확실하다.

```json
{
  "track": "castle_R01", "theme": "castle",
  "referenced": 128, "written": 98,
  "unmatched": ["ad_board_a", "..."],
  "textures": [ { "name": "road_15_j", "width": 256, "height": 256, "transparent": false } ]
}
```

이번 결과:

| 트랙 | 참조 | 매칭 | 투명 | 미매칭 |
|---|---|---|---|---|
| northeu_R01 | 116 | 108 | 37 | 8 |
| castle_R01 | 128 | 98 | 8 | 30 |

northeu의 108/37은 `theme_common.rho`에서 네 개를 더 채운 뒤의 값이다(§3.6).
castle의 30개 중 19개도 같은 아카이브에 있지만 아직 넣지 않았다.

### 3.4 `transparency`는 마커가 아니라 실제 이미지다

텍스처 이름이 `transparency`인 면이 있다. castle에는 파일이 실제로 있고 열어보면
**32×32 RGBA, 전 픽셀 알파 0**이다. northeu는 이름만 참조하고 파일이 없다.

임포터가 이 이름을 특별 취급해 전용 투명 머티리얼을 준다(`InvisibleTexture`).
파일 부재로 판정하지 않는 이유는, 단순히 없는 텍스처(광고판)는 "그려야 하는데
이미지만 없는" 다른 상황이기 때문이다.

**URP 함정**: 알파가 0이어도 Lit 셰이더는 스페큘러와 환경 반사를 계속 계산한다.
그래서 투명한 면이 하이라이트를 받아 거울처럼 번들거린다. 끄려면 **float과 키워드를
둘 다** 세팅해야 한다 — 패스는 키워드를 읽고 인스펙터는 float을 읽는다.

```csharp
blank.SetFloat("_SpecularHighlights", 0f);
blank.SetFloat("_EnvironmentReflections", 0f);
blank.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
blank.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
```

### 3.5 폴백 머티리얼은 파이프라인 기본값을 쓰지 말 것

임포터가 텍스처를 못 붙인 면에 `GraphicsSettings.currentRenderPipeline.defaultMaterial`
(인스펙터의 `Lit`)을 물리고 있었다. 그건 **에디터 전역 공유 에셋**이라 색을 바꾸면
다른 모든 것에 영향을 준다. 지금은 임포터가 `Untextured`라는 자기 머티리얼을 만든다
(흰색, smoothness 0, 양면).

### 3.6 미매칭의 절반은 `theme_common.rho`에 있었다

> 이 절은 처음에 "어느 아카이브에도 없다"로 써 두었는데 **틀렸다.** 뒤진 세 곳
> (`Index/track/` 309개 인덱스, `analysis/reports/rho5/`, `theme_<테마>.rho`)에
> `theme_common.rho`가 빠져 있었다. 그 아카이브에 텍스처 1,079개가 있고, 여기서
> 미매칭 42개 중 **25개**가 나온다.

부스터존·점프존이 대표적이다. 어느 테마에도 속하지 않는 공용 오브젝트라 테마
아카이브에는 있을 수가 없다. northeu_R01이 참조하던 네 개가 전부 여기 있었다:

| 이름 | 형식 | 무엇 |
|---|---|---|
| `점프존발판` | PNG | 빨간 갈매기 점프대 판 |
| `점프존1` | PNG | 분홍 `J` 게이트 간판 |
| `부스터존발판` | DDS | 초록 갈매기 부스터 판 |
| `부스터존` | PNG | 초록 `B` 게이트 간판 |

northeu의 테마 아카이브에는 `점프존2`/`점프존발판2`라는 **번호가 다른 자매 파일**만
들어 있었다. 그래서 "테마에 있는데 이름이 안 맞는다"로 보여 더 찾지 않았던 것이다.

남은 17개(`ad_board_*`, `ad_fence_*`, `ad_start`, `house_badac01_t`, `house_toproad02_t`,
`no_sideset_c`, `nort_animal02`, `road_09_j`, `t02`, `t13`)는 여전히
어디에도 없다. 데모의 `ad_board_*`(광고판)와 같은 경우이고, 데모 트랙 매니페스트에도
`unmatched`로 남아 있다.

> 미매칭을 만나면 **`theme_common.rho`를 먼저 인덱싱할 것.** 테마 이름으로 좁히면
> 놓친다.

### 3.7 한글 텍스처 이름은 깨지지 않는다

콘솔에서 깨져 보여도 데이터는 멀쩡하다. KTRK의 텍스처 이름 116개 중 17개가
비ASCII인데 전부 유효한 UTF-8이고, 디스크의 PNG 20개와 정상 매칭된다. 터미널
코드페이지 문제일 뿐이니 **인코딩 변환을 시도하지 말 것**.

### 3.8 간판 상하반전은 트랙별 목록이다

`KtrkImporter.UpsideDownSignTracks`에 트랙명을 넣으면 sign/`ad_` 텍스처를 쓰는
비충돌 메쉬의 UV를 아일랜드 단위로 v 미러링한다. 정점은 건드리지 않으므로 물리에
닿지 않는다.

ice_R01은 13개 트랙 3,287면을 세어 확인한 것이고, TC 두 트랙은 **화면 관찰**로
추가했다. 근거 등급이 다르므로 주석에 구분해 두었다.

> **머티리얼·UV를 바꾸면 `KtrkImporter.Version`을 올려야 한다.** 결과가 아티팩트에
> 구워지므로, 버전을 안 올리면 기존 임포트가 그대로 남는다. 이번 작업에서 16 → 19까지
> 올라갔다 (17 간판, 18 투명 머티리얼, 19 흰색 폴백).

## 4. 코스(체크포인트)

TC판 트랙은 **데모와 완전히 같은 코스 포맷**을 쓴다. `the::ToRoad` 객체와 `track`
객체의 `course` 태그(`road` 자식에 `start`/`end` 속성)가 그대로 있어서
`derive_course_gates.py`가 수정 없이 통한다.

### 4.1 생성기 경로 상수가 낡았다

`EXTRACTED`/`MESHES`/`OUTPUT`이 저장소에 없는 `analysis/track-assets/`와 `src/`를
가리킨다. 실제 자산은 `Assets/Tracks/{extracted,meshes}`에 있다. **스크립트를 고치지
말고 런타임에 덮어쓸 것** — 다른 사람의 실행에 영향을 주지 않는다.

```python
dcg.EXTRACTED = root + '/Assets/Tracks/extracted'
dcg.MESHES    = root + '/Assets/Tracks/meshes'
dcg.OUTPUT    = root + '/temp/kart_course_data.regen.c'
dcg.main()
```

### 4.2 재생성 전에 반드시 동일성 검증

`kart_course_data.c`는 데모 13개 트랙 체크포인트 전부의 출처다(660KB). 잘못
재생성하면 모든 트랙의 랩 판정이 함께 깨진다. **먼저 13개로만 재생성해 기존 파일과
`diff`가 동일한지 확인한 뒤에** 손댈 것. 이번에 확인했고 바이트 단위로 동일했다.

TC 트랙은 별도 입력 트리에 staging해서 **별도 파일**로 뽑는다:

```
Reference/TCGames/CourseSource/extracted/track_<트랙>/track.1s
Reference/TCGames/CourseSource/meshes/track_<트랙>.ktrk
        ↓
Scripts/Runtime/Gameplay/kart_course_data_tcgames.c
```

`TrackCourseBuilder.SourcePaths`가 두 파일을 순서대로 읽고, 같은 이름이면 먼저 읽은
데모 쪽이 이긴다.

### 4.3 체크포인트가 공중에 뜨는 문제 — 기준면 불일치

가장 헷갈렸던 버그다. 생성기는 게이트를 `ground_z`만큼 내려서 굽는데:

```python
Space(minimum, maximum, start_quad[2][0] if start_quad else minimum[2])
```

TC 트랙은 **페인트된 start 스트라이프가 없어서** `minimum[2]`로 폴백한다. 그런데
Unity의 `KartTrackStart.SceneGroundZ`는 `StartKind != None`이면 `StartLine.Z`를 쓴다.
`StartKind`를 `AxisClear`로 올려놨더니 두 기준면이 어긋나 게이트가 떴다:

| 트랙 | 생성기 | Unity | 오차 |
|---|---|---|---|
| northeu_R01 | −49.35 | 383.59 | **432.94** |
| castle_R01 | 43.81 | 138.93 | **95.11** |

**해결**: `StartKind = None`. 스트라이프 쿼드가 없다는 사실 그대로이고, `SceneGroundZ`가
`Minimum.Z`를 반환해 생성기의 폴백과 같은 규칙이 된다.

> `StartKind`는 출발 쿼드의 증거 등급인 동시에 **게이트가 구워진 기준면까지 결정**한다.
> 코드만 봐서는 드러나지 않으니 주의.

TC 트랙에는 데모식 출발선 스트라이프가 없다 — `start` 텍스처를 쓰는 메쉬는 전부
수직 배너(`ad_start`, `no_start_ob_1`)다. 출발 위치는 **코스의 start 게이트**에서
나온다(`KartCourse.StartPose`). 코스가 로드되면 `StartLine`은 씬을 내리는 데만 쓰인다.

## 5. 3D 미니맵

`the::ToMinimap` 페이로드(origin_x, origin_y, scale, width, height)가 `track.1s`에
들어 있다. 문서에는 메모리 오프셋만 있고 파일 레이아웃이 없어서, **이미 알려진 값을
바이트로 만들어 역으로 찾았다**:

```
aa47 8e034011 3805 0000000000 | origin_x origin_y scale | width height tail
     ^클래스 스탬프  ^2         ^5           ^마커+13
```

리더를 **데모 4개 트랙에 먼저 돌려 기존 표와 float 단위로 일치하는지 확인**한 뒤
TC 트랙에 적용했다. 결과는 `KartDemoData.MinimapTable`에 있다.

```
northeu_R01  origin (823.046692, 913.042908)  scale 0.13309437   256x256
castle_R01   origin (696.287170, 945.925293)  scale 0.246547982  256x256
```

## 6. 카트

**물리 파라미터는 가져올 수 없다.** TC판 `param.xml`은 데모와 다른 물리 모델이다:

| 필드 | 데모 `<Dynamics>` | TC `<BodyParam>` |
|---|---|---|
| ForwardAccelForce | 3300 | 147 |
| DragFactor | +0.725 | −0.0768 |
| SteerConstraint | 28 | 3 |
| Mass, AirFriction, Grip/Brake, DriftTrigger | 있음 | **없음** |

이름만 같고 단위·수식이 다르며, 복구 엔진이 요구하는 16개 중 6개만 존재한다. 넣으면
그 카트의 핸들링이 아니라 망가진 숫자가 된다.

그래서 파라곤 6종은 **차체 지오메트리만** 자기 것이고(각 `model.mesh.json` 바운드에서
half width/length, model height 측정) 동역학은 데모의 `Standard()`를 쓴다.

### 6.0 세대는 `itemTable.kml`의 `grade`에 있다

카트 자기 파일에는 **세대 정보가 없다.** `burst6`이 SR이고 `burst10`이 Z7인데
번호는 계열마다 어긋나서 이름으로는 못 맞춘다.

답은 `DataPack1`의 `etc_/itemTable.kml`(1.18MB, UTF-16)이다. 카트 1,631행마다
`grade` 속성이 있고 그게 세대다:

| grade | 세대 |
|---|---|
| 1–5 | 클래식 1~5세대 |
| **6** | **SR** |
| **7** | **Z7** |
| **8** | **HT** |
| **9** | **뉴** |
| **10 / 11 / 12** | **9th / X / V1** |

10·11·12는 파라곤이 못박아 준다 — `paragon_9th`가 10, `paragonX`가 11, `paragonV1`이
12다.

`param.xml`의 `EngineSound`로 교차 검증했다. grade 6 카트 22종이 **예외 없이**
`cotton6`을 쓰고, 7/8/9도 각각 `cotton7`/`cotton8`/`cotton85`로 줄이 맞는다.

> `EngineSound` 하나만 보면 안 된다. 값의 최다수인 `lodi4`(360종)는 HT·뉴 이후를
> 뭉뚱그린 범용이라 세대를 못 가른다. **`grade`가 1차 근거, `EngineSound`가 검증이다.**

### 6.0.1 엔진 폴더 이름 대조

우리 프리셋 이름(classic/sr/z7/ht/new/jiu/x/v1)은 위키 쪽 통칭이고, 클라이언트
내부 이름은 다르다. `sound_fx_kart.rho`의 `engine_*` 폴더를 추출해 **실제 오디오를
디코딩 비교**해서 확정했다:

| 프리셋 | TC 폴더 | 비고 |
|---|---|---|
| Classic | **없음** | 2004 엔진이라 TC판에 없다 |
| Sr / Z7 / Ht / New | `engine_cotton6` / `cotton7` / `cotton8` / `cotton85` | |
| Jiu / X / V1 | `engine_9th` / `engine_X` / `engine_V1` | |

> **함정**: `lodi4`의 motor.ogg는 `cotton85`(New)와 **바이트 단위로 같다.** booster까지
> 봐야 갈린다 — `lodi4`의 booster는 `cotton6`(SR) 것이다. motor만 비교하면 New 집계가
> 통째로 틀린다.

### 6.0.2 아틀라스가 규약을 안 지키는 카트가 있다

세대·계열별로 카트를 하나씩 고를 때 **아이템 id가 가장 낮은 것**을 기본으로 하되,
아틀라스를 먼저 검사해야 한다. 기준은 `1.png`에 **45×20 파랑 블록이 정확히 900텍셀
한 덩어리**로 있는지다.

marathon10(grade 7 마라톤 중 id 최소)은 파랑이 **322개가 시트 전체에 흩뿌려져** 있다.
`KartSkinPainter`는 파랑 텍셀마다 판을 찍으므로 **NEXON 판때기로 뒤덮인다.** 그래서
Z7 마라톤은 marathon11로 갔다. 같은 이유로 걸러진 것: saber11, saber12, cotton13,
saber_z7gt, solid9, solid11, saber13_pc.

### 6.0.3 뉴 세대는 알파가 아니라 시안으로 도색 영역을 키잉한다

뉴 5종을 처음 넣었을 때 **전부 하늘색으로 덮여 나왔다.** 원인은 아틀라스가
도색 영역을 표시하는 방법이 세대별로 다르기 때문이다.

- 데모~HT: 도색 영역은 **알파**다. 투명한 텍셀에 `base`가 비쳐 보인다.
- 뉴 이후: 아틀라스가 거의 전부 불투명이고(cotton19는 알파 255가 94%),
  같은 영역을 **시안 단색으로 칠해** 표시한다.

데모 규약에서 시안은 레이싱 넘버 앵커라 페인터가 그대로 뒀고, 그게 화면까지 갔다.

**가르는 기준은 8비트 정확도다.** 이게 깔끔하게 갈린다:

| | 시안(565 매치) | 그중 정확히 `(0,255,255)` |
|---|---|---|
| 데모 26종 앵커 | 1~2 | **전부** |
| 뉴 5종 도색 영역 | 2,866~21,598 | **전부** (17390/17390 등) |
| 파라곤 네온 | 284~503 | **0개** |
| burst10·cotton15 네온 | 15·87 | **0개** |

네온은 안티에일리어싱이 들어가 있어서 **565로 반올림해야만** 키에 걸린다. 그래서
`ToRgb565` 매치만으로는 절대 구분이 안 된다.

`KartSkinPainter`의 규칙은 이제 세 갈래다:

1. 정확한 8비트 시안 + 자기 숫자 상자(10×17) 안에 혼자 → **넘버 앵커**
2. 정확한 8비트 시안 + 뭉쳐 있음 → **도색 영역** (`KeyPaintAreas`가 알파 0으로
   내려 `PaintBody`가 `base`로 블렌드한다)
3. 정확하지 않음 → **원화**, 손대지 않는다

> `KeyPaintAreas`는 **알파만** 쓴다. RGB의 시안을 같이 지우면, 두 텍셀짜리 영역에서
> 첫 번째를 지운 뒤 두 번째가 "혼자"로 보여 앵커로 둔갑한다.

### 6.0.4 뉴 세대는 데모와 두 역할이 **뒤바뀌어** 있다

여기서 두 번 틀렸고, 둘 다 화면에 그대로 드러났다.

| | 데모 ~ HT | **뉴 이후** |
|---|---|---|
| 정확한 시안 단색 | (없음. 넘버 앵커뿐) | **드라이버 색이 들어갈 자리** |
| 알파로 뚫린 자리 | **드라이버 색이 들어갈 자리** | **흰색 고정 차체** |

1. 시안을 그냥 두었더니 → 5종 전부 **하늘색으로 도배**
2. 시안을 도색으로 인식시켰지만 알파 쪽은 데모식으로 두었더니 → 흰 차체여야 할
   곳까지 **테마색으로 도배**

흰색은 지어낸 값이 아니다. 그 텍셀들이 아틀라스에 **이미 흰색으로 들어 있다** —
데모 26종도 도색 영역을 `(255,255,255)` 알파 0으로 갖고 있고(알파가 키라서 색은
무시된다), 뉴 아틀라스도 같은 흰색을 갖고 있다. 다만 뉴에서는 그게 **화면에 나가는
값**이다.

`KartSkinPainter`는 아틀라스에 **앵커가 아닌 정확한 시안이 하나라도 있으면** 뉴
규약으로 읽는다. 있으면 시안에 `base`를 박고 나머지는 흰색 위에 합성하고, 없으면
데모 그대로 `base` 위에 합성한다. 데모 26종과 SR~HT·파라곤은 정확한 시안이 앵커
1~2개뿐이라 판정에 걸리지 않는다.

### 6.1 스킨은 `1.png`다 — `0.png`가 아니라

카트 KTRK는 **텍스처 이름을 하나도 싣지 않는다**(`model.mesh.json`의 `Textures`가
빈 배열이다). 트랙처럼 면마다 텍스처를 찾는 게 아니라, 카트는 아틀라스 한 장을
`KartSpecAsset.SkinTemplate`에 물려 `KartView`가 전 렌더러에 같은 머티리얼을 씌운다.
그러니 **어느 PNG가 몸통인지만 정하면 된다.**

카트 폴더에는 `0.png`와 `1.png`가 있다. 정답은 `1.png`이고, 근거는 그림이 아니라
키 텍셀이다:

| | `0.png` | `1.png` |
|---|---|---|
| 알파 255인 텍셀 | **0개** (6종 전부) | 대부분 |
| 45×20 파랑 번호판 키 | 없음 | **정확히 900개** (6종 전부) |

`0.png`는 6종 모두 전 픽셀 알파 0이라 가져오지 않는다.

`1.png`가 900개의 파랑 키를 그대로 갖고 있다는 것은, TC판 아틀라스가 데모와 **같은
템플릿 규약**(마젠타 필러 / 시안 앵커 / 파랑 판 코너)을 쓴다는 뜻이다. 그래서
`KartSkinPainter`가 손대지 않고 통한다. 알파가 255인 곳은 `Blend(base, c, 255) == c`라
원화가 그대로 나오고, paragonX처럼 알파가 낮은 곳이 있는 카트는 데모 카트와 똑같이
드라이버 색을 먹는다.

목적지는 모델과 같은 규칙으로 나눈다:

```
Assets/_Project/Art/Karts/Skins/TCGames/<카트>.png
```

`KartCatalogBuilder`는 모델과 마찬가지로 **평평한 데모 폴더를 먼저 보고 없을 때만
`TCGames/`를 본다.**

### 6.2 시안 앵커: 네온을 마커로 읽으면 안 된다

여기가 유일하게 코드를 고쳐야 했던 곳이다.

시안(0,255,255)은 레이싱 넘버 앵커다. 데모 26종은 전부 **고립된 한 텍셀**로 갖고
있다(2개 또는 1개, practice1은 0개). 그런데 TC 카트의 원화에는 **네온이 있고, 밝은
시안 네온은 RGB565에서 같은 키로 양자화된다.**

| 카트 | 시안 텍셀 | 실제 정체 |
|---|---|---|
| paragon_9th / _golden | 401 / 503 | 글로우 라인. 가장 큰 덩어리가 150텍셀 |
| paragonV1_gold | 284 | 덩어리 + **AA가 몇 텍셀 떨어뜨려 놓은 낱개 점** |
| paragonV1 / paragonX / paragonX_gold | 0 | 앵커 자체가 없음 |

고치기 전에는 paragon_9th의 글로우 라인이 **숫자 9로 뒤덮였다.**

`KartSkinPainter.IsAnchor`가 판정한다. **앵커는 자기 숫자가 덮을 10×17 상자 안에
혼자 있는 시안 텍셀이다.** 앵커의 정의에서 바로 따라 나오는 규칙이다 — 그만큼
가까운 두 앵커는 숫자를 서로 겹쳐 찍게 되니까.

"이웃 8칸만 본다"로는 **부족하다.** paragonV1_gold의 AA 낱개 점은 아무것과도 닿아
있지 않아서 통과해 버리고, 글로우 옆에 `1`이 두 개 찍힌다. 실제로 이렇게 한 번
틀렸다.

검증은 26 + 6종 전부에 실제 `KartSkinPainter`를 돌려 앵커 수를 센 것이다. 데모는
바꾸기 전과 **한 개도 다르지 않고**(2/2/2/1/1 … practice1 0), 파라곤은 6종 모두 0이다.

## 7. 스키드마크

`stuff.rho`의 `skidMark/`에는 `model/`과 `texture/` 두 폴더가 있다.

- `model/RainBow.png` — **상점 판매용 카드 이미지**. 이걸 쓰면 글자 박힌 카드가
  트레일을 따라 반복된다. (한 번 이렇게 넣었다.)
- `texture/무지개.png` — **실제 스키드 텍스처**. 64×32로 데모 `skidmark.png`와 같은 규격.

`_상점` 접미사가 붙은 파일은 전부 상점 아이콘이다. 접미사 없는 것이 실물이다.

`F3`으로 순환한다(`SkidMarkTrail.Styles`). 전환 시 기존 자국은 지운다 — 한 머티리얼을
공유하므로 섞이지 않게.

## 8. BGM

**데모와 TC판은 같은 이름으로 다른 녹음을 배포한다.** 데모 `village_01.ogg`는
648,263 B, TC판은 453,950 B다. 한 폴더에 넣으면 덮어써서 데모 음악이 사라진다.
`Audio/Music/TCGames/`로 반드시 분리할 것.

리믹스는 `sound_bgm_<테마>2.rho`라는 **별도 아카이브**에 테마당 1곡씩 있다
(`<곡>_re.ogg`). 처음에 `<테마>.rho`만 봐서 놓쳤다.

현재 27곡: village 5, forest 4, desert 5, ice 6, northeu 4, castle 3.

`RaceMusicPlayer`는 모든 테마를 TCGames 세트에서 뽑는다(6개 테마를 다 덮으므로).
재생 순서는 **리믹스 → 01 → 02 → … → 다시 리믹스**이고, 위치는 트랙이 아니라
**테마에 붙는다** — 같은 테마의 다른 트랙으로 옮기거나 리플레이하면 다음 곡으로
넘어가고, 테마가 바뀌면 그 테마의 리믹스부터 시작한다.

`game_end`는 완주 스팅어라 데모 폴더에서 가져오고 1회만 재생한다.

## 9. 랩 수

데모는 테마 아카이브의 `track.xml`에 `laps` 속성이 있다.

```xml
<Track name='village_R01' folder='village_R01' laps='2'/>
```

R 코스는 2랩, `village_R03`만 1랩, I 코스는 대체로 3랩이지만 `forest_I02`는 2랩이다.
`track.rho`의 `challenge.xml`에 챌린지가 덮어쓰는 `lap` 속성이 또 있다(데모의 타임
챌린지 2개는 `lap='1'`).

**TC판 테마 아카이브에는 `track.xml`이 없다.** northeu_R01(1랩), castle_R01(2랩)은
사용자가 알려준 값이다. 자동으로 뽑을 경로를 아직 못 찾았다.

## 10. 검증

```powershell
# 어셈블리 컴파일 (에디터가 열려 있어도 됨)
dotnet build OrangeCarrrrr.Editor.csproj
dotnet build OrangeCarrrrr.UI.csproj
```

에디터가 프로젝트를 잠그고 있으면 `analyzeHeadless`나 Unity batchmode 테스트는 못
돌린다. 그럴 때는 Core 소스를 net8.0 콘솔로 컴파일해 로직만 검증하는 방법이 있다
(세션 중 `flowcheck` 하네스로 사용).

에디터에서 마무리:

1. 포커스 → 자동 임포트 (`.ktrk`, PNG, `textures.json`)
2. **OrangeCarrrrr → Rebuild Track Courses**
3. **OrangeCarrrrr → Build Missing Track Scenes**

## 11. 남은 것

- TC 트랙의 랩 수를 자동으로 얻을 경로 (§9)
- **castle_R01의 미매칭 30개 중 19개가 `theme_common.rho`에 있다.** 아직 안 넣었다.
  northeu의 네 개와 같은 절차면 된다 (§3.6). 나머지 17개는 정말로 없다
- `점프존1`/`점프존발판` vs 테마의 `점프존2`/`점프존발판2` — 메쉬가 참조하는 쪽은
  전자라 그것을 넣었지만, 왜 두 벌인지는 모른다 (§3.6)
- grade 13이 무엇인지 확인 안 했다 (XUN 계열로 보인다). 계열 카트도 4~8종씩 있다
- **툰 램프 셰이딩**. `DataPack1`의 `etc_/toonForBlend_00~03.png`가 흰색→파스텔
  램프다. 원본은 차체를 그릴 때 이걸로 음영을 준다 — 넣으면 뉴 이후 카트의 흰
  차체가 원본처럼 입체적으로 보인다
- northeu_R01 `RoadObj05`의 `end='end05'`가 존재하지 않는 요소명이라 원본 로직이
  기본값으로 폴백 (데모 `ice_R01`의 `lifein`과 같은 케이스, 무해)
- 텍스처는 테마 아카이브 전량을 넣어 두었다. 참조되지 않는 것도 남아 있다(의도적)
