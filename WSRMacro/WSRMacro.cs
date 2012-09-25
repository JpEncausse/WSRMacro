using System;
using System.Speech;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;
using System.IO;
using System.Xml.XPath;
using System.Net;
using System.Threading;
using System.Text;
using System.Web;
using System.Globalization;
using System.Collections.Generic;

namespace encausse.net {
  /**
   * AUTOMATE
   * ********
   *                         * * * * * * * * * * * * *
   *                         *                       *
   * Main => SetGrammar => Start => Recognized => Complete
   *             *           *           *
   *             *           *           *
   *          Watcher   LoadGrammar    HTTP
   */

  class WSRMacro {

    // ==========================================
    //  WSRMacro CONSTRUCTOR
    // ==========================================

    protected double CONFIDENCE = 0.75;
    protected double CONFIDENCE_DICTATION = 0.30;
    protected String server = "127.0.0.1";
    protected String port = "8080";
    protected List<String> directories = null;

    public WSRMacro(List<String> dir, double confidence, String server, String port) {

      this.CONFIDENCE = confidence;
      this.server = server;
      this.port = port;
      this.directories = dir;

      log("INIT", "--------------------------------");
      log("INIT", "Windows Speech Recognition Macro");
      log("INIT", "================================");
      log("INIT", "Server: " + server + ":" + port);
      log("INIT", "Confidence: " + confidence);
      log("INIT", "--------------------------------");

      // Watch Directories
      foreach (string directory in this.directories) {
        AddDirectoryWatcher(directory);
      }

      // Start Engine
      StartRecognizer();
    }

    protected bool debug = false;
    public void SetDebug(bool debug) {
      this.debug = debug;
    }

    protected void log(string context, string msg) {
      log(0, context, msg);
    }
    protected void log(int level, string context, string msg) {
      if (level < 0 && !debug) { return; }
      Console.WriteLine("[{0}] [{1}]\t {2}", DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss"), context, msg);
    }

    // ==========================================
    //  WSRMacro GRAMMAR
    // ==========================================

    protected DictationGrammar dication = null;   // The dictation grammar
    protected String DIR_PATH = null;             // FIXME: Resolved absolute path
    protected bool reload = true;

    protected void LoadGrammar() {

      if (!reload) {
        return;
      }

      // Stop Watching
      StopDirectoryWatcher();

      // Unload All Grammar
      log("GRAMMAR", "Unload");
      SpeechRecognitionEngine sre = GetEngine();
      sre.UnloadAllGrammars();

      // Iterate throught directories
      foreach (string directory in this.directories) {
        DirectoryInfo dir = new DirectoryInfo(directory);
        if (DIR_PATH == null) {
          DIR_PATH = dir.FullName;
        }
        LoadGrammar(dir);
      }

      // Add a Dictation Grammar
      log("GRAMMAR", "Load dictation grammar");
      dication = new DictationGrammar("grammar:dictation");
      dication.Name = "dictation";
      dication.Enabled = false;
      sre.LoadGrammar(dication);

      // Start Watching
      StartDirectoryWatcher();
    }

    protected void LoadGrammar(DirectoryInfo dir) {
      log("GRAMMAR", "Load directory: " + dir.FullName);

      // Load Grammar
      foreach (FileInfo f in dir.GetFiles("*.xml")) {
        LoadGrammar(f.FullName, f.Name);
      }

      // Recursive directory
      foreach (DirectoryInfo d in dir.GetDirectories()) {
        LoadGrammar(d);
      }
    }

    public void LoadGrammar(String file, String name) {

      log("GRAMMAR", "Load file: " + name + " : " + file);

      // Create a Grammar from file
      Grammar grammar = new Grammar(file);
      grammar.Enabled = true;
      grammar.Name = name;

      // Load the grammar object into the recognizer.
      SpeechRecognitionEngine sre = GetEngine();
      sre.LoadGrammar(grammar);

      // FIXME: unload grammar with same name ?
    }

    // ==========================================
    //  WSRMacro FILESYSTEM WATCHER
    // ==========================================

    protected Dictionary<string, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>();

    public void AddDirectoryWatcher(String directory) {

      if (!Directory.Exists(directory)) {
        throw new Exception("Macro's directory do not exists: " + directory);
      }

      if (watchers.ContainsKey(directory)) {
        return;
      }
      log("WATCHER", "Watching directory: " + directory);

      // Build the watcher
      FileSystemWatcher watcher = new FileSystemWatcher();
      watcher.Path = directory;
      watcher.Filter = "*.xml";
      watcher.IncludeSubdirectories = true;
      watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
      watcher.Changed += new FileSystemEventHandler(watcher_Changed);

      // Add watcher to a Map
      watchers.Add(directory, watcher);
    }

    public void StartDirectoryWatcher() {
      log("WATCHER", "Start watching");
      foreach (FileSystemWatcher watcher in watchers.Values) {
        watcher.EnableRaisingEvents = true;
      }
    }

    public void StopDirectoryWatcher() {
      log("WATCHER", "Stop watching");
      foreach (FileSystemWatcher watcher in watchers.Values) {
        watcher.EnableRaisingEvents = false;
      }
    }

    // Event Handler
    protected void watcher_Changed(object sender, FileSystemEventArgs e) {
      reload = true;
      StopRecognizer();
    }

    // ==========================================
    //  WSRMacro ENGINE
    // ==========================================

    protected SpeechRecognitionEngine recognizer = null;

    public void StartRecognizer() {
      // Load grammar if needed
      LoadGrammar();

      // Start Recognizer
      log("ENGINE", "Start listening");
      GetEngine().RecognizeAsync(RecognizeMode.Multiple);
    }

    public void StopRecognizer() {
      log("ENGINE", "Stop listening");
      GetEngine().RecognizeAsyncStop();
    }

    public SpeechRecognitionEngine GetEngine() {
      if (recognizer != null) {
        return recognizer;
      }

      log("ENGINE", "Init recognizer");
      recognizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("fr-FR"));

      // Add a handler for the SpeechRecognized event.
      recognizer.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(recognizer_SpeechRecognized);

      // Add a handler for the SpeechRecognizedCompleted event.
      recognizer.RecognizeCompleted += new EventHandler<RecognizeCompletedEventArgs>(recognizer_RecognizeCompleted);

      // Add a handler for the AudioStateChangedEvent event.
      recognizer.AudioStateChanged += new EventHandler<AudioStateChangedEventArgs>(recognizer_AudioStateChanged);

      // Set recognizer properties
      // SetupProperties(recognizer);

      // Set the input to the recognizer.
      if (!SetupDevice(recognizer)) {
        recognizer.SetInputToDefaultAudioDevice();
      }

      return recognizer;
    }

    protected void recognizer_RecognizeCompleted(object sender, RecognizeCompletedEventArgs e) {
      String resultText = e.Result != null ? e.Result.Text : "<null>";
      log("ENGINE", "RecognizeCompleted (" + DateTime.Now.ToString("mm:ss.f") + "): " + resultText);
      // log("[Engine]  BabbleTimeout: {0}; InitialSilenceTimeout: {1}; Result text: {2}", e.BabbleTimeout, e.InitialSilenceTimeout, resultText);
      StartRecognizer();
    }

    protected void recognizer_AudioStateChanged(object sender, AudioStateChangedEventArgs e) {
      // log("[Engine] AudioStateChanged ({0}): {1}", DateTime.Now.ToString("mm:ss.f"), e.AudioState);
    }

    // ==========================================
    //  WSRMacro SENSOR & SETUP
    // ==========================================

    public void SetupProperties(SpeechRecognitionEngine sre) {
      sre.InitialSilenceTimeout = TimeSpan.FromSeconds(3);
      sre.BabbleTimeout = TimeSpan.FromSeconds(2);
      sre.EndSilenceTimeout = TimeSpan.FromSeconds(1);
      sre.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);

      log(-2, "ENGINE", "BabbleTimeout: " + sre.BabbleTimeout);
      log(-2, "ENGINE", "InitialSilenceTimeout: " + sre.InitialSilenceTimeout);
      log(-2, "ENGINE", "EndSilenceTimeout: " + sre.EndSilenceTimeout);
      log(-2, "ENGINE", "EndSilenceTimeoutAmbiguous: " + sre.EndSilenceTimeoutAmbiguous);

      // Set Max Alternate to 0
      recognizer.MaxAlternates = 2;
      log("ENGINE", "MaxAlternates: " + recognizer.MaxAlternates);
    }


    public virtual Boolean SetupDevice(SpeechRecognitionEngine sre) {
      log("ENGINE", "Using Default Sensors !");
      return false;
    }

    // ==========================================
    //  WSRMacro SPEECH RECOGNIZED
    // ==========================================
    
    protected String dictationUrl = null; // Last dication URL

    // Handle the SpeechRecognized event.
    protected void recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e) {
      RecognitionResult rr = e.Result;

      // 0. Prevent while speaking
      if (speaking) {
        log("ENGINE", "Speech rejected while speaking: " + rr.Confidence + " Text: " + rr.Text);
        return;
      }

      // 1. Handle dictation mode
      if (HandleDictation(rr)) {
        return;
      }

      // 2. Handle speech mode
      XPathNavigator xnav = HandleSpeech(rr);
      if (xnav == null) {
        return;
      }

      // 3. Parse Result's TTS
      String tts = GetTTS(xnav);
      Say(tts);

      // 4. Parse Result's URL
      String url = GetURL(xnav);

      // 5. Parse Result's Dication
      if (hasDictation(xnav)) {
        this.dictationUrl = url;
        return;
      }

      // 6. Otherwise send the request
      SendRequest(url);
    }

    protected bool HandleDictation(RecognitionResult rr) {
      if (!this.dication.Enabled) {
        return false;
      }

      if (rr.Confidence < CONFIDENCE_DICTATION) {
        log("ENGINE", "Dictation rejected: " + rr.Confidence + " Text: " + rr.Text);
        return true;
      }
      log("ENGINE", "Dictation recognized: " + rr.Confidence + " Text: " + rr.Text);

      // Stop dictation
      this.dication.Enabled = false;

      // Send previous request with dication
      String dication = System.Uri.EscapeDataString(rr.Text);
      SendRequest(this.dictationUrl + "&dictation=" + dication);

      this.dictationUrl = null;
      return true;
    }

    protected XPathNavigator HandleSpeech(RecognitionResult rr) {
      XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();
      double confidence = GetConfidence(xnav);

      if (rr.Confidence < confidence) {
        log("ENGINE", "Speech rejected: " + rr.Confidence + " Text: " + rr.Text);
        return null;
      }

      log("ENGINE", "Speech recognized: " + rr.Confidence + " Text: " + rr.Text);
      log(-1, "ENGINE", xnav.OuterXml);

      return xnav;
    }

    // ==========================================
    //  WSRMacro HTTP
    // ==========================================

    protected void SendRequest(String url) {
      if (url == null) { return; }

      log("HTTP", "Build HttpRequest: " + url);

      HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
      req.Method = "GET";

      log("HTTP", "Send HttpRequest: " + req.Address);

      try {
        HttpWebResponse res = (HttpWebResponse)req.GetResponse();
        log("HTTP", "Response status: " + res.StatusCode);

        // Handle Response
        using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) {
          Say(sr.ReadToEnd());
        }
      }
      catch (WebException ex) {
        log("HTTP", "Exception: " + ex.Message);
      }
    }

    // ==========================================
    //  WSRMacro XPATH
    // ==========================================

    protected double GetConfidence(XPathNavigator xnav) {
      XPathNavigator level = xnav.SelectSingleNode("/SML/action/@threashold");
      if (level != null) {
        log("HTTP", "Using confidence level: " + level.Value);
        return level.ValueAsDouble;
      }
      return CONFIDENCE;
    }

    protected String GetURL(XPathNavigator xnav) {
      XPathNavigator xurl = xnav.SelectSingleNode("/SML/action/@uri");
      if (xurl == null) { return null; }

      // Build URI
      String url = xurl.Value + "?";
      url = url.Replace("http://127.0.0.1:8080", "http://" + server + ":" + port);

      // Build QueryString
      url += GetQueryString(xnav.Select("/SML/action/*"));

      // Append Directory Path
      url += "directory=" + DIR_PATH;

      return url;
    }

    protected String GetQueryString(XPathNodeIterator it) {
      String qs = "";
      while (it.MoveNext()) {
        String children = "";
        if (it.Current.Name == "confidence")
          continue;
        if (it.Current.Name == "uri")
          continue;
        if (it.Current.HasChildren) {
          children = GetQueryString(it.Current.SelectChildren(String.Empty, it.Current.NamespaceURI));
        }
        qs += (children == "") ? (it.Current.Name + "=" + it.Current.Value + "&") : (children);
      }
      return qs;
    }

    protected String GetTTS(XPathNavigator xnav) {
      XPathNavigator tts = xnav.SelectSingleNode("/SML/action/@tts");
      if (tts != null) { return tts.Value; }
      return null;
    }

    protected Boolean hasDictation(XPathNavigator xnav) {
      XPathNavigator dictation = xnav.SelectSingleNode("/SML/action/@dictation");
      if (dictation == null) { return false; }

      dication.Enabled = true;
      return true;
    }

    // ==========================================
    //  WSRMacro SPEECH
    // ==========================================
    protected Boolean speaking = false;
    public void Say(String tts) {
      if (tts == null) { return; }
      speaking = true;
      log("[TTS] Say: {0}", tts);
      using (SpeechSynthesizer synthesizer = new SpeechSynthesizer()) {

        // Configure the audio output.
        synthesizer.SetOutputToDefaultAudioDevice();

        // Build and speak a prompt.
        PromptBuilder builder = new PromptBuilder();
        builder.AppendText(tts);
        synthesizer.Speak(builder);
      }
      speaking = false;
    }
  }
}