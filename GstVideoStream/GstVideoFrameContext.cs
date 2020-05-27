namespace AzureFaceDoor.Client
{
    using System;
    using Gst;
    using Buffer = Gst.Buffer;

    /// <summary>
    /// Provides access to GStreamer video frame's raw data in the context of a short-live event (new video sample)
    /// </summary>
    public class GstVideoFrameContext: IGstVideoFrameContext, IDisposable
    {
        /// MapInfo object - GStreamer buffer mapped to CPU memory 
        private MapInfo _mapInfo;

        /// Buffer - GStreamer buffer represented by this GstVideoFrameContext object 
        private Buffer _buffer = null;

        /// Frame Width
        public int Width { get; private set; }

        /// Frame Height
        public int Height { get; private set; }

        /// Frame Format
        public string Format { get; private set; }

        /// Frame stride size (size in bytes of a single scan-line)
        public long Stride { get; private set; }

        /// Frame memory size in bytes
        public long Size { get; private set; }

        /// Frame raw data buffer
        public IntPtr Buffer
        {
            get { return _mapInfo.DataPtr; }
        }

        /// Copies frame's raw data to an unmanaged memory pointer destination
        public void CopyTo(IntPtr destination, long destinationSizeInBytes = 0)
        {
            long memSize = destinationSizeInBytes > 0 ?
                        System.Math.Min(destinationSizeInBytes, Stride * Height) : 
                        Stride * Height;
            _mapInfo.CopyTo(destination, memSize);
        }

        /// Frees unmanaged resources
        public void Dispose()
        {
            if (_buffer != null && _mapInfo.DataPtr != null)
            {
                _buffer.Unmap(_mapInfo);
            }
            _buffer?.Dispose();
            _buffer = null;
        }

        /// Constructs a GstVideoFrameContext from Gstreamer Sample object
        public GstVideoFrameContext(Sample sample) 
        {
            if(sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }

            _buffer = sample.Buffer;

            if (_buffer != null && _buffer.Map(out _mapInfo, MapFlags.Read))
            {
                using(var caps = sample.Caps)
                {
                    using(var cap = caps[0])
                    {
                        Format = cap.GetString("format");

                        int width, height;
                        cap.GetInt("width", out width);
                        cap.GetInt("height", out height);
                        Width = width;
                        Height = height;

                        Size = (long)_mapInfo.Size;
                        Stride = Size / Height;
                    }
                }
            }
        }

        private GstVideoFrameContext() { }
    }
}