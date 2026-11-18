using System.Security.Cryptography;

namespace Split.Api;

/// <summary>
/// Makes the shareable part of a group URL.
///
/// Readable words rather than a raw id, because this is a link people paste into a group chat
/// and occasionally read aloud. The four-digit suffix is what carries the actual entropy:
/// roughly 36 * 36 * 10000 combinations, which is enough that the slugs do not collide in
/// practice but is emphatically <em>not</em> a secret. See the README on what that means.
/// </summary>
public static class SlugGenerator
{
    private static readonly string[] Adjectives =
    [
        "amber", "brisk", "calm", "dusty", "eager", "fresh", "golden", "hazy",
        "ivory", "jolly", "keen", "lucky", "mellow", "noble", "olive", "plucky",
        "quiet", "rapid", "sunny", "teal", "upbeat", "vivid", "warm", "zesty",
        "bright", "clever", "deep", "early", "fair", "gentle", "humble", "lively",
        "merry", "neat", "polished", "quick", "ready", "steady"
    ];

    private static readonly string[] Nouns =
    [
        "tapir", "otter", "heron", "gecko", "lemur", "panda", "robin", "tiger",
        "walrus", "badger", "cuckoo", "dolphin", "egret", "falcon", "gibbon", "hornbill",
        "ibis", "jaguar", "kestrel", "lynx", "marten", "narwhal", "osprey", "puffin",
        "quokka", "raven", "seal", "toucan", "urchin", "viper", "wombat", "yak",
        "albatross", "bison", "crane", "dingo", "elk", "ferret"
    ];

    /// <summary>Returns something like <c>bright-tapir-4821</c>.</summary>
    public static string Next()
    {
        string adjective = Adjectives[RandomNumberGenerator.GetInt32(Adjectives.Length)];
        string noun = Nouns[RandomNumberGenerator.GetInt32(Nouns.Length)];
        int number = RandomNumberGenerator.GetInt32(1000, 10000);

        return $"{adjective}-{noun}-{number}";
    }
}
