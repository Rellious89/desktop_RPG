using System;
using Common;

namespace Character
{
    /// <summary>
    /// 캐릭터 이름 Localization을 화면 하나에 연결한다. 참조가 없거나 번역을 읽지 못한 경우에만
    /// 기존 <see cref="CharacterDefinition.DisplayName"/>을 폴백으로 사용한다.
    /// </summary>
    public sealed class CharacterNameBinding
    {
        private CharacterDefinition character;
        private LocalizedTextReference localizedName;
        private Action<string> apply;

        /// <summary>현재 Locale의 이름을 즉시 읽는다. 토스트처럼 일회성 문자열이 필요할 때 사용한다.</summary>
        public static string GetCurrent(CharacterDefinition definition)
        {
            if (definition == null) return string.Empty;
            if (!definition.HasLocalizedName) return definition.DisplayName;

            string value = definition.LocalizedName.GetLocalizedString();
            return string.IsNullOrEmpty(value) ? definition.DisplayName : value;
        }

        /// <summary>
        /// 이름을 표시할 캐릭터와 출력 대상을 연결한다. Locale이 바뀌면 같은 출력 대상으로 새 이름이
        /// 자동 전달된다. 같은 연결을 다시 요청한 경우에는 중복 구독하지 않는다.
        /// </summary>
        public void Bind(CharacterDefinition definition, Action<string> applyName)
        {
            if (ReferenceEquals(character, definition) && apply == applyName) return;

            Unbind();
            character = definition;
            apply = applyName;

            if (apply == null) return;
            if (character == null)
            {
                apply(string.Empty);
                return;
            }

            if (!character.HasLocalizedName)
            {
                apply(character.DisplayName);
                return;
            }

            // 비동기 문자열이 도착하기 전에도 이전 캐릭터 이름이 남지 않도록 우선 폴백을 넣는다.
            apply(character.DisplayName);
            localizedName = character.LocalizedName;
            localizedName.StringChanged += HandleStringChanged;
        }

        public void Unbind()
        {
            if (localizedName != null) localizedName.StringChanged -= HandleStringChanged;
            localizedName = null;
            character = null;
            apply = null;
        }

        private void HandleStringChanged(string value)
        {
            if (apply == null) return;
            apply(string.IsNullOrEmpty(value) && character != null ? character.DisplayName : value);
        }
    }
}
