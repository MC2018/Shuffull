using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Support.V4.Media.Session;
using AndroidX.Core.App;
using Shuffull.Mobile.Droid.Services;
using Shuffull.Mobile.Services;
using System;
using Xamarin.Forms;

[assembly: Dependency(typeof(ForegroundService))]
namespace Shuffull.Mobile.Droid.Services
{
    [Service]
    internal class ForegroundService : Service, IForegroundService
    {
        public override IBinder OnBind(Intent intent)
        {
            throw new NotImplementedException();
        }

        public override StartCommandResult OnStartCommand(Intent intent, [GeneratedEnum] StartCommandFlags flags, int startId)
        {
            string channelId = "ForegroundServiceChannel";
            var notificationManager = (NotificationManager)GetSystemService(NotificationService);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var notificationChannel = new NotificationChannel(channelId, channelId, NotificationImportance.Low);

                notificationManager.CreateNotificationChannel(notificationChannel);
            }

            var notificationBuilder = new NotificationCompat.Builder(this, channelId)
                .SetSmallIcon(Resource.Drawable.exo_icon_play)
                .SetContentTitle("Oi")
                .SetContentText("Playing...")
                .SetPriority(NotificationCompat.PriorityDefault)
                .SetOngoing(true);
            /*
            var remoteViews = new RemoteViews(PackageName, Resource.Layout.Notification);
            remoteViews.SetImageViewResource(Resource.Id.PlayPauseButton, Resource.Drawable.Pause);
            remoteViews.SetOnClickPendingIntent(Resource.Id.PlayPauseButton, CreatePendingIntent(ActionPlayPause));
            notificationBuilder.SetCustomContentView(remoteViews);*/
            //MediaPlayer mediaPlayer = MediaPlayer.Create(Android.App.Application.Context, Android.Net.Uri.Parse("https://192.168.0.39:7117/music/rain.mp3"));
            //mediaPlayer.Start();

            //var mediaSession = new MediaSessionCompat(Android.App.Application.Context, "sample");
            //var mediaSessionConnector = new MediaSessionConnector(mediaSession);
            //mediaSessionConnector.SetPlayer();
            throw new NotImplementedException();
        }

        bool IForegroundService.StartService()
        {
            var intent = new Intent(Android.App.Application.Context, typeof(ForegroundService));
            Android.App.Application.Context.StartForegroundService(intent);
            return true;
        }

        bool IForegroundService.StopService()
        {
            var intent = new Intent(Android.App.Application.Context, typeof(ForegroundService));
            Android.App.Application.Context.StopService(intent);
            return true;
        }
    }
}