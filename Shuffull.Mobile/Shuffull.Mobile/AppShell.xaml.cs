using Shuffull.Mobile.ViewModels;
using Shuffull.Mobile.Views;
using System;
using System.Collections.Generic;
using Xamarin.Forms;

namespace Shuffull.Mobile
{
    public partial class AppShell : Xamarin.Forms.Shell
    {
        public AppShell()
        {
            InitializeComponent();
            //Routing.RegisterRoute(nameof(ItemDetailPage), typeof(ItemDetailPage));
            //Routing.RegisterRoute(nameof(NewItemPage), typeof(NewItemPage));
        }

        private async void OnMenuItemClicked(object sender, EventArgs e)
        {
            if (sender is MenuItem)
            {
                var menuItem = sender as MenuItem;
                var path = menuItem.CommandParameter as string;

                await Shell.Current.GoToAsync($"//{path}");
                Shell.Current.FlyoutIsPresented = false;
            }
        }
    }
}
