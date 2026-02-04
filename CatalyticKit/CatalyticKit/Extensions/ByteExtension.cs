namespace CatalyticKit;

public static class ByteExtension
{
    /// <summary>
    /// 将字节数组转换为带空格的十六进制字符串（例如：A0 B1 C2）
    /// </summary>
    public static string ToHexStringWithSpaces(this byte[] data)
    {
        if (data.Length == 0)
            return string.Empty;

        // 查找表：比数学运算 (b > 9 ? ...) 更快
        char[] lookup = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        // 长度说明：每个字节占2位 + 1个空格，最后会多出一个空格稍后处理
        char[] c = new char[data.Length * 3];

        for (var i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            // 高4位
            c[i * 3] = lookup[b >> 4];
            // 低4位
            c[i * 3 + 1] = lookup[b & 0xF];
            // 分隔符（空格）
            c[i * 3 + 2] = ' ';
        }

        // new string(char[], start, length)
        // 这里的 length - 1 是为了去掉最后一个多余的空格
        return new string(c, 0, c.Length - 1);
    }
}