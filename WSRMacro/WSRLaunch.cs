using System;
using System.Globalization;
using System.Collections.Generic;
using NDesk.Options;

namespace encausse.net {

  class WSRLaunch {
    
    static void Main(string[] args) {

      bool help = false;
      List<string> directories = new List<string>();
      List<string> context = new List<string>();
      String server = "127.0.0.1";
      String port = "8080";
      int loopback = 8888; 
      double confidence = 0.75;
      double trigger = 0.92;
      bool kinect = false;
      bool gesture = false;
      bool picture = false;
      int websocket = -1;

      CultureInfo culture = CultureInfo.CreateSpecificCulture("en-US");

      var p = new OptionSet() {
        { "d|directory=", "the {DIRECTORY} of grammar. (default is /macros)", v => directories.Add (v) },
        { "c|confidence=", "the Grammar {CONFIDENCE}. (default is 0.75)", v => confidence = double.Parse(v, culture) },
        { "t|trigger=", "the Grammar {CONFIDENCE TRIGGER}. (default is 0.92)", v => trigger = double.Parse(v, culture) },
        { "s|server=", "the NodeJS {SERVER}. (default is 127.0.0.1)", v => server = v },
        { "p|port=", "the NodeJS {PORT}. (default is 8080)", v => port = v },
        { "l|loopback=", "the local {PORT}. (default is 8088)", v => loopback = int.Parse(v, culture) },
        { "k|kinect", "the {KINECT} mode. (default is false)", v => kinect = v != null },
        { "g|gesture", "the {KINECT} gesture mode. (default is false)", v => gesture = v != null },
        { "f|picture", "the {KINECT} picture mode. (default is false)", v => picture = v != null },
        { "h|help",  "show this message and exit", v => help = v != null },
        { "debug",  "display more debug data", v => DEBUG = v != null },
        { "ctx|context=",  "the starting context files", v => context.Add (v) },
        { "sck|websocket=",  "the websocket server port", v => websocket = int.Parse(v, culture) },
      };

      // Parsing arguments
      List<string> extra;
      try {
        extra = p.Parse(args);
      }
      catch (OptionException e) {
        Console.Write("WSRLaunch: ");
        Console.WriteLine(e.Message);
        Console.WriteLine("Try `WSRLaunch --help' for more information.");
        return;
      }

      // Show Help
      if (help) {
        ShowHelp(p);
        return;
      }

      // Set default values
      if (directories.Count == 0) {
        directories.Add("macros");
      }

      // Run WSRMacro
      WSRMacro macros = kinect ? new WSRKinectMacro(directories, confidence, trigger, server, port, loopback, context, gesture, picture, websocket)
                               : new WSRMacro(directories, confidence, trigger, server, port, loopback, context);

      // Start
      macros.StartRecognizer();

      // Keep the console window open.
      Console.ReadLine();
    }

    static void ShowHelp(OptionSet p) {
      Console.WriteLine("Usage: greet [OPTIONS]+ message");
      Console.WriteLine("Greet a list of individuals with an optional message.");
      Console.WriteLine("If no message is specified, a generic greeting is used.");
      Console.WriteLine();
      Console.WriteLine("Options:");
      p.WriteOptionDescriptions(Console.Out);
    }

    // ==========================================
    //  LOG
    // ==========================================

    public static bool DEBUG = false;
    public static void log(string context, string msg) {
      log(0, context, msg);
    }
    public static void log(int level, string context, string msg) {
      if (level < -1) { return; } // Traces
      if (level < 0 && !DEBUG) { return; }
      Console.WriteLine("[{0}] [{1}]\t {2}", DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss"), context, msg);
    }
  }
}