# MM_GameFramework_CodeSamples

Unity/C# 프로젝트에서 직접 작성한 코드 중 **게임 기반 시스템 설계, 개발 환경 자동화, 네트워크, 엔진 기능 확장, 성능 최적화 경험**을 보여줄 수 있는 코드를 선별한 저장소입니다.

단순히 기능을 동작시키는 것보다 **시스템이 어떤 책임을 가져야 하는지, 기능이 확장되었을 때도 안정적으로 유지될 수 있는지, 반복되는 개발 비용을 줄일 수 있는지**를 고민하며 작성한 코드를 중심으로 정리했습니다.

## Development Philosophy

저는 안정적인 개발은 시스템에 대한 이해에서 시작한다고 생각합니다.

기능을 구현할 때 현재 요구사항만 해결하기보다,

* 문제가 발생한 원인이 무엇인지
* 각 클래스와 시스템이 어떤 책임을 가져야 하는지
* 기능이나 콘텐츠가 증가해도 유지할 수 있는 구조인지
* 반복되는 작업을 자동화할 수 있는지
* 성능 문제를 측정하고 검증할 수 있는지

를 함께 고려하려고 합니다.

이 저장소에는 이러한 개발 방향이 비교적 잘 드러나는 코드를 프로젝트별로 선별했습니다.

---

# Code Samples

## 01. Stat Modifier System

**Project:** 판타지 키우기
**Category:** Game Framework / Architecture

레벨, 장비, 스킬 등 여러 시스템이 동일한 능력치에 영향을 줄 때 발생할 수 있는 **중복 적용 및 해제 누락 문제**를 줄이기 위해 제작한 스탯 보정 시스템입니다.

Modifier를 식별 가능한 Handle 단위로 관리하고, 고정값과 비율 보정의 계산 책임을 분리하여 여러 성장 시스템이 하나의 스탯을 안전하게 변경할 수 있도록 구성했습니다.

### Key Points

* Modifier 등록 / 갱신 / 해제 구조
* Handle 기반 보정값 식별
* 고정값 / 비율 보정 분리
* 여러 시스템에서 동일 스탯에 접근할 때의 충돌 최소화
* 스탯 계산 책임 분리

### Files

* `EntityStatModifier.cs`
* `EntityStatModifierCalculator.cs`
* 관련 Handle 타입

---

## 02. Addressables Stage Loading

**Project:** 판타지 키우기
**Category:** Resource Management / Async

스테이지가 증가할수록 모든 데이터를 메모리에 유지하는 구조의 부담이 커지는 문제를 개선하기 위해 작성한 **Addressables 기반 비동기 스테이지 로딩 시스템**입니다.

현재 스테이지와 다음 스테이지만 유지하고 사용하지 않는 Addressables Handle은 Release하도록 구성했습니다. 또한 여러 비동기 로드 요청이 겹쳤을 때 이전 요청의 결과가 늦게 도착하여 현재 상태를 덮어쓰는 문제를 방지하기 위해 요청 버전을 관리했습니다.

### Key Points

* Addressables 비동기 로드
* Resource Handle 생명주기 관리
* 현재 / 다음 스테이지만 유지
* 사용하지 않는 Handle Release
* 비동기 요청 순서 충돌 방지

### Files

* `AddressableStageFeatureBase.cs`

---

## 03. Multiplayer Build Automation

**Project:** Flash
**Category:** Tools / Automation

멀티플레이 기능을 테스트할 때 여러 클라이언트를 반복해서 직접 빌드하고 실행해야 하는 작업을 줄이기 위해 만든 **Unity Editor 자동화 툴**입니다.

한 번의 실행으로 지정한 수만큼 독립적인 클라이언트를 빌드하고 실행할 수 있도록 구성하여 멀티플레이 테스트 과정의 반복 작업을 줄였습니다.

### Key Points

* Unity Editor 기반 개발 도구
* 다중 클라이언트 자동 빌드
* 빌드 완료 후 클라이언트 자동 실행
* 반복 테스트 과정 단축
* 개발자 작업 편의성 개선

### Files

* `MultiplayersBuildAndRun.cs`

---

## 04. Netcode Lobby / Relay Matchmaking

**Project:** Flash
**Category:** Network

Unity Netcode 환경에서 멀티플레이 세션을 구성하기 위해 작성한 **Lobby / Relay 기반 매치메이킹 코드**입니다.

현재 생성된 Lobby를 검색한 뒤 참가 가능한 방이 존재하면 참가하고, 없을 경우 새로운 방을 생성하는 흐름을 구성했습니다. 개발 과정에서는 Host와 Client의 씬 로딩 시점 차이, NetworkTransform 기반 위치 동기화, 서로 다른 빌드 버전 간 매칭과 같은 문제도 함께 다뤘습니다.

### Key Points

* Unity Netcode
* Unity Lobby / Relay
* Lobby 검색 / 생성 / 참가
* 멀티플레이 로딩 순서 처리
* 빌드 버전 기반 매칭 분리

### Files

* `RelayManager.cs`

---

## 05. Interface Inspector Extension

**Project:** Flash
**Category:** Unity Editor Extension

Unity Inspector에서 Interface 타입을 보다 편리하게 사용할 수 있도록 제작한 **CustomAttribute / PropertyDrawer 기반 Editor 확장 기능**입니다.

Inspector에서 Component를 할당할 때 지정한 Interface의 구현 여부를 검사하도록 하여 잘못된 참조가 등록되는 것을 줄이고, Interface 기반 구조를 Unity Inspector에서도 쉽게 사용할 수 있도록 만들었습니다.

### Key Points

* CustomAttribute
* PropertyDrawer
* Interface 구현 여부 검사
* Inspector 작업 편의성 개선
* 잘못된 참조 할당 방지

### Files

* `RequireInterfaceAttribute.cs`
* `RequireInterfaceDrawer.cs`

---

## 06. Mirror Reflection Rendering

**Project:** Flash
**Category:** Rendering / Optimization

게임의 핵심 기믹인 거울 반사를 구현하기 위해 제작한 **ScriptableRenderFeature 기반 렌더링 기능**입니다.

거울 반사가 반복될 때 발생하는 연산 비용을 줄이기 위해 반사 깊이를 제한하고, 카메라와 일정 거리 이상 떨어진 거울의 반사 연산을 중지하도록 구성했습니다. 또한 거리에 따라 반사 효과를 Fade하여 연산 최적화와 시각적 전환을 함께 처리했습니다.

### Key Points

* ScriptableRenderFeature
* 거울 반사 렌더링
* Reflect 깊이 제한
* 거리 기반 연산 중단
* 거리 기반 Fade

### Files

* `MirrorReflectionFeature.cs`
* 관련 Registrar 코드

---

## 07. Playables Animation System

**Project:** Flash
**Category:** Animation / Runtime System

장착 가능한 스킬의 수가 늘어날수록 Animator StateMachine에 모든 스킬 애니메이션을 미리 등록해야 하는 문제를 줄이기 위해 **Playables API 기반 애니메이션 재생 구조**를 적용했습니다.

AnimationClip을 기반으로 필요한 애니메이션을 런타임에 재생할 수 있도록 구성했으며, 기존 Animator StateMachine 기반 애니메이션과 함께 사용할 수 있도록 구현했습니다.

### Key Points

* Unity Playables API
* Runtime AnimationClip 재생
* 스킬 추가 시 Animator 의존성 감소
* 기존 StateMachine과의 호환

### Files

* `PlayerAnimation.cs`

---

## 08. Vehicle Physics System

**Project:** Full Accel
**Category:** Physics / Simulation

WheelCollider를 기반으로 실제 차량의 움직임을 표현하기 위해 작성한 **차량 물리 시스템**입니다.

Unity의 기본 Rigidbody 동작만 사용하는 대신 차량 물리 관련 공식을 분석하여 Anti Roll Bar, LSD, ABS, 자동 변속 등의 기능을 C# 코드로 구현했습니다.

### Key Points

* WheelCollider
* Anti Roll Bar
* LSD
* ABS
* 자동 변속
* 차량 물리 공식의 코드 구현

### Files

* `VehicleControl.cs`

---

## 09. Object Pooling System

**Project:** Outland Shelter
**Category:** Performance / Object Lifecycle

수백 마리의 적과 연사 무기에서 반복적으로 발생하는 Instantiate / Destroy 비용을 줄이기 위해 적용한 **Object Pooling 시스템**입니다.

게임 내에서 반복적으로 생성되는 동적 객체를 재사용하도록 변경했으며, 적용 전후 테스트에서 FPS 기준 약 1.9배의 성능 향상을 확인했습니다.

### Key Points

* Object Pooling
* Instantiate / Destroy 비용 감소
* 반복 생성 객체 재사용
* 객체 생명주기 관리
* 실제 성능 변화 측정

### Files

* `ObjectPoolManager.cs`

---

## 10. Resource Spawn Overlap Prevention

**Project:** Outland Shelter
**Category:** Algorithm / Gameplay System

자원을 무작위 위치에 생성할 때 여러 자원이 동일한 공간에 겹쳐 생성되는 문제를 해결하기 위해 작성한 **공간 충돌 기반 스폰 알고리즘**입니다.

단순히 좌표가 동일한지만 검사하는 것이 아니라 각 자원의 Hitbox Boundary 크기를 기준으로 생성 가능 영역을 판단하고, 기존 자원과 겹치는 경우 새로운 좌표를 선정하도록 구성했습니다.

### Key Points

* 랜덤 스폰
* Bounding 영역 기반 겹침 검사
* 공간 충돌 판정
* 재생성 좌표 선정
* 스폰 안정성 개선

### Files

* `ResourceSpawner.cs`

---

# Projects

## 판타지 키우기

2D 방치형 RPG 프로젝트입니다.

이 저장소에는 프로젝트 전체 코드가 아닌, 다음과 같이 시스템 설계 관점에서 의미가 있다고 판단한 코드만 선별했습니다.

* Addressables 기반 비동기 스테이지 로딩
* Handle 기반 Stat Modifier 시스템
* 성장 시스템 구조

## Flash

거울의 가속 효과를 활용하는 멀티플레이 액션 레이싱 게임입니다.

개인 프로젝트로 진행하며 Unity의 기본 기능만 사용하는 데 그치지 않고 프로젝트에서 필요했던 기능을 직접 확장했습니다.

* Unity Netcode / Lobby / Relay
* CustomEditor / PropertyDrawer
* Build Automation
* Playables API
* ScriptableRenderFeature

## Full Accel

실제 운전대와 페달 하드웨어를 연동한 운전 시뮬레이션 프로젝트입니다.

* WheelCollider 기반 차량 물리
* UDP 하드웨어 통신
* 렌더링 성능 분석 및 최적화

차량의 세부 Mesh까지 Shadow Casting에 참여하면서 발생한 렌더링 병목을 분석하여 필요한 Mesh만 그림자를 생성하도록 수정했고, 테스트 환경에서 FPS를 **6.2 → 13.8**, Batches를 **1547 → 526**으로 개선했습니다.

## Outland Shelter

좀비 웨이브로부터 거점을 지키는 타워 디펜스 게임입니다.

* Object Pooling
* Dictionary 기반 탐색 구조
* 적 AI
* 자원 스폰 알고리즘

다수의 적과 투사체에서 발생하는 생성 / 삭제 비용을 줄이기 위해 Object Pooling을 적용했고 FPS 기준 약 **1.9배의 성능 향상**을 확인했습니다.

---

# Repository Structure

```text
MM_GameFramework_CodeSamples/
│
├─ README.md
│
├─ 01_StatModifierSystem/
│
├─ 02_AddressablesStageLoading/
│
├─ 03_MultiplayerBuildAutomation/
│
├─ 04_NetworkMatchmaking/
│
├─ 05_EditorExtension/
│
├─ 06_MirrorRendering/
│
├─ 07_PlayablesAnimation/
│
├─ 08_VehiclePhysics/
│
├─ 09_ObjectPooling/
│
└─ 10_ResourceSpawner/
```

---

# Notes

본 저장소는 각 프로젝트의 전체 소스코드를 복제한 저장소가 아니라, **제가 직접 작성한 코드 중 개발 방식과 기술적 문제 해결 과정을 보여주기 위한 코드 샘플을 선별한 저장소**입니다.

따라서 일부 코드는 원본 프로젝트의 다른 Component, ScriptableObject, 데이터 또는 Unity Package에 의존할 수 있으며, 코드의 의도와 구조를 이해하는 데 필요한 범위의 파일을 함께 포함했습니다.

각 샘플의 설명은 해당 코드를 작성하게 된 문제와 설계 의도를 중심으로 정리했습니다.
