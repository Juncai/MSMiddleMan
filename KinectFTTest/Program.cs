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
        enum aType
        {
            haha,
            hehe
        }

        private static void Main(string[] args)
        {
            string btype = "haha";
            object cType = Enum.Parse(typeof(aType), btype);
            if ((aType)cType == aType.haha)
            {
                Console.WriteLine("haha");
            }
        }
    }
}
