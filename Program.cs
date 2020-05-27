using System;
using System.Timers;
using CommandLine;
using Gst;

namespace AzureFaceDoor.Client
{
    class Program
    {
        private const int MeasureFPSOnFrameCount = 300; // We measure and print FPS every 300 frames 

        private static long _intervalFrameCount = 0; // Frame counter 
        private static double _intervalFps = -1; // Measured FPS 

        private static System.DateTime _startTime; // When FPS measurement interval starts 
        private static Timer _timer; // "hearthbeat" timer 
        private static bool _started = false;
        private static GstVideoStream _stream = null; // SGtreamer video stream object 

        static void Main(string[] args)
        {
            if(args.Length == 0)
            {
                Console.WriteLine($"Please provide GStreamer command line, camera index (--camera_index option) or source uri (--source_uri option).");
                return;
            }

            try
            {
                var result = Parser.Default.ParseArguments<CommandLineOptions>(args);
                result.WithParsed<CommandLineOptions>(options =>
                      {
                          if (options.CameraIndex != -1) // camera index specified
                          {
                              _stream = new GstVideoStream(options.CameraIndex, 0, 0);
                          }
                          else if (!string.IsNullOrEmpty(options.URI)) // URI source provided 
                          {
                              _stream = new GstVideoStream(options.URI);
                          }
                      });

                // if neither --camera_index nor --source_uri options were provided, pass the command line to GStreamer command line parser 
                if (_stream == null)
                {
                    _stream = GstVideoStream.FromAppSinkCommandLine(string.Join(' ', args));
                }
            }
            catch(GLib.GException ex)
            {
                Console.WriteLine($"GStreamer pipeline creating failure: {ex.Message}.");
                return;
            }

            // creating heartbeat timer - for showing that the app is working and for printing FPS every MeasureFPSOnFrameCount (300) frames 
            _timer = new Timer(1000);
            _timer.Elapsed += OnTimerElapsed;

            Console.WriteLine($"{Environment.NewLine}Starting GStreamer video stream at {System.DateTime.Now}.{Environment.NewLine}Command line: {_stream.GstCommandLine}");

            _stream.NewFrame += OnNewFrame; // new frame handler 
            _stream.Error += OnError;       // error handler 
            _stream.EndOfStream += OnEOS;   // end of stream handler 

            _stream.Play(); // start pipeline 

            _timer.Start(); // start timer 
            
            _started = true; 
            Console.WriteLine("Started");

            // waiting for the end of stream or a stream error 
            _stream.WaitForMessageAsync(MessageType.Eos | MessageType.Error, TimeSpan.Zero).Wait();
            Console.WriteLine($"{Environment.NewLine}Stream stopped at {System.DateTime.Now}.");

            _timer.Stop();
            _stream.Dispose();
        }

        private static void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_started)
            {
                Console.Write(".");

                // if FPS was calculated, print it and reset back to -1 
                if (_intervalFps >= 0)
                {
                    Console.WriteLine($"\r{System.DateTime.Now} = {_intervalFps} FPS");
                    _intervalFps = -1;
                }
            }
        }

        private static void OnNewFrame (GstVideoStream sender, IGstVideoFrameContext frameContext)
        {
            // if frame count is zero, reset interval start time 
            if (_intervalFrameCount == 0)
            {
                _startTime = System.DateTime.UtcNow;
            }

            // increment number of frames in the current interval 
            _intervalFrameCount++;

            // if frame counter  is equal to MeasureFPSOnFrameCount (300), calculate FPS and reset frame counter  
            if(_intervalFrameCount == MeasureFPSOnFrameCount)
            {
                var intervalSpan = System.DateTime.UtcNow - _startTime;
                _intervalFps = _intervalFrameCount / intervalSpan.TotalSeconds;
                _intervalFrameCount = 0;
            }

            /*
                Use frameContext.Buffer for accessing raw buffer as IntPtr. 
                Make sure the format is what you expect by checking frameContext.Format (RGBA is the only supported one for now) 
                Use frameContext.CopyTo for copying raw frame data to another unmanaged buffer (e.g. WritableBitmap) 
            */
        }

        private static void OnError(GstVideoStream sender, GLib.GException error, string debug)
        {
            Console.WriteLine($"{Environment.NewLine}ERROR: {error.Message} ({debug})");
        }
        
        private static void OnEOS(GstVideoStream sender)
        {
            Console.WriteLine($"{Environment.NewLine}=== End Of Stream ===");
        }
    }
}
