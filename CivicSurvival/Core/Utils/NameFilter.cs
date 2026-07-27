using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Client-side nickname pre-filter (leetspeak normalization + banned word detection).
    /// Authoritative moderation is server-side; this is a UX pre-check to avoid a round-trip
    /// on obviously invalid names.
    /// </summary>
    public static class NameFilter
    {
        private const int MIN_LENGTH = 3;
        private const int MAX_LENGTH = 20;

        // Allowed characters: alphanumeric and underscore only
        // MA0009 suppressed: simple character class, no backtracking risk
#pragma warning disable MA0009
        private static readonly Regex AllowedChars = new(@"^[a-zA-Z0-9_]+$");
#pragma warning restore MA0009

#pragma warning disable CIVIC148 // All collections below are immutable hardcoded data — no runtime accumulation
        // Banned words (normalized form, lowercase, no underscores)
        // These are checked with simple "contains" - safe for unique substrings
        // Keep in step with the server-side moderation list (authoritative).
        private static readonly HashSet<string> BannedWords = new()
        {
            // Impersonation
            "admin", "moderator", "official", "developer", "support",

            // Hate speech / offensive (unique substrings, no false positives)
            "fuck", "shit", "nazi", "hitler", "nigger", "faggot",
            "putin", "rashist", "huilo", "stalin",
            "heil", "reich", "gestapo", "swastika",

            // Variations
            "pootin", "putler", "ruzzia", "rusnia",

            // Z-symbolism (Russian war symbols)
            "wagner", "prigozhin", "kadyrov",
            "zwastika", "zwarrior", "zpatriot", "zforce"
        };

        // Words that need word boundary check (Scunthorpe problem)
        // These appear as substrings in legitimate words:
        // - "russia" in "Belarussian", "Prussian", "Borussia"
        // - "ork" in "work", "fork", "NewYork", "network"
        // - "ss" in many words
        // - "zv"/"vz" are short Z-symbols that could appear elsewhere
        // Keep in step with the server-side moderation list (authoritative).
        private static readonly HashSet<string> BoundaryBannedWords = new()
        {
            "ss", "kk", "kkk",
            "russia", "ork",
            "zv", "vz"  // Z-symbolism (short forms)
        };

        // FIX: Pre-compiled regex patterns for boundary-sensitive words (avoids compilation per call)
        // FIX MINOR: Inline initialization to avoid static constructor (CA1810/S3963)
        private static readonly Dictionary<string, Regex> BoundaryRegexCache = InitBoundaryRegexCache();

        // Collapse repeated chars: "hiiitler" → "hitler". Capture needed for $1 in Replace()
        // S25-#13 FIX: Changed from {2,} (3+ repeats) to + (2+ repeats) so "hiiitler" → "hitler" not "hitlr"
#pragma warning disable MA0009 // ReDoS: input limited to MAX_LENGTH=20, no backtracking risk
#pragma warning disable MA0023 // explicit capture `(.)` is required — $1 backreference is used in Replace()
        private static readonly Regex CollapseRepeatsRegex = new(@"(.)\1+", RegexOptions.Compiled);
#pragma warning restore MA0023
#pragma warning restore MA0009

        private static Dictionary<string, Regex> InitBoundaryRegexCache()
        {
            var cache = new Dictionary<string, Regex>();
            // MA0009 suppressed: words are hardcoded short literals, no backtracking risk
#pragma warning disable MA0009
            foreach (var word in BoundaryBannedWords)
            {
                cache[word] = new Regex($@"\b{word}\b", RegexOptions.Compiled);
            }
#pragma warning restore MA0009
            return cache;
        }

        // Leetspeak mapping
        private static readonly Dictionary<char, char> LeetMap = new()
        {
            { '1', 'i' }, { '!', 'i' }, { '|', 'i' },
            { '3', 'e' },
            { '4', 'a' }, { '@', 'a' },
            { '0', 'o' },
            { '5', 's' }, { '$', 's' },
            { '7', 't' }, { '+', 't' },
            { '8', 'b' },
            { '(', 'c' },
            { '9', 'g' }
        };

        // Cyrillic homoglyphs → Latin
        private static readonly Dictionary<char, char> HomoglyphMap = new()
        {
            { 'а', 'a' }, { 'А', 'a' },
            { 'е', 'e' }, { 'Е', 'e' },
            { 'о', 'o' }, { 'О', 'o' },
            { 'р', 'p' }, { 'Р', 'p' },
            { 'с', 'c' }, { 'С', 'c' },
            { 'у', 'y' }, { 'У', 'y' },
            { 'х', 'x' }, { 'Х', 'x' },
            { 'і', 'i' }, { 'І', 'i' },
            { 'ї', 'i' }, { 'Ї', 'i' }
        };
#pragma warning restore CIVIC148

        /// <summary>
        /// Validate nickname. Returns (isValid, errorMessage).
        /// Error is null on success, non-null on failure.
        /// </summary>
        public static (bool IsValid, string Error) Validate(string? nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
                return (false, "Nickname cannot be empty");

            if (nickname!.Length < MIN_LENGTH)
                return (false, $"Nickname must be at least {MIN_LENGTH} characters");

            if (nickname.Length > MAX_LENGTH)
                return (false, $"Nickname cannot exceed {MAX_LENGTH} characters");

            // S25-H4 FIX: Normalize BEFORE AllowedChars check.
            // Old order: AllowedChars rejected Cyrillic → HomoglyphMap never reached.
            // Now: "Рutіn" normalizes to "putin" → passes AllowedChars → caught by BannedWords.
            var (collapsed, preCollapse) = Normalize(nickname);

            // Check long banned words against BOTH forms:
            //   collapsed   — catches repeat-char bypass ("hiiitler" → "hitler")
            //   preCollapse — catches words with intrinsic double letters
            //                 ("support", "official"); CollapseRepeats would
            //                 eat the doubles (support → suport) and miss them.
            foreach (var banned in BannedWords)
            {
                if (collapsed.Contains(banned, System.StringComparison.Ordinal)
                    || preCollapse.Contains(banned, System.StringComparison.Ordinal))
                    return (false, "Nickname contains restricted content");
            }

            // FIX H104+H105: Check boundary-sensitive words on PRE-COLLAPSE form.
            // CollapseRepeats destroys "russia"→"rusia", "kkk"→"k", "ss"→"s",
            // making \brussia\b / \bkkk\b / \bss\b unreachable.
            foreach (var banned in BoundaryBannedWords)
            {
                if (BoundaryRegexCache[banned].IsMatch(preCollapse))
                    return (false, "Nickname contains restricted content");
            }

            if (!AllowedChars.IsMatch(nickname))
                return (false, "Only letters, numbers, and underscore allowed");

            return (true, string.Empty);
        }

        /// <summary>
        /// Quick check if nickname is valid.
        /// </summary>
        public static bool IsValid(string? nickname)
        {
            return Validate(nickname).IsValid;
        }

        /// <summary>
        /// Normalize nickname for banned word detection.
        /// Converts leetspeak, removes underscores, collapses repeats.
        /// Returns (collapsed, preCollapse) — preCollapse retains repeated chars
        /// for boundary-sensitive words like "russia","kkk","ss" that CollapseRepeats would destroy.
        /// </summary>
        private static (string Collapsed, string PreCollapse) Normalize(string input)
        {
            var sb = new StringBuilder(input.Length);
#pragma warning disable CA1308 // Normalize strings to uppercase
            // Rationale: All banned words, LeetMap, and HomoglyphMap values are lowercase.
            // Changing to ToUpperInvariant() requires ~50+ coordinated changes with high
            // risk of breaking the profanity filter. ASCII lowercase normalization is safe.
            string lower = input.ToLowerInvariant();
#pragma warning restore CA1308

            foreach (char c in lower)
            {
                // Skip underscores (used to bypass filters: hit_ler)
                if (c == '_') continue;

                char converted = c;

                // Leetspeak conversion
                if (LeetMap.TryGetValue(c, out char leetChar))
                {
                    converted = leetChar;
                }
                // Homoglyph conversion (Cyrillic → Latin)
                else if (HomoglyphMap.TryGetValue(c, out char homoChar))
                {
                    converted = homoChar;
                }

                sb.Append(converted);
            }

            // FIX H104+H105: Preserve pre-collapse form for boundary-sensitive words.
            // CollapseRepeats("russia") → "rusia" which evades \brussia\b boundary check.
            string preCollapse = sb.ToString();

            // Collapse repeated characters: "hiiitler" → "hitler"
            string collapsed = CollapseRepeatsRegex.Replace(preCollapse, "$1");

            return (collapsed, preCollapse);
        }
    }
}
