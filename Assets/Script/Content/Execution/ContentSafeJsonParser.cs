using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 为递归内容参数提供最小只读 JSON 解析；只产生字典、数组、字符串、数字、布尔和 null，不执行类型绑定或任意代码。
/// </summary>
internal static class ContentSafeJsonParser
{
    /// <summary>
    /// 解析单个完整 JSON 值，并拒绝尾随非空白字符和超过 128 层的输入。
    /// </summary>
    /// <param name="json">需要读取的 JSON 文本。</param>
    /// <param name="value">成功解析的纯数据对象。</param>
    /// <returns>语法完整且深度安全时返回 true。</returns>
    public static bool TryParse(string json, out object value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }
        Reader reader = new Reader(json);
        return reader.TryReadValue(0, out value) && reader.IsAtEnd();
    }

    /// <summary>维护解析位置并实现受限递归下降读取。</summary>
    private sealed class Reader
    {
        private const int MaximumDepth = 128;
        private readonly string text;
        private int index;

        /// <summary>创建从文本起点开始的读取器。</summary>
        public Reader(string value)
        {
            text = value;
        }

        /// <summary>跳过尾部空白后判断是否消费了完整输入。</summary>
        public bool IsAtEnd()
        {
            SkipWhitespace();
            return index == text.Length;
        }

        /// <summary>按当前首字符读取任意 JSON 值。</summary>
        public bool TryReadValue(int depth, out object value)
        {
            value = null;
            if (depth > MaximumDepth)
            {
                return false;
            }
            SkipWhitespace();
            if (index >= text.Length)
            {
                return false;
            }
            char token = text[index];
            if (token == '{') return TryReadObject(depth + 1, out value);
            if (token == '[') return TryReadArray(depth + 1, out value);
            if (token == '"')
            {
                bool succeeded = TryReadString(out string parsed);
                value = parsed;
                return succeeded;
            }
            if (token == 't') return TryReadLiteral("true", true, out value);
            if (token == 'f') return TryReadLiteral("false", false, out value);
            if (token == 'n') return TryReadLiteral("null", null, out value);
            return token == '-' || char.IsDigit(token) ? TryReadNumber(out value) : false;
        }

        /// <summary>读取使用字符串 key 的 JSON 对象。</summary>
        private bool TryReadObject(int depth, out object value)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            value = result;
            index++;
            SkipWhitespace();
            if (TryConsume('}')) return true;
            while (index < text.Length)
            {
                if (!TryReadString(out string key) || result.ContainsKey(key)) return false;
                SkipWhitespace();
                if (!TryConsume(':') || !TryReadValue(depth, out object item)) return false;
                result.Add(key, item);
                SkipWhitespace();
                if (TryConsume('}')) return true;
                if (!TryConsume(',')) return false;
                SkipWhitespace();
            }
            return false;
        }

        /// <summary>读取保持原始顺序的 JSON 数组。</summary>
        private bool TryReadArray(int depth, out object value)
        {
            List<object> result = new List<object>();
            value = result;
            index++;
            SkipWhitespace();
            if (TryConsume(']')) return true;
            while (index < text.Length)
            {
                if (!TryReadValue(depth, out object item)) return false;
                result.Add(item);
                SkipWhitespace();
                if (TryConsume(']')) return true;
                if (!TryConsume(',')) return false;
                SkipWhitespace();
            }
            return false;
        }

        /// <summary>读取带标准反斜杠和 Unicode 转义的 JSON 字符串。</summary>
        private bool TryReadString(out string value)
        {
            value = null;
            SkipWhitespace();
            if (!TryConsume('"')) return false;
            StringBuilder builder = new StringBuilder();
            while (index < text.Length)
            {
                char current = text[index++];
                if (current == '"')
                {
                    value = builder.ToString();
                    return true;
                }
                if (current < 0x20) return false;
                if (current != '\\')
                {
                    builder.Append(current);
                    continue;
                }
                if (index >= text.Length) return false;
                char escaped = text[index++];
                switch (escaped)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (!TryReadUnicodeEscape(out char unicode)) return false;
                        builder.Append(unicode);
                        break;
                    default: return false;
                }
            }
            return false;
        }

        /// <summary>读取四位十六进制 Unicode 转义。</summary>
        private bool TryReadUnicodeEscape(out char value)
        {
            value = default;
            if (index + 4 > text.Length) return false;
            string hex = text.Substring(index, 4);
            index += 4;
            if (!ushort.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                return false;
            value = (char)code;
            return true;
        }

        /// <summary>读取 JSON 整数或浮点数并拒绝非有限值。</summary>
        private bool TryReadNumber(out object value)
        {
            value = null;
            int start = index;
            if (text[index] == '-') index++;
            if (index >= text.Length) return false;
            if (text[index] == '0') index++;
            else
            {
                if (!char.IsDigit(text[index])) return false;
                while (index < text.Length && char.IsDigit(text[index])) index++;
            }
            bool floating = false;
            if (index < text.Length && text[index] == '.')
            {
                floating = true;
                index++;
                int fractionStart = index;
                while (index < text.Length && char.IsDigit(text[index])) index++;
                if (fractionStart == index) return false;
            }
            if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
            {
                floating = true;
                index++;
                if (index < text.Length && (text[index] == '+' || text[index] == '-')) index++;
                int exponentStart = index;
                while (index < text.Length && char.IsDigit(text[index])) index++;
                if (exponentStart == index) return false;
            }
            string token = text.Substring(start, index - start);
            if (!floating && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
            {
                value = integer;
                return true;
            }
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) &&
                !double.IsNaN(number) && !double.IsInfinity(number))
            {
                value = number;
                return true;
            }
            return false;
        }

        /// <summary>读取 true、false 或 null 固定文本。</summary>
        private bool TryReadLiteral(string literal, object literalValue, out object value)
        {
            value = null;
            if (index + literal.Length > text.Length ||
                !string.Equals(text.Substring(index, literal.Length), literal, StringComparison.Ordinal))
                return false;
            index += literal.Length;
            value = literalValue;
            return true;
        }

        /// <summary>当前字符匹配时前进一位。</summary>
        private bool TryConsume(char expected)
        {
            if (index >= text.Length || text[index] != expected) return false;
            index++;
            return true;
        }

        /// <summary>跳过 JSON 允许的四种空白字符。</summary>
        private void SkipWhitespace()
        {
            while (index < text.Length &&
                   (text[index] == ' ' || text[index] == '\t' || text[index] == '\r' || text[index] == '\n'))
                index++;
        }
    }
}
}
