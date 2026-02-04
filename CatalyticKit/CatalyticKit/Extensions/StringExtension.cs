namespace CatalyticKit;


public static class StringExtension
{
    extension(string? value)
    {
        /// <summary>
        /// 将字符串转换为布尔值。支持 "true"/"false" 以及数字形式（0为false，非0为true）。
        /// </summary>
        /// <param name="defaultValue">转换失败时返回的默认值。默认为 false。</param>
        /// <returns>转换后的布尔值，如果转换失败则返回默认值。</returns>
        public bool ToBool(bool defaultValue = false)
        {
            // 尝试解析布尔值
            if (bool.TryParse(value, out bool boolResult))
            {
                return boolResult;
            }
            // 尝试解析为数字：0为false，非0为true
            if (int.TryParse(value, out int intResult))
            {
                return intResult != 0;
            }
            return defaultValue;
        }

        /// <summary>
        /// 将字符串转换为整数。
        /// </summary>
        /// <param name="defaultValue">转换失败时返回的默认值。默认为 0。</param>
        /// <returns>转换后的整数值，如果转换失败则返回默认值。</returns>
        public int ToInt(int defaultValue = 0)
        {
            if (int.TryParse(value, out int intResult))
            {
                return intResult;
            }
            return defaultValue;
        }

        /// <summary>
        /// 将字符串转换为双精度浮点数。
        /// </summary>
        /// <param name="defaultValue">转换失败时返回的默认值。默认为 0.0。</param>
        /// <returns>转换后的双精度浮点数，如果转换失败则返回默认值。</returns>
        public double ToDouble(double defaultValue = 0.0)
        {
            if (double.TryParse(value, out double doubleResult))
            {
                return doubleResult;
            }
            return defaultValue;
        }

        /// <summary>
        /// 将字符串转换为日期时间。
        /// </summary>
        /// <param name="defaultValue">转换失败时返回的默认值。</param>
        /// <returns>转换后的日期时间，如果转换失败则返回默认值。</returns>
        public DateTime ToDateTime(DateTime defaultValue)
        {
            if (DateTime.TryParse(value, out DateTime dateTimeResult))
            {
                return dateTimeResult;
            }
            return defaultValue;
        }
    }

    /// <param name="hexString">十六进制字符串，如 "48656C6C6F" 或 "48-65-6C-6C-6F"。</param>
    extension(string hexString)
    {
        /// <summary>
        /// 将十六进制字符串转换为字节数组。支持空格、短横线、冒号分隔的格式。
        /// </summary>
        /// <returns>转换后的字节数组，如果输入为空则返回空数组。</returns>
        /// <exception cref="FormatException">当字符串不是有效的十六进制格式时抛出。</exception>
        public byte[] ToBytes()
        {
            if (string.IsNullOrWhiteSpace(hexString))
            {
                return Array.Empty<byte>();
            }
            // 移除可能的分隔符和空格
            hexString = hexString.Replace(" ", "").Replace("-", "").Replace(":", "");
            return Convert.FromHexString(hexString);
        }

        /// <summary>
        /// 尝试将 Hex 字符串转换成字节数组（安全模式，失败不闪退）。
        /// </summary>
        /// <param name="data">转换成功后的数据，失败为 null。</param>
        /// <param name="separator">分隔符 (默认 "-", 无分隔符传 null 或 "")。</param>
        /// <returns>是否成功。</returns>
        public bool TryToBytes(out byte[] data, string separator = "-")
        {
            data = [];
            if (string.IsNullOrEmpty(hexString)) return false;

            separator ??= string.Empty;
            int step = 2 + separator.Length;

            // 1. 长度校验：必须是步长的整数倍
            if ((hexString.Length + separator.Length) % step != 0) return false;

            int byteCount = (hexString.Length + separator.Length) / step;
            byte[] buffer = new byte[byteCount];

            for (int i = 0; i < byteCount; i++)
            {
                int charIndex = i * step;

                // 2. 字符校验 & 转换
                // 如果 GetHexVal 返回 -1，说明字符非法，直接中止
                int hi = GetHexVal(hexString[charIndex]);
                int lo = GetHexVal(hexString[charIndex + 1]);

                if (hi == -1 || lo == -1) return false;

                buffer[i] = (byte)((hi << 4) | lo);
            }

            data = buffer;
            return true;
        }
    }

    // 辅助函数：把 'A'->10, 'f'->15, 非法->-1
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int GetHexVal(char hex)
    {
        int val = hex;
        if (val >= 48 && val <= 57) return val - 48; // 0-9
        if (val >= 65 && val <= 70) return val - 55; // A-F
        if (val >= 97 && val <= 102) return val - 87; // a-f
        return -1;
    }
}