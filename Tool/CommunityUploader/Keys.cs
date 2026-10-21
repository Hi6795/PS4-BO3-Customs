namespace CommunityUploader;

internal static partial class Keys
{
    public static string KeyId { get; private set; } = "";
    public static string Key { get; private set; } = "";

    static Keys() => Fill();

    static partial void Fill();
}
