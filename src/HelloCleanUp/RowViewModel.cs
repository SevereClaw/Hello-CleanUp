using System.ComponentModel;
using HelloCleanUp.Core;

namespace HelloCleanUp
{
    /// <summary>One card in a list: a tweak, a cleanup target or a preinstalled app.</summary>
    public sealed class RowViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public Tweak Tweak { get; private set; }
        public CleanTarget Target { get; private set; }
        public AppEntry App { get; private set; }

        /// <summary>For apps: installed for the current account right now.</summary>
        public bool AppInstalled { get; set; }

        public string Title { get; private set; }
        public string Description { get; private set; }
        public bool Recommended { get; private set; }

        /// <summary>Last scanned size for cleanup targets (-1 = unknown).</summary>
        public long Size { get; set; }

        private bool _isChecked;
        public bool IsChecked
        {
            get { return _isChecked; }
            set { if (_isChecked != value) { _isChecked = value; Raise("IsChecked"); } }
        }

        private string _statusText = "…";
        public string StatusText
        {
            get { return _statusText; }
            private set { _statusText = value; Raise("StatusText"); }
        }

        private string _statusKind = "none";
        public string StatusKind
        {
            get { return _statusKind; }
            private set { _statusKind = value; Raise("StatusKind"); }
        }

        public RowViewModel(Tweak t)
        {
            Tweak = t; Title = t.Title; Description = t.Description; Recommended = t.Recommended;
            IsChecked = t.Recommended;
        }

        public RowViewModel(CleanTarget t)
        {
            Target = t; Title = t.Title; Description = t.Description; Recommended = t.Recommended;
            IsChecked = t.Recommended;
            Size = -1;
        }

        public RowViewModel(AppEntry app, string description, bool isChecked)
        {
            App = app; Title = app.Title; Description = description; Recommended = app.Recommended;
            IsChecked = isChecked;
        }

        public void SetStatus(string text, string kind)
        {
            StatusText = text;
            StatusKind = kind;
        }

        public void SetTweakState(TweakState s)
        {
            switch (s)
            {
                case TweakState.Applied: SetStatus("применено", "applied"); break;
                case TweakState.Partial: SetStatus("частично", "partial"); break;
                default: SetStatus("как в Windows", "off"); break;
            }
        }

        private void Raise(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
