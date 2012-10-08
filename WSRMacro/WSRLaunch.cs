using System;
using System.Globalization;
using System.Collections.Generic;
using NDesk.Options;

namespace encausse.net {

  class WSRLaunch {
    
    static void Main(string[] args) {

      bool help = false;
      List<string> directories = new List<string>();
      String server = "127.0.0.1";
      String port = "8080";
      double confidence = 0.75;
      bool kinect = false;
      

      CultureInfo culture = CultureInfo.CreateSpecificCulture("en-US");

      var p = new OptionSet() {
        { "d|directory=", "the {DIRECTORY} of grammar. (default is /macros)", v => directories.Add (v) },
        { "c|confidence=", "the Grammar {CONFIDENCE}. (default is 0.75)", v => confidence = double.Parse(v, culture) },
        { "s|server=", "the NodeJS {SERVER}. (default is 127.0.0.1)", v => server = v },
        { "p|port=", "the NodeJS {PORT}. (default is 8080)", v => port = v },
        { "k|kinect", "the {KINECT} mode. (default is true)", v => kinect = v != null },
        { "h|help",  "show this message and exit", v => help = v != null },
        { "debug",  "display more debug data", v => debug = v != null },
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
      WSRMacro macros = kinect ? new WSRKinectMacro(directories, confidence, server, port)
                               : new WSRMacro(directories, confidence, server, port);

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

    static bool debug = false;
    public static void log(string context, string msg) {
      log(0, context, msg);
    }
    public static void log(int level, string context, string msg) {
      if (level < -1) { return; } // Traces
      if (level < 0  && !debug) { return; }
      Console.WriteLine("[{0}] [{1}]\t {2}", DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss"), context, msg);
    }
  }
}