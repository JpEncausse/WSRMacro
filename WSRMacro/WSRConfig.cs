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
    
    // Grammar contex
    public List<string> directories = new List<string>();
    public List<string> context = new List<string>();
    private bool hasContext = false;

    public double trigger = 0.85;
    public double confidence = 0.70;
    public double dictation  = 0.40;
    public bool adaptation = false;

    // NodeJS Server
    public String server = "127.0.0.1";
    public String port = "8080";

    // Engine language
    public String language = "fr-FR";
    public String voice = null;

    // Websocket port
    public int websocket = -1;
    
    // Httpserver port
    public int loopback = 8088;

    public bool DEBUG = false;

    bool kinect   = false;
    bool gesture  = false;
    bool picture  = false;

    public int faceTrackingOn = 6;
    public int faceTrackingOff = 24;
    public int faceTrackingReq = 5;

    // ==========================================
    //  CONSTRUCTOR
    // ==========================================

    OptionSet options;
    private WSRConfig() {

      // Parse double
      var culture = CultureInfo.CreateSpecificCulture("en-US");

      // Build OptionSet
      this.options = new OptionSet() {
        { "d|directory=", "the {DIRECTORY} of grammar. (default is /macros)", v => directories.Add (v) },
        { "ctx|context=",  "the starting context files", v => context.Add (v) },
        
        { "t|trigger=", "the Grammar {CONFIDENCE TRIGGER}. (default is 0.92)", v => trigger = double.Parse(v, culture) },
        { "c|confidence=", "the Grammar {CONFIDENCE}. (default is 0.75)", v => confidence = double.Parse(v, culture) },
        { "dictation=", "the Grammar {CONFIDENCE} for dictation. (default is 0.40)", v => dictation = double.Parse(v, culture) },
        { "l|loopback=", "the local {PORT}. (default is 8088)", v => loopback = int.Parse(v, culture) },
        { "language",  "the recognition engine {LANGUAGE}", v => language = v },
        { "debug",  "display more debug data", v => DEBUG = v != null },

        { "s|server=", "the NodeJS {SERVER}. (default is 127.0.0.1)", v => server = v },
        { "p|port=", "the NodeJS {PORT}. (default is 8080)", v => port = v },
        
        { "k|kinect", "the {KINECT} mode. (default is false)", v => kinect = v != null },
        { "g|gesture", "the {KINECT} gesture mode. (default is false)", v => gesture = v != null },
        { "f|picture", "the {KINECT} picture mode. (default is false)", v => picture = v != null },
        { "h|help",  "show this message and exit", v => help = v != null },
        
        
        { "sck|websocket=",  "the websocket server port (should be 7777)", v => websocket = int.Parse(v, culture) },
        
      };
    }

    // Singleton
    private static WSRConfig config;
    public static WSRConfig GetInstance() {
      if (config == null) {
        config = new WSRConfig();
      }
      return config;
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
          else if (property.Key == "trigger")    { trigger = double.Parse(property.Value, culture); }
          else if (property.Key == "confidence") { confidence = double.Parse(property.Value, culture); }
          else if (property.Key == "dictation")  { dictation = double.Parse(property.Value, culture); }
          else if (property.Key == "adaptation") { adaptation = bool.Parse(property.Value); }
          else if (property.Key == "loopback")   { loopback = int.Parse(property.Value, culture); }
          else if (property.Key == "language")   { language = property.Value;  }
          else if (property.Key == "voice")      { voice = property.Value; }
          else if (property.Key == "debug")      { DEBUG = bool.Parse(property.Value); }

          else if (property.Key == "server")     { server = property.Value; }
          else if (property.Key == "port")       { port = property.Value; }

          else if (property.Key == "kinect")     { kinect = bool.Parse(property.Value); }
          else if (property.Key == "gesture")    { gesture = bool.Parse(property.Value); }
          else if (property.Key == "picture")    { picture = bool.Parse(property.Value); }
          else if (property.Key == "websocket")  { websocket = int.Parse(property.Value, culture); }

          else if (property.Key == "faceTrackingOn")  { faceTrackingOn = int.Parse(property.Value, culture); }
          else if (property.Key == "faceTrackingOff") { faceTrackingOff = int.Parse(property.Value, culture); }
          else if (property.Key == "faceTrackingReq") { faceTrackingReq = int.Parse(property.Value, culture); }
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
      Console.WriteLine("Usage: WSRLaunch [OPTIONS]+ message");
      Console.WriteLine();
      Console.WriteLine("Options:");
      p.WriteOptionDescriptions(Console.Out);
    }

    // ==========================================
    //  GETTER / SETTER
    // ==========================================

    public bool IsKinect() {
      return kinect;
    }

    public bool IsPictureMode() {
      return picture;
    }

    public bool IsGestureMode() {
      return gesture;
    }

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
    //  LOGGING
    //  http://www.brutaldev.com/post/2012/03/27/Logging-setup-in-5-minutes-with-NLog
    // ==========================================

    public void SetupLogging() {

      LoggingConfiguration config = new LoggingConfiguration();

      // Build Targets ----------

      ColoredConsoleTarget consoleTarget = new ColoredConsoleTarget();
      consoleTarget.Layout = "${date:format=HH\\:MM\\:ss} ${logger} ${message}";

      FileTarget fileTarget = new FileTarget();
      fileTarget.FileName = "${basedir}/${shortdate}.log";
      fileTarget.Layout = "${message}";

      var sentinalTarget = new NLogViewerTarget() {
        Name = "sentinal",
        Address = "udp://127.0.0.1:9999"
      };

      // Add Targets ----------

      config.AddTarget("console", consoleTarget);
      config.AddTarget("file", fileTarget);
      config.AddTarget("sentinal", sentinalTarget);

      // Add Rules ----------

      LoggingRule rule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
      config.LoggingRules.Add(rule1);

      LoggingRule rule2 = new LoggingRule("*", LogLevel.Debug, fileTarget);
      config.LoggingRules.Add(rule2);

      LoggingRule rule3 = new LoggingRule("*", LogLevel.Debug, sentinalTarget);
      config.LoggingRules.Add(rule3);

      LogManager.Configuration = config;
    
    }

    public void logInfo(String context, String msg) {
      Logger logger = LogManager.GetLogger("SARAH");
      logger.Info("[{0}] [{1}]\t {2}", DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss"), context, msg);
    }

    public void logDebug(String context, String msg) {
      if (!DEBUG) { return; }
      Logger logger = LogManager.GetLogger("SARAH");
      logger.Debug("[{0}] [{1}]\t {2}", DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss"), context, msg);
    }

    public void logError(String context, Exception ex) {
      Logger logger = LogManager.GetLogger("SARAH");
      logger.Error("[{0}] [{1}]\t {2}", DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss"), context, ex);
    }
  }
}
