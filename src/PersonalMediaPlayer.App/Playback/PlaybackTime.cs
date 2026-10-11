namespace PersonalMediaPlayer.App.Playback;

// A typed clock time: 12:35 is minutes and seconds, 1:02:30 is hours, minutes, and seconds.
internal static class PlaybackTime
{
    internal static bool TryParse(string? text, out long milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        var numbers = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.Length is < 1 or > 4)
            {
                return false;
            }

            foreach (var character in part)
            {
                if (character is < '0' or > '9')
                {
                    return false;
                }
            }

            if (!int.TryParse(part, out var value) || value < 0)
            {
                return false;
            }

            numbers[index] = value;
        }

        int hours;
        int minutes;
        int seconds;
        if (numbers.Length == 2)
        {
            hours = 0;
            minutes = numbers[0];
            seconds = numbers[1];
        }
        else
        {
            hours = numbers[0];
            minutes = numbers[1];
            seconds = numbers[2];
            if (minutes > 59)
            {
                return false;
            }
        }

        if (seconds > 59)
        {
            return false;
        }

        var total = (hours * 3600L + minutes * 60L + seconds) * 1000L;
        if (total < 0 || total > 24L * 60 * 60 * 1000)
        {
            return false;
        }

        milliseconds = total;
        return true;
    }
}
