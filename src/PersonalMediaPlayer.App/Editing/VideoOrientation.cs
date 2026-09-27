namespace PersonalMediaPlayer.App.Editing;

internal readonly record struct VideoOrientation(int QuarterTurnsClockwise, bool FlipHorizontal, bool FlipVertical)
{
    public bool IsIdentity => QuarterTurnsClockwise == 0 && !FlipHorizontal && !FlipVertical;

    public (int Width, int Height) DisplaySize(int width, int height)
        => (QuarterTurnsClockwise & 1) == 1 ? (height, width) : (width, height);

    public string? LibVlcTransform => (QuarterTurnsClockwise, FlipHorizontal, FlipVertical) switch
    {
        (0, false, false) => null,
        (0, true, false) => "hflip",
        (0, false, true) => "vflip",
        (0, true, true) => "180",
        (1, false, false) => "90",
        (1, true, false) => "antitranspose",
        (1, false, true) => "transpose",
        (1, true, true) => "270",
        (2, false, false) => "180",
        (2, true, false) => "vflip",
        (2, false, true) => "hflip",
        (2, true, true) => null,
        (3, false, false) => "270",
        (3, true, false) => "transpose",
        (3, false, true) => "antitranspose",
        (3, true, true) => "90",
        _ => null
    };

    public string? FfmpegFilters
    {
        get
        {
            if (IsIdentity)
            {
                return null;
            }

            var filters = new List<string>();
            if (FlipHorizontal)
            {
                filters.Add("hflip");
            }

            if (FlipVertical)
            {
                filters.Add("vflip");
            }

            switch (QuarterTurnsClockwise)
            {
                case 1:
                    filters.Add("transpose=1");
                    break;
                case 2:
                    filters.Add("hflip");
                    filters.Add("vflip");
                    break;
                case 3:
                    filters.Add("transpose=2");
                    break;
            }

            return string.Join(',', filters);
        }
    }
}
