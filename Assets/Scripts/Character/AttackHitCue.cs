using UnityEngine;

namespace Character
{
    /// <summary>
    /// PlayerCharacterAnimator.HitPoint가 타격마다 함께 실어 보내는 값. 데미지 자체는 기존과 동일하게
    /// Damage가 담당하고, Sound/EffectPrefab/EffectOffset/EffectScale은 현재 재생 중인 공격 모션
    /// (AttackMotionDefinition)의 Hit Presentation 값이다 - 이 값들의 단일 원천은 공격 에셋이고,
    /// 구독자(AudioManager/HitEffectSpawner)가 대신 채워 넣는 기본값은 없다. Sound/EffectPrefab이
    /// null이면 "기본값을 쓰라"가 아니라 <b>"이 공격에는 사운드/이펙트가 없다"</b>는 뜻이다.
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
