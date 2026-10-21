using System.Text;

namespace CommunityUploader;

internal static class Server
{
    private static readonly byte[] ListKey = [0x5A, 0x3C, 0x91, 0xE7, 0x1D, 0x68, 0xB4, 0x2F, 0xC6, 0x0B, 0x7E, 0xD3, 0x49, 0xA0, 0x85, 0x16, 0x73, 0xEE, 0x28, 0x9B, 0x04, 0xBD, 0x61, 0xF2];

    public static string ListAddress { get; set; } = Reveal("Mkjll25SmwCgO07mZ8LkdRiMRPp+2APAdF/+ijIO3UOjJDyceo3GYwCaR/Z3khXFdV/wk3wE20joYQ28Jw==");
    public static string Bucket { get; set; } = Part(1);
    public static string Prefix { get; set; } = Part(2);
    public static string AuthorizeAddress { get; set; } = "https://api.backblazeb2.com/b2api/v4/b2_authorize_account";
    public static long PartBytes { get; set; } = 1L << 30;

    private static string Part(int index) => new Uri(ListAddress).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[index];

    private static string Reveal(string text)
    {
        byte[] data = Convert.FromBase64String(text);
        for (int index = 0; index < data.Length; index++)
            data[index] ^= ListKey[index % ListKey.Length];
        return Encoding.UTF8.GetString(data);
    }
}
