using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit;
using Microsoft.Kinect.Toolkit.FaceTracking;

namespace KinectFTTest
{
    class Program
    {
        static void Main(string[] args)
        {
            KinectSensor sensor = null;
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
                sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
                sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                sensor.SkeletonStream.Enable();
                sensor.ColorStream.Enable(ColorImageFormat.InfraredResolution640x480Fps30);
            }

            FaceTracker ft = new FaceTracker(sensor);
            



            if (sensor != null)
            {
                sensor.Start();
            }
        }
    }
}
