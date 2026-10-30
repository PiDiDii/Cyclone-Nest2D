using System.Windows;

namespace CycloneNest2D.Desktop;

public partial class ConfirmationDialog : Window
{
    private ConfirmationDialog(string title, string message, string confirmText, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        ConfirmButton.Content = confirmText;
    }

    public static bool Confirm(Window owner, string title, string message, string confirmText = "Xác nhận") =>
        new ConfirmationDialog(title, message, confirmText, owner).ShowDialog() == true;

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
