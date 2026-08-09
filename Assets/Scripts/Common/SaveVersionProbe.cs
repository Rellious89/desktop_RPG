namespace Common
{
    /// <summary>저장 원문을 훑어본 결과. <see cref="SaveVersionProbe.Probe"/>가 돌려준다.</summary>
    public enum SaveVersionProbeStatus
    {
        /// <summary>내용이 없다(빈 문자열/공백). 파일이 없는 것과 같게 다뤄 새 게임으로 간다.</summary>
        Empty,

        /// <summary>JSON 객체이긴 한데 <c>saveVersion</c> 항목이 없다 = 버전 필드가 없던 예전 파일.
        /// 빈 객체 <c>{}</c>도 여기에 들어간다.</summary>
        Unversioned,

        /// <summary>최상위에 정수 <c>saveVersion</c>이 있다. 그 값이 파일의 진짜 버전이다.</summary>
        Versioned,

        /// <summary>JSON으로 읽을 수 없거나 <c>saveVersion</c> 값이 정수가 아니다 = 손상된 파일.</summary>
        Malformed,
    }

    /// <summary>훑어본 결과와 그때 읽은 버전 번호를 함께 담는다.</summary>
    public readonly struct SaveVersionProbeResult
    {
        public SaveVersionProbeStatus Status { get; }

        /// <summary>파일의 저장 형식 번호. <see cref="SaveVersionProbeStatus.Unversioned"/>면
        /// <see cref="SaveData.UnversionedSaveVersion"/>(0), 읽을 수 없으면
        /// <see cref="SaveData.UnknownSaveVersion"/>(-1)이다.</summary>
        public int Version { get; }

        /// <summary>버전을 알아냈는가. 여기가 true인 결과만 마이그레이션 대상이 된다.</summary>
        public bool IsReadable =>
            Status == SaveVersionProbeStatus.Unversioned || Status == SaveVersionProbeStatus.Versioned;

        public SaveVersionProbeResult(SaveVersionProbeStatus status, int version)
        {
            Status = status;
            Version = version;
        }

        public override string ToString() => $"{Status}(v{Version})";
    }

    /// <summary>
    /// 저장 원문에서 <b>저장 형식 번호만</b> 뽑아내는 최소 판독기.
    ///
    /// <b>왜 역직렬화하지 않고 원문을 훑는가.</b> 버전을 알아내려고 JsonUtility로 SaveData를 만들면
    /// 이미 늦는다 - 버전 필드가 없던 예전 파일은 <see cref="SaveData.saveVersion"/>이 필드 기본값(현재
    /// 버전)으로 채워져 "최신 파일"과 구분이 되지 않고, 형식이 바뀐 필드는 역직렬화 과정에서 이미
    /// 잘못 해석되거나 버려진다. 그래서 <b>아무것도 해석하기 전에</b> 원문에서 버전부터 확정하고,
    /// 그 버전에 맞는 처리를 고른다.
    ///
    /// <b>문법을 엄격하게 지킨다.</b> 값을 건너뛸 때도 대충 구분자까지 훑고 마는 것이 아니라 JSON
    /// 문법대로 읽는다 - 선행/후행 쉼표, 빠진 쉼표, <c>tru</c>/<c>12abc</c> 같은 엉터리 리터럴,
    /// <c>[1,2}</c>처럼 짝이 맞지 않는 괄호가 모두 <see cref="SaveVersionProbeStatus.Malformed"/>다.
    /// 느슨하게 읽으면 <b>깨진 파일이 정상으로 통과</b>해서, 뒤이은 역직렬화가 값 절반을 조용히 잃은
    /// 문서를 만들고 그것이 그대로 저장돼 원본을 덮어쓴다. 여기서 막는 편이 훨씬 싸다.
    ///
    /// 최상위 항목만 버전으로 센다 - 중첩된 객체 안에 우연히 같은 이름이 있어도 무시한다.
    ///
    /// 이름은 JsonUtility가 쓰는 그대로 <c>"saveVersion"</c>만 찾는다(이스케이프로 쓴 키는 찾지 않는다).
    /// JsonUtility가 쓴 파일에는 그런 표기가 나오지 않고, 우리가 읽는 파일은 JsonUtility가 쓴 것뿐이다.
    /// </summary>
    public static class SaveVersionProbe
    {
        private const string VersionKey = "saveVersion";

        /// <summary>중첩을 허용하는 최대 깊이. 저장 문서는 3단을 넘지 않는다(문서 → 목록 → 항목).
        /// 제한이 없으면 여는 괄호만 수만 개인 파일 하나로 재귀가 스택을 넘겨 <b>잡을 수 없는</b>
        /// StackOverflow로 앱이 죽는다 - 손상된 파일은 예외 없이 결과값으로 다뤄야 한다.</summary>
        private const int MaxDepth = 32;

        public static SaveVersionProbeResult Probe(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SaveVersionProbeResult(SaveVersionProbeStatus.Empty, SaveData.UnknownSaveVersion);
            }

            int i = SkipWhitespace(json, 0);
            if (i >= json.Length || json[i] != '{') return Malformed();

            bool foundVersion = false;
            int version = SaveData.UnversionedSaveVersion;

            if (!TryParseTopLevelObject(json, i, out i, ref foundVersion, ref version)) return Malformed();

            // 최상위 객체 뒤에 공백 말고 뭔가 더 있으면 우리가 쓴 파일이 아니다.
            i = SkipWhitespace(json, i);
            if (i != json.Length) return Malformed();

            return foundVersion
                ? new SaveVersionProbeResult(SaveVersionProbeStatus.Versioned, version)
                : new SaveVersionProbeResult(
                    SaveVersionProbeStatus.Unversioned, SaveData.UnversionedSaveVersion);
        }

        private static SaveVersionProbeResult Malformed() =>
            new SaveVersionProbeResult(SaveVersionProbeStatus.Malformed, SaveData.UnknownSaveVersion);

        /// <summary>
        /// 최상위 객체를 읽으면서 <c>saveVersion</c> 항목만 따로 챙긴다.
        ///
        /// <b>같은 키가 여러 번 나오면 가장 큰 값을 고른다.</b> 어느 쪽이 진짜인지 알 수 없는 파일이니
        /// 가장 보수적인 쪽, 곧 <b>미래 버전 차단에 걸리는 쪽</b>을 택하는 것이다 - 현재 버전과 미래
        /// 버전이 섞여 있을 때 앞의 것을 믿으면 미래 형식 파일을 헌 형식으로 읽어 덮어쓰게 된다.
        /// (손상으로 처리하지 않는 이유도 같다. 손상이면 호출부가 새 게임을 시작해 파일을 덮어쓸 수
        /// 있지만, 미래 버전이면 불러오기도 저장도 막혀 파일이 그대로 남는다.)
        /// </summary>
        private static bool TryParseTopLevelObject(
            string json, int start, out int next, ref bool foundVersion, ref int version)
        {
            next = start;

            int i = SkipWhitespace(json, start + 1); // 여는 중괄호 건너뛰기
            if (i >= json.Length) return false;

            if (json[i] == '}')
            {
                next = i + 1;
                return true;
            }

            while (true)
            {
                i = SkipWhitespace(json, i);
                if (i >= json.Length || json[i] != '"') return false; // 키는 반드시 문자열이다.

                if (!TryParseString(json, i, out string key, out i)) return false;

                i = SkipWhitespace(json, i);
                if (i >= json.Length || json[i] != ':') return false;

                i = SkipWhitespace(json, i + 1);
                if (i >= json.Length) return false;

                if (key == VersionKey)
                {
                    if (!TryParseVersionNumber(json, i, out int parsed, out i)) return false;

                    version = !foundVersion || parsed > version ? parsed : version;
                    foundVersion = true;
                }
                else if (!TryParseValue(json, i, 1, out i))
                {
                    return false;
                }

                i = SkipWhitespace(json, i);
                if (i >= json.Length) return false;

                if (json[i] == ',')
                {
                    // 쉼표 뒤에는 반드시 항목이 하나 더 온다 - 후행 쉼표는 여기서 걸린다.
                    i++;
                    continue;
                }

                if (json[i] == '}')
                {
                    next = i + 1;
                    return true;
                }

                return false; // 항목 사이에 쉼표가 빠졌다.
            }
        }

        private static int SkipWhitespace(string json, int i)
        {
            while (i < json.Length && IsJsonWhitespace(json[i])) i++;
            return i;
        }

        private static bool IsJsonWhitespace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r';

        /// <summary>여는 따옴표 위치에서 시작해 문자열 하나를 읽고, 닫는 따옴표 <b>다음</b> 위치를 준다.</summary>
        private static bool TryParseString(string json, int start, out string value, out int next)
        {
            value = null;
            next = start;

            int i = start + 1; // 여는 따옴표 건너뛰기
            int from = i;

            while (i < json.Length)
            {
                char c = json[i];

                if (c == '"')
                {
                    value = json.Substring(from, i - from);
                    next = i + 1;
                    return true;
                }

                if (c == '\\')
                {
                    if (i + 1 >= json.Length) return false;

                    char escape = json[i + 1];
                    if (escape == 'u')
                    {
                        if (i + 5 >= json.Length) return false;
                        for (int h = i + 2; h <= i + 5; h++)
                        {
                            if (!IsHexDigit(json[h])) return false;
                        }

                        i += 6;
                        continue;
                    }

                    if (escape != '"' && escape != '\\' && escape != '/' && escape != 'b' &&
                        escape != 'f' && escape != 'n' && escape != 'r' && escape != 't')
                    {
                        return false;
                    }

                    i += 2;
                    continue;
                }

                // 제어 문자는 반드시 이스케이프돼 있어야 한다.
                if (c < ' ') return false;

                i++;
            }

            return false; // 닫히지 않은 문자열
        }

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        /// <summary>
        /// 버전 값을 읽는다. <b>부호도 소수점도 지수도 없는 정수</b>만 받는다 - 그 외에는 우리가 쓴
        /// 파일이 아니므로 손상으로 다룬다.
        ///
        /// int 범위를 넘는 값은 실패가 아니라 <see cref="int.MaxValue"/>로 접는다 - 그런 파일은
        /// 어차피 우리가 모르는 미래 형식이고, "손상"으로 처리하면 호출부가 새 게임을 시작해 덮어쓸 수
        /// 있는 반면 "미래 버전"으로 처리하면 파일을 건드리지 않고 막는다. 모르는 파일은 지우지 않는 쪽이 옳다.
        /// </summary>
        private static bool TryParseVersionNumber(string json, int start, out int version, out int next)
        {
            version = SaveData.UnknownSaveVersion;
            next = start;

            int i = start;
            long parsed = 0;
            int digits = 0;

            while (i < json.Length && json[i] >= '0' && json[i] <= '9')
            {
                if (parsed <= int.MaxValue) parsed = (parsed * 10) + (json[i] - '0');
                digits++;
                i++;
            }

            if (digits == 0) return false; // 음수, 실수, 문자열, null, true/false 전부 여기서 걸린다.

            // 숫자 바로 뒤에 소수점이나 지수가 붙어 있으면 정수가 아니다.
            if (i < json.Length && (json[i] == '.' || json[i] == 'e' || json[i] == 'E')) return false;

            version = parsed > int.MaxValue ? int.MaxValue : (int)parsed;
            next = i;
            return true;
        }

        /// <summary>관심 없는 값 하나를 JSON 문법대로 읽고 지나간다.</summary>
        private static bool TryParseValue(string json, int start, int depth, out int next)
        {
            next = start;

            if (depth > MaxDepth) return false;
            if (start >= json.Length) return false;

            char c = json[start];

            if (c == '"') return TryParseString(json, start, out _, out next);
            if (c == '{') return TryParseObject(json, start, depth, out next);
            if (c == '[') return TryParseArray(json, start, depth, out next);
            if (c == 't') return TryParseLiteral(json, start, "true", out next);
            if (c == 'f') return TryParseLiteral(json, start, "false", out next);
            if (c == 'n') return TryParseLiteral(json, start, "null", out next);

            return TryParseNumber(json, start, out next);
        }

        private static bool TryParseObject(string json, int start, int depth, out int next)
        {
            next = start;

            int i = SkipWhitespace(json, start + 1);
            if (i >= json.Length) return false;

            if (json[i] == '}')
            {
                next = i + 1;
                return true;
            }

            while (true)
            {
                i = SkipWhitespace(json, i);
                if (i >= json.Length || json[i] != '"') return false;

                if (!TryParseString(json, i, out _, out i)) return false;

                i = SkipWhitespace(json, i);
                if (i >= json.Length || json[i] != ':') return false;

                i = SkipWhitespace(json, i + 1);
                if (!TryParseValue(json, i, depth + 1, out i)) return false;

                i = SkipWhitespace(json, i);
                if (i >= json.Length) return false;

                if (json[i] == ',')
                {
                    i++;
                    continue;
                }

                if (json[i] == '}')
                {
                    next = i + 1;
                    return true;
                }

                return false;
            }
        }

        private static bool TryParseArray(string json, int start, int depth, out int next)
        {
            next = start;

            int i = SkipWhitespace(json, start + 1);
            if (i >= json.Length) return false;

            if (json[i] == ']')
            {
                next = i + 1;
                return true;
            }

            while (true)
            {
                i = SkipWhitespace(json, i);
                if (!TryParseValue(json, i, depth + 1, out i)) return false;

                i = SkipWhitespace(json, i);
                if (i >= json.Length) return false;

                if (json[i] == ',')
                {
                    i++;
                    continue;
                }

                if (json[i] == ']')
                {
                    next = i + 1;
                    return true;
                }

                // 여기서 걸러야 [1,2} 처럼 짝이 맞지 않는 괄호가 통과하지 않는다.
                return false;
            }
        }

        private static bool TryParseLiteral(string json, int start, string literal, out int next)
        {
            next = start;

            if (start + literal.Length > json.Length) return false;

            for (int k = 0; k < literal.Length; k++)
            {
                if (json[start + k] != literal[k]) return false;
            }

            next = start + literal.Length;
            return true;
        }

        /// <summary>JSON 수 문법: <c>-? (0 | [1-9][0-9]*) ('.' [0-9]+)? ([eE] [+-]? [0-9]+)?</c>.</summary>
        private static bool TryParseNumber(string json, int start, out int next)
        {
            next = start;

            int i = start;
            if (i < json.Length && json[i] == '-') i++;

            if (i >= json.Length || !IsDigit(json[i])) return false;

            if (json[i] == '0')
            {
                i++; // 앞자리 0 뒤에 숫자가 더 오는 표기(01)는 JSON이 아니다.
            }
            else
            {
                while (i < json.Length && IsDigit(json[i])) i++;
            }

            if (i < json.Length && json[i] == '.')
            {
                i++;
                if (i >= json.Length || !IsDigit(json[i])) return false;
                while (i < json.Length && IsDigit(json[i])) i++;
            }

            if (i < json.Length && (json[i] == 'e' || json[i] == 'E'))
            {
                i++;
                if (i < json.Length && (json[i] == '+' || json[i] == '-')) i++;
                if (i >= json.Length || !IsDigit(json[i])) return false;
                while (i < json.Length && IsDigit(json[i])) i++;
            }

            next = i;
            return true;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
    }
}
