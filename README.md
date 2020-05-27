# GStreamer video processing pipeline for an RTSP or web camera
Made for [Video capture using GStreamer and gstreamer-netcore](https://vladkol.com/posts/gstreamer/) post. 

## Requirements 
* .NET Core SDK 3.1+ 
* Visual Studio Code or Visual Studio 2019+ 
* Gstreamer 16.2+ 

## How to run 
1. Build in Visual Studio, Visual Studio Code or with ```dotnet build``` command
2. Run in one of 3 ways: 
> * with **--camera_index** option - provide index of web cam device connected to your machine (use 0 if not sure):``` GStreamerDotNet --camera_index 0```
> * with **--source_uri** option - provide uri to use as the source:``` GStreamerDotNet --source_uri rtsp://admin:password@192.168.0.17:554/``` 
> * any gstreamer pipeline command line (without 'gst-launch-1.0' and its switches). **It must end with an *appsink* element**: 
``` GStreamerDotNet uridecodebin uri=https://someurl ! queue ! videoconvert ! appsink```

Main purpose of this sample is to introduce [GstVideoStream class](GstVideoStream/GstVideoStream.cs) that helps creating video procesing pipelines and handling raw video frames. 

Read [this post](https://vladkol.com/posts/gstreamer/) for the details.
