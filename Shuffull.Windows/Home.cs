using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Shared;
using Shuffull.Shared.Networking.Models;
using Shuffull.Windows.Constants;
using Shuffull.Windows.Extensions;
using Shuffull.Windows.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Shuffull.Windows
{
    public partial class Home : Form
    {
        public long _playlistId;

        public Home()
        {
            InitializeComponent();
            Core.Initialize();

            // sync
            // set last played as current song
            MediaManager.QueueLastPlayedSong();

            label1.Text = Directories.MusicFolderAbsolutePath;
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var playlists = context.Playlists.ToList();
            playlistSelectorBox.DataSource = playlists;
            playlistSelectorBox.DisplayMember = "Name";
            playlistSelectorBox.SelectedIndex = -1;
            UpdateMusicControllerPanelAccess(false);
        }

        private void UpdateMusicControllerPanelAccess(bool enabled)
        {
            foreach (Control control in musicControllerPanel.Controls)
            {
                control.Enabled = enabled;
            }
        }

        private void playlistSelectorBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var comboBox = (ComboBox)sender;

            if (comboBox.SelectedIndex == -1 || comboBox.SelectedItem is not Playlist playlist)
            {
                return;
            }

            MediaManager.CurrentPlaylistId = playlist.PlaylistId;
            Console.WriteLine(playlist.PlaylistId);
            UpdateMusicControllerPanelAccess(true);
        }

        async private void playButton_Click(object sender, EventArgs e)
        {
            if (!MediaManager.IsPlaying)
            {
                await MediaManager.Play();
            }
            else
            {
                MediaManager.Pause();
            }
        }

        async private void skipButton_Click(object sender, EventArgs e)
        {
            await MediaManager.Skip();
        }

        async private void previousButton_Click(object sender, EventArgs e)
        {
            await MediaManager.Previous();
        }
    }
}
