using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit;
using Microsoft.Kinect.Toolkit.FaceTracking;

using Point = System.Windows.Point;

namespace MSMiddleMan
{
    class FSTracker : IDisposable
    {
        KinectSensor sensor = null;

        private const uint MaxMissedFrames = 100;

        private readonly Dictionary<int, SkeletonFaceTracker> trackedSkeletons = new Dictionary<int, SkeletonFaceTracker>();

        private byte[] colorImage;

        private ColorImageFormat colorImageFormat = ColorImageFormat.RgbResolution640x480Fps30;

        private short[] depthImage;

        private DepthImageFormat depthImageFormat = DepthImageFormat.Resolution640x480Fps30;

        private bool disposed;

        private Skeleton[] skeletonData;

        private VHMsg.Client vhmsg;

        // Jon
        //private static HashSet<int> mouthPoints = new HashSet<int> { 7, 31, 33, 40, 41,
        //64, 66, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89};
        //private static HashSet<int> mouthPoints = new HashSet<int> { 40, 81, 82, 83, 84, 87};
        private const int LeftTopLowerLip = 84;
        private const int LeftBottomUpperLip = 82;
        private const int MiddleTopLowerLip = 40;
        private const int MiddleBottomUpperLip = 87;
        private const int RightTopLowerLip = 83;
        private const int RightBottomUpperLip = 81;
        private const int mouthOpenThreshold = 2;
        public bool mouthOpen = false;

        //private bool faceTracked = false;


        ~FSTracker()
        {
            this.Dispose(false);
        }

        public KinectSensor Kinect
        {
            get
            {
                return this.sensor;
            }

            set
            {
                this.sensor = value;
            }
        }

        public FSTracker(VHMsg.Client vhm)
        {
            // get message client
            this.vhmsg = vhm;

            // init the tracker
            foreach (KinectSensor potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }

            if (this.sensor != null)
            {
                this.sensor.ColorStream.Enable(this.colorImageFormat);
                this.sensor.DepthStream.Enable(this.depthImageFormat);

                this.sensor.DepthStream.Range = DepthRange.Default;
                this.sensor.SkeletonStream.EnableTrackingInNearRange = false;
                
                //this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
                this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Default;
                this.sensor.SkeletonStream.Enable();
                this.sensor.AllFramesReady += OnAllFramesReady;
            }

        }

        public void startTracking()
        {
            this.sensor.Start();
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                this.ResetFaceTracking();
                this.sensor.Stop();
                this.sensor.Dispose();

                this.disposed = true;
            }
        }

        private void OnAllFramesReady(object sender, AllFramesReadyEventArgs allFramesReadyEventArgs)
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
                if (this.depthImageFormat != depthImageFrame.Format)
                {
                    this.ResetFaceTracking();
                    this.depthImage = null;
                    this.depthImageFormat = depthImageFrame.Format;
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
                            this.trackedSkeletons.Add(skeleton.TrackingId, new SkeletonFaceTracker(vhmsg));
                        }

                        // Give each tracker the upated frame.
                        SkeletonFaceTracker skeletonFaceTracker;
                        if (this.trackedSkeletons.TryGetValue(skeleton.TrackingId, out skeletonFaceTracker))
                        {
                            skeletonFaceTracker.OnFrameReady(this.Kinect, colorImageFormat, colorImage, depthImageFormat, depthImage, skeleton);
                            skeletonFaceTracker.LastTrackedFrame = skeletonFrame.FrameNumber;
                            mouthOpen = skeletonFaceTracker.mouthOpen;
                        }
                    }
                }

                this.RemoveOldTrackers(skeletonFrame.FrameNumber);
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
        }

        private void OnSensorChanged(KinectSensor oldSensor, KinectSensor newSensor)
        {
            if (oldSensor != null)
            {
                oldSensor.AllFramesReady -= this.OnAllFramesReady;
                this.ResetFaceTracking();
            }

            if (newSensor != null)
            {
                newSensor.AllFramesReady += this.OnAllFramesReady;
            }
        }

        /// <summary>
        /// Clear out any trackers for skeletons we haven't heard from for a while
        /// </summary>
        private void RemoveOldTrackers(int currentFrameNumber)
        {
            var trackersToRemove = new List<int>();

            foreach (var tracker in this.trackedSkeletons)
            {
                uint missedFrames = (uint)currentFrameNumber - (uint)tracker.Value.LastTrackedFrame;
                if (missedFrames > MaxMissedFrames)
                {
                    // There have been too many frames since we last saw this skeleton
                    trackersToRemove.Add(tracker.Key);
                }
            }

            foreach (int trackingId in trackersToRemove)
            {
                this.RemoveTracker(trackingId);
            }
        }

        private void RemoveTracker(int trackingId)
        {
            this.trackedSkeletons[trackingId].Dispose();
            this.trackedSkeletons.Remove(trackingId);
        }

        private void ResetFaceTracking()
        {
            foreach (int trackingId in new List<int>(this.trackedSkeletons.Keys))
            {
                this.RemoveTracker(trackingId);
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

            private bool faceTracked = false;

            public bool mouthOpen = false;

            private const int mouthDelayFrame = 5;

            private int mouthOpenFrame = 0;

            private int mouthClosedFrame = 0;

            private bool handRaised = false;

            private Posture bodyPosture = Posture.Untracked;

            private const double leanThreshold = 0.05;

            private VHMsg.Client vhmsg;

            public void Dispose()
            {
                if (this.faceTracker != null)
                {
                    this.faceTracker.Dispose();
                    this.faceTracker = null;
                }
            }

            public SkeletonFaceTracker(VHMsg.Client vhm)
            {
                this.vhmsg = vhm;
            }

            //public void DrawFaceModel(DrawingContext drawingContext)
            //{
            //    if (!this.lastFaceTrackSucceeded || this.skeletonTrackingState != SkeletonTrackingState.Tracked)
            //    {
            //        return;
            //    }

            //    var faceModelPts = new List<Point>();
            //    var faceModel = new List<FaceModelTriangle>();

            //    for (int i = 0; i < this.facePoints.Count; i++)
            //    {
            //        faceModelPts.Add(new Point(this.facePoints[i].X + 0.5f, this.facePoints[i].Y + 0.5f));
            //    }

            //    foreach (var t in faceTriangles)
            //    {
            //        if (mouthPoints.Contains(t.First) && mouthPoints.Contains(t.Second) &&
            //            mouthPoints.Contains(t.Third))
            //        {
            //            var triangle = new FaceModelTriangle();
            //            triangle.P1 = faceModelPts[t.First];
            //            triangle.P2 = faceModelPts[t.Second];
            //            triangle.P3 = faceModelPts[t.Third];
            //            faceModel.Add(triangle);
            //        }
            //        //var triangle = new FaceModelTriangle();
            //        //triangle.P1 = faceModelPts[t.First];
            //        //triangle.P2 = faceModelPts[t.Second];
            //        //triangle.P3 = faceModelPts[t.Third];
            //        //faceModel.Add(triangle);
            //    }

            //    var faceModelGroup = new GeometryGroup();
            //    for (int i = 0; i < faceModel.Count; i++)
            //    {
            //        var faceTriangle = new GeometryGroup();
            //        faceTriangle.Children.Add(new LineGeometry(faceModel[i].P1, faceModel[i].P2));
            //        faceTriangle.Children.Add(new LineGeometry(faceModel[i].P2, faceModel[i].P3));
            //        faceTriangle.Children.Add(new LineGeometry(faceModel[i].P3, faceModel[i].P1));
            //        faceModelGroup.Children.Add(faceTriangle);
            //    }

            //    drawingContext.DrawGeometry(Brushes.LightYellow, new Pen(Brushes.LightYellow, 1.0), faceModelGroup);
            //}

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

                // face tracking
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

                    if (!this.lastFaceTrackSucceeded || this.skeletonTrackingState != SkeletonTrackingState.Tracked)
                    {
                        // TODO handle face tracker failure
                        if (faceTracked)
                        {
                            faceTracked = false;
                            Console.WriteLine("Face untracked!");
                        }
                        return;
                    } 
                    else
                    {
                        if (!faceTracked)
                        {
                            faceTracked = true;
                            Console.WriteLine("Face track succeeded!");

                        }
                    }

                    var faceModelPts = new List<Point>();
                    var faceModel = new List<FaceModelTriangle>();

                    for (int i = 0; i < this.facePoints.Count; i++)
                    {
                        faceModelPts.Add(new Point(this.facePoints[i].X + 0.5f, this.facePoints[i].Y + 0.5f));
                    }

                    // TODO handle mouth change interval
                    if (isMouthOpen(faceModelPts))
                    {
                        mouthOpenFrame++;
                        mouthClosedFrame = 0;
                        if (!mouthOpen && mouthOpenFrame >= mouthDelayFrame)
                        {
                            mouthOpen = true;
                            Console.WriteLine("Mouth open!");
                        }
                    }
                    else
                    {
                        mouthOpenFrame = 0;
                        mouthClosedFrame++;
                        if (mouthOpen && mouthClosedFrame >= mouthDelayFrame)
                        {
                            mouthOpen = false;
                            Console.WriteLine("Mouth closed!");
                        }
                    }

                    // hand tracking
                    if (isHandRaised(skeletonOfInterest, HandSide.Left) == HandRaiseState.Up || isHandRaised(skeletonOfInterest, HandSide.Right) == HandRaiseState.Up)
                    {
                        if (!handRaised)
                        {
                            handRaised = true;
                            Console.WriteLine("Hand raised!");
                            vhmsg.SendMessage("userActivities handRaised");
                        }
                    }
                    else if (isHandRaised(skeletonOfInterest, HandSide.Left) == HandRaiseState.Down && isHandRaised(skeletonOfInterest, HandSide.Right) == HandRaiseState.Down)
                    {
                        if (handRaised)
                        {
                            handRaised = false;
                            Console.WriteLine("Hands down!");
                            vhmsg.SendMessage("userActivities handsDown");
                        }
                    }
                    else
                    {
                        if (handRaised)
                        {
                            handRaised = false;
                        }
                    }


                    // leaning tracking
                    Posture pos = detectPosture(skeletonOfInterest);
                    if (pos == Posture.Forward)
                    {
                        if (bodyPosture != Posture.Forward)
                        {
                            bodyPosture = Posture.Forward;
                            Console.WriteLine("Lean forward!");
                            // TODO send fw msg
                            vhmsg.SendMessage("userActivities lean Forward");
                        }
                    }
                    else if (pos == Posture.Upright)
                    {
                        if (bodyPosture != Posture.Upright)
                        {
                            bodyPosture = Posture.Upright;
                            Console.WriteLine("Sit Upright!");
                            // TODO send ur msg
                            vhmsg.SendMessage("userActivities lean Upright");
                        }
                    }
                    else if (pos == Posture.Backward)
                    {
                        if (bodyPosture != Posture.Backward)
                        {
                            bodyPosture = Posture.Backward;
                            Console.WriteLine("Lean backward!");
                            // TODO send bw msg
                            vhmsg.SendMessage("userActivities lean Backward");
                        }
                    }
                    else if (pos == Posture.Untracked)
                    {
                        if (bodyPosture != Posture.Untracked)
                        {
                            bodyPosture = Posture.Untracked;
                            Console.WriteLine("Leaning untracked!");
                            // TODO send ut msg
                            vhmsg.SendMessage("userActivities lean Untracked");
                        }
                    }

                }
            }



            private enum Posture
            {
                Upright,
                Forward,
                Backward,
                Untracked
            }

            private Posture detectPosture(Skeleton skl)
            {
                Joint shoulderCenter = skl.Joints[JointType.ShoulderCenter];
                Joint spine = skl.Joints[JointType.Spine];


                if (shoulderCenter.TrackingState == JointTrackingState.Tracked &&
                    spine.TrackingState == JointTrackingState.Tracked)
                {
                    //// test
                    //Console.WriteLine("shoulderCenter " + shoulderCenter.Position.Z.ToString());
                    //Console.WriteLine("spine " + spine.Position.Z.ToString());

                    if (spine.Position.Z - shoulderCenter.Position.Z > leanThreshold)
                    {
                        return Posture.Forward;
                    }
                    else if (spine.Position.Z - shoulderCenter.Position.Z < -leanThreshold)
                    {
                        return Posture.Backward;
                    }
                    else
                    {
                        return Posture.Upright;
                    }
                }
                return Posture.Untracked;
            }

            private enum HandSide
            {
                Left,
                Right
            }

            private enum HandRaiseState
            {
                Untracked,
                Up,
                Down
            }

            private HandRaiseState isHandRaised(Skeleton skl, HandSide handSide)
            {
                Joint hand = skl.Joints[(JointType)Enum.Parse(typeof(JointType), "Hand" + handSide.ToString())];
                Joint shoulder = skl.Joints[(JointType)Enum.Parse(typeof(JointType), "Shoulder" + handSide.ToString())];
                Joint elbow = skl.Joints[(JointType)Enum.Parse(typeof(JointType), "Elbow" + handSide.ToString())];

                if (hand.TrackingState == JointTrackingState.Tracked && 
                    shoulder.TrackingState == JointTrackingState.Tracked && 
                    elbow.TrackingState == JointTrackingState.Tracked)
                {
                    if (hand.Position.Y > 0.5 * (shoulder.Position.Y + elbow.Position.X))
                    {
                        return HandRaiseState.Up;
                    }
                    else
                    {
                        return HandRaiseState.Down;
                    }
                }
                return HandRaiseState.Untracked;
            }

            private bool isMouthOpen(List<Point> faceMPoints)
            {
                return (distanceBetweenPoints(faceMPoints[LeftTopLowerLip], faceMPoints[LeftBottomUpperLip]) > mouthOpenThreshold)
                    && (distanceBetweenPoints(faceMPoints[MiddleTopLowerLip], faceMPoints[MiddleBottomUpperLip]) > mouthOpenThreshold)
                    && (distanceBetweenPoints(faceMPoints[RightTopLowerLip], faceMPoints[RightBottomUpperLip]) > mouthOpenThreshold);
            }

            private double distanceBetweenPoints(Point fPoint, Point sPoint)
            {
                return Math.Sqrt(Math.Pow((fPoint.X - sPoint.X), 2) + Math.Pow((fPoint.Y - sPoint.Y), 2));
            }

            private struct FaceModelTriangle
            {
                public Point P1;
                public Point P2;
                public Point P3;
            }
        }
    }
}
