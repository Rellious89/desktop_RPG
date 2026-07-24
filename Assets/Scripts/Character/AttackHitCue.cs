using UnityEngine;

namespace Character
{
    /// <summary>
    /// PlayerCharacterAnimator.HitPoint가 타격마다 함께 실어 보내는 값. 데미지 자체는 기존과 동일하게
    /// Damage가 담당하고, Sound/EffectPrefab/EffectOffset/EffectScale은 현재 재생 중인 공격 모션의
    /// Hit Presentation 값이다 - 비어 있으면(null, Vector2.zero, 등) 구독자가 각자의 기본값으로
    /// fallback한다(AudioManager는 hitClip, TargetCombatController/HitEffectSpawner는 defaultEffectPrefab).
    /// </summary>
    public readonly struct AttackHitCue
    {
        public readonly int Damage;
        public readonly AudioClip Sound;
        public readonly GameObject EffectPrefab;
        public readonly Vector2 EffectOffset;
        public readonly float EffectScale;

        public AttackHitCue(int damage, AudioClip sound, GameObject effectPrefab, Vector2 effectOffset, float effectScale)
        {
            Damage = damage;
            Sound = sound;
            EffectPrefab = effectPrefab;
            EffectOffset = effectOffset;
            EffectScale = effectScale;
        }
    }
}
