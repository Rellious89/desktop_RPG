using System;
using System.Collections.Generic;
using Character;
using Common;
using UnityEngine;

namespace Skill
{
    /// <summary>
    /// 자동 공격 스킬의 선택 순서와 세션 한정 쿨다운을 소유한다.
    ///
    /// 저장하거나 씬을 찾지 않는다. 생성 때 받은 카탈로그/저장 문서와 단조 시간만 보며,
    /// 해금 여부는 <see cref="CharacterSkillUnlockService"/>에 그대로 맡긴다. 따라서 캐릭터 보유,
    /// 실제 캐릭터 카탈로그, 요구 레벨, 관계 참조 무결성 규칙이 성장 UI와 전투에서 갈라지지 않는다.
    /// </summary>
    public sealed class AutoAttackSkillRuntime
    {
        public const string AttackMotionBehaviorKey = "attack_motion";

        /// <summary>timeScale과 무관한 단조 시간을 공급하는 시험 경계.</summary>
        public interface ITimeSource
        {
            double NowSeconds { get; }
        }

        /// <summary>Unity 실행 세션의 unscaled 단조 시간을 사용한다.</summary>
        public sealed class RealtimeSource : ITimeSource
        {
            public double NowSeconds => Time.realtimeSinceStartupAsDouble;
        }

        private readonly struct CooldownKey : IEquatable<CooldownKey>
        {
            public CooldownKey(string characterId, string skillId)
            {
                CharacterId = characterId;
                SkillId = skillId;
            }

            private string CharacterId { get; }
            private string SkillId { get; }

            public bool Equals(CooldownKey other)
            {
                return string.Equals(CharacterId, other.CharacterId, StringComparison.Ordinal)
                       && string.Equals(SkillId, other.SkillId, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => obj is CooldownKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((CharacterId != null ? StringComparer.Ordinal.GetHashCode(CharacterId) : 0) * 397)
                           ^ (SkillId != null ? StringComparer.Ordinal.GetHashCode(SkillId) : 0);
                }
            }
        }

        private readonly SkillCatalog skills;
        private readonly CharacterSkillCatalog relations;
        private readonly CharacterSkillUnlockService unlocks;
        private readonly ITimeSource time;
        private readonly Dictionary<CooldownKey, double> lastStartedAt =
            new Dictionary<CooldownKey, double>();

        public AutoAttackSkillRuntime(
            CharacterCatalog characters,
            SkillCatalog skills,
            CharacterSkillCatalog relations,
            SaveData document,
            ITimeSource time)
        {
            this.skills = skills;
            this.relations = relations;
            this.time = time ?? throw new ArgumentNullException(nameof(time));
            unlocks = new CharacterSkillUnlockService(characters, skills, relations, document);
        }

        /// <summary>
        /// 현재 캐릭터가 지금 시작할 수 있는 공격 스킬을 고른다. 조회만 하며 쿨다운을 소비하지 않는다.
        /// 우선순위는 CharacterSkill.display_order 오름차순, 이후 skill_id Ordinal 오름차순이다.
        /// </summary>
        public bool TrySelectReady(string characterId, out SkillDefinition selected)
        {
            selected = null;
            if (string.IsNullOrEmpty(characterId) || skills == null || relations == null) return false;

            int selectedOrder = int.MaxValue;
            double now = time.NowSeconds;
            IReadOnlyList<CharacterSkillDefinition> all = relations.Relations;

            for (int i = 0; i < all.Count; i++)
            {
                CharacterSkillDefinition relation = all[i];
                if (relation == null) continue;
                if (!string.Equals(relation.CharacterId, characterId, StringComparison.Ordinal)) continue;
                if (!unlocks.IsUnlocked(characterId, relation.SkillId)) continue;

                SkillDefinition skill = skills.Find(relation.SkillId);
                if (!IsExecutableAttackMotion(skill)) continue;
                if (!IsReady(characterId, skill, now)) continue;

                if (selected == null
                    || relation.DisplayOrder < selectedOrder
                    || relation.DisplayOrder == selectedOrder
                    && string.CompareOrdinal(skill.SkillId, selected.SkillId) < 0)
                {
                    selected = skill;
                    selectedOrder = relation.DisplayOrder;
                }
            }

            return selected != null;
        }

        /// <summary>
        /// 선택된 스킬 모션이 기존 공격 재생 파이프라인에 실제로 들어간 직후 호출한다.
        /// 선택 조회와 분리되어 있으므로 시작이 취소된 입력은 쿨다운을 소비하지 않는다.
        /// </summary>
        public void MarkStarted(string characterId, SkillDefinition skill)
        {
            if (string.IsNullOrEmpty(characterId) || !IsExecutableAttackMotion(skill)) return;
            lastStartedAt[new CooldownKey(characterId, skill.SkillId)] = time.NowSeconds;
        }

        private bool IsReady(string characterId, SkillDefinition skill, double now)
        {
            var key = new CooldownKey(characterId, skill.SkillId);
            if (!lastStartedAt.TryGetValue(key, out double startedAt)) return true;

            // 시간 공급자가 잘못되어 뒤로 움직여도 쿨다운을 조기에 끝내지 않는다. 정상 런타임 공급자는
            // Time.realtimeSinceStartupAsDouble이므로 timeScale/일시정지와 무관하게 앞으로만 간다.
            double elapsed = now - startedAt;
            return elapsed >= 0d && elapsed >= skill.CooldownSeconds;
        }

        private static bool IsExecutableAttackMotion(SkillDefinition skill)
        {
            if (skill == null || string.IsNullOrEmpty(skill.SkillId)) return false;
            if (!string.Equals(skill.BehaviorKey, AttackMotionBehaviorKey, StringComparison.Ordinal)) return false;

            float cooldown = skill.CooldownSeconds;
            if (float.IsNaN(cooldown) || float.IsInfinity(cooldown) || cooldown <= 0f) return false;

            AttackMotionDefinition motion = skill.AttackMotion;
            return motion != null && motion.Frames.Length > 0;
        }
    }
}
