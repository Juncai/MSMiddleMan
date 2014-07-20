using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit;
using Microsoft.Kinect.Toolkit.FaceTracking;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KinectFTTest
{
    class Program
    {
        //public static readonly DependencyProperty KinectProperty = DependencyProperty.Register(
        //    "Kinect",
        //    typeof(KinectSensor),
        //    typeof(Program),
        //    new PropertyMetadata(
        //        null, (o, args) => ((Program)o).OnSensorChanged((KinectSensor)args.OldValue, (KinectSensor)args.NewValue)));



        private const uint MaxMissedFrames = 100;

        private readonly Dictionary<int, SkeletonFaceTracker> trackedSkeletons = new Dictionary<int, SkeletonFaceTracker>();

        private byte[] colorImage;

        private ColorImageFormat colorImageFormat = ColorImageFormat.Undefined;

        private short[] depthImage;

        private DepthImageFormat depthImageFormat = DepthImageFormat.Undefined;

        private bool disposed;

        private Skeleton[] skeletonData;


        static void Main(string[] args)
        {
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    sensor = potentialSensor;
                    break;
                }
            }

            if (sensor != null)
            {
                // prepare color frame
                sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
                colorPixels = new byte[sensor.ColorStream.FramePixelDataLength];
                colorBitmap = new WriteableBitmap(sensor.ColorStream.FrameWidth, 
                    sensor.ColorStream.FrameHeight, 96.0, 96.0, PixelFormats.Bgr32, null);
                sensor.ColorFrameReady += SensorColorFrameReady;

                // prepare depth frame
                sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                depthPixels = new short[sensor.DepthStream.FramePixelDataLength];
                depthBitmap = new WriteableBitmap(sensor.DepthStream.FrameWidth,
                    sensor.DepthStream.FrameHeight, 96.0, 96.0, PixelFormats.Bgr32, null);
                sensor.DepthFrameReady += SensorDepthFrameReady;

                sensor.SkeletonStream.Enable();
                sensor.ColorStream.Enable(ColorImageFormat.InfraredResolution640x480Fps30);
            }
            
            FaceTracker ft = new FaceTracker(sensor);
            FaceTrackFrame ftFrame = ft.Track(ColorImageFormat.RgbResolution640x480Fps30, colorPixels, DepthImageFormat.Resolution640x480Fps30, depthPixels);



            if (sensor != null)
            {
                sensor.Start();
            }
        }

        static void SensorAllFrameReady(object sender, AllFramesReadyEventArgs allFramesReadyEventArgs)
        {
            ColorImageFrame colorImageFrame = null;
            DepthImageFrame depthImageFrame = null;
            SkeletonFrame skeletonFrame = null;

            try
            {
                colorImageFrame = allFramesReadyEventArgs.OpenColorImageFrame();
                depthImageFrame = allFramesReadyEventArgs.OpenDepthImageFrame();
                skeletonFrame = allFramesReadyEventArgs.OpenSkeletonFrame();

                if (colorImageFrame == null || depthImageFrame == null || skeletonFrame == null)
                {
                    return;
                }

                // Check for image format changes.  The FaceTracker doesn't
                // deal with that so we need to reset.
                if (depthImageFormat != depthImageFrame.Format)
                {
                    ResetFaceTracking();
                    depthImage = null;
                    depthImageFormat = depthImageFrame.Format;
                }

                if (this.colorImageFormat != colorImageFrame.Format)
                {
                    this.ResetFaceTracking();
                    this.colorImage = null;
                    this.colorImageFormat = colorImageFrame.Format;
                }

                // Create any buffers to store copies of the data we work with
                if (this.depthImage == null)
                {
                    this.depthImage = new short[depthImageFrame.PixelDataLength];
                }

                if (this.colorImage == null)
                {
                    this.colorImage = new byte[colorImageFrame.PixelDataLength];
                }

                // Get the skeleton information
                if (this.skeletonData == null || this.skeletonData.Length != skeletonFrame.SkeletonArrayLength)
                {
                    this.skeletonData = new Skeleton[skeletonFrame.SkeletonArrayLength];
                }

                colorImageFrame.CopyPixelDataTo(this.colorImage);
                depthImageFrame.CopyPixelDataTo(this.depthImage);
                skeletonFrame.CopySkeletonDataTo(this.skeletonData);

                // Update the list of trackers and the trackers with the current frame information
                foreach (Skeleton skeleton in this.skeletonData)
                {
                    if (skeleton.TrackingState == SkeletonTrackingState.Tracked
                        || skeleton.TrackingState == SkeletonTrackingState.PositionOnly)
                    {
                        // We want keep a record of any skeleton, tracked or untracked.
                        if (!this.trackedSkeletons.ContainsKey(skeleton.TrackingId))
                        {
                            this.trackedSkeletons.Add(skeleton.TrackingId, new SkeletonFaceTracker());
                        }

                        // Give each tracker the upated frame.
                        SkeletonFaceTracker skeletonFaceTracker;
                        if (this.trackedSkeletons.TryGetValue(skeleton.TrackingId, out skeletonFaceTracker))
                        {
                            skeletonFaceTracker.OnFrameReady(this.Kinect, colorImageFormat, colorImage, depthImageFormat, depthImage, skeleton);
                            skeletonFaceTracker.LastTrackedFrame = skeletonFrame.FrameNumber;
                        }
                    }
                }

                this.RemoveOldTrackers(skeletonFrame.FrameNumber);

                this.InvalidateVisual();
            }
            finally
            {
                if (colorImageFrame != null)
                {
                    colorImageFrame.Dispose();
                }

                if (depthImageFrame != null)
                {
                    depthImageFrame.Dispose();
                }

                if (skeletonFrame != null)
                {
                    skeletonFrame.Dispose();
                }
            }






            using (ColorImageFrame colorFrame = e.OpenColorImageFrame())
            {
                if (colorFrame != null)
                {
                    // Copy the pixel data from the image to a temporary array
                    colorFrame.CopyPixelDataTo(colorPixels);

                    // Write the pixel data into our bitmap
                    colorBitmap.WritePixels(
                        new Int32Rect(0, 0, colorBitmap.PixelWidth, colorBitmap.PixelHeight),
                        colorPixels,
                        colorBitmap.PixelWidth * sizeof(int),
                        0);
                }
            }
        }

        static void SensorDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
            {
                if (depthFrame != null)
                {
                    // Copy the pixel data from the image to a temporary array
                    depthFrame.CopyPixelDataTo(depthPixels);

                    // Write the pixel data into our bitmap
                    depthBitmap.WritePixels(
                        new Int32Rect(0, 0, depthBitmap.PixelWidth, depthBitmap.PixelHeight),
                        depthPixels,
                        depthBitmap.PixelWidth * sizeof(int),
                        0);
                }
            }
        }
    }

    private class SkeletonFaceTracker : IDisposable
    {
        private static FaceTriangle[] faceTriangles;

        private EnumIndexableCollection<FeaturePoint, PointF> facePoints;

        private FaceTracker faceTracker;

        private bool lastFaceTrackSucceeded;

        private SkeletonTrackingState skeletonTrackingState;

        public int LastTrackedFrame { get; set; }

        public void Dispose()
        {
            if (this.faceTracker != null)
            {
                this.faceTracker.Dispose();
                this.faceTracker = null;
            }
        }

        public void DrawFaceModel(DrawingContext drawingContext)
        {
            if (!this.lastFaceTrackSucceeded || this.skeletonTrackingState != SkeletonTrackingState.Tracked)
            {
                return;
            }

            var faceModelPts = new List<Point>();
            var faceModel = new List<FaceModelTriangle>();

            for (int i = 0; i < this.facePoints.Count; i++)
            {
                faceModelPts.Add(new Point(this.facePoints[i].X + 0.5f, this.facePoints[i].Y + 0.5f));
            }

            foreach (var t in faceTriangles)
            {
                var triangle = new FaceModelTriangle();
                triangle.P1 = faceModelPts[t.First];
                triangle.P2 = faceModelPts[t.Second];
                triangle.P3 = faceModelPts[t.Third];
                faceModel.Add(triangle);
            }

            var faceModelGroup = new GeometryGroup();
            for (int i = 0; i < faceModel.Count; i++)
            {
                var faceTriangle = new GeometryGroup();
                faceTriangle.Children.Add(new LineGeometry(faceModel[i].P1, faceModel[i].P2));
                faceTriangle.Children.Add(new LineGeometry(faceModel[i].P2, faceModel[i].P3));
                faceTriangle.Children.Add(new LineGeometry(faceModel[i].P3, faceModel[i].P1));
                faceModelGroup.Children.Add(faceTriangle);
            }

            drawingContext.DrawGeometry(Brushes.LightYellow, new Pen(Brushes.LightYellow, 1.0), faceModelGroup);
        }

        /// <summary>
        /// Updates the face tracking information for this skeleton
        /// </summary>
        internal void OnFrameReady(KinectSensor kinectSensor, ColorImageFormat colorImageFormat, byte[] colorImage, DepthImageFormat depthImageFormat, short[] depthImage, Skeleton skeletonOfInterest)
        {
            this.skeletonTrackingState = skeletonOfInterest.TrackingState;

            if (this.skeletonTrackingState != SkeletonTrackingState.Tracked)
            {
                // nothing to do with an untracked skeleton.
                return;
            }

            if (this.faceTracker == null)
            {
                try
                {
                    this.faceTracker = new FaceTracker(kinectSensor);
                }
                catch (InvalidOperationException)
                {
                    // During some shutdown scenarios the FaceTracker
                    // is unable to be instantiated.  Catch that exception
                    // and don't track a face.
                    Debug.WriteLine("AllFramesReady - creating a new FaceTracker threw an InvalidOperationException");
                    this.faceTracker = null;
                }
            }

            if (this.faceTracker != null)
            {
                FaceTrackFrame frame = this.faceTracker.Track(
                    colorImageFormat, colorImage, depthImageFormat, depthImage, skeletonOfInterest);

                this.lastFaceTrackSucceeded = frame.TrackSuccessful;
                if (this.lastFaceTrackSucceeded)
                {
                    if (faceTriangles == null)
                    {
                        // only need to get this once.  It doesn't change.
                        faceTriangles = frame.GetTriangles();
                    }

                    this.facePoints = frame.GetProjected3DShape();
                }
            }
        }

        private struct FaceModelTriangle
        {
            public Point P1;
            public Point P2;
            public Point P3;
        }
    }
}
