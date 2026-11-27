using System.Collections.Frozen;

namespace LyricPulse.Core.Visuals;

/// <summary>
/// Curated lyric stopword set, irregular lyric lemma mappings, and zero-allocation
/// span token normalization utilities shared by StarterLexicon and GloVe visual matchers.
/// </summary>
internal static class LyricTextNormalizationData
{
    private static readonly string[] StopwordArray =
    [
        "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", "any", "are", "aren",
        "as", "at", "be", "because", "been", "before", "being", "below", "between", "both", "but", "by",
        "can", "cannot", "could", "couldn", "did", "didn", "do", "does", "doesn", "doing", "don", "each",
        "few", "for", "from", "further", "get", "gets", "getting", "got", "gotta", "gonna", "wanna", "had",
        "hadn", "has", "hasn", "have", "haven", "having", "he", "her", "here", "hers", "herself", "him",
        "himself", "his", "how", "if", "in", "into", "is", "isn", "it", "its", "itself", "just", "know",
        "let", "like", "ll", "me", "might", "more", "most", "much", "must", "my", "myself", "no", "nor",
        "not", "now", "of", "off", "on", "once", "only", "or", "other", "ought", "our", "ours", "ourselves",
        "out", "own", "really", "re", "same", "say", "says", "said", "shall", "she", "should", "shouldn",
        "so", "some", "still", "such", "than", "that", "the", "their", "theirs", "them", "themselves",
        "then", "there", "these", "they", "this", "those", "through", "to", "too", "under", "until", "up",
        "us", "ve", "very", "was", "wasn", "we", "well", "were", "weren", "what", "when", "where", "which",
        "while", "who", "whom", "why", "will", "with", "won", "would", "wouldn", "you", "your", "yours",
        "yourself", "yourselves", "yeah", "yea", "yes", "yep", "nah", "nope", "oh", "ooh", "oooh", "ah",
        "aah", "aaah", "uh", "uhh", "um", "umm", "hmm", "hm", "mm", "mmm", "na", "la", "da", "ba", "woah",
        "whoa", "hey", "yo", "ay", "ayy", "aye", "ha", "hah", "haha", "o", "ya", "em", "ain", "aint",
        "cause", "cuz", "cos", "tho", "though", "im", "ive", "id", "ill", "youre", "youve", "youll",
        "hes", "shes", "thats", "theres", "whats", "whos", "dont", "doesnt", "didnt", "cant", "wont",
        "couldnt", "wouldnt", "shouldnt", "isnt", "arent", "wasnt", "werent", "every", "onto", "inside",
        "across", "take", "feel", "clean", "inch", "sudden",
        // Common Hindi/Hinglish grammatical stopwords (zero conflict with English concepts)
        "hai", "hain", "hoon", "hun", "mein", "mera", "meri", "mere", "tera", "teri", "tere",
        "tum", "tumhara", "tumhari", "tumhare", "tumhe", "mujhe", "mujhko", "tujhe", "tujhko",
        "humein", "hamara", "hamari", "apna", "apni", "apne", "nahi", "nahin", "kya", "kyun",
        "kyunki", "kaise", "kaisa", "kaisi", "aur", "bhi", "toh", "phir", "kuch", "koi", "woh",
        "yeh", "lekin", "magar", "agar", "jab", "tab", "yahan", "wahan", "jahan", "kahan",
        "unka", "unki", "unke", "iska", "iski", "iske", "uska", "uski", "uske",
        "है", "हैं", "हूं", "हो", "का", "की", "के", "को", "से", "में", "पर", "भी", "तो",
        "और", "क्या", "क्यों", "कब", "कैसे", "जब", "तब", "अब", "यह", "वह", "मैं", "हम",
        "तुम", "तू", "तेरा", "तेरी", "तेरे", "मेरा", "मेरी", "मेरे", "अपना", "अपनी", "नहीं",
        "मत", "बस", "फिर", "कुछ", "सब", "कोई", "मुझे", "तुझे", "लेकिन", "मगर", "अगर"
    ];

    public static FrozenSet<string> Stopwords { get; } = StopwordArray.ToFrozenSet(StringComparer.Ordinal);

    public static FrozenDictionary<string, string> IrregularLemmas { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["broken"] = "broken_heart",
            ["broke"] = "money",
            ["sang"] = "sing",
            ["sung"] = "sing",
            ["flew"] = "flight",
            ["flown"] = "flight",
            ["cried"] = "cry",
            ["cries"] = "cry",
            ["tears"] = "cry",
            ["hearts"] = "heart",
            ["burning"] = "fire",
            ["burnt"] = "fire",
            ["burned"] = "fire",
            ["falling"] = "fall",
            ["fell"] = "fall",
            ["fallen"] = "fall",
            ["stars"] = "star",
            ["wings"] = "flight",
            ["eyes"] = "eye",
            ["hands"] = "hand",
            ["nights"] = "night",
            ["flames"] = "fire",
            ["memories"] = "soul",
            ["shadows"] = "ghost",
            ["dying"] = "skull",
            ["died"] = "skull",
            ["lying"] = "key",
            ["lied"] = "key",
            ["shining"] = "light",
            ["shone"] = "light",
            ["ran"] = "run",
            ["running"] = "run",
            ["drove"] = "car",
            ["driven"] = "car",
            ["rode"] = "car",
            ["wrote"] = "letter",
            ["written"] = "letter",
            ["saw"] = "eye",
            ["seen"] = "eye",
            ["fought"] = "weapon",
            ["drank"] = "wine",
            ["drunk"] = "wine",
            ["slept"] = "coffee",
            ["woke"] = "coffee",
            ["woken"] = "coffee",
            ["held"] = "hand",
            ["lost"] = "compass",
            ["found"] = "compass",
            ["heard"] = "headphones",
            ["shattered"] = "broken_heart",
            ["bleeding"] = "broken_heart",
            ["frozen"] = "snow",
            ["froze"] = "snow",
            ["blew"] = "wind",
            ["blown"] = "wind",
            ["rose"] = "flower",
            ["risen"] = "rise",
            ["swam"] = "water",
            ["sank"] = "water",
            ["sunk"] = "water",
            ["forgotten"] = "eye_off",
            ["forgot"] = "eye_off",
            ["hidden"] = "key",
            ["hid"] = "key",
            ["built"] = "puzzle",
            ["made"] = "puzzle",
            ["sent"] = "paper_plane",
            ["left"] = "paper_plane",
            ["gone"] = "paper_plane",
            ["struck"] = "weapon",
            ["shot"] = "weapon",
            ["tangled"] = "anchor_chain",
            ["bound"] = "anchor_chain",
            ["lit"] = "fire_spark",
            ["caught"] = "hand",
            ["spoke"] = "letter",
            ["spoken"] = "letter",
            ["felt"] = "wave_heart",
            ["dreamt"] = "dream",
            ["dreamed"] = "dream",
            ["dreaming"] = "dream",
            ["chasing"] = "run",
            ["waking"] = "coffee",
            ["living"] = "life"
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool TryNormalizeToken(ReadOnlySpan<char> raw, Span<char> destination, out int length)
    {
        length = 0;
        var trimmed = raw.Trim(".,!?;:\"'()[]{}<>-—–…♪♫`~।॥");
        if (trimmed.Length < 2 || trimmed.Length > destination.Length)
        {
            return false;
        }

        var written = 0;
        var hasDevanagari = false;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (char.IsDigit(ch) || ch is >= '\u0966' and <= '\u096F')
            {
                return false;
            }

            if (ch is '\u2019' or '\u2018')
            {
                ch = '\'';
            }

            if (ch is >= 'A' and <= 'Z')
            {
                ch = (char)(ch + 32);
            }

            if ((ch is >= 'a' and <= 'z') || ch == '\'')
            {
                destination[written++] = ch;
                continue;
            }

            // Preserve Devanagari letters and vowel matras while folding nukta & chandrabindu variants
            if (ch is >= '\u0901' and <= '\u0963')
            {
                if (ch == '\u093C') // combining nukta -> strip so इश्क़ == इश्क, ज़िंदगी == जिंदगी
                {
                    continue;
                }

                ch = ch switch
                {
                    '\u0901' => '\u0902', // chandrabindu ँ -> anusvara ं (चाँद == चांद, आँसू == आंसू)
                    '\u0958' => 'क',
                    '\u0959' => 'ख',
                    '\u095A' => 'ग',
                    '\u095B' => 'ज',
                    '\u095C' => 'ड',
                    '\u095D' => 'ढ',
                    '\u095E' => 'फ',
                    '\u095F' => 'य',
                    _ => ch
                };

                destination[written++] = ch;
                hasDevanagari = true;
            }
        }

        if (hasDevanagari)
        {
            length = written;
            return written >= 2;
        }

        var span = StripContraction(destination[..written]);
        if (span.Length < 2)
        {
            return false;
        }

        var finalLen = 0;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] != '\'')
            {
                destination[finalLen++] = span[i];
            }
        }

        length = finalLen;
        return finalLen >= 2;
    }

    private static ReadOnlySpan<char> StripContraction(ReadOnlySpan<char> token)
    {
        ReadOnlySpan<string> suffixes = ["'ve", "'re", "'ll", "n't", "'s", "'d", "'m"];
        foreach (var suffix in suffixes)
        {
            if (token.EndsWith(suffix, StringComparison.Ordinal) && token.Length - suffix.Length >= 2)
            {
                return token[..^suffix.Length];
            }
        }

        return token;
    }
}
