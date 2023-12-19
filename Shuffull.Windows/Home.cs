using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Shuffull.Shared;
using Shuffull.Shared.Networking.Models;
using Shuffull.Shared.Networking.Models.Server;
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
        private bool _constructorLoading = true;

        public Home()
        {
            InitializeComponent();
            Core.Initialize();
            Task.Run(() => SyncManager.Initialize()).Wait();
            // set last played as current song
            MediaManager.QueueLastPlayedSong();

            label1.Text = Directories.MusicFolderAbsolutePath;
            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var playlists = context.Playlists.ToList();
            var localSessionData = context.LocalSessionData.FirstOrDefault();

            if (localSessionData == null)
            {
                throw new Exception("Local session data is null");
            }

            // Data loading
            var playlistIndex = playlists.FindIndex(x => x.PlaylistId == localSessionData.CurrentPlaylistId);
            playlistSelectorBox.DataSource = playlists;
            playlistSelectorBox.DisplayMember = "Name";
            UpdateMusicControllerPanelAccess(playlistIndex != -1);

            activelyDownloadCheckBox.Checked = localSessionData.ActivelyDownload;

            _constructorLoading = false;

            // Data setting
            Thread.Sleep(10);
            playlistSelectorBox.SelectedIndex = playlistIndex;
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

            if (_constructorLoading || comboBox.SelectedIndex == -1 || comboBox.SelectedItem is not Playlist playlist)
            {
                return;
            }

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

        async private void playPlaylistButton_Click(object sender, EventArgs e)
        {
            MediaManager.CurrentPlaylistId = ((Playlist)playlistSelectorBox.SelectedItem).PlaylistId;
            MediaManager.ClearQueue();
            await MediaManager.Skip();
        }

        async private void activelyDownloadCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (_constructorLoading)
            {
                return;
            }

            var context = Program.ServiceProvider.GetRequiredService<ShuffullContext>();
            var localSessionData = context.LocalSessionData.FirstOrDefault();

            if (localSessionData != null)
            {
                localSessionData.ActivelyDownload = activelyDownloadCheckBox.Checked;
                await context.SaveChangesAsync();
            }
        }

        async private void logoutButton_Click(object sender, EventArgs e)
        {
            await AuthManager.ClearAuthentication();
            Dispose();
        }
    }
}
