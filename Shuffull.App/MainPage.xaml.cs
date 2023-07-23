using MediaManager;
using MediaManager.Library;

namespace Shuffull.App
{
    public partial class MainPage : ContentPage
    {
        int count = 0;

        public MainPage()
        {
            InitializeComponent();

            /*
            CrossMediaManager.Current.MediaItemChanged += (obj, args) =>
            {
                var implementation = (MediaManagerImplementation)obj;

                if (args.MediaItem.Title == PREVIOUS_SONG)
                {
                    Play("http://clips.vorwaerts-gmbh.de/big_buck_bunny.mp4", implementation);
                }
                else if (args.MediaItem.Title == NEXT_SONG)
                {
                    Play("http://clips.vorwaerts-gmbh.de/big_buck_bunny.mp4", implementation);
                }
            };*/

            CrossMediaManager.Current.PropertyChanged += (obj, args) =>
            {
                var implementation = (MediaManagerImplementation)obj;
                Console.WriteLine("Changed Property: " + args.PropertyName);

                if (args.PropertyName == "State")
                {
                    var title = implementation.Queue.Current.Title;
                    Console.WriteLine("New State: " + implementation.State);
                    Console.WriteLine("Title: " + title);

                    if (implementation.State == MediaManager.Player.MediaPlayerState.Buffering)
                    {
                        if (title == PREVIOUS_SONG)
                        {
                            Play("http://clips.vorwaerts-gmbh.de/big_buck_bunny.mp4", implementation);
                        }
                        else if (title == NEXT_SONG)
                        {
                            Play("http://clips.vorwaerts-gmbh.de/big_buck_bunny.mp4", implementation);
                        }
                    }
                }
            };
        }

        private async void OnCounterClicked(object sender, EventArgs e)
        {
            count++;

            if (count == 1)
                CounterBtn.Text = $"Clicked {count} time";
            else
                CounterBtn.Text = $"Clicked {count} times";

            SemanticScreenReader.Announce(CounterBtn.Text);
            
            /*
            CrossMediaManager.Current.PropertyChanged += (obj, args) => {
                var implementation = (MediaManagerImplementation)obj;
                Console.WriteLine("Changed Property: " + args.PropertyName);

                if (args.PropertyName == "State")
                {
                    var title = implementation.Queue.Current.Title;
                    Console.WriteLine("New State: " + implementation.State);
                    Console.WriteLine("Title: " + title);

                    if (implementation.State == MediaManager.Player.MediaPlayerState.Buffering)
                    {
                        if (title == PREVIOUS_SONG)
                        {
                            Play("http://clips.vorwaerts-gmbh.de/big_buck_bunny.mp4", implementation);
                        }
                        else if (title == NEXT_SONG)
                        {
                            Play("http://clips.vorwaerts-gmbh.de/big_buck_bunny.mp4", implementation);
                        }
                    }
                }
            };*/

            Play("http://clips.vorwaerts-gmbh.de/big_buck_bunny.mp4", CrossMediaManager.Current);
        }

        private const string PREVIOUS_SONG = "Shuffull.App - Previous Song";
        private const string NEXT_SONG = "Shuffull.App - Next Song";

        private async void Play(string url, IMediaManager manager)
        {
            //var queue = manager.Queue;
            manager.Queue.Clear();
            var one = new MediaItem("http://192.168.11.92:7117/music/nothing.mp3");
            var two = new MediaItem(url);
            var three = new MediaItem("http://192.168.11.92:7117/music/nothing.mp3");
            one.DisplayTitle = "two";
            one.Title = PREVIOUS_SONG;
            two.DisplayTitle = "two";
            two.DisplaySubtitle = "previous";
            three.DisplayTitle = "two";
            three.DisplaySubtitle = "next";
            three.Title = NEXT_SONG;
            manager.Queue.Add(one);
            manager.Queue.Add(two);
            manager.Queue.Add(three);
            await manager.Play();
            await manager.PlayQueueItem(two);

            Console.WriteLine("COUNT: " + manager.Queue.Count());

            if (manager.Queue.Count() > 3)
            {

            }

        }
    }
}