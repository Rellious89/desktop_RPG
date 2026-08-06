using UnityEngine;

namespace Character
{
    /// <summary>
    /// PlayerCharacterAnimator의 공격 재생 루프(Windup/Strike/Recovery)가 실제로 필요로 하는 값만 뽑은
    /// 인터페이스. 재생 루프를 데이터 소유자(현재는 AttackMotionDefinition 에셋 하나)와 분리해서,
    /// 루프가 "이 값들을 어디서 가져왔는지" 신경 쓰지 않게 하기 위해 존재한다.
    /// </summary>
    public interface IAttackMotion
    {
        Sprite[] Frames { get; }

        /// <summary>Frames와 같은 인덱스를 공유하는 프레임 동기화 오버레이 스프라이트. 별도 FPS/재생
        /// 상태가 없고, 본체가 Frame N을 보여줄 때 OverlayFrames[N]을 그대로 겹쳐 그린다. 배열이
        /// 비어 있거나(길이 0) 해당 인덱스가 범위 밖/null이면 그 프레임에는 오버레이가 없다.</summary>
        Sprite[] OverlayFrames { get; }

        float AnimationFps { get; }
        int HitFrameIndex { get; }
        float EndFrameDuration { get; }

        /// <summary>Direct Input 모드의 대기열(pendingAttacks) 만료 시간. Accumulated Input 모드에는
        /// 대기열이 없으므로 이 값을 보지 않는다(누적 모드의 입력 공백은 NoInputGraceTime이 담당한다).</summary>
        float QueueExpireTimeout { get; }

        /// <summary>true면 이 공격은 누적 입력 모드다 - 키 입력 1회 = 타격 1회가 아니라, 입력을 모아
        /// RequiredInputsToStrike에 도달하는 순간 타격한다. Windup 프레임은 시간(FPS)이 아니라 충전
        /// 진행률로 진행되고, Recovery만 기존 FPS/EndFrameDuration으로 재생한다. false면 기존 Direct
        /// Input 동작 그대로다.</summary>
        bool UseAccumulatedInput { get; }

        /// <summary>누적 입력 모드에서 공격 시작(첫 입력 1회 포함)부터 타격까지 필요한 총 입력 수. 1 이상.</summary>
        int RequiredInputsToStrike { get; }

        /// <summary>누적 입력이 끊긴 뒤 현재 충전량을 그대로 유지하는 시간(초).</summary>
        float NoInputGraceTime { get; }

        /// <summary>유예 시간이 지난 뒤 가득 찬 충전량이 0까지 줄어드는 데 걸리는 시간(초). 0이면 즉시 0.</summary>
        float ChargeDecayDuration { get; }

        /// <summary>타격 이후(Recovery 포함) 들어온 입력을 다음 공격의 충전으로 넘길지 여부.</summary>
        bool CarryOverflowInputs { get; }

        int CastFrameIndex { get; }
        GameObject CastEffectPrefab { get; }
        Vector2 CastEffectOffset { get; }
        float CastEffectScale { get; }
        AudioClip CastSound { get; }

        GameObject HitEffectPrefab { get; }
        Vector2 HitEffectOffset { get; }
        float HitEffectScale { get; }

        /// <summary>true면 이 공격이 <see cref="HitEffectJitter"/>로 타격 이펙트의 랜덤 출력 범위를
        /// 직접 정한다. false면 맞는 쪽(HitEffectSpawner)에 설정된 기본 범위를 그대로 쓴다 - 지터 0도
        /// 정당한 값이라 값만으로는 "지정 안 함"을 표현할 수 없어서 이 플래그로 구분한다.</summary>
        bool OverrideHitEffectJitter { get; }

        /// <summary>타격 이펙트가 흩어지는 범위(월드 유닛, X/Y 각각 ±값). <see cref="OverrideHitEffectJitter"/>가
        /// false면 이 값은 쓰이지 않는다.</summary>
        Vector2 HitEffectJitter { get; }

        AudioClip HitSound { get; }

        /// <summary>Cast Frame에서 발사할 발사체 prefab. null이면 이 공격에는 발사체가 없고, 발사체 관련
        /// 처리를 전부 건너뛴 기존 근접 공격과 완전히 동일하게 동작한다. 발사체 내부의 프레임/재생
        /// 데이터는 공격 모션이 아니라 prefab 자신이 소유한다.</summary>
        GameObject ProjectilePrefab { get; }

        /// <summary>시전자 Actor Origin(캐릭터 Transform) 기준 발사 위치 로컬 오프셋. 캐릭터
        /// SpriteRenderer가 flipX 상태면 X만 좌우 반전해서 적용한다.</summary>
        Vector2 ProjectileLaunchOffset { get; }

        /// <summary>발사체 prefab 원본 로컬 스케일에 곱할 배율.</summary>
        float ProjectileScale { get; }
    }
}
