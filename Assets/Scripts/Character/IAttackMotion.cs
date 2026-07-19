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
        float AnimationFps { get; }
        int HitFrameIndex { get; }
        float EndFrameDuration { get; }
        float QueueExpireTimeout { get; }
    }
}
