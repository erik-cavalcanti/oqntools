namespace OQNTools.Everse.UI
{
    using System.Windows;
    using OQNTools.Everse;
    using OQNTools.Everse.Utils;

    /// <summary>
    /// Resources.
    /// </summary>
    public partial class Resources : ResourceDictionary
    {
        private void EngworksLink(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Hyperlink.Run(Links.everseWebsite);
        }

        private void AddInLink(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Hyperlink.Run("https://e-verse.com");
        }
    }
}
