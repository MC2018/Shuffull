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
            var authorized = await AuthManager.RefreshAuthentication(username, password);

            if (authorized)
            {
                ShowHome();
            }
            else
            {
                errorLabel.Text = "Invalid username/password";
            }
        }

        async private void signUpButton_Click(object sender, EventArgs e)
        {
            var username = usernameTextBox.Text;
            var password = passwordTextBox.Text;
            var authorized = await AuthManager.CreateAccount(username, password);

            if (authorized)
            {
                ShowHome();
            }
            else
            {
                errorLabel.Text = "Issue occurred";
            }
        }
    }
}
