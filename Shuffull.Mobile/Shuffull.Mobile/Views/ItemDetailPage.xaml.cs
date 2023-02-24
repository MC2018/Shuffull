using Shuffull.Mobile.ViewModels;
using System.ComponentModel;
using Xamarin.Forms;

namespace Shuffull.Mobile.Views
{
    public partial class ItemDetailPage : ContentPage
    {
        public ItemDetailPage()
        {
            InitializeComponent();
            BindingContext = new ItemDetailViewModel();
        }
    }
}