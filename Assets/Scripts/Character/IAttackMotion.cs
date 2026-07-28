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
        float QueueExpireTimeout { get; }

        int CastFrameIndex { get; }
        GameObject CastEffectPrefab { get; }
        Vector2 CastEffectOffset { get; }
        float CastEffectScale { get; }
        AudioClip CastSound { get; }

        GameObject HitEffectPrefab { get; }
        Vector2 HitEffectOffset { get; }
        float HitEffectScale { get; }
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
