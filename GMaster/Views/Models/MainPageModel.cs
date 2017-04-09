﻿namespace GMaster.Views
{
    using System;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Annotations;
    using Camera;
    using Logger;
    using Windows.ApplicationModel;
    using Windows.UI.Xaml;

    public class MainPageModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer cameraRefreshTimer;

        private DeviceInfo selectedDevice;

        public MainPageModel()
        {
            LumixManager = new LumixManager(CultureInfo.CurrentCulture.TwoLetterISOLanguageName);

            LumixManager.DeviceDiscovered += Lumix_DeviceDiscovered;

            cameraRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            cameraRefreshTimer.Tick += CameraRefreshTimer_Tick;
            cameraRefreshTimer.Start();

            ConnectableDevices.CollectionChanged += ConnectableDevices_CollectionChanged;
            Wifi.AutoconnectAlways = GeneralSettings.WiFiAutoconnectAlways;
            foreach (string ap in GeneralSettings.WiFiAutoconnectAccessPoints.Value)
            {
                Wifi.AutoconnectAccessPoints.Add(ap);
            }

            Wifi.PropertyChanged += Wifi_PropertyChanged;
        }

        private void Wifi_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(WiFiHelper.AutoconnectAlways):
                    GeneralSettings.WiFiAutoconnectAlways.Value = Wifi.AutoconnectAlways;
                    break;
                case nameof(WiFiHelper.AutoconnectAccessPoints):
                    GeneralSettings.WiFiAutoconnectAccessPoints.Value = Wifi.AutoconnectAccessPoints;
                    break;
            }
        }

        public async Task Init()
        {
            await Wifi.Init();
            await LoadLutsInfo();
        }

        public event Action<Lumix> CameraDisconnected;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<DeviceInfo> ConnectableDevices { get; } = new ObservableCollection<DeviceInfo>();

        public ObservableCollection<ConnectedCamera> ConnectedCameras { get; } =
            new ObservableCollection<ConnectedCamera>();

        public Donations Donations { get; } = new Donations();

        public GeneralSettings GeneralSettings { get; } = new GeneralSettings();

        public ObservableCollection<LutInfo> InstalledLuts { get; } = new ObservableCollection<LutInfo>();

        public object IsDebug => Debugger.IsAttached;

        public LumixManager LumixManager { get; }

        public DeviceInfo SelectedDevice
        {
            get => selectedDevice;

            set
            {
                selectedDevice = value;
                OnPropertyChanged();
            }
        }

        public string Version
        {
            get
            {
                var ver = Package.Current.Id.Version;
                return $"v{ver.Major}.{ver.Minor}.{ver.Build}";
            }
        }

        public CameraViewModel View1 { get; } = new CameraViewModel();

        public WiFiHelper Wifi { get; } = new WiFiHelper();

        public void AddConnectableDevice(DeviceInfo device)
        {
            ConnectableDevices.Add(device);
            if (SelectedDevice == null)
            {
                SelectedDevice = device;
            }
        }

        public ConnectedCamera AddConnectedDevice(Lumix lumix)
        {
            ConnectableDevices.Remove(lumix.Device);
            if (!GeneralSettings.Cameras.TryGetValue(lumix.Udn, out var settings))
            {
                settings = new CameraSettings(lumix.Udn);
            }

            settings.GeneralSettings = GeneralSettings;

            var connectedCamera = new ConnectedCamera
            {
                Camera = lumix,
                Model = this,
                Settings = settings,
                SelectedLut = InstalledLuts.SingleOrDefault(l => l?.Id == settings.LutId),
                SelectedAspect = settings.Aspect,
                IsAspectAnamorphingVideoOnly = settings.IsAspectAnamorphingVideoOnly
            };

            ConnectedCameras.Add(connectedCamera);
            lumix.Disconnected += Lumix_Disconnected;
            return connectedCamera;
        }

        public async Task ConnectCamera(DeviceInfo modelSelectedDevice)
        {
            var lumix = await LumixManager.ConnectCamera(modelSelectedDevice);
            var connectedCamera = AddConnectedDevice(lumix);

            if (View1.SelectedCamera == null)
            {
                View1.SelectedCamera = connectedCamera;
            }
        }


        public async Task LoadLutsInfo()
        {
            var lutFolder = await App.GetLutsFolder();

            foreach (var file in (await lutFolder.GetFilesAsync()).Where(f => f.FileType == ".info"))
            {
                InstalledLuts.Add(await LutInfo.LoadfromFile(file));
            }
        }

        public async Task StartListening()
        {
            await LumixManager.StartListening();
            var task = Task.Run(async () => await LumixManager.SearchCameras());
        }

        public void StopListening()
        {
            LumixManager.StopListening();
        }

        protected virtual void OnCameraDisconnected(Lumix obj)
        {
            CameraDisconnected?.Invoke(obj);
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void CameraRefreshTimer_Tick(object sender, object e)
        {
            await LumixManager.SearchCameras();
        }

        private void ConnectableDevices_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ConnectableDevices));
        }

        private async void Lumix_DeviceDiscovered(DeviceInfo dev)
        {
            try
            {
                await App.RunAsync(async () =>
                {
                    var camerafound = false;
                    var cameraauto = false;
                    if (GeneralSettings.Cameras.TryGetValue(dev.Uuid, out var settings))
                    {
                        cameraauto = settings.Autoconnect;
                        camerafound = true;
                    }

                    if ((camerafound && cameraauto) || (!camerafound && GeneralSettings.Autoconnect))
                    {
                        try
                        {
                            await ConnectCamera(dev);
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }
                    else
                    {
                        AddConnectableDevice(dev);
                    }
                });
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void Lumix_Disconnected(Lumix lumix, bool stillAvailbale)
        {
            lumix.Disconnected -= Lumix_Disconnected;
            ConnectedCameras.Remove(ConnectedCameras.Single(c => c.Udn == lumix.Udn));
            if (stillAvailbale)
            {
                AddConnectableDevice(lumix.Device);
            }

            OnCameraDisconnected(lumix);
        }
    }
}