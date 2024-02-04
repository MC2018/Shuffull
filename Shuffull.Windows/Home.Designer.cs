namespace Shuffull.Windows
{
    partial class Home
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            playlistSelectorBox = new ComboBox();
            musicControllerPanel = new Panel();
            playPlaylistButton = new Button();
            previousButton = new Button();
            skipButton = new Button();
            playButton = new Button();
            activelyDownloadCheckBox = new CheckBox();
            logoutButton = new Button();
            musicControllerPanel.SuspendLayout();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(454, 155);
            label1.Name = "label1";
            label1.Size = new Size(38, 15);
            label1.TabIndex = 0;
            label1.Text = "label1";
            // 
            // playlistSelectorBox
            // 
            playlistSelectorBox.DropDownStyle = ComboBoxStyle.DropDownList;
            playlistSelectorBox.FormattingEnabled = true;
            playlistSelectorBox.Location = new Point(105, 290);
            playlistSelectorBox.Name = "playlistSelectorBox";
            playlistSelectorBox.Size = new Size(168, 23);
            playlistSelectorBox.TabIndex = 1;
            playlistSelectorBox.SelectedIndexChanged += playlistSelectorBox_SelectedIndexChanged;
            // 
            // musicControllerPanel
            // 
            musicControllerPanel.Controls.Add(playPlaylistButton);
            musicControllerPanel.Controls.Add(previousButton);
            musicControllerPanel.Controls.Add(skipButton);
            musicControllerPanel.Controls.Add(playButton);
            musicControllerPanel.Location = new Point(12, 319);
            musicControllerPanel.Name = "musicControllerPanel";
            musicControllerPanel.Size = new Size(776, 119);
            musicControllerPanel.TabIndex = 2;
            // 
            // playPlaylistButton
            // 
            playPlaylistButton.Location = new Point(122, 3);
            playPlaylistButton.Name = "playPlaylistButton";
            playPlaylistButton.Size = new Size(99, 23);
            playPlaylistButton.TabIndex = 4;
            playPlaylistButton.Text = "Play Playlist";
            playPlaylistButton.UseVisualStyleBackColor = true;
            playPlaylistButton.Click += playPlaylistButton_Click;
            // 
            // previousButton
            // 
            previousButton.Location = new Point(280, 32);
            previousButton.Name = "previousButton";
            previousButton.Size = new Size(75, 23);
            previousButton.TabIndex = 2;
            previousButton.Text = "Previous";
            previousButton.UseVisualStyleBackColor = true;
            previousButton.Click += previousButton_Click;
            // 
            // skipButton
            // 
            skipButton.Location = new Point(442, 32);
            skipButton.Name = "skipButton";
            skipButton.Size = new Size(75, 23);
            skipButton.TabIndex = 1;
            skipButton.Text = "Skip";
            skipButton.UseVisualStyleBackColor = true;
            skipButton.Click += skipButton_Click;
            // 
            // playButton
            // 
            playButton.Location = new Point(361, 32);
            playButton.Name = "playButton";
            playButton.Size = new Size(75, 23);
            playButton.TabIndex = 0;
            playButton.Text = "Play/Pause";
            playButton.UseVisualStyleBackColor = true;
            playButton.Click += playButton_Click;
            // 
            // activelyDownloadCheckBox
            // 
            activelyDownloadCheckBox.AutoSize = true;
            activelyDownloadCheckBox.Location = new Point(232, 151);
            activelyDownloadCheckBox.Name = "activelyDownloadCheckBox";
            activelyDownloadCheckBox.Size = new Size(125, 19);
            activelyDownloadCheckBox.TabIndex = 3;
            activelyDownloadCheckBox.Text = "Actively Download";
            activelyDownloadCheckBox.UseVisualStyleBackColor = true;
            activelyDownloadCheckBox.CheckedChanged += activelyDownloadCheckBox_CheckedChanged;
            // 
            // logoutButton
            // 
            logoutButton.Location = new Point(650, 66);
            logoutButton.Name = "logoutButton";
            logoutButton.Size = new Size(75, 23);
            logoutButton.TabIndex = 4;
            logoutButton.Text = "Logout";
            logoutButton.UseVisualStyleBackColor = true;
            logoutButton.Click += logoutButton_Click;
            // 
            // Home
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(logoutButton);
            Controls.Add(activelyDownloadCheckBox);
            Controls.Add(musicControllerPanel);
            Controls.Add(playlistSelectorBox);
            Controls.Add(label1);
            Name = "Home";
            Text = "Home";
            FormClosing += form_FormClosing;
            musicControllerPanel.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private ComboBox playlistSelectorBox;
        private Panel musicControllerPanel;
        private Button previousButton;
        private Button skipButton;
        private Button playButton;
        private CheckBox activelyDownloadCheckBox;
        private Button playPlaylistButton;
        private Button logoutButton;
    }
}