namespace AzureFaceDoor.Client
{
    using System;

    /// <summary>
    /// Interface for implementing access to GStreamer video frame's raw data in the context of a short-live event
    /// </summary>
    public interface IGstVideoFrameContext 
    {
        /// Frame Width
        int Width { get; }
        /// Frame Height
        int Height { get; }
        /// Frame Format
        string Format { get; }
        /// Frame stride size (size in bytes of a single scan-line)
        long Stride { get; }
        /// Frame memory size in bytes
        long Size { get; }
        /// Frame raw data buffer
        IntPtr Buffer { get; }
        /// Copies frame's raw data to an unmanaged memory pointer destination
        void CopyTo(IntPtr destination, long destinationSizeInBytes = 0);
    }
}