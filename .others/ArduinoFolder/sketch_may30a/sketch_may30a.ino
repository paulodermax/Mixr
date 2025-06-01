using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Foundation;

class Program
{
    static async Task Main(string[] args)
    {
        var sessions = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var currentSession = sessions.GetCurrentSession();

        if (currentSession != null)
        {
            var mediaProperties = await currentSession.TryGetMediaPropertiesAsync();
            Console.WriteLine($"Title: {mediaProperties.Title}");
            Console.WriteLine($"Artist: {mediaProperties.Artist}");
            Console.WriteLine($"Album: {mediaProperties.AlbumTitle}");
        }
        else
        {
            Console.WriteLine("Keine aktive Mediensession gefunden.");
        }
    }
}
