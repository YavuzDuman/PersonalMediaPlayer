namespace PersonalMediaPlayer.UiTests;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        try
        {
            new DownloadPreviewAfterLeaveTests().LeavingDuringSaveDoesNotPlayTheNextReadyAudio();
            Console.WriteLine("The ready audio did not start after leaving Download.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
