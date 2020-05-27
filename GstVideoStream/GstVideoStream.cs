namespace AzureFaceDoor.Client
{
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;
    using Gst;
    using Gst.App;

    /// <summary>
    /// Creates and handles a GStreamer pipeline providing access to raw video samples and pipeline events 
    /// </summary>
    public class GstVideoStream : IDisposable
    {
        public delegate void NewFrameHandler(GstVideoStream sender, IGstVideoFrameContext frameContext);
        public delegate void ErrorHandler(GstVideoStream sender, GLib.GException error, string debug);
        public delegate void MessageHandler(GstVideoStream sender, Gst.Message message);
        public delegate void StateChangedHandler(GstVideoStream sender, State oldState, State newState, State pendingState);
        public delegate void EndOfStreamHandler(GstVideoStream sender);

        private const int _renderTimerFrequency = 60;
        private const int _messageTimerFrequency = 30;

        private Pipeline _pipeline = null;
        private AppSink _videoSink = null;
        private string _videoSinkName = string.Empty;

        private CancellationTokenSource _cancelSource = new CancellationTokenSource();
        private Timer _renderTimer = null;
        private Timer _messageTimer = null;
        private long _sampleLock = 0;

        public event NewFrameHandler NewFrame;
        public event ErrorHandler Error;
        public event MessageHandler Message;
        public event StateChangedHandler StateChanged;
        public event EndOfStreamHandler EndOfStream;

        public Pipeline GstPipeline { get => _pipeline; }
        public bool IsSynchronized { get; private set; }
        public string GstCommandLine { get; private set; }

        private GstVideoStream()
        {            
        }

        /// <summary>
        /// Constructs GstVideoStream with a web camera source 
        /// width and height parameters - desired camera stream resolution 
        /// If zeroes - runs with default resolution 
        /// </summary>
        public GstVideoStream(int cameraDeviceIndex, int width, int height)
        {
            InitializeGst();

            string sourceColorSpace = "RGBA";
            string resolutionString = (width > 0 && height > 0) ?
                                        $",width={width},height={height}" :
                                        "";
            string sourceString = $"autovideosrc ! video/x-raw,format={sourceColorSpace}{resolutionString}";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                sourceString = $"avfvideosrc device-index={cameraDeviceIndex} ! video/x-raw,format=BGRA{resolutionString}";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            {
                sourceString = $"v4l2src device=/dev/video{cameraDeviceIndex} ! video/x-raw,format={sourceColorSpace}{resolutionString}";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                sourceString = $"ksvideosrc device-index={cameraDeviceIndex} ! video/x-raw,format={sourceColorSpace}{resolutionString}";
            }

            LaunchAppSinkString($"{sourceString} ! queue ! videoconvert ! appsink");
        }

        /// <summary>
        /// Constructs GstVideoStream with URI source 
        /// sourceOptions - additional options for uridecodebin 
        /// synchronized - if true, the stream will be synchronized to the clock (may cause skipping frames, but important for real-time)
        /// </summary>
        public GstVideoStream(string sourceUri, string sourceOptions = null, bool synchronized = false)
        {
            InitializeGst();

            bool validUri = false;
            if (Gst.Uri.IsValid(sourceUri))
            {
                var protocol = Gst.Uri.GetProtocol(sourceUri);
                if (Gst.Uri.ProtocolIsValid(protocol) && Gst.Uri.ProtocolIsSupported(URIType.Src, protocol))
                {
                    validUri = true;
                }
            }
            if (!validUri)
            {
                // trying as a file path (replacing '\' on Windows)
                sourceUri = "file://" + sourceUri.Replace('\\', '/');
            }

            string options = (!string.IsNullOrWhiteSpace(sourceOptions))? sourceOptions : string.Empty;
            IsSynchronized = synchronized;

            LaunchAppSinkString($"uridecodebin uri=\"{sourceUri}\" {sourceOptions} ! queue ! videoconvert ! appsink");
        }

        /// <summary>
        /// Creates GstVideoStream with an arbitrary gstreamer pipeline command line.  
        /// Last element of the pipeline must be appsink.
        /// synchronized - if true, the stream will be synchronized to the clock (may cause skipping frames, but important for real-time)
        /// </summary>
        public static GstVideoStream FromAppSinkCommandLine(string commandLine, bool synchronized = false)
        {
            GstVideoStream stream = new GstVideoStream();
            stream.IsSynchronized = synchronized;
            stream.LaunchAppSinkString(commandLine);
            return stream;
        } 

        private void LaunchAppSinkString(string commandLine)
        {
            InitializeGst();

            string trimmedCmd = commandLine.Trim();
            if (!trimmedCmd.EndsWith("appsink", true, null))
            {
                throw new ArgumentException("Command line should end with 'appsink'", nameof(commandLine));
            }

            GstCommandLine = trimmedCmd;

            _videoSinkName = $"appSink_{Guid.NewGuid().ToString().Replace('-','_')}";
            _pipeline =  Gst.Parse.Launch(trimmedCmd.Replace("appsink", $"appsink name={_videoSinkName}", true, null)) as Pipeline;

            InitializeAppSink();
        }

        /// Starts the pipeline
        public void Play()
        {
            _pipeline?.SetState(State.Playing);
        }

        /// Pauses the pipeline
        public void Pause()
        {
            _pipeline?.SetState(State.Paused);
        }

        /// Stops the pipeline
        public void Stop()
        {
            _pipeline?.SetState(State.Null);
        }

        /// Returns a task that completes when one of Messages specified in filter occurs. 
        /// The task returns true if it completed because of of the messages came within specified timeout. 
        /// If timeout is zero, the task will wait forever.
        public System.Threading.Tasks.Task<bool> WaitForMessageAsync(MessageType filter, TimeSpan timeout)
        {
            EventWaitHandle eventHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
            MessageHandler msgHandler = null; 
            
            msgHandler = new MessageHandler( (GstVideoStream sender, Gst.Message message) =>
            {
                if((message.Type & filter) == message.Type)
                {
                    this.Message -= msgHandler;
                    eventHandle.Set();
                }
            });

            this.Message += msgHandler;

            return System.Threading.Tasks.Task.Run<bool>(() =>
            {
                bool bRes;
                if(timeout == TimeSpan.Zero)
                {
                    bRes = eventHandle.WaitOne();
                }
                else
                {
                    bRes = eventHandle.WaitOne(timeout);
                }
                
                eventHandle.Dispose();
                return bRes;
            }, _cancelSource.Token);
        }

        /// Frees unmanaged resources
        public void Dispose()
        {
            Stop();
            _cancelSource.Cancel();
            _messageTimer.DisposeAsync();
            _renderTimer.DisposeAsync();
        }

        private void InitializeGst()
        {
            if (!Gst.Application.InitCheck())
            {
                Gst.Application.Init();
            }
            GtkSharp.GstreamerSharp.ObjectManager.Initialize();
        }

        private void InitializeAppSink()
        {
            _videoSink = _pipeline.GetChildByName(_videoSinkName) as AppSink;
            _videoSink["caps"] = Caps.FromString("video/x-raw,format=RGBA");
            _videoSink.Drop = true;
            _videoSink.Qos = true;
            _videoSink.Sync = IsSynchronized;

            _renderTimer = new Timer(RenderTimerProc, this, 0, 1000 / _renderTimerFrequency);
            _messageTimer = new Timer(MessageTimerProc, this, 0, 1000 / _messageTimerFrequency);
        }

        private void RenderTimerProc(object _)
        {
            if (System.Threading.Interlocked.CompareExchange(ref _sampleLock, 1, 0) == 0)
            {

                try
                {
                    if (_pipeline != null && _videoSink != null)
                    {
                        PullAndProcessVideoSample();
                    }
                }
                finally
                {
                    System.Threading.Interlocked.Decrement(ref _sampleLock);
                }
            }
        }

        private void MessageTimerProc(object _)
        {
            if (_pipeline != null)
            {
                using (var bus = _pipeline.Bus)
                {
                    var message = bus.Poll(MessageType.Any, 0);
                    if (message != null)
                    {
                        OnNewMessage(message);
                        message.Dispose();
                    }
                }
            }
        }

        private void PullAndProcessVideoSample()
        {
            if (_videoSink != null)
            {
                Sample sample = _videoSink.TryPullSample(0);
                if(sample != null)
                {
                    using (sample)
                    {
                        using (var context = new GstVideoFrameContext(sample))
                        {
                            NewFrame?.Invoke(this, context);
                        }
                    }
                }
            }
        }

        private void OnNewMessage(Gst.Message message)
        {
            switch(message.Type)
            {
                case MessageType.Error:
                    GLib.GException ex;
                    string debug;
                    message.ParseError(out ex, out debug);
                    Error?.Invoke(this, ex, debug);
                    break;
                case MessageType.Eos:
                    EndOfStream?.Invoke(this);
                    break;
                case MessageType.StateChanged:
                    State oldState, newState, pendingState;
                    message.ParseStateChanged(out oldState, out newState, out pendingState);
                    StateChanged?.Invoke(this, oldState, newState, pendingState);
                    break;
            }

            Message?.Invoke(this, message);
        }

        
    }
}