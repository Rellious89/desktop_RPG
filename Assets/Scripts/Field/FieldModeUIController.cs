using System.Collections;
using Common;
using Dungeon;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Field
{
    /// <summary>
    /// <see cref="FieldModeManager"/>가 "지금 어디인가"를 바꾸면, 그 말대로 <b>화면의 UI만</b> 그 모습으로
    /// 만드는 구독자. 전투/몬스터/회복소/필드 루트는 <see cref="FieldModeRuntimeController"/>의 몫이고,
    /// 하단 메뉴 버튼의 표시 여부는 <see cref="FieldModeMenuButtonVisibilityController"/>의 몫이며,
    /// 여기서는 <b>상태 표시 두 개와 진입 연출 하나</b>만 다룬다 - 세 컨트롤러가 같은 이벤트를
    /// 각자 구독하므로 서로를 참조하지 않고, 한쪽을 떼어내도 다른 쪽이 그대로 돈다.
    ///
    /// <b>모드 판정은 하나도 하지 않는다.</b> 마을인지 던전인지는 매니저만 알고, 이 컴포넌트는
    /// <see cref="FieldModeManager.FieldModeChanged"/>가 <b>확정된 뒤에</b> 알려주는 값을 그대로 그린다.
    /// 마을 복귀 버튼도 <see cref="FieldModeManager.TryReturnToTown"/>을 부르기만 하고, 버튼/표시/연출을
    /// 직접 바꾸지 않는다 - 요청이 거부되면 아무것도 바뀌지 않아야 하는데, 클릭 핸들러가 미리 화면을
    /// 바꾸면 거부된 요청이 화면에만 반영되는 경로가 생기기 때문이다.
    ///
    /// <b>연결은 전부 Inspector 명시 참조다.</b> Find/이름 탐색/GetChild를 하나도 쓰지 않는다 - 씬 계층이
    /// 바뀌어도 이 코드가 조용히 다른 오브젝트를 잡지 않아야 하고, 무엇이 빠졌는지는 실행 즉시 로그로
    /// 드러나야 한다. 참조가 빠져도 전투에는 영향이 없으므로(UI만 담당한다) 빠진 부분만 포기하고 나머지는
    /// 계속 동작한다.
    ///
    /// <b>메뉴 버튼의 표시 여부는 더 이상 여기서 정하지 않는다.</b> 어느 버튼이 마을/던전에서 보일지는
    /// <see cref="FieldModeMenuButtonVisibilityController"/>의 Inspector 목록이 혼자 소유한다 - 같은 대상을
    /// 두 컴포넌트가 껐다 켜면 어느 쪽이 마지막이었는지에 따라 결과가 달라지기 때문이다. 이 컴포넌트가
    /// 마을 복귀 버튼에 대해 하는 일은 <b>클릭 리스너를 안쪽 Button에 거는 것뿐</b>이다.
    ///
    /// <b>진입 연출의 수명주기는 여기가 소유한다.</b> <see cref="UITweenTransition"/>은 스스로 퇴장하거나
    /// 대상을 끄지 않으므로, 켜기 -> Enter -> 유지 -> Exit -> 끄기를 이 컴포넌트가 코루틴 하나로 관리한다.
    /// 유지 시간은 <see cref="Time.timeScale"/>과 무관한 실시간 기준이다(Tween 쪽도 Ignore Time Scale이다).
    /// 연출 도중에 새 전환이 들어오면 기존 코루틴/Tween/문구 구독을 전부 끊고 처음부터 다시 재생하며,
    /// <b>세대 번호</b>로 이전 Exit 완료 콜백이 새 연출을 꺼버리지 않게 막는다.
    ///
    /// <b>문구는 코드가 직접 구독한다.</b> lb_SafeArea/lb_DangerousArea처럼 문구가 고정된 것은 각 오브젝트의
    /// <see cref="LocalizedTMPText"/>가 그대로 담당하고 여기서는 활성 상태만 바꾼다. 반대로 필드 이름은
    /// 마을/던전마다 참조가 달라지므로 <see cref="LocalizedTextReference.StringChanged"/>를 직접 구독했다가
    /// 연출이 끝나면 짝지어 해제한다 - 표시 중에 Locale이 바뀌면 그 구독이 화면 문구도 함께 갱신한다.
    /// 참조가 비어 있으면 <b>문구를 비우고 경고만 남긴다</b> - 한국어/영어를 코드에 적어 메우지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class FieldModeUIController : MonoBehaviour
    {
        [Header("Field Mode (필수)")]
        [Tooltip("모드 전환의 단일 소유자. 이 컴포넌트는 여기서 나오는 FieldModeChanged만 듣고 따라간다 - " +
                 "모드를 직접 판정하지 않는다.")]
        [SerializeField] private FieldModeManager fieldModeManager;

        [Header("Buttons")]
        [Tooltip("마을 복귀 버튼의 Button. 클릭 리스너를 코드에서 걸어 FieldModeManager.TryReturnToTown()만 " +
                 "호출한다 - 실제로 눌리는 안쪽 Button을 연결한다. 이 버튼이 어느 필드에서 보일지는 " +
                 "FieldModeMenuButtonVisibilityController가 정하므로 여기서는 표시를 건드리지 않는다.")]
        [SerializeField] private Button returnTownButton;

        [Header("Field Status Indicator")]
        [Tooltip("안전 지대 표시(lb_SafeArea) 오브젝트. 마을에서만 켠다 - 문구 로컬라이징은 이 오브젝트에 " +
                 "붙은 LocalizedTMPText가 그대로 담당하고 여기서는 활성 상태만 바꾼다.")]
        [SerializeField] private GameObject safeAreaObject;

        [Tooltip("위험 지대 표시(lb_DangerousArea) 오브젝트. 던전에서만 켠다 - 문구 로컬라이징은 이 " +
                 "오브젝트에 붙은 LocalizedTMPText가 담당한다.")]
        [SerializeField] private GameObject dangerousAreaObject;

        [Header("Field Entry Transition")]
        [Tooltip("진입 연출 루트(FieldTransitionLayer). 연출 시작에 켜고 Exit 완료 콜백에서 끈다 - " +
                 "평소에는 꺼져 있어야 한다.")]
        [SerializeField] private GameObject transitionRoot;

        [Tooltip("진입 연출의 UITweenTransition. Play Enter On Enable은 반드시 꺼둔다 - 재생 시점은 이 " +
                 "컴포넌트가 정한다.")]
        [SerializeField] private UITweenTransition transitionTween;

        [Tooltip("필드 이름을 표시할 TextMeshProUGUI(lb_FieldName). 필드마다 참조가 달라지는 동적 문구라 " +
                 "이 컴포넌트가 직접 채운다 - 같은 오브젝트에 LocalizedTMPText를 함께 두지 않는다.")]
        [SerializeField] private TextMeshProUGUI fieldNameText;

        [Tooltip("마을 진입 연출에 표시할 마을 이름. 카테고리 번호 + 숫자 키로 지정한다 - 비워두면 문구를 " +
                 "비우고 경고를 남긴다(코드에 문자열을 적어 메우지 않는다).")]
        [SerializeField] private LocalizedTextReference townName = new LocalizedTextReference();

        [Tooltip("Enter가 끝난 뒤 화면에 유지할 시간(초). Time.timeScale과 무관한 실시간 기준이다.")]
        [Min(0f)]
        [SerializeField] private float holdDuration = 1.5f;

        /// <summary>아직 한 번도 연출을 재생하지 않았음을 뜻하는 세대 값.</summary>
        private const int NoTransitionGeneration = 0;

        /// <summary>지금 유효한 연출 세대. 새 연출이 시작될 때마다 올라가며, Exit 완료 콜백은 <b>자기가
        /// 시작될 때의 세대와 같을 때만</b> 화면을 끈다 - 그러지 않으면 이미 새 연출이 켜둔
        /// <see cref="transitionRoot"/>를 이전 연출의 콜백이 꺼버린다.</summary>
        private int transitionGeneration = NoTransitionGeneration;

        private Coroutine transitionRoutine;

        /// <summary>지금 <see cref="LocalizedTextReference.StringChanged"/>를 구독 중인 필드 이름 참조.
        /// 구독한 것과 해제하는 것이 언제나 같은 객체여야 하므로 필드에 남겨둔다.</summary>
        private LocalizedTextReference boundFieldName;

        private bool subscribed;
        private bool listenerRegistered;

        /// <summary>최초 동기화(<see cref="Start"/>)를 지났는지. 이 값이 켜진 뒤의 활성화는 "런타임 중에
        /// 껐다 켠 것"이므로, 상태는 다시 맞추되 <b>모드가 실제로 달라졌을 때만</b> 연출을 재생한다.</summary>
        private bool startCompleted;

        /// <summary>가장 마지막으로 <b>연출까지 반영한</b> 모드/던전. 재활성화가 가짜 진입 연출을 만들지
        /// 않도록 "이미 보여준 필드"를 기억해 둔다.</summary>
        private FieldMode presentedMode = FieldMode.Town;
        private DungeonDefinition presentedDungeon;
        private bool hasPresented;

        /// <summary>지금 진입 연출이 재생 중인지(읽기 전용 런타임 상태). 검증/디버깅용이다.</summary>
        public bool IsPlayingTransition => transitionRoutine != null;

        /// <summary>
        /// 구독과 클릭 리스너를 <b>지웠다 다시 건다</b> - 껐다 켜기를 반복해도 핸들러가 두 번 붙지 않는다.
        ///
        /// 최초 활성화라면 나머지는 <see cref="Start"/>가 맡는다. 그 뒤의 재활성화에서는 꺼져 있는 동안
        /// <see cref="FieldModeManager.FieldModeChanged"/>를 놓쳤을 수 있으므로 매니저가 <b>지금</b> 말하는
        /// 상태로 버튼과 표시를 다시 맞추되, 모드가 그대로였다면 진입 연출은 재생하지 않는다 - 껐다 켠 것은
        /// 필드에 새로 들어온 것이 아니다.
        /// </summary>
        private void OnEnable()
        {
            Subscribe();
            RegisterReturnTownListener();

            if (!startCompleted) return;

            ApplyState(RequireMode(), RequireDungeon(), forceTransition: false);
        }

        /// <summary>구독과 리스너를 짝지어 해제하고, 재생 중이던 연출을 끊는다. 꺼진 컨트롤러는 Exit를
        /// 진행시킬 수 없으므로 화면에 배너가 얼어붙지 않도록 연출 루트도 함께 감춘다 - 여기서 나가는 것은
        /// UI 상태뿐이라 전투/보상/몬스터 쪽에는 아무 일도 일어나지 않는다.</summary>
        private void OnDisable()
        {
            Unsubscribe();
            UnregisterReturnTownListener();
            CancelFieldTransition();
            SetActiveIfNeeded(transitionRoot, false);
        }

        /// <summary>
        /// 초기 상태를 매니저가 말하는 그대로 한 번 적용한다. <b>시작 시점의 마을도 실제 진입으로 본다</b> -
        /// 그래서 최초 동기화에서는 모드가 바뀌지 않았어도 진입 연출을 한 번 재생한다.
        /// </summary>
        private void Start()
        {
            startCompleted = true;

            ValidateReferences();

            ApplyState(RequireMode(), RequireDungeon(), forceTransition: true);
        }

        private void Subscribe()
        {
            if (fieldModeManager == null) return;

            fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
            fieldModeManager.FieldModeChanged += HandleFieldModeChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;

            subscribed = false;
            if (fieldModeManager != null) fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
        }

        private void RegisterReturnTownListener()
        {
            if (returnTownButton == null) return;

            // 지웠다 다시 건다 - 같은 메서드가 두 번 등록되어 한 번의 클릭이 두 번의 복귀 요청이 되는
            // 경로를 만들지 않는다(두 번째 요청은 매니저가 거부하지만 경고 로그가 남는다).
            returnTownButton.onClick.RemoveListener(HandleReturnTownClicked);
            returnTownButton.onClick.AddListener(HandleReturnTownClicked);
            listenerRegistered = true;
        }

        private void UnregisterReturnTownListener()
        {
            if (!listenerRegistered) return;

            listenerRegistered = false;
            if (returnTownButton != null) returnTownButton.onClick.RemoveListener(HandleReturnTownClicked);
        }

        /// <summary>매니저가 없을 때도 UI가 최소한 마을 모습으로는 서 있도록 기본값을 돌려준다
        /// (매니저의 시작값과 같다). 무엇이 빠졌는지는 <see cref="ValidateReferences"/>가 이미 남겼다.</summary>
        private FieldMode RequireMode() =>
            fieldModeManager != null ? fieldModeManager.CurrentMode : FieldMode.Town;

        private DungeonDefinition RequireDungeon() =>
            fieldModeManager != null ? fieldModeManager.CurrentDungeon : null;

        /// <summary>참조를 한 번에 점검한다 - 무엇이 빠졌는지 로그 하나로 알 수 있어야 한다. 이 컴포넌트는
        /// UI만 담당하므로 참조가 빠져도 전투를 막지 않고, 빠진 부분만 포기한다.</summary>
        private void ValidateReferences()
        {
            if (fieldModeManager == null)
            {
                Debug.LogError($"[FieldModeUIController] '{name}': Field Mode Manager가 연결되지 않았습니다 - " +
                               "모드 전환을 따라갈 수 없어 UI가 마을 상태에서 멈춥니다.", this);
            }
            if (returnTownButton == null)
            {
                Debug.LogWarning($"[FieldModeUIController] '{name}': Return Town Button이 연결되지 않아 마을 복귀 " +
                                 "버튼이 동작하지 않습니다 - Inspector에서 btn_ReturnTown의 Button을 연결하세요.", this);
            }
            if (safeAreaObject == null)
            {
                Debug.LogWarning($"[FieldModeUIController] '{name}': Safe Area Object가 연결되지 않아 안전 지대 " +
                                 "표시를 제어하지 못합니다 - Inspector에서 lb_SafeArea를 연결하세요.", this);
            }
            if (dangerousAreaObject == null)
            {
                Debug.LogWarning($"[FieldModeUIController] '{name}': Dangerous Area Object가 연결되지 않아 위험 " +
                                 "지대 표시를 제어하지 못합니다 - Inspector에서 lb_DangerousArea를 연결하세요.", this);
            }
            if (transitionRoot == null || transitionTween == null || fieldNameText == null)
            {
                Debug.LogWarning($"[FieldModeUIController] '{name}': 진입 연출 참조가 비어 있어 필드 진입 연출을 " +
                                 "재생하지 않습니다 - Transition Root / Transition Tween / Field Name Text를 " +
                                 "모두 연결하세요.", this);
            }
            if (townName == null || !townName.HasReference)
            {
                Debug.LogWarning($"[FieldModeUIController] '{name}': Town Name에 Localization Table/Key가 지정되지 " +
                                 "않아 마을 진입 연출의 문구가 비어 있게 됩니다 - Inspector에서 Category와 Key를 " +
                                 "지정하세요.", this);
            }
        }

        /// <summary>상태가 <b>확정된 뒤에</b> 불린다 - 매니저는 실제로 바뀐 전환에서만 이 이벤트를 발행하므로
        /// 여기까지 왔다면 언제나 새로운 필드에 들어온 것이고, 진입 연출도 반드시 한 번 재생한다.</summary>
        private void HandleFieldModeChanged(FieldMode mode, DungeonDefinition dungeon)
        {
            ApplyState(mode, dungeon, forceTransition: true);
        }

        /// <summary>모드 하나를 실제 UI 상태로 만든다 - 진입점이 최초 동기화든 재활성화든 전환 이벤트든
        /// 언제나 같은 경로를 지나게 하기 위해 하나로 모아둔다.</summary>
        /// <param name="forceTransition">모드가 그대로여도 진입 연출을 재생할지. 최초 동기화와 실제 전환은
        /// true이고, 재활성화는 false다 - 껐다 켠 것만으로 가짜 진입 연출이 나오면 안 된다.</param>
        private void ApplyState(FieldMode mode, DungeonDefinition dungeon, bool forceTransition)
        {
            SyncStaticUI(mode);

            bool fieldChanged = !hasPresented || presentedMode != mode || presentedDungeon != dungeon;

            hasPresented = true;
            presentedMode = mode;
            presentedDungeon = dungeon;

            if (!forceTransition && !fieldChanged) return;

            PlayFieldTransition(mode, dungeon);
        }

        /// <summary>상태 표시를 모드에 맞춘다. 연출과 달리 <b>상태를 그대로 반영하기만 하므로</b>
        /// 몇 번을 다시 불러도 결과가 같다.
        ///
        /// <b>메뉴 버튼은 여기서 건드리지 않는다.</b> 어느 버튼이 어느 필드에서 보일지는
        /// <see cref="FieldModeMenuButtonVisibilityController"/>의 Inspector 목록이 혼자 정한다 - 같은
        /// 대상을 두 곳에서 껐다 켜면 어느 쪽이 마지막이었는지에 따라 결과가 달라지기 때문이다.</summary>
        private void SyncStaticUI(FieldMode mode)
        {
            bool inTown = mode == FieldMode.Town;

            SetActiveIfNeeded(safeAreaObject, inTown);
            SetActiveIfNeeded(dangerousAreaObject, !inTown);
        }

        /// <summary>
        /// 마을 복귀 요청. <b>여기서 하는 일은 요청 전달뿐이다</b> - 성공하면
        /// <see cref="FieldModeManager.FieldModeChanged"/>를 통해 나머지 UI가 갱신되고, 이미 마을이거나
        /// 거부되면(같은 프레임의 두 번째 전환, 전환 콜백 중 재진입) 아무것도 바뀌지 않은 채 로그만 남는다.
        /// </summary>
        private void HandleReturnTownClicked()
        {
            if (fieldModeManager == null)
            {
                Debug.LogError($"[FieldModeUIController] '{name}': Field Mode Manager가 없어 마을 복귀 요청을 " +
                               "전달할 수 없습니다.", this);
                return;
            }

            // 전환 연출이 있으면 그쪽에 맡긴다 - 연출이 끝난 뒤 같은 TryReturnToTown이 호출되므로
            // 거부 규칙과 로그는 어느 경로로 가든 동일하다. 연출이 없으면 예전처럼 곧바로 전달한다.
            if (FieldTransitionSequencer.Instance != null
                && FieldTransitionSequencer.Instance.TryPlayReturnToTown())
            {
                return;
            }

            // 거부 사유는 매니저가 로그로 남기므로 결과를 여기서 다시 해석하지 않는다.
            fieldModeManager.TryReturnToTown();
        }

        // ---- 진입 연출 ----

        /// <summary>진입 연출을 처음부터 재생한다. 재생 중이던 연출과 그 문구 구독은 여기서 전부 끊긴다 -
        /// 이전 연출의 Exit 완료 콜백이 <b>새</b> 연출을 꺼버리지 않도록 세대를 올린다.</summary>
        private void PlayFieldTransition(FieldMode mode, DungeonDefinition dungeon)
        {
            CancelFieldTransition();

            if (transitionRoot == null || transitionTween == null || fieldNameText == null)
            {
                // 무엇이 빠졌는지는 Start의 검증이 이미 남겼다 - 전환 때마다 같은 로그를 쏟지 않는다.
                return;
            }

            SetActiveIfNeeded(transitionRoot, true);
            transitionRoutine = StartCoroutine(FieldTransitionRoutine(mode, dungeon, transitionGeneration));
        }

        /// <summary>켜기 -> 문구 -> Enter -> 유지 -> Exit로 이어지는 연출 한 번. 대기는 전부 실시간
        /// 기준이라 <see cref="Time.timeScale"/>이 0이어도 그대로 진행된다.</summary>
        private IEnumerator FieldTransitionRoutine(FieldMode mode, DungeonDefinition dungeon, int generation)
        {
            // 구독 자체가 최초 로드를 유발하므로, 이 호출로 문구가 화면에 적용된다.
            BindFieldName(mode, dungeon);

            transitionTween.PlayEnter();

            // UITweenTransition은 Enter 완료를 코드 콜백으로 주지 않으므로 재생 상태로 기다린다.
            // Tween 쪽이 Ignore Time Scale이라 timeScale이 0이어도 끝난다.
            while (transitionTween != null && transitionTween.IsPlaying)
            {
                yield return null;
            }

            // 연출 대상이 도중에 꺼졌다면(다른 시스템이 레이어를 껐다) Exit를 진행시킬 수 없다 -
            // 문구 구독만 정리하고 조용히 끝낸다.
            if (!IsTransitionTargetLive())
            {
                transitionRoutine = null;
                FinishFieldTransition(generation);
                yield break;
            }

            if (holdDuration > 0f)
            {
                yield return new WaitForSecondsRealtime(holdDuration);
            }

            if (!IsTransitionTargetLive())
            {
                transitionRoutine = null;
                FinishFieldTransition(generation);
                yield break;
            }

            // 나머지(루트 끄기, 구독 해제)는 Exit 완료 콜백이 맡는다 - 중간에 취소되면 그 콜백은
            // 호출되지 않으므로 여기서 미리 끄지 않는다.
            transitionRoutine = null;
            transitionTween.PlayExit(() => FinishFieldTransition(generation));
        }

        /// <summary>Exit가 끝난 뒤의 뒷정리. <b>자기 세대일 때만</b> 화면을 끈다 - 이미 새 연출이 시작됐다면
        /// 그 연출이 켜둔 루트를 이전 콜백이 꺼버리면 안 된다.</summary>
        private void FinishFieldTransition(int generation)
        {
            if (generation != transitionGeneration) return;

            UnbindFieldName();
            SetActiveIfNeeded(transitionRoot, false);
        }

        /// <summary>재생 중이던 연출을 흔적 없이 끊는다 - 코루틴, Tween, 문구 구독, 그리고 <b>아직 오지 않은
        /// 완료 콜백</b>까지. 세대를 먼저 올리므로 이 시점 이후에 들어오는 이전 콜백은 전부 무시된다.
        /// <see cref="transitionRoot"/>는 여기서 끄지 않는다 - 새 연출이 곧바로 다시 쓰기 때문이다.</summary>
        private void CancelFieldTransition()
        {
            transitionGeneration++;

            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }

            // Stop()은 취소된 Tween의 완료 콜백을 호출하지 않는다(UITweenTransition의 계약).
            if (transitionTween != null) transitionTween.Stop();

            UnbindFieldName();
        }

        /// <summary>연출을 계속 진행시킬 수 있는 상태인지. 꺼진 오브젝트의 Tween은 갱신되지 않아 Exit가
        /// 영영 끝나지 않으므로, 진행 전에 <b>실제로</b> 살아 있는지 확인한다.</summary>
        private bool IsTransitionTargetLive() =>
            transitionTween != null && transitionTween.isActiveAndEnabled;

        // ---- 필드 이름(로컬라이징) ----

        /// <summary>현재 필드의 이름 참조를 연결한다. 마을이면 Inspector의 Town Name, 던전이면 그 던전의
        /// <see cref="DungeonDefinition.DungeonName"/>이며, 참조가 없으면 <b>문구를 비우고 경고를 남긴다</b> -
        /// 코드에 적어둔 문자열로 메우지 않는다.</summary>
        private void BindFieldName(FieldMode mode, DungeonDefinition dungeon)
        {
            UnbindFieldName();

            if (fieldNameText == null) return;

            // 같은 텍스트를 정적 키로 덮어쓰는 컴포넌트가 남아 있으면 실행 중에는 꺼 둔다 - 두 컴포넌트가
            // 같은 TMP를 서로 다른 근거로 쓰면 어느 쪽이 마지막이었는지에 따라 문구가 달라진다.
            DungeonStaticLocalizerGuard.DisableIfPresent(fieldNameText, nameof(FieldModeUIController));

            // 구독보다 먼저 비운다. 로컬라이징 테이블이 아직 로드되지 않았으면 문구가 한두 프레임 뒤에
            // 도착하는데, 그동안 <b>이전 필드의 이름이나 씬에 적혀 있던 임시 문구</b>가 그대로 보이면
            // 안 된다 - 잠깐 비는 편이 틀린 이름을 보여주는 것보다 낫다.
            fieldNameText.text = string.Empty;

            LocalizedTextReference reference = ResolveFieldName(mode, dungeon);
            if (reference == null || !reference.HasReference)
            {
                // 위에서 이미 비웠다 - 참조가 없다고 해서 이전 필드의 이름이 남아 있으면 안 된다.
                return;
            }

            boundFieldName = reference;
            // 구독 자체가 최초 로드를 유발하고, 표시 중에 Locale이 바뀌면 자동으로 다시 호출된다.
            boundFieldName.StringChanged += ApplyFieldName;
        }

        /// <summary>이 필드의 이름 참조를 고른다. 설정이 잘못된 경우는 여기서 경고를 남긴다 - 필드에
        /// 들어올 때마다 한 번씩만 나오므로 로그가 쏟아지지 않는다.</summary>
        private LocalizedTextReference ResolveFieldName(FieldMode mode, DungeonDefinition dungeon)
        {
            if (mode == FieldMode.Town)
            {
                if (townName != null && townName.HasReference) return townName;

                Debug.LogWarning($"[FieldModeUIController] '{name}': Town Name에 Localization Table/Key가 " +
                                 "지정되지 않아 마을 진입 연출의 문구를 비워 둡니다 - Inspector에서 Category와 " +
                                 "Key를 지정하세요.", this);
                return null;
            }

            if (dungeon == null)
            {
                Debug.LogWarning($"[FieldModeUIController] '{name}': 던전 모드인데 던전이 비어 있어 진입 연출의 " +
                                 "문구를 비워 둡니다.", this);
                return null;
            }

            if (!dungeon.HasDungeonName)
            {
                Debug.LogWarning($"[FieldModeUIController] '{name}': 던전 '{dungeon.DungeonId}'의 이름에 " +
                                 "Localization Table/Key가 지정되지 않아 진입 연출의 문구를 비워 둡니다 - " +
                                 "던전 에셋에서 Category와 Key를 지정하세요.", dungeon);
                return null;
            }

            return dungeon.DungeonName;
        }

        private void UnbindFieldName()
        {
            if (boundFieldName == null) return;

            boundFieldName.StringChanged -= ApplyFieldName;
            boundFieldName = null;
        }

        private void ApplyFieldName(string localizedText)
        {
            if (fieldNameText != null) fieldNameText.text = localizedText;
        }

        // ---- 공용 ----

        /// <summary>오브젝트 하나를 켜거나 끈다. 이미 같은 상태면 다시 부르지 않는다 - 결과는 같고,
        /// 자식들의 OnEnable/OnDisable이 불필요하게 다시 돌지 않는다.</summary>
        private static void SetActiveIfNeeded(GameObject target, bool active)
        {
            if (target == null) return;
            if (target.activeSelf == active) return;

            target.SetActive(active);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (holdDuration < 0f) holdDuration = 0f;

            if (fieldNameText != null && fieldNameText.TryGetComponent(out LocalizedTMPText _))
            {
                Debug.LogWarning($"[FieldModeUIController] '{name}': Field Name Text('{fieldNameText.name}')에 " +
                                 "LocalizedTMPText가 함께 붙어 있습니다 - 필드 이름은 필드마다 바뀌는 동적 " +
                                 "문구이므로 정적 키로 덮어쓰면 안 됩니다. 해당 컴포넌트를 제거하세요.", this);
            }
        }
#endif
    }
}
