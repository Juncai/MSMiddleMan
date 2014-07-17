//#define JUN
//#define detectLeanByHeadPos
//#define debug
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using System.Xml;
using System.Timers;
using System.Speech.Recognition;

namespace PerceptionTest
{
    class Program
    {
        [DllImport("msvcrt.dll")]
        public static extern int _kbhit();

        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        [DllImport("kernel32")]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);

        // An enumerated type for the control messages
        // sent to the handler routine.
        public enum CtrlTypes
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT,
            CTRL_CLOSE_EVENT,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT
        }

        // A delegate type to be used as the handler routine
        // for SetConsoleCtrlHandler.
        public delegate bool HandlerRoutine(CtrlTypes CtrlType);


        private static bool isRunning = true;
        private static VHMsg.Client vhmsg;

        private static bool trackHead = true;
        private static bool trackEyes = false;
        private static bool trackSmile = false;
        private static bool trackGaze = false;
        private static bool trackAddressee = false;
        private static string[] character;
        private static bool shouldListen = false;
        private static int gazeTime = 200;//ms
        private static System.Timers.Timer gazeTimer = new System.Timers.Timer();
        private static bool shouldReadPML = true;
        private static HandlerRoutine hr;

        private static Queue<double> deltaDsIn10 = new Queue<double>(300);
        private static Queue<double> deltaDsInHalf = new Queue<double>(15);
        private static bool isLeaningForward = false;
        private static bool isLeaningBackward = false;
        private static double bodyMotionIn10 = 0;
        private static double bodyMotionInHalf = 0;
        private static double forwardTotal = 0;
        private static double backwardTotal = 0;
        private static bool trackLeaning = false;
        private static double? prevZ = null;
        private static string direction = "NONE";
        private static bool trackHand = false;
        private static int lHandUpFrameCount = 0;
        private static int rHandUpFrameCount = 0;
        private static DateTime lastSpeakTimestamp = DateTime.Now;
        private static bool isSpeaking = false;

        private static void OnTimedEvent(object src, ElapsedEventArgs e)
        {
            //Console.WriteLine("The Elapsed event was raised at {0}", e.SignalTime);
            shouldReadPML = true;
            gazeTimer.Interval = gazeTime;
            gazeTimer.Enabled = true;
        }


        private static bool ConsoleCtrlCheck(CtrlTypes ctrlType)
        {

            // Put your own handler here

            switch (ctrlType)
            {

                case CtrlTypes.CTRL_C_EVENT:

                    //isclosing = true;

                    Console.WriteLine("CTRL+C received!");
                    //Thread.Sleep(5000);
                    break;



                case CtrlTypes.CTRL_BREAK_EVENT:

                    //isclosing = true;

                    Console.WriteLine("CTRL+BREAK received!");
                    //Thread.Sleep(5000);
                    break;



                case CtrlTypes.CTRL_CLOSE_EVENT:

                    //isclosing = true;

                    Console.WriteLine("Program being closed!");
                    CleanupBeforeExiting();
                    //Thread.Sleep(3000);
                    break;

                case CtrlTypes.CTRL_LOGOFF_EVENT:

                case CtrlTypes.CTRL_SHUTDOWN_EVENT:

                    //isclosing = true;

                    Console.WriteLine("User is logging off!");
                    //Thread.Sleep(5000);
                    break;
            }

            return true;

        }


        private static void Main(string[] args)
        {
            // Subscribing
            shouldListen = false;
            hr = new HandlerRoutine(ConsoleCtrlCheck);
            SetConsoleCtrlHandler(hr, true);

            using (vhmsg = new VHMsg.Client())
            {
                InitSpeechRecognizer();
                gazeTimer.Elapsed += new ElapsedEventHandler(OnTimedEvent);
                string configFile = "config.ini";
                if (args.Length > 0)
                {
                    configFile = args[0];
                    Console.WriteLine("Reading config file - " + configFile);
                }

                ReadConfigFile(configFile);

                vhmsg.OpenConnection();

                Console.WriteLine("VHMSG_SERVER: {0}", vhmsg.Server);
                Console.WriteLine("VHMSG_SCOPE: {0}", vhmsg.Scope);

                Console.WriteLine("Press q to quit");
                Console.WriteLine("Listening to vrPerception Messages");

                vhmsg.MessageEvent += new VHMsg.Client.MessageEventHandler(MessageAction);
                vhmsg.SubscribeMessage("vrPerception");
                vhmsg.SubscribeMessage("vrAllCall");
                vhmsg.SubscribeMessage("vrKillComponent");
                vhmsg.SubscribeMessage("vrPerceptionApplication");

                vhmsg.SendMessage("vrComponent perception-test-application");


                while (isRunning)
                {
                    Thread.Sleep(100);
                    if (isSpeaking && DateTime.Now.Subtract(lastSpeakTimestamp).TotalMilliseconds > 3000)
                    {
                        isSpeaking = false;
                        vhmsg.SendMessage("userActivities stopTalking");
                    }
                    if (_kbhit() != 0)
                    {
                        char c = Console.ReadKey(true).KeyChar;
                        if (c == 'q')
                        {
                            isRunning = false;
                        }
                    }
                }
                CleanupBeforeExiting();
            }

        }

        static void InitSpeechRecognizer()
        {
            // TODO improve grammar
            SpeechRecognizer recognizer = new SpeechRecognizer();
            Choices greetings = new Choices();
            greetings.Add(new string[] { "hello", "hi" });
            GrammarBuilder gb = new GrammarBuilder();
            gb.Append(greetings);
            Grammar g = new Grammar(gb);
            recognizer.LoadGrammar(g);
            recognizer.SpeechRecognized +=
                new EventHandler<SpeechRecognizedEventArgs>(sre_SpeechRecognized);

        }

        static void sre_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            lastSpeakTimestamp = DateTime.Now;
            if (!isSpeaking)
            {
                vhmsg.SendMessage("userActivities speak");
                isSpeaking = true;
            }
                Console.WriteLine(e.Result.Text);
        }

        static void CleanupBeforeExiting()
        {
            vhmsg.SendMessage("vrProcEnd perception-test-application");
            Reset();
            vhmsg.CloseConnection();
        }

        private static void Reset()
        {
            CharacterReset();

            trackEyes = false;
            trackGaze = false;
            trackAddressee = false;
            trackHead = false;
            trackSmile = false;
            trackLeaning = false;
            gazeTimer.Enabled = false;
            trackHand = false;
        }

        static void CharacterReset()
        {
            //string messageToBeSentToSBM = @"bml char <character> <gaze direction=""RIGHT"" angle=""0"" sbm:joint-range=""EYES NECK"" target=""Camera"" sbm:handle=""bradGaze""/>";
            string messageToBeSentToSBM = @"bml char <character> <gaze direction=""RIGHT"" angle=""0"" sbm:joint-range=""EYES NECK"" target=""Camera""/>";
            //reset NPCEditor facing 
            string messageToNPCEditor = @"<action name=""SetFacingCharacter"" target=""user""><param name=""facingCharacter"">none</param></action>";
            vhmsg.SendMessage("NPCEditor", messageToNPCEditor);
            for (int i = 0; i < character.Length; ++i)
            {
                vhmsg.SendMessage("sbm", messageToBeSentToSBM.Replace(@"<character>", character[i]));
                vhmsg.SendMessage("sbm receiver skeleton " + character[i] + " generic norotation skullbase");
            }

        }

        static pml ParsePML(string xml)
        {
            pml myPML = new pml();
            XmlSerializer serializer = new XmlSerializer(myPML.GetType());
            StringReader stringReader = new StringReader(xml);

            XmlTextReader xmlReader = new XmlTextReader(stringReader);

            myPML = (pml)serializer.Deserialize(xmlReader);

            xmlReader.Close();
            stringReader.Close();

#if debug

            Console.WriteLine(myPML.body[0].layer2.skeleton.joint[0].jointName);
#endif

            return myPML;
        }

        static void SendLeaningMessage(pml myPML)
        {
            if (myPML.body.Length > 0)
            {
                body myBody = myPML.body[0];

                if (trackLeaning)
                {
                    if (myBody.layer2 != null && myBody.layer2.posture.Count() > 0)
                    {
                        postureType pType = myBody.layer2.posture[0].postureType;
                        if (pType == postureType.leaning_forward)
                        {
                            direction = "FORWARD";
                        }
                        else if (pType == postureType.leaning_backward)
                        {
                            direction = "BACKWARD";
                        }
                        else
                        {
                            direction = "NONE";
                        }

                        vhmsg.SendMessage("userActivities", string.Format("lean {0}", direction));
                        Console.WriteLine(string.Format("lean {0}", direction));
                    }
                    else if (myBody.layer2 != null && myBody.layer2.headPose != null && myBody.layer2.headPose.position != null)
                    {
                        double deltaD = 0;
                        
                        if (prevZ != null)
                        {
                            deltaD = myBody.layer2.headPose.position.z - (double)prevZ;
                        }

                        prevZ = myBody.layer2.headPose.position.z;

                        deltaDsIn10.Enqueue(deltaD);
                        bodyMotionIn10 += deltaD;
                        deltaDsInHalf.Enqueue(deltaD);
                        bodyMotionInHalf += deltaD;
                        if (deltaDsIn10.Count == 300)
                        {
                            bodyMotionIn10 -= deltaDsIn10.Dequeue();
                        }
                        if (deltaDsInHalf.Count == 15)
                        {
                            bodyMotionInHalf -= deltaDsInHalf.Dequeue();
                        }

                        if (deltaD > 0)
                        {
                            //continueBackward++;
                            //continueForward = 0;
                            backwardTotal += deltaD;
                        }
                        else if (deltaD < 0)
                        {
                            //continueForward++;
                            //continueBackward = 0;
                            forwardTotal += deltaD;
                        }
                        //else
                        //{
                        //    //continueForward = (continueForward == 0) ? 0 : continueForward++;
                        //    //continueBackward = (continueBackward == 0) ? 0 : continueBackward++;
                        //}

                        if (bodyMotionInHalf <= -12)
                        {
                            isLeaningForward = true;
                            isLeaningBackward = false;
                            direction = "FORWARD";
                        }
                        else if (bodyMotionInHalf >= 12)
                        {
                            isLeaningBackward = true;
                            isLeaningForward = false;
                            direction = "BACKWARD";
                        }
                        else
                        {
                            isLeaningBackward = false;
                            isLeaningForward = false;
                            direction = "NONE";
                        }

                        vhmsg.SendMessage("userActivities", string.Format("lean {0} {1} {2} {3}", direction, bodyMotionIn10, forwardTotal, backwardTotal));
                        Console.WriteLine(string.Format("lean {0} {1} {2} {3}", direction, bodyMotionIn10, forwardTotal, backwardTotal));
                        //string messageToBeSentToSBM = ConvertToQuaternionAndString(myBody.layer2.headPose.position.x, myBody.layer2.headPose.position.y, myBody.layer2.headPose.position.z, myBody.layer2.headPose.rotation.rotX, myBody.layer2.headPose.rotation.rotY, myBody.layer2.headPose.rotation.rotZ);
                        //for (int i = 0; i < character.Length; ++i)
                        //{
                        //    vhmsg.SendMessage("sbm", messageToBeSentToSBM.Replace(@"<character>", character[i]));
                        //}

                    }


                }
            }
        }

        static void SendInterruptionMessage(pml myPML)
        {
            if (myPML.body.Length > 0)
            {
                body myBody = myPML.body[0];

                if (trackHand)
                {
                    if (myBody.layer2 != null)
                    {
                        if (myBody.layer2.handPoseLeft != null && myBody.layer2.handPoseLeft.position != null &&
                            myBody.layer2.skeleton != null && myBody.layer2.skeleton.joint.Count() > 0)
                        {
                            // TODO finish detecting hand pose
                            position lShoulderPos = null;
                            position lElbowPos = null;

                            foreach (joint j in myBody.layer2.skeleton.joint)
                            {
                                if (j.jointName.Equals("left_shoulder"))
                                {
                                    lShoulderPos = j.position;
                                }
                                if (j.jointName.Equals("left_elbow"))
                                {
                                    lElbowPos = j.position;
                                }
                            }

                            if (lShoulderPos != null && lElbowPos != null)
                            {
                                position lHandPos = myBody.layer2.handPoseLeft.position;
                                if (lHandPos.y >= (lShoulderPos.y + lElbowPos.y) / 2)
                                {
                                    lHandUpFrameCount++;
                                }
                                else
                                {
                                    lHandUpFrameCount = 0;
                                }
                            }
                        }

                        if (myBody.layer2.handPoseRight != null && myBody.layer2.handPoseRight.position != null &&
                            myBody.layer2.skeleton != null && myBody.layer2.skeleton.joint.Count() > 0)
                        {
                            // TODO finish detecting hand pose
                            position rShoulderPos = null;
                            position rElbowPos = null;

                            foreach (joint j in myBody.layer2.skeleton.joint)
                            {
                                if (j.jointName.Equals("right_shoulder"))
                                {
                                    rShoulderPos = j.position;
                                }
                                if (j.jointName.Equals("right_elbow"))
                                {
                                    rElbowPos = j.position;
                                }
                            }

#if debug
                            Console.WriteLine("rShoulderPos", rShoulderPos.y);
#endif
                            if (rShoulderPos != null && rElbowPos != null)
                            {
                                position rHandPos = myBody.layer2.handPoseRight.position;
                                if (rHandPos.y >= (rShoulderPos.y + rElbowPos.y) / 2)
                                {
                                    rHandUpFrameCount++;
                                }
                                else
                                {
                                    rHandUpFrameCount = 0;
                                }
                            }
                        }

                        if (rHandUpFrameCount >= 10 || lHandUpFrameCount >= 10)
                        {
                            vhmsg.SendMessage("userActivities", "RaiseHand");
                            Console.WriteLine("The user raises his/her hand.");
                        }
                    }
                }
            }

        }

        static void SendMessage(pml myPML)
        {
            if (myPML.body.Length > 0)
            {
                body myBody = myPML.body[0];

                if (trackHead)
                {
                    if (myBody.layer2 != null && myBody.layer2.headPose != null && myBody.layer2.headPose.position != null)
                    {
                        string messageToBeSentToSBM = ConvertToQuaternionAndString(myBody.layer2.headPose.position.x, myBody.layer2.headPose.position.y, myBody.layer2.headPose.position.z, myBody.layer2.headPose.rotation.rotX, myBody.layer2.headPose.rotation.rotY, myBody.layer2.headPose.rotation.rotZ);
                        for (int i = 0; i < character.Length; ++i)
                        {
                            vhmsg.SendMessage("sbm", messageToBeSentToSBM.Replace(@"<character>", character[i]));
                        }

                    }
                }
                else if (trackGaze)
                {
                    if (shouldReadPML)
                    {
                        shouldReadPML = false;
                        if (myBody.layer2 != null && myBody.layer2.headPose != null && myBody.layer2.headPose.position != null)
                        {
                            //string messageToBeSentToSBM = ConvertToQuaternionAndString(myBody.layer2.headPose.position.x, myBody.layer2.headPose.position.y, myBody.layer2.headPose.position.z, myBody.layer2.headPose.rotation.rotX, myBody.layer2.headPose.rotation.rotY, myBody.layer2.headPose.rotation.rotZ);
                            string messageToBeSentToSBM = string.Empty;
                            if (myBody.layer2.headPose.rotation.rotY <= -0.2)
                            {
                                //try right
                                //messageToBeSentToSBM = @"bml char <character> <gaze direction=""LEFT"" angle=""80"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera"" sbm:handle=""bradGaze""/>";
                                messageToBeSentToSBM = @"bml char <character> <gaze direction=""LEFT"" angle=""80"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera""/>";
                            }
                            else if (myBody.layer2.headPose.rotation.rotY >= 0.2)
                            {
                                // try left
                                //messageToBeSentToSBM = @"bml char <character> <gaze direction=""RIGHT"" angle=""80"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera"" sbm:handle=""bradGaze""/>";
                                messageToBeSentToSBM = @"bml char <character> <gaze direction=""RIGHT"" angle=""80"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera""/>";
                            }
                            else
                            {
                                // no gaze
                                //messageToBeSentToSBM = @"bml char <character> <gaze direction=""RIGHT"" angle=""0"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera"" sbm:handle=""bradGaze""/>";
                                messageToBeSentToSBM = @"bml char <character> <gaze direction=""RIGHT"" angle=""0"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera""/>";
                            }
                            for (int i = 0; i < character.Length; ++i)
                            {
                                vhmsg.SendMessage("sbm", messageToBeSentToSBM.Replace(@"<character>", character[i]));
                            }

                        }
                    }
                }
                else if (trackAddressee)
                {
                    if (shouldReadPML)
                    {
                        shouldReadPML = false;
                        if (myBody.layer2 != null && myBody.layer2.headPose != null && myBody.layer2.headPose.position != null)
                        {
                            string characterOnLeft = @"rachel";
                            string characterOnRight = @"brad";

                            if (myBody.layer2.headPose.rotation.rotY <= -0.2)
                            {
                                //left
                                string messageToNPCEditor = string.Format(@"<action name=""SetFacingCharacter"" target=""user""><param name=""facingCharacter"">{0}</param></action>", characterOnLeft);
                                vhmsg.SendMessage("NPCEditor", messageToNPCEditor);
                            }
                            else if (myBody.layer2.headPose.rotation.rotY >= 0.2)
                            {
                                //right
                                string messageToNPCEditor = string.Format(@"<action name=""SetFacingCharacter"" target=""user""><param name=""facingCharacter"">{0}</param></action>", characterOnRight);
                                vhmsg.SendMessage("NPCEditor", messageToNPCEditor);
                            }
                            else
                            {
                                //reset
                                string messageToNPCEditor = string.Format(@"<action name=""SetFacingCharacter"" target=""user""><param name=""facingCharacter"">{0}</param></action>", "none");
                                vhmsg.SendMessage("NPCEditor", messageToNPCEditor);
                            }
                        }
                    }
                }
            }
        }

        static void ParseAndSendMessage(string xml)
        {
            //XmlSerializer serializer = new XmlSerializer(pml::typeid);
            pml myPML = new pml();
            XmlSerializer serializer = new XmlSerializer(myPML.GetType());
            //string strXML = xml;
            //strXML = strXML.Substring(strXML.IndexOf(" ") + 1);
            StringReader stringReader = new StringReader(xml);

            XmlTextReader xmlReader = new XmlTextReader(stringReader);

            myPML = (pml)serializer.Deserialize(xmlReader);
            
            xmlReader.Close();
            stringReader.Close();


            if (myPML.body.Length > 0)
            {
                body myBody = myPML.body[0];

                if (trackHead)
                {
                    if (myBody.layer2 != null && myBody.layer2.headPose != null && myBody.layer2.headPose.position != null)
                    {
                        string messageToBeSentToSBM = ConvertToQuaternionAndString(myBody.layer2.headPose.position.x, myBody.layer2.headPose.position.y, myBody.layer2.headPose.position.z, myBody.layer2.headPose.rotation.rotX, myBody.layer2.headPose.rotation.rotY, myBody.layer2.headPose.rotation.rotZ);
                        for (int i = 0; i < character.Length; ++i)
                        {
                            vhmsg.SendMessage("sbm", messageToBeSentToSBM.Replace(@"<character>", character[i]));
                        }

                    }
                }
                else if (trackGaze)
                {
                    if (shouldReadPML)
                    {
                        shouldReadPML = false;
                        if (myBody.layer2 != null && myBody.layer2.headPose != null && myBody.layer2.headPose.position != null)
                        {
                            //string messageToBeSentToSBM = ConvertToQuaternionAndString(myBody.layer2.headPose.position.x, myBody.layer2.headPose.position.y, myBody.layer2.headPose.position.z, myBody.layer2.headPose.rotation.rotX, myBody.layer2.headPose.rotation.rotY, myBody.layer2.headPose.rotation.rotZ);
                            string messageToBeSentToSBM = string.Empty;
                            if (myBody.layer2.headPose.rotation.rotY <= -0.2)
                            {
                                //try right
                                //messageToBeSentToSBM = @"bml char <character> <gaze direction=""LEFT"" angle=""80"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera"" sbm:handle=""bradGaze""/>";
                                messageToBeSentToSBM = @"bml char <character> <gaze direction=""LEFT"" angle=""80"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera""/>";
                            }
                            else if (myBody.layer2.headPose.rotation.rotY >= 0.2)
                            {
                                // try left
                                //messageToBeSentToSBM = @"bml char <character> <gaze direction=""RIGHT"" angle=""80"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera"" sbm:handle=""bradGaze""/>";
                                messageToBeSentToSBM = @"bml char <character> <gaze direction=""RIGHT"" angle=""80"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera""/>";
                            }
                            else
                            {
                                // no gaze
                                //messageToBeSentToSBM = @"bml char <character> <gaze direction=""RIGHT"" angle=""0"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera"" sbm:handle=""bradGaze""/>";
                                messageToBeSentToSBM = @"bml char <character> <gaze direction=""RIGHT"" angle=""0"" sbm:joint-range=""EYES NECK""  sbm:joint-speed=""500 100"" target=""Camera""/>";
                            }
                            for (int i = 0; i < character.Length; ++i)
                            {
                                vhmsg.SendMessage("sbm", messageToBeSentToSBM.Replace(@"<character>", character[i]));
                            }

                        }
                    }
                }
                else if (trackAddressee)
                {
                    if (shouldReadPML)
                    {
                        shouldReadPML = false;
                        if (myBody.layer2 != null && myBody.layer2.headPose != null && myBody.layer2.headPose.position != null)
                        {
                            string characterOnLeft = @"rachel";
                            string characterOnRight = @"brad";

                            if (myBody.layer2.headPose.rotation.rotY <= -0.2)
                            {
                                //left
                                string messageToNPCEditor = string.Format(@"<action name=""SetFacingCharacter"" target=""user""><param name=""facingCharacter"">{0}</param></action>", characterOnLeft);
                                vhmsg.SendMessage("NPCEditor", messageToNPCEditor);
                            }
                            else if (myBody.layer2.headPose.rotation.rotY >= 0.2)
                            {
                                //right
                                string messageToNPCEditor = string.Format(@"<action name=""SetFacingCharacter"" target=""user""><param name=""facingCharacter"">{0}</param></action>", characterOnRight);
                                vhmsg.SendMessage("NPCEditor", messageToNPCEditor);
                            }
                            else
                            {
                                //reset
                                string messageToNPCEditor = string.Format(@"<action name=""SetFacingCharacter"" target=""user""><param name=""facingCharacter"">{0}</param></action>", "none");
                                vhmsg.SendMessage("NPCEditor", messageToNPCEditor);
                            }
                        }
                    }
                }
            }
        }


        private static string ConvertToQuaternionAndString(double x, double y, double z, double rotX, double rotY, double rotZ)
        {
            //For converting data from euler to quaternion
            double cos_z_2 = Math.Cos(0.5*rotX);
            double cos_y_2 = Math.Cos(0.5*rotZ);
            double cos_x_2 = Math.Cos(0.5*rotY);

            double sin_z_2 = Math.Sin(0.5*rotX);
            double sin_y_2 = Math.Sin(0.5*rotZ);
            double sin_x_2 = Math.Sin(0.5*rotY);

            // and now compute quaternion
            double quatW = cos_z_2*cos_y_2*cos_x_2 + sin_z_2*sin_y_2*sin_x_2;
            double quatX = cos_z_2*cos_y_2*sin_x_2 - sin_z_2*sin_y_2*cos_x_2;
            double quatY = cos_z_2*sin_y_2*cos_x_2 + sin_z_2*cos_y_2*sin_x_2;
            double quatZ = sin_z_2*cos_y_2*cos_x_2 - cos_z_2*sin_y_2*sin_x_2;

#if JUN
            return string.Format(@"bml char <character> <head type=""NOD""/>");
#else
            return string.Format("receiver skeleton <character> generic rotation skullbase {0} {1} {2} {3}", quatW, quatX, quatY, quatZ);
#endif
        }

        

        private static void MessageAction(object sender, VHMsg.Message args)
        {
            //Console.WriteLine("Received Message '" + args.s + "'");

            //Ict.ElvinUtility eu = (Ict.ElvinUtility)sender;

            string[] splitargs = args.s.Split(" ".ToCharArray());
#if JUN
            Console.WriteLine(splitargs[0]);
            Console.WriteLine(splitargs.Length);
            Console.WriteLine(shouldListen);
#endif

            if (splitargs.Length > 0)
            {
                if (splitargs[0] == "vrPerception")
                {
                    if (shouldListen)
                    {
                        if (splitargs.Length > 3)
                        {
//#if JUN
//                            Console.WriteLine(splitargs[1]);
//#endif
                            // TODO body motion detect
                            string pmlArg = args.s.Substring(args.s.IndexOf(splitargs[2]));
                            //ParseAndSendMessage(pmlArg);
                            pml myPML = ParsePML(pmlArg);
                            SendMessage(myPML);
                            SendLeaningMessage(myPML);
                            SendInterruptionMessage(myPML);
                        }
                    }
                }
                else if (splitargs[0] == "vrPerceptionApplication")
                {
                    if (splitargs.Length >= 2)
                    {
                        string mode = splitargs[1];

                        if (mode.ToUpper().Trim().Equals("TOGGLE"))
                        {
                            //shouldListen = !shouldListen;
                            shouldListen = false;
                            CharacterReset();
                            Console.WriteLine("TOGGLE - " + shouldListen);
                        }
                        else if (mode.ToUpper().Trim().Equals("TRACKGAZE"))
                        {
                            shouldListen = true;
                            trackGaze = true;
                            trackEyes = false;
                            trackHead = false;
                            trackSmile = false;
                            trackAddressee = false;
                            CharacterReset();
                            //gazeTimer = new System.Timers.Timer(gazeTime);
                            gazeTimer.Interval = gazeTime;
                            gazeTimer.Enabled = true;
                            Console.WriteLine("TrackGaze - ON");
                        }
                        else if (mode.ToUpper().Trim().Equals("TRACKHEAD"))
                        {
                            shouldListen = true;
                            trackGaze = false;
                            trackEyes = false;
                            trackHead = true;
                            trackSmile = false;
                            trackAddressee = false;
                            CharacterReset();
                            gazeTimer.Enabled = false;
                            Console.WriteLine("TrackHead - ON");
                        }
                        else if (mode.ToUpper().Trim().Equals("TRACKADDRESSEE"))
                        {
                            shouldListen = true;
                            trackGaze = false;
                            trackEyes = false;
                            trackHead = false;
                            trackSmile = false;
                            trackAddressee = true;
                            CharacterReset();
                            gazeTimer.Interval = gazeTime;
                            gazeTimer.Enabled = true;
                            //gazeTimer.Enabled = false;
                            Console.WriteLine("TrackAddressee - ON");
                        }
                        else if (mode.ToUpper().Trim().Equals("RESET"))
                        {
                            Reset();
                            Console.WriteLine("RESET");
                        }
                        else if (mode.ToUpper().Trim().Equals("RESETCOUNTER"))
                        {
                            ResetCounter();
                        }
                        else if (mode.ToUpper().Trim().Equals("TRACKLEANING"))
                        {
                            shouldListen = true;
                            trackLeaning = true;
                            Console.WriteLine("TrackLeaning - ON");
                        }
                        else if (mode.ToUpper().Trim().Equals("TRACKHAND"))
                        {
                            shouldListen = true;
                            trackHand = true;
                            Console.WriteLine("TrackHand - ON");
                        }
                        else if (mode.ToUpper().Trim().Equals("TESTTRACKHAND"))
                        {
                            StringBuilder sb = new StringBuilder();
                            using (StreamReader file = new StreamReader(@"D:\Jun\TRUST\docs\OneFrameTestHand.txt"))
                            {
                                string line;
                                while ((line = file.ReadLine()) != null)
                                {
                                    sb.Append(line).Append("\n");
                                }
                            }

                            for (int i = 0; i < 10; i++)
                            {
                                vhmsg.SendMessage("vrPerception", sb.ToString());
                            }

                        }
                        //ParseAndSendMessage(pmlArg);
                    }
                }
                else if (splitargs[0] == "vrAllCall")
                {
                    vhmsg.SendMessage("vrComponent perception-test-application");
                }
                else if (splitargs[0] == "vrKillComponent")
                {
                    if (splitargs.Length > 1)
                    {
                        if (splitargs[1] == "all" ||
                            splitargs[1] == "perception-test-application")
                        {
                            isRunning = false;
                        }
                    }
                }
            }
        }


        private static void ReadConfigFile(string configFile)
        {
            string dir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            configFile = @"D:\Jun\TRUST\MultiSenseMiddleMan\MSMiddleMan\MSMiddleMan\" + configFile;

            StringBuilder temp = new StringBuilder(255);
            GetPrivateProfileString("Global", "trackHead", "true", temp, 255, configFile);
            trackHead = ToBoolean(temp.ToString());

            temp = new StringBuilder(255);
            GetPrivateProfileString("Global", "trackEyes", "false", temp, 255, configFile);
            trackEyes = ToBoolean(temp.ToString());

            temp = new StringBuilder(255);
            GetPrivateProfileString("Global", "trackSmile", "false", temp, 255, configFile);
            trackSmile = ToBoolean(temp.ToString());

            temp = new StringBuilder(255);
            GetPrivateProfileString("Global", "trackGaze", "false", temp, 255, configFile);
            trackGaze = ToBoolean(temp.ToString());

            temp = new StringBuilder(255);
            GetPrivateProfileString("Global", "trackAddressee", "false", temp, 255, configFile);
            trackAddressee = ToBoolean(temp.ToString());


            temp = new StringBuilder(255);
            GetPrivateProfileString("Global", "numberOfCharacters", "1", temp, 255, configFile);
            int numOfChars = Int32.Parse((temp.ToString()));

            character = new string[numOfChars];

            for (int i = 0; i < numOfChars; ++i)
            {
                temp = new StringBuilder(255);
                GetPrivateProfileString("Global", "character" + (i+1), "brad", temp, 255, configFile);
                character[i] = temp.ToString();
                Console.WriteLine("Character - " + character[i]);
            }

            temp = new StringBuilder(255);
            GetPrivateProfileString("Global", "gazeTimer", "200", temp, 255, configFile);
            gazeTime = Int32.Parse((temp.ToString()));

            if (trackGaze)
            {
                gazeTimer.Interval = gazeTime;
                gazeTimer.Enabled = true;
            }
        }

        private static void ResetCounter()
        {
            deltaDsIn10.Clear();
            deltaDsInHalf.Clear();
            isLeaningForward = false;
            isLeaningBackward = false;
            bodyMotionIn10 = 0;
            forwardTotal = 0;
            backwardTotal = 0;
            trackLeaning = false;
            prevZ = null;
        }

        private static bool ToBoolean(string s)
        {
            if (s == "false" || s == "False" || s == "0" || s == "FALSE" || s == "NO" || s == "no" || s == "No")
                return false;
            else
                return true;
        }
    }
}
