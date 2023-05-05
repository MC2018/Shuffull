using Shuffull.Mobile.Tools;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Shuffull.Mobile.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class PlaylistSelect : ContentPage
    {
        private List<long> _playlistIds;

        public ObservableCollection<string> PlaylistNames { get; set; }

        public PlaylistSelect()
        {
            InitializeComponent();
            var playlists = DataManager.GetPlaylists().OrderBy(x => x.Name).ToList();
            PlaylistNames = new ObservableCollection<string>(playlists.Select(x => x.Name).ToList());
            _playlistIds = playlists.Select(x => x.PlaylistId).ToList();

            MyListView.ItemsSource = PlaylistNames;
        }

        async void Handle_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            if (e.Item == null)
            {
                return;
            }

            DataManager.CurrentPlaylistId = _playlistIds[e.ItemIndex];

            //Deselect Item
            ((ListView)sender).SelectedItem = null;
            await Shell.Current.GoToAsync($"//AboutPage");
        }
    }
}
