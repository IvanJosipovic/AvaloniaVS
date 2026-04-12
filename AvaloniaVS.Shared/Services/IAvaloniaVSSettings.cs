using System.ComponentModel;
using Serilog.Events;

namespace AvaloniaVS.Services
{
    public interface IAvaloniaVSSettings : INotifyPropertyChanged
    {
        LogEventLevel MinimumLogVerbosity { get; set; }
        string ZoomLevel { get; set; }
        void Save();
        void Load();
        bool UsageTracking { get; set; }
    }
}
