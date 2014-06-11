using System;
using System.Collections.Generic;
using System.Globalization;
using System.Configuration;

using NDesk.Options;

using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Layouts;
using CodeBits;

namespace net.encausse.sarah {

  public class WSRConfig {
    bool help = false;

    // Logging
    public bool DEBUG  = false;
    private int udpport = 9999;
    private String logfile = null;

    // Grammar context
    public String Name = "SARAH";
    public String Id   = "SARAH";
    public double trigger = 0.85;
    public double confidence = 0.70;

    public int restart = 1000 * 60 * 60; // 1 hour

    // Pitch
    public int PitchDelta = 40;

    // Httpserver port
    public int loopback = 8888;

    // RTPPort
    public int rtpport = -1;

    // Engine language
    public String language = "fr-FR";
    public String voice = null;
    public String audio = "audio";
    public String Speakers = "0";
    public int SpkVolTTS  = 100;
    public int SpkVolPlay = 100;

    public bool SpeechOnly = false;

    // Google
    public String GoogleKey { get; set; }

    // Grammar
    public int ctxTimeout = 30000;
    public List<string> directories = new List<string>();
    public List<string> context = new List<string>();
    private bool hasContext = false;

    // NodeJS Server
    private String server = "127.0.0.1";
    private String port = "8080";

    // Kinect
    public bool Adaptation { get; set; }
    public bool IsSeated   { get; set; }
    public int SensorElevation { get; set; }
    public int GestureFix = 80;
    public int Echo = -1;
    public int FPS = 1;

    public int MaxAlternates = 10;
    public TimeSpan InitialSilenceTimeout      = TimeSpan.FromSeconds(0);
    public TimeSpan BabbleTimeout              = TimeSpan.FromSeconds(0);
    public TimeSpan EndSilenceTimeout          = TimeSpan.FromSeconds(0.150);
    public TimeSpan EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(0.500);

    public bool StandByGesture = false;
    public bool StandByFace    = false;

    // Task (in ms)
    public TimeSpan Motion      = TimeSpan.FromMilliseconds(200);
    public TimeSpan Gesture     = TimeSpan.FromMilliseconds(45);
    public TimeSpan QRCode      = TimeSpan.FromMilliseconds(200);
    public TimeSpan Color       = TimeSpan.FromMilliseconds(0);
    public TimeSpan FaceDetec   = TimeSpan.FromMilliseconds(45);
    public TimeSpan FaceReco    = TimeSpan.FromMilliseconds(200);
    public TimeSpan FaceTrack   = TimeSpan.FromMilliseconds(45);
    public TimeSpan FaceRepaint = TimeSpan.FromMilliseconds(15);

    public int MotionTH = 10;
    public TimeSpan StandBy     = TimeSpan.FromMilliseconds(300000);
    public TimeSpan GestureTH   = TimeSpan.FromMilliseconds(1000);
    public TimeSpan QRCodeTH    = TimeSpan.FromMilliseconds(2000);
    public TimeSpan ColorTH     = TimeSpan.FromMilliseconds(0);
    public TimeSpan FaceTH      = TimeSpan.FromMilliseconds(300000);

    // Websocket port
    public int WebSocket  = -1;
    public String WSType  = "png";
    public bool WSSmooth  = false;
    public bool WSAverage = false;

    // ==========================================
    //  GETTER / SETTER
    // ==========================================

    public bool HasContext() {
      return hasContext;
    }

    public String GetRemoteURL() {
      return "http://" + server + ":" + port;
    }

    public String getDirectory() {
      if (directories == null) {
        return "";
      }
      return directories[0];
    }

    // ==========================================
    //  CONSTRUCTOR
    // ==========================================

    // Singleton
    private static WSRConfig config;
    public  static WSRConfig GetInstance() {
      if (config == null) {
        config = new WSRConfig();
      }
      return config;
    }

    OptionSet options;
    private WSRConfig() {

      // Parse double
      var culture = CultureInfo.CreateSpecificCulture("en-US");

      // Build OptionSet
      this.options = new OptionSet() {
        { "k|kinect",  "the {KINECT} mode. (default is false)", v => IsKinect = v != null },
        { "audio",  "the {KINECT} audio mode only.", v => SpeechOnly = v != null },
        { "s|server=", "the NodeJS {SERVER}. (default is 127.0.0.1)", v => server = v },
        { "name=", "the client name. (default is SARAH)", v => Name = v },
        { "client=", "the client id. (default is SARAH)", v => Id = v },
        { "p|port=", "the NodeJS {PORT}. (default is 8080)", v => port = v },
        { "d|directory=", "the {DIRECTORY} of grammar. (default is /macros)", v => directories.Add (v) },
        { "t|trigger=", "the Grammar {CONFIDENCE TRIGGER}. (default is 0.92)", v => trigger = double.Parse(v, culture) },
        { "c|confidence=", "the Grammar {CONFIDENCE}. (default is 0.75)", v => confidence = double.Parse(v, culture) },
        { "l|loopback=", "the local {PORT}. (default is 8088)", v => loopback = int.Parse(v, culture) },
        { "language",  "the recognition engine {LANGUAGE}", v => language = v },
        { "debug",  "display more debug data", v => DEBUG = v != null },
        { "h|help",  "show this message and exit", v => help = v != null }
      };
    }

    // ==========================================
    //  OPTIONS
    // ==========================================

    bool parse = false;
    public WSRConfig Parse(string[] args) {

      // Loading INI File
      ParseINI();

      // Parsing arguments
      List<string> extra;
      try { extra = options.Parse(args); }
      catch (OptionException e) {
        Console.Write("WSRLaunch: ");
        Console.WriteLine(e.Message);
        Console.WriteLine("Try `WSRLaunch --help' for more information.");
        return this;
      }

      // Parsing done
      parse = true;
      return this;
    }

    private void ParseINI() {
      // Parse double
      var culture = CultureInfo.CreateSpecificCulture("en-US");

      var ini = new IniFile(@"custom.ini");
      foreach (IniFile.Section section in ini) {
        foreach (IniFile.Property property in section) {
          if (section.Name == "directory") {
            directories.Add(property.Value);
          }
          else if (section.Name == "context") {
            context.Add(property.Value);
          }
          else if (property.Key == "debug")        { DEBUG       = bool.Parse(property.Value);}
          else if (property.Key == "logfile")      { logfile     = property.Value; }
          else if (property.Key == "udpport")      { udpport     = int.Parse(property.Value); }
          else if (property.Key == "ctxTimeout")   { ctxTimeout  = int.Parse(property.Value); }
          else if (property.Key == "restart")      { restart     = int.Parse(property.Value); }
          else if (property.Key == "pitch")        { PitchDelta  = int.Parse(property.Value);  }
          else if (property.Key == "name")         { Name        = property.Value; }
          else if (property.Key == "client")       { Id          = property.Value; }
          else if (property.Key == "trigger")      { trigger     = double.Parse(property.Value, culture); }
          else if (property.Key == "confidence")   { confidence  = double.Parse(property.Value, culture); }
          else if (property.Key == "adaptation")   { Adaptation  = bool.Parse(property.Value); }
          else if (property.Key == "loopback")     { loopback    = int.Parse(property.Value, culture); }
          else if (property.Key == "rtpport")      { rtpport     = int.Parse(property.Value, culture); }
          else if (property.Key == "fps")          { FPS         = int.Parse(property.Value, culture); }
          else if (property.Key == "language")     { language    = property.Value; }
          else if (property.Key == "voice")        { voice       = property.Value; }
          else if (property.Key == "audio")        { audio       = property.Value; }
          else if (property.Key == "server")       { server      = property.Value; }
          else if (property.Key == "port")         { port        = property.Value; }
          else if (property.Key == "speakers")     { Speakers    = property.Value; }
          else if (property.Key == "google")       { GoogleKey = property.Value;   }
          else if (property.Key == "only")         { SpeechOnly = bool.Parse(property.Value); }
          else if (property.Key == "spVolTTS")     { SpkVolTTS   = int.Parse(property.Value);  }
          else if (property.Key == "spVolPlay")    { SpkVolPlay  = int.Parse(property.Value);  }
          else if (property.Key == "echo")         { Echo        = int.Parse(property.Value);  }
          else if (property.Key == "kinect")       { IsKinect    = bool.Parse(property.Value); }
          else if (property.Key == "seated")       { IsSeated    = bool.Parse(property.Value); }
          else if (property.Key == "elevation")    { SensorElevation = int.Parse(property.Value); }
          else if (property.Key == "gestureFix")   { GestureFix  = int.Parse(property.Value); }
          else if (property.Key == "motionTH")     { MotionTH    = int.Parse(property.Value); }
          else if (property.Key == "standby")      { StandBy     = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "motion")       { Motion      = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "gesture")      { Gesture     = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "gestureTH")    { GestureTH   = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "qrcode")       { QRCode      = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "qrcodeTH")     { QRCodeTH    = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "color")        { Color       = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "colorTH")      { ColorTH     = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "facedetec")    { FaceDetec   = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "facereco")     { FaceReco    = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "facetrack")    { FaceTrack   = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "faceTH")       { FaceTH      = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "facerepaint")  { FaceRepaint = TimeSpan.FromMilliseconds(int.Parse(property.Value)); }
          else if (property.Key == "websocket")    { WebSocket   = int.Parse(property.Value, culture); }
          else if (property.Key == "websockSmooth"){ WSSmooth    = bool.Parse(property.Value); }
          else if (property.Key == "websockAvg")   { WSAverage   = bool.Parse(property.Value); }
          else if (property.Key == "gestureSB")    { StandByGesture = bool.Parse(property.Value); }
          else if (property.Key == "faceSB")       { StandByFace = bool.Parse(property.Value); }
          else if (property.Key == "alternate")           { MaxAlternates              = int.Parse(property.Value); }
          else if (property.Key == "initialSilence")      { InitialSilenceTimeout      = TimeSpan.FromSeconds(double.Parse(property.Value, culture)); }
          else if (property.Key == "babble")              { BabbleTimeout              = TimeSpan.FromSeconds(double.Parse(property.Value, culture)); }
          else if (property.Key == "endSilence")          { EndSilenceTimeout          = TimeSpan.FromSeconds(double.Parse(property.Value, culture)); }
          else if (property.Key == "endSilenceAmbiguous") { EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(double.Parse(property.Value, culture)); }

        }
      }
    }

    public bool Validate() {

      // No parsing done
      if (!parse) { return false; }

      // Show Help
      if (help) {
        ShowHelp(this.options);
        return false;
      }

      // Set Default Values
      if (directories.Count == 0) {
        directories.Add("macros");
      }

      this.hasContext = context != null && context.Count > 0;

      return true;
    }

    // ==========================================
    //  HELPER
    // ==========================================

    protected void ShowHelp(OptionSet p) {
      Console.WriteLine("Usage: {process} [OPTIONS]+ message");
      Console.WriteLine();
      Console.WriteLine("Options:");
      p.WriteOptionDescriptions(Console.Out);
    }

    // ==========================================
    //  MICRO or KINECT
    // ==========================================
    
    // Internal
    private bool IsKinect { get; set; }
    public  WSRMicro WSR  { get; set; }

    public void Start(){
      if (WSR != null) return;

      logInfo("INIT", "==========================================");
      logInfo("INIT", "S.A.R.A.H. => " + (IsKinect ? "KINECT" : "MICRO"));
      logInfo("INIT", "==========================================");
      logInfo("INIT", "Server: " + GetRemoteURL());
      logInfo("INIT", "Confidence: " + confidence);
      logInfo("INIT", "==========================================");

      WSR = config.IsKinect ? new WSRKinect() : new WSRMicro();
      WSR.Init();
    }

    // ==========================================
    //  LOGGING
    //  http://www.brutaldev.com/post/2012/03/27/Logging-setup-in-5-minutes-with-NLog
    // ==========================================

    public void SetupLogging() {

      LoggingConfiguration config = new LoggingConfiguration();
      
      // Console ----------
      ColoredConsoleTarget consoleTarget = new ColoredConsoleTarget();
      consoleTarget.Layout = "${date:format=HH\\:MM\\:ss} ${logger} ${message}";

      config.AddTarget("console", consoleTarget);

      LoggingRule rule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
      config.LoggingRules.Add(rule1);

      LoggingRule rule2 = new LoggingRule("*", LogLevel.Warn, consoleTarget);
      config.LoggingRules.Add(rule2);

      // File ----------

      if (null != logfile) {
        FileTarget fileTarget = new FileTarget();
        fileTarget.FileName = logfile;
        fileTarget.Layout = "${message}";

        config.AddTarget("file", fileTarget);

        LoggingRule rule3 = new LoggingRule("*", LogLevel.Debug, fileTarget);
        config.LoggingRules.Add(rule3);

        LoggingRule rule4 = new LoggingRule("*", LogLevel.Warn, fileTarget);
        config.LoggingRules.Add(rule4);
      }

      // View ----------

      var viewerTarget = new NLogViewerTarget() {
        Name = "viewer",
        Address = "udp://localhost:" + udpport,
        Layout = "${message}"
      };
      viewerTarget.Renderer.IncludeNLogData = false;
      config.AddTarget("viewer", viewerTarget);

      LoggingRule rule5 = new LoggingRule("*", LogLevel.Debug, viewerTarget);
      config.LoggingRules.Add(rule5);

      LoggingRule rule6 = new LoggingRule("*", LogLevel.Warn, viewerTarget);
      config.LoggingRules.Add(rule6);

      LogManager.ReconfigExistingLoggers();
      LogManager.Configuration = config;
      logInfo("LOGGING", "STARTING LOGGING");
    }

    public void logInfo(String context, String msg) {
      Logger logger = LogManager.GetLogger("SARAH");
      logger.Info("[{0}] [{1}]\t {2}", DateTime.Now.ToString("HH:mm:ss"), context, msg);
    }
    public void logWarning(String context, String msg) {
      Logger logger = LogManager.GetLogger("SARAH");
      logger.Warn("[{0}] [{1}]\t {2}", DateTime.Now.ToString("HH:mm:ss"), context, msg);
    }
    public void logDebug(String context, String msg) {
      if (!DEBUG) { return; }
      Logger logger = LogManager.GetLogger("SARAH");
      logger.Debug("[{0}] [{1}]\t {2}", DateTime.Now.ToString("HH:mm:ss"), context, msg);
    }
    public void logError(String context, String msg) {
      Logger logger = LogManager.GetLogger("SARAH"); 
      logger.Error("[{0}] [{1}]\t {2}", DateTime.Now.ToString("HH:mm:ss"), context, msg);
    }
    public void logError(String context, Exception ex) {
      Logger logger = LogManager.GetLogger("SARAH");
      logger.Error("[{0}] [{1}]\t {2}", DateTime.Now.ToString("HH:mm:ss"), context, ex);
    }
  }
}
