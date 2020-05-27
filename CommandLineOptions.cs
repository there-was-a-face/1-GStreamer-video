namespace AzureFaceDoor.Client
{
    using CommandLine;    
    public class CommandLineOptions
    {
        [Option("camera_index", Default=-1, SetName="source", Required = false, HelpText = "Camera device index to use as video source")]
        public int CameraIndex { get; set; } = -1;

        [Option("source_uri", SetName = "source", Required = false, HelpText = "URI to use as video source")]
        public string URI { get; set; }
    }
}