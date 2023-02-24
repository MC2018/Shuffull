using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows.Input;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using LibVLCSharp.Shared;

namespace Shuffull.Mobile.ViewModels
{
    public class AboutViewModel : BaseViewModel
    {
        public ICommand OpenWebCommand { get; }
        public ICommand CallSiteCommand { get; }
        public ICommand PlayMusicCommand { get; }
        private string _resultLabel;
        public string ResultLabel
        {
            get { return _resultLabel; }
            set
            {
                _resultLabel = value;
                OnPropertyChanged(nameof(ResultLabel)); // Notify that there was a change on this property
            }
        }



        private MediaPlayer _mediaPlayer;
        public MediaPlayer MediaPlayer
        {
            get => _mediaPlayer;
            private set => SetProperty(ref _mediaPlayer, value);
        }

        public void Play()
        {
            MediaPlayer = _mediaPlayer;
            MediaPlayer.Play();
        }

        public void Stop()
        {
            MediaPlayer.Stop();
            MediaPlayer.Dispose();
            //_item.Media.Dispose();
        }



        // needed in order to ignore SSL cert validation
        private static HttpClientHandler handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (sender, certificate, chain, errors) => true };
        private static HttpClient client = new HttpClient(handler);
        private static string url = "https://192.168.0.39:7117/";
        //private static LibVLC libvlc = new LibVLC();

        public AboutViewModel()
        {
            Title = "About";
            
            OpenWebCommand = new Command(async () => await Browser.OpenAsync(url));
            CallSiteCommand = new Command(async () =>
            {

                var result = await client.GetAsync("music/getmusic");
                ResultLabel = await result.Content.ReadAsStringAsync();
            });
            PlayMusicCommand = new Command(async () =>
            {
                //using (var libvlc = new LibVLC())
                /*using (var mediaPlayer = new MediaPlayer(libvlc))
                using (var media = new Media(libvlc, new Uri("http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ElephantsDream.mp4")))
                {
                    // Start recording
                    mediaPlayer.Play(media);
                }*/
            });

            client.BaseAddress = new Uri(url);
            ResultLabel = "N/A";



            //_mediaPlayer = new MediaPlayer();
        }
    }
}