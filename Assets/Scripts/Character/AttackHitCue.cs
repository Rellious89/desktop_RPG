using UnityEngine;

namespace Character
{
    /// <summary>
    /// PlayerCharacterAnimator.HitPoint가 타격마다 함께 실어 보내는 값. 데미지 자체는 기존과 동일하게
    /// Damage가 담당하고, Sound/EffectPrefab/EffectOffset/EffectScale은 현재 재생 중인 공격 모션
    /// (AttackMotionDefinition)의 Hit Presentation 값이다 - Jitter만은 예외적으로 "공격이 직접 정할
    /// 수도, 맞는 쪽 기본값에 맡길 수도" 있어서 값과 함께 어느 쪽인지를 나타내는 플래그를 싣는다.
    /// 나머지 값들의 단일 원천은 공격 에셋이고,
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

        /// <summary>이 공격이 타격 이펙트의 랜덤 출력 범위를 직접 정하는지. false면 <see cref="EffectJitter"/>는
        /// 의미가 없고, 맞는 쪽 스포너에 설정된 기본 범위가 쓰인다 - 지터 0("정확히 한 점")도 정당한 값이라
        /// 값만으로는 "지정 안 함"을 표현할 수 없기 때문에 플래그를 따로 싣는다.</summary>
        public readonly bool OverrideEffectJitter;

        /// <summary>타격 이펙트가 흩어지는 범위(월드 유닛, X/Y 각각 ±값).</summary>
        public readonly Vector2 EffectJitter;

        public AttackHitCue(int damage, AudioClip sound, GameObject effectPrefab, Vector2 effectOffset, float effectScale,
            bool overrideEffectJitter, Vector2 effectJitter)
        {
            Damage = damage;
            Sound = sound;
            EffectPrefab = effectPrefab;
            EffectOffset = effectOffset;
            EffectScale = effectScale;
            OverrideEffectJitter = overrideEffectJitter;
            EffectJitter = effectJitter;
        }
    }
}
