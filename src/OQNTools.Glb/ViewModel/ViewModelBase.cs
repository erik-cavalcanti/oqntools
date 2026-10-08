namespace OQNTools.Everse.ViewModel
{
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using OQNTools.Everse.Model;
    using OQNTools.Everse.Utils;

    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public virtual void Dispose()
        {
        }

        protected void OnPropertyChanged(UnitObject unitobject = null, [CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (unitobject != null)
            {
            #if REVIT2019 || REVIT2020

                SettingsConfig.SetValue("units", unitobject.DisplayUnitType.ToString());

            #else

            SettingsConfig.SetValue("units", unitobject.ForgeTypeId.TypeId.ToString());

            #endif
            }
        }
    }
}
