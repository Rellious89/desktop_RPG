using UnityEngine;

namespace Character
{
    /// <summary>
    /// PlayerCharacterAnimator의 공격 재생 루프(Windup/Strike/Recovery)가 실제로 필요로 하는 값만 뽑은
    /// 인터페이스. 레거시 단일 슬롯인 AttackAnimation(일반 클래스)과 ScriptableObject 에셋인
    /// AttackMotionDefinition을 재생 루프 입장에서 동일하게 다루기 위해 존재한다 - 재생 루프는 이 값들을
    /// 어디서 가져왔는지 신경 쓰지 않는다.
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
    }
}
