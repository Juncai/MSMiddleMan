//#define JUN
//#define detectLeanByHeadPos
//#define debug
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Timers;
using System.Speech.Recognition;
using MSMiddleMan;

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
        private static double? prevZ;

        private static FSTracker fsTracker = null;
        private static bool trackLeaning = false;
        private static bool trackHand = false;
        private static DateTime lastSpeechDetected = DateTime.Now;
        private static bool speechDetected = false;
        private static bool userInteracting = false;
        private static bool mouthOpen = false;
        private static bool isTalking = false;

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
            using (SpeechRecognitionEngine recognizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("en-US")))
            using (fsTracker = new FSTracker(vhmsg))
            {
                //recognizer.PauseRecognizerOnRecognition = true;
                //recognizer.UnloadAllGrammars();
                recognizer.SetInputToDefaultAudioDevice();
                Choices greetings = new Choices();
                greetings.Add(new string[] { "Hi", "Rachel" });
                GrammarBuilder gb = new GrammarBuilder();
                gb.Append(greetings);
                Grammar g = new Grammar(gb);
                recognizer.LoadGrammar(g);
                recognizer.SpeechRecognized +=
                    new EventHandler<SpeechRecognizedEventArgs>(sre_SpeechRecognized);
                recognizer.SpeechDetected +=
                    new EventHandler<SpeechDetectedEventArgs>(ser_SpeechDetected);

                // start detecting
                recognizer.RecognizeAsync(RecognizeMode.Multiple);
                fsTracker.startTracking();

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
                vhmsg.SubscribeMessage("vrAllCall");
                vhmsg.SubscribeMessage("vrKillComponent");
                vhmsg.SubscribeMessage("vrPerceptionApplication");

                vhmsg.SendMessage("vrComponent perception-test-application");


                while (isRunning)
                {
                    Thread.Sleep(100);
                    if (isTalking && !speechDetected && DateTime.Now.Subtract(lastSpeechDetected).TotalMilliseconds > 3000)
                    {
                        //userInteracting = false;
                        isTalking = false;
                        vhmsg.SendMessage("userActivities stopTalking");
                        Console.WriteLine("userActivities stopTalking");
                    }

                    // get mouth state
                    mouthOpen = fsTracker.mouthOpen;

                    speechDetected = false;

                    //Console.WriteLine(recognizer.AudioState);
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

        static void ser_SpeechDetected(object sender, SpeechDetectedEventArgs e)
        {
#if debug
            Console.WriteLine("there is a speech");
#endif
            speechDetected = true;
            lastSpeechDetected = DateTime.Now;
        }

        static void sre_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            Console.WriteLine(e.Result.Text);
            if (!userInteracting && mouthOpen)
            {
                // TODO send speaking msg
                vhmsg.SendMessage("userActivities speak");
                Console.WriteLine("userActivities speak");
                isTalking = true;
                //userInteracting = true;
            }
        }

        static void CleanupBeforeExiting()
        {
            vhmsg.SendMessage("vrProcEnd Middleman-application");
            Reset();
            vhmsg.CloseConnection();
        }

        private static void Reset()
        {
            //CharacterReset();

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
                if (splitargs[0] == "vrPerceptionApplication")
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
