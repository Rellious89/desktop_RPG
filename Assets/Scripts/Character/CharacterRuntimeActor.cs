using Common;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 씬에 하나만 존재하는 <b>런타임 캐릭터 액터</b>. 캐릭터마다 씬 오브젝트를 하나씩 배치해 두고
    /// 켜고 끄는 대신, 이 오브젝트 하나가 <see cref="CharacterDefinition"/>이 가리키는 모션 프로필만
    /// 갈아 끼워 어떤 캐릭터든 연기한다.
    ///
    /// <b>캐릭터 오브젝트를 절대 생성하거나 파괴하지 않는다.</b> Instantiate/Destroy를 쓰면 캐릭터가
    /// 바뀔 때마다 SpriteRenderer/FlashOnCue/HitEffectSpawner/발사체 풀이 통째로 새로 만들어져,
    /// 지금 풀링으로 없앤 연타 중 GC 압박이 교체 시점에 되살아난다. 이 컴포넌트가 하는 일은
    /// "이미 있는 컴포넌트들에 새 프로필을 적용하고 오브젝트를 켜고 끄는 것"뿐이다.
    ///
    /// 적용 순서는 고정이다.
    ///   1. 정의/프로필/필수 컴포넌트를 <b>먼저</b> 전부 검증한다 - 하나라도 어긋나면 지금 화면에
    ///      있는 캐릭터를 전혀 건드리지 않고 실패로 돌려준다.
    ///   2. 오브젝트를 끈다 - 각 컴포넌트의 OnDisable이 전투/충전/발사체/오버레이/이동/색을 스스로
    ///      정리한다(정리 규칙을 여기에 다시 적어 두지 않는다).
    ///   3. 애니메이터에 새 프로필을 적용하고(Base Idle 0프레임), 이동 컨트롤러의 기준점/배율을
    ///      새 프로필 기준으로 다시 잡는다.
    ///   4. 오브젝트를 켠다.
    ///
    /// 중간에 실패하면 <see cref="CurrentDefinition"/>은 그대로 두고, 되돌릴 수 있는 직전 프로필이
    /// 있으면 그 상태로 복구한 뒤 원래 활성 상태까지 되살린다 - 실패한 교체 때문에 화면에서 캐릭터가
    /// 사라지는 경로를 만들지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterRuntimeActor : MonoBehaviour
    {
        /// <summary>지금 이 액터가 연기하고 있는 캐릭터. 아무도 투입되지 않았거나
        /// <see cref="Deactivate"/> 직후면 null이다. 교체에 실패해도 이 값은 바뀌지 않는다.</summary>
        public CharacterDefinition CurrentDefinition { get; private set; }

        private PlayerCharacterAnimator animator;
        private AttackMovement attackMovement;
        private bool componentsResolved;

        /// <summary>
        /// 이 액터가 <paramref name="definition"/>의 캐릭터를 연기하도록 만들고 활성화한다.
        /// 같은 정의로 다시 불러도 안전하다(그 캐릭터를 Base Idle 0프레임부터 다시 시작한다).
        /// 오브젝트가 이미 꺼져 있어도, 아직 Awake가 불리기 전이어도 안전하다.
        /// </summary>
        /// <returns>교체가 실제로 일어났으면 true. false면 <see cref="CurrentDefinition"/>과 화면
        /// 상태가 호출 이전 그대로 유지된다(가능한 범위에서 복구된다).</returns>
        public bool TryApply(CharacterDefinition definition)
        {
            if (definition == null)
            {
                Debug.LogError($"[CharacterRuntimeActor] '{name}': 적용할 Character Definition이 비어 있습니다 - " +
                               "교체를 취소합니다.", this);
                return false;
            }

            CharacterMotionProfile profile = definition.MotionProfile;
            if (!CharacterMotionProfile.IsPlayable(profile))
            {
                Debug.LogError($"[CharacterRuntimeActor] '{name}': '{definition.CharacterId}'의 Character Motion " +
                               "Profile이 없거나 재생 가능한 Base Idle 프레임이 없습니다 - 교체를 취소합니다.", definition);
                return false;
            }

            if (!TryResolveComponents()) return false;

            // 실패했을 때 되돌아갈 곳. 애니메이터가 지금 들고 있는 프로필이 유일한 근거다
            // (CurrentDefinition은 아직 이 액터가 한 번도 적용받지 않았을 수 있다).
            CharacterMotionProfile previousProfile = animator.MotionProfile;
            bool wasActive = gameObject.activeSelf;

            // 값을 바꾸는 동안에는 항상 꺼둔다 - 이전 캐릭터의 자세/오프셋이 한 프레임이라도 새
            // 캐릭터의 것으로 보이지 않게 하고, OnDisable의 기존 정리 경로를 그대로 재사용한다.
            if (gameObject.activeSelf) gameObject.SetActive(false);

            if (!animator.TryApplyMotionProfile(profile))
            {
                RestorePreviousState(previousProfile, wasActive);
                return false;
            }
            if (!attackMovement.RefreshFromCurrentProfile())
            {
                // 애니메이터는 이미 새 프로필로 바뀌었다 - 배치가 어긋난 채로 켜지 않고 되돌린다.
                RestorePreviousState(previousProfile, wasActive);
                return false;
            }

            CurrentDefinition = definition;
            gameObject.SetActive(true);
            return true;
        }

        /// <summary>아무도 투입되지 않은 상태로 만든다 - 오브젝트를 끄는 것만으로 전투/충전/발사체/
        /// 오버레이 스프라이트/이동 오프셋/섬광 색이 각 컴포넌트의 OnDisable에서 정리된다. 이미 꺼져
        /// 있으면 다시 끄지 않는다(중복 OnDisable 없음) - 몇 번을 불러도 안전하다.</summary>
        public void Deactivate()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            CurrentDefinition = null;
        }

        /// <summary>교체에 실패했을 때 직전 프로필로 되돌린다. 되돌릴 수 있는 프로필이 없으면(액터가
        /// 처음부터 재생 불가능한 상태였던 경우) 잘못된 자세로 켜두지 않고 꺼진 채로 남긴다.</summary>
        private void RestorePreviousState(CharacterMotionProfile previousProfile, bool wasActive)
        {
            if (!CharacterMotionProfile.IsPlayable(previousProfile))
            {
                Debug.LogError($"[CharacterRuntimeActor] '{name}': 교체에 실패했고 되돌릴 이전 프로필도 없어 " +
                               "액터를 비활성 상태로 둡니다.", this);
                return;
            }

            if (!animator.TryApplyMotionProfile(previousProfile) || !attackMovement.RefreshFromCurrentProfile())
            {
                Debug.LogError($"[CharacterRuntimeActor] '{name}': 이전 프로필 '{previousProfile.name}'으로 되돌리는 " +
                               "데도 실패했습니다 - 액터를 비활성 상태로 둡니다.", this);
                return;
            }

            if (wasActive) gameObject.SetActive(true);
        }

        /// <summary>이 액터가 캐릭터를 연기하는 데 반드시 필요한 컴포넌트를 한 번만 찾아 캐시한다.
        /// 여섯 캐릭터 오브젝트가 공통으로 갖고 있는 구성이 그대로 요구 조건이다 - 빠진 것이 있으면
        /// 조용히 절반만 동작시키지 않고 무엇이 없는지 남긴 뒤 교체 자체를 거절한다.
        ///
        /// ProjectileSpawner와 AttackFrameOverlay는 요구하지 않는다 - PlayerCharacterAnimator가
        /// 필요할 때 스스로 한 번만 만들어 재사용한다.</summary>
        private bool TryResolveComponents()
        {
            if (componentsResolved) return true;

            animator = GetComponent<PlayerCharacterAnimator>();
            attackMovement = GetComponent<AttackMovement>();

            string missing = null;
            if (GetComponent<SpriteRenderer>() == null) missing = Append(missing, nameof(SpriteRenderer));
            if (animator == null) missing = Append(missing, nameof(PlayerCharacterAnimator));
            if (attackMovement == null) missing = Append(missing, nameof(AttackMovement));
            if (GetComponent<FlashOnCue>() == null) missing = Append(missing, nameof(FlashOnCue));
            if (GetComponent<HitEffectSpawner>() == null) missing = Append(missing, nameof(HitEffectSpawner));
            if (GetComponent<ActorOutlineController>() == null) missing = Append(missing, nameof(ActorOutlineController));

            if (missing != null)
            {
                Debug.LogError($"[CharacterRuntimeActor] '{name}': 캐릭터 액터에 필요한 컴포넌트가 없습니다 - " +
                               $"{missing}. 교체를 진행하지 않습니다.", this);
                return false;
            }

            componentsResolved = true;
            return true;
        }

        private static string Append(string accumulated, string entry)
        {
            return accumulated == null ? entry : accumulated + ", " + entry;
        }
    }
}
