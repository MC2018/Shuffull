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
    public partial class Login : Form
    {
        public Login()
        {
            InitializeComponent();

            if (!AuthManager.IsExpired)
            {
                ShowHome();
            }
        }

        private void ShowHome()
        {
            Hide();
            new Home().ShowDialog();
            Show();
        }

        async private void loginButton_Click(object sender, EventArgs e)
        {
            var username = usernameTextBox.Text;
            var password = passwordTextBox.Text;

            try
            {
                await AuthManager.RefreshAuthentication(username, password);
                ShowHome();
            }
            catch (HttpRequestException ex)
            {
                errorLabel.Text = ex.Message;
            }
            catch
            {
                errorLabel.Text = "An unknown error occurred";
                return;
            }
        }

        async private void signUpButton_Click(object sender, EventArgs e)
        {
            var username = usernameTextBox.Text;
            var password = passwordTextBox.Text;

            try
            {
                await AuthManager.CreateAccount(username, password);
                ShowHome();
            }
            catch (HttpRequestException ex)
            {
                errorLabel.Text = ex.Message;
            }
            catch
            {
                errorLabel.Text = "An unknown error occurred";
                return;
            }
        }
    }
}
