using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Session;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace VolumeControl
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    // ReSharper disable once RedundantExtendsListEntry
    public partial class App : Application, ClientListener
    {
        public const string APPLICATION_VERSION = "v8";
        public const int PROTOCOL_VERSION = 7;

        private static readonly object m_lock = new object();

        private CoreAudioController m_coreAudioController;
        public Server Server
        {
            get;
            private set;
        }

        private PcAudio m_audioState;
        private UpdateListener m_updateListener;
        private readonly Dictionary<string, AudioSessionKeeper> m_sessions = new Dictionary<string, AudioSessionKeeper>();

        private AudioSessionVolumeListener m_sessionVolumeListener;
        private AudioSessionMuteListener m_sessionMuteListener;

        public static App instance
        {
            get;
            private set;
        }

        // ReSharper disable once IdentifierTypo
        private readonly JsonSerializerSettings m_jsonsettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Converters = new JsonConverter[] { new FloatFormatConverter() }
        };

        private readonly Subject<bool> m_updateSubject = new Subject<bool>();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            instance = this;

            if(!enforceSingleInstance())
                return;

            var _ = (TaskbarIcon) FindResource("NotificationIcon");

            var OpenCommandBinding = new CommandBinding(ApplicationCommands.Open, OpenCommandExecuted);
            CommandManager.RegisterClassCommandBinding(typeof(object), OpenCommandBinding);

            var CloseCommandBinding = new CommandBinding(ApplicationCommands.Close, CloseCommandExecuted);
            CommandManager.RegisterClassCommandBinding(typeof(object), CloseCommandBinding);

            init();

            var wnd = new MainWindow();
            wnd.Show();
        }

        private bool enforceSingleInstance()
        {
            const string appName = "PcVolumeControl";

            var _ = new Mutex(true, appName, out var createdNew);

            // ReSharper disable once InvertIf
            if (!createdNew)
            {
                MessageBox.Show("The application is already running :)\n\nLook for the icon in your System Tray.", appName, MessageBoxButton.OK, MessageBoxImage.Information);

                //app is already running! Exiting the application  
                Current.Shutdown();
            }

            return createdNew;
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            stopServer();
        }

        private void OpenCommandExecuted(object target, ExecutedRoutedEventArgs e)
        {
            ToggleMainWindow();
        }

        private void CloseCommandExecuted(object target, ExecutedRoutedEventArgs e)
        {
            Current.Shutdown();
        }

        private void ToggleMainWindow()
        {
            if (!IsWindowOpen<MainWindow>())
            {
                var window = new MainWindow();
                window.Show();
            }
            else
            {
                GetOpenWindow<MainWindow>().Close();
            }
        }

        public static void StartOnBoot()
        {
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            var key = Registry.CurrentUser.OpenSubKey(path, true);
            key?.SetValue("Adventure", getExePath());
        }

        public static void RemoveOnBoot()
        {
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            var key = Registry.CurrentUser.OpenSubKey(path, true);
            key?.DeleteValue("Adventure", false);
        }

        private static string getExePath()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().Location;
        }

        private void init()
        {
            m_updateListener = new UpdateListener(this);
            m_updateSubject
                .Synchronize()
                .Throttle(TimeSpan.FromMilliseconds(10))
                .SubscribeOnDispatcher()
                .Subscribe(m_updateListener);

            m_sessionVolumeListener = new AudioSessionVolumeListener(this);
            m_sessionMuteListener = new AudioSessionMuteListener(this);

            m_coreAudioController = new CoreAudioController();

            m_coreAudioController.DefaultPlaybackDevice.GetCapability<IAudioSessionController>().SessionCreated.Subscribe(new AudioSessionAddedListener(this));
            m_coreAudioController.DefaultPlaybackDevice.GetCapability<IAudioSessionController>().SessionDisconnected.Subscribe(new AudioSessionRemovedListener(this));
            m_coreAudioController.AudioDeviceChanged.Subscribe(new DeviceChangeListener(this));

            var masterVolumeListener = new MasterVolumeListener(this);

            m_coreAudioController.DefaultPlaybackDevice.VolumeChanged
                                //.Throttle(TimeSpan.FromMilliseconds(10))
                                .Subscribe(masterVolumeListener);

            m_coreAudioController.DefaultPlaybackDevice.MuteChanged
                                //.Throttle(TimeSpan.FromMilliseconds(10))
                                .Subscribe(masterVolumeListener);

            new Thread(() =>
            {
                updateState(null);

                Server = new Server(this);
            }).Start();
        }

        private class UpdateListener : IObserver<bool>
        {
            private readonly App m_app;

            public UpdateListener(App app)
            {
                m_app = app;
            }

            public void OnCompleted()
            {

            }

            public void OnError(Exception error)
            {

            }

            public void OnNext(bool value)
            {
                m_app.updateAndDispatchAudioState();
            }
        }

        private void updateWindow()
        {
            if (!Current.Dispatcher.HasShutdownFinished)
            {
                Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ((MainWindow) Current.MainWindow)?.updateConnectionStatus();
                }));
            }
        }

        public void onServerStart()
        {
            updateWindow();
        }

        public void onServerEnd()
        {
            updateWindow();
        }

        public void onClientConnect()
        {
            requestUpdate();
        }

        private void dispatchAudioState()
        {
            if(Server != null)
            {
                ThreadPool.QueueUserWorkItem(o =>
                {
                    var json = JsonConvert.SerializeObject(m_audioState, m_jsonsettings);
                    //Console.WriteLine("Sending audio state: " + json);
                    //Console.WriteLine("Sending audio state");
                    Server.sendData(json);
                });
            }
        }

        public void startServer()
        {
            Server = new Server(this);
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public bool stopServer()
        {
            var stopped = false;
            if (Server != null)
            {
                Server.stop();
                Server = null;

                stopped = true;
            }

            updateWindow();

            return stopped;
        }

        private void updateAndDispatchAudioState()
        {
            updateState(null);

            if (m_audioState != null)
            {
                //Console.WriteLine("dispatching audio state");
                dispatchAudioState();
            }
            //else
            //{
                //Console.WriteLine("m_audioState NULL no dispatch");
            //}
        }

        public void requestUpdate()
        {
            m_updateSubject.OnNext(true);
        }

        public static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }

        private void cleanUpSessionKeepers()
        {
            var defaultDevice = m_coreAudioController.GetDefaultDevice(DeviceType.Playback, Role.Multimedia);

            IDictionary<string, IAudioSession> currentSessions = new Dictionary<string, IAudioSession>();
            foreach (var session in defaultDevice.GetCapability<IAudioSessionController>())
            {
                currentSessions[session.Id] = session;
            }

            var deadSessions =
                m_sessions.Values.Where(session => !currentSessions.ContainsKey(session.id())).ToList();

            foreach (var session in deadSessions)
            {
                session.Dispose();
                m_sessions.Remove(session.id());
            }
        }

        public void onClientMessage(string message, TcpClient tcpClient)
        {
            if (message != null)
            {
                ThreadPool.QueueUserWorkItem(o =>
                {
                    try
                    {
                        //Console.WriteLine("client message: " + message);
                        Debug.WriteLine(message);
                        var pcAudio = JsonConvert.DeserializeObject<PcAudio>(message, m_jsonsettings);

                        if(pcAudio == null)
                            return;

                        if (PROTOCOL_VERSION == pcAudio.protocolVersion)
                        {
                            updateState(pcAudio);
                        }
                        else
                        {
                            //Console.WriteLine("Bad version from client. Dropping client.");
                            tcpClient.Close();
                        }
                        //else
                        //{
                            //Console.WriteLine("Null message from client.");
                        //}
                    }
                    catch (JsonException)
                    {
                        //Console.WriteLine("Bad message from client. Dropping client.");
                        tcpClient.Close();
                    }
                });
            }
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        private bool updateState(PcAudio audioUpdate)
        {
            //Console.WriteLine("update");

            lock (m_lock)
            {
                var audioState = new PcAudio
                {
                    protocolVersion = PROTOCOL_VERSION,
                    applicationVersion = APPLICATION_VERSION
                };

                cleanUpSessionKeepers();

                var defaultDevice = m_coreAudioController.GetDefaultDevice(DeviceType.Playback, Role.Multimedia);
                if(defaultDevice == null)
                    return true;

                var defaultDeviceId = defaultDevice.Id.ToString();

                // Add all available audio devices to our list of device IDs
                var devices = m_coreAudioController.GetPlaybackDevices();
                foreach (var device in devices)
                {
                    if (device.State == DeviceState.Active)
                    {
                        audioState.deviceIds.Add(device.Id.ToString(), device.FullName);
                    }
                }

                // Master device updates
                if (audioUpdate?.defaultDevice != null)
                {
                    if (!audioUpdate.defaultDevice.deviceId.Equals(defaultDeviceId))
                    {
                        var deviceId = Guid.Parse(audioUpdate.defaultDevice.deviceId);

                        var newDefaultAudioDevice = m_coreAudioController.GetDevice(deviceId);
                        if (newDefaultAudioDevice != null)
                        {
                            //Console.WriteLine("Updated default audio device: " + audioUpdate.defaultDevice.deviceId);

                            newDefaultAudioDevice.SetAsDefault();
                            newDefaultAudioDevice.SetAsDefaultCommunications();

                            return false;
                        }
                        //else
                        //{
                        //Console.WriteLine("Failed to update default audio device. Could not find device for ID: " + audioUpdate.defaultDevice.deviceId);
                        //}
                    }
                    else
                    {
                        if (audioUpdate.defaultDevice.masterMuted != null || audioUpdate.defaultDevice.masterVolume != null)
                        {
                            if (audioUpdate.defaultDevice.masterMuted != null)
                            {
                                var muted = audioUpdate.defaultDevice.masterMuted ?? m_coreAudioController.DefaultPlaybackDevice.IsMuted;
                                //Console.WriteLine("Updating master mute: " + muted);

                                m_coreAudioController.DefaultPlaybackDevice.SetMuteAsync(muted).Wait();
                            }

                            // ReSharper disable once InvertIf
                            if (audioUpdate.defaultDevice.masterVolume != null)
                            {
                                var volume = audioUpdate.defaultDevice.masterVolume ?? (float)m_coreAudioController.DefaultPlaybackDevice.Volume;
                                //Console.WriteLine("Updating master volume: " + volume);

                                m_coreAudioController.DefaultPlaybackDevice.SetVolumeAsync(volume).Wait();
                            }

                            return false;
                        }
                    }
                }

                // Create our default audio device and populate it's volume and mute status
                var audioDevice = new AudioDevice(defaultDevice.FullName, defaultDeviceId);
                audioState.defaultDevice = audioDevice;

                var defaultPlaybackDevice = m_coreAudioController.DefaultPlaybackDevice;
                audioDevice.masterVolume = (float) defaultPlaybackDevice.Volume;
                audioDevice.masterMuted = defaultPlaybackDevice.IsMuted;

                // Go through all audio sessions
                foreach (var session in defaultDevice.GetCapability<IAudioSessionController>())
                {
                    if(session.IsSystemSession)
                        continue;

                    // If we haven't seen this before, create our book keeper
                    var sessionId = session.Id;
                    if (!m_sessions.ContainsKey(sessionId))
                    {
                        //Console.WriteLine("Found new audio session");

                        var sessionKeeper = new AudioSessionKeeper(session, m_sessionVolumeListener, m_sessionMuteListener);
                        m_sessions.Add(session.Id, sessionKeeper);
                    }

                    try
                    {
                        // Audio session update
                        if (audioUpdate?.defaultDevice?.deviceId != null)
                        {
                            if (audioUpdate.defaultDevice.deviceId.Equals(defaultDeviceId))
                            {
                                if (audioUpdate.defaultDevice.sessions != null && audioUpdate.defaultDevice.sessions.Count > 0)
                                {
                                    foreach(var sessionUpdate in audioUpdate.defaultDevice.sessions.
                                        Where(sessionUpdate => sessionUpdate.id.Equals(session.Id)))
                                    {
                                        //Console.WriteLine("Adjusting volume: " + sessionUpdate.name + " - " + sessionUpdate.volume);
                                        //Console.WriteLine("Adjusting mute: " + sessionUpdate.muted + " - " + sessionUpdate.muted);

                                        session.SetVolumeAsync(sessionUpdate.volume).Wait();
                                        session.SetMuteAsync(sessionUpdate.muted).Wait();

                                        break;
                                    }
                                }
                            }
                        }

                        var sessionName = session.DisplayName;
                        if (sessionName == null || sessionName.Trim() == "")
                        {
                            using var process = Process.GetProcessById(session.ProcessId);
                        }

                        var audioSession = new AudioSession(sessionName, session.Id, (float)session.Volume, session.IsMuted);
                        audioDevice.sessions.Add(audioSession);
                    }
                    catch (Exception)
                    {
                        //Console.WriteLine(e.Message);
                        //Console.WriteLine(e.StackTrace);
                        //Console.WriteLine("Process in audio session no longer alive");

                        var sessionKeeper = m_sessions[session.Id];
                        m_sessions.Remove(session.Id);
                        sessionKeeper.Dispose();
                    }
                }

                m_audioState = audioState;
            }

            return true;
        }

        private static bool IsWindowOpen<T>(string name = "") where T : Window
        {
            return string.IsNullOrEmpty(name)
               ? Current.Windows.OfType<T>().Any()
               : Current.Windows.OfType<T>().Any(w => w.Name.Equals(name));
        }

        private static Window GetOpenWindow<T>() where T : Window
        {
            return Current.Windows.OfType<T>().First();
        }
    }
}
