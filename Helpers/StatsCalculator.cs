using System.Globalization;

namespace QuickNotes.Helpers;

public static class StatsCalculator
{
    public sealed record DailyStats(DateOnly Date, int WordCount, int NoteCount);
    public sealed record WordFreq(string Word, int Count);
    public sealed record AllStats
    {
        public int TotalNotes { get; init; }
        public int TotalWords { get; init; }
        public double AvgWordsPerNote { get; init; }
        public int LongestNoteWords { get; init; }
        public int CurrentStreak { get; init; }
        public int BestStreak { get; init; }
        public DateOnly? BestStreakStart { get; init; }
        public DateOnly? BestStreakEnd { get; init; }
        public required List<DailyStats> Daily { get; init; }
        public required List<WordFreq> TopWords { get; init; }
        public DateOnly? FirstNoteDate { get; init; }
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "el", "la", "los", "las", "lo", "un", "una", "unos", "unas",
        "de", "del", "en", "y", "e", "o", "a", "al", "con", "por",
        "para", "que", "es", "se", "no", "su", "le", "les", "lo",
        "como", "más", "pero", "sin", "entre", "este", "esta", "esto",
        "tan", "era", "son", "has", "han", "había", "habían",
        "the", "and", "for", "are", "but", "not", "you", "all",
        "can", "had", "her", "was", "one", "our", "out", "has",
        "have", "been", "some", "them", "then", "with", "were",
        "your", "what", "when", "where", "which", "who", "how",
        "its", "also", "than", "very", "just", "about", "over",
        "into", "after", "before", "between", "through", "during",
        "because", "from", "that", "this", "these", "those",
    };

    public static AllStats Compute(IEnumerable<Models.Note> notes)
    {
        var noteList = notes
            .Where(n => !n.IsDeleted)
            .OrderBy(n => n.LastModified)
            .ToList();

        if (noteList.Count == 0)
        {
            return new AllStats
            {
                TotalNotes = 0,
                TotalWords = 0,
                AvgWordsPerNote = 0,
                LongestNoteWords = 0,
                CurrentStreak = 0,
                BestStreak = 0,
                Daily = [],
                TopWords = [],
            };
        }

        int totalWords = 0;
        int longestWords = 0;
        var dailyMap = new Dictionary<DateOnly, DailyStats>();

        foreach (var note in noteList)
        {
            int wc = CountWords(note.PlainText);
            totalWords += wc;
            if (wc > longestWords) longestWords = wc;

            var day = DateOnly.FromDateTime(note.LastModified);
            if (dailyMap.TryGetValue(day, out var ds))
                dailyMap[day] = ds with { WordCount = ds.WordCount + wc, NoteCount = ds.NoteCount + 1 };
            else
                dailyMap[day] = new DailyStats(day, wc, 1);
        }

        var firstDate = noteList.Min(n => (DateOnly?)DateOnly.FromDateTime(n.LastModified));
        var today = DateOnly.FromDateTime(DateTime.Now);

        // Fill missing days (no activity = 0 words)
        if (firstDate.HasValue)
        {
            for (var d = firstDate.Value; d <= today; d = d.AddDays(1))
            {
                if (!dailyMap.ContainsKey(d))
                    dailyMap[d] = new DailyStats(d, 0, 0);
            }
        }

        var daily = dailyMap.Values.OrderBy(d => d.Date).ToList();

        // Streaks
        int currentStreak = 0;
        int bestStreak = 0;
        int bestStreakStartIdx = -1;
        int bestStreakEndIdx = -1;
        int tempStreak = 0;
        int tempStart = -1;

        for (int i = 0; i < daily.Count; i++)
        {
            if (daily[i].WordCount > 0)
            {
                if (tempStreak == 0) tempStart = i;
                tempStreak++;
                if (tempStreak > bestStreak || (tempStreak == bestStreak && i == daily.Count - 1))
                {
                    // If tying and this is the end (i.e. current streak equal to best), keep current as better
                    // Otherwise update only if strictly bigger, or same but currentStreak
                    bestStreak = tempStreak;
                    bestStreakStartIdx = tempStart;
                    bestStreakEndIdx = i;
                }
            }
            else
            {
                tempStreak = 0;
            }
        }

        // Current streak = trailing consecutive days with words ending today
        currentStreak = 0;
        for (int i = daily.Count - 1; i >= 0; i--)
        {
            if (daily[i].WordCount > 0)
                currentStreak++;
            else
                break;
        }

        // Re-check best streak properly
        bestStreak = 0;
        int bestStart = -1, bestEnd = -1;
        tempStreak = 0;
        tempStart = -1;
        for (int i = 0; i < daily.Count; i++)
        {
            if (daily[i].WordCount > 0)
            {
                if (tempStreak == 0) tempStart = i;
                tempStreak++;
                if (tempStreak > bestStreak)
                {
                    bestStreak = tempStreak;
                    bestStart = tempStart;
                    bestEnd = i;
                }
            }
            else
            {
                tempStreak = 0;
            }
        }

        DateOnly? bestStartDate = bestStart >= 0 ? daily[bestStart].Date : null;
        DateOnly? bestEndDate = bestEnd >= 0 ? daily[bestEnd].Date : null;

        // Top words
        var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in noteList)
        {
            foreach (var word in Tokenize(note.PlainText))
            {
                if (StopWords.Contains(word) || word.Length == 1) continue;
                wordCounts.TryGetValue(word, out int c);
                wordCounts[word] = c + 1;
            }
        }

        var topWords = wordCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(10)
            .Select(kv => new WordFreq(kv.Key, kv.Value))
            .ToList();

        return new AllStats
        {
            TotalNotes = noteList.Count,
            TotalWords = totalWords,
            AvgWordsPerNote = noteList.Count > 0 ? Math.Round((double)totalWords / noteList.Count, 1) : 0,
            LongestNoteWords = longestWords,
            CurrentStreak = currentStreak,
            BestStreak = bestStreak,
            BestStreakStart = bestStartDate,
            BestStreakEnd = bestEndDate,
            Daily = daily,
            TopWords = topWords,
            FirstNoteDate = firstDate,
        };
    }

    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split([' ', '\n', '\r', '\t', '\x00A0'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var buffer = new List<char>(32);
        foreach (char c in text!)
        {
            if (char.IsLetter(c) || c == '\'' || (c >= 0x00E1 && c <= 0x00FA))
            {
                buffer.Add(c);
            }
            else
            {
                if (buffer.Count > 0)
                {
                    yield return new string(buffer.ToArray()).ToLowerInvariant();
                    buffer.Clear();
                }
            }
        }
        if (buffer.Count > 0)
            yield return new string(buffer.ToArray()).ToLowerInvariant();
    }
}
