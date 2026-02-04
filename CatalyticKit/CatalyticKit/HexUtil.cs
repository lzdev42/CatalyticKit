using System.Text;

namespace CatalyticKit;

public static class HexUtil
{
    private static readonly char[] HexChars = "0123456789ABCDEF".ToCharArray();

    /// <summary>
    /// 将字节数组转换为 Hex 字符串 (e.g. "01 02 0A FF")
    /// </summary>
    public static string ToHexString(byte[]? data)
    {
        if (data == null || data.Length == 0) return "";

        var sb = new StringBuilder(data.Length * 3);
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            sb.Append(HexChars[b >> 4]);
            sb.Append(HexChars[b & 0x0F]);
            if (i < data.Length - 1)
            {
                sb.Append(' ');
            }
        }
        return sb.ToString();
    }
}
