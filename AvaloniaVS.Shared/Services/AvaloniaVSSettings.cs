using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using Serilog;
using Serilog.Events;

namespace AvaloniaVS.Services
{
    [Export(typeof(IAvaloniaVSSettings))]
    public class AvaloniaVSSettings : IAvaloniaVSSettings, INotifyPropertyChanged
    {
        private const string SettingsKey = nameof(AvaloniaVSSettings);
        private readonly WritableSettingsStore _settings;
        private LogEventLevel _minimumLogVerbosity = LogEventLevel.Information;
        private string _zoomLevel;
        private bool _usageTracking;

        [ImportingConstructor]
        public AvaloniaVSSettings(SVsServiceProvider vsServiceProvider)
        {
            var shellSettingsManager = new ShellSettingsManager(vsServiceProvider);
            _settings = shellSettingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
            Load();
        }

        public LogEventLevel MinimumLogVerbosity
        {
            get => _minimumLogVerbosity;
            set
            {
                if (_minimumLogVerbosity != value)
                {
                    _minimumLogVerbosity = value;
                    RaisePropertyChanged();
                }
            }
        }

        public string ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                if (_zoomLevel != value)
                {
                    _zoomLevel = value;
                    RaisePropertyChanged();
                }
            }
        }

        public bool UsageTracking
        {
            get => _usageTracking;
            set
            {
                if (_usageTracking != value)
                {
                    _usageTracking = value;
                    RaisePropertyChanged();
                }
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;

        public void Load()
        {
            try
            {
                MinimumLogVerbosity = (LogEventLevel)_settings.GetInt32(
                    SettingsKey,
                    nameof(MinimumLogVerbosity),
                    (int)LogEventLevel.Information);
                ZoomLevel = _settings.GetString(
                    SettingsKey,
                    nameof(ZoomLevel),
                  "100%");
                UsageTracking = _settings.GetBoolean(
                    SettingsKey,
                    nameof(UsageTracking),
                    true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load settings");
            }
        }

        public void Save()
        {
            try
            {
                if (!_settings.CollectionExists(SettingsKey))
                {
                    _settings.CreateCollection(SettingsKey);
                }

                _settings.SetInt32(SettingsKey, nameof(MinimumLogVerbosity), (int)MinimumLogVerbosity);
                _settings.SetString(SettingsKey, nameof(ZoomLevel), ZoomLevel);
                _settings.SetBoolean(SettingsKey, nameof(UsageTracking), UsageTracking);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save settings");
            }
        }

        private void RaisePropertyChanged([CallerMemberName] string propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
