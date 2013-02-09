using System;
using System.IO;
using System.Text;
using System.Xml.XPath;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using CloudSpeech;
using System.Web;

/*
using System.Speech;
using System.Speech.Recognition;
using System.Speech.AudioFormat;
*/

using Microsoft.Speech;
using Microsoft.Speech.Recognition;
using Microsoft.Speech.AudioFormat; 



namespace net.encausse.sarah {
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

  public partial class WSRMacro : IDisposable {

    // Singleton
    protected static WSRMacro manager;
    public static WSRMacro GetInstance() {
      if (manager == null) {
        manager = new WSRMacro();
        manager.Init();
      }
      return manager;
    }

    // ==========================================
    //  WSRMacro CONSTRUCTOR
    // ==========================================

    public virtual void Init() {
      logInfo("INIT", "--------------------------------");
      logInfo("INIT", "Windows Speech Recognition Macro");
      logInfo("INIT", "================================");
      logInfo("INIT", "Server: " + WSRConfig.GetInstance().GetRemoteURL());
      logInfo("INIT", "Confidence: " + WSRConfig.GetInstance().confidence);
      logInfo("INIT", "--------------------------------");

      // Watch Directories
      foreach (string directory in WSRConfig.GetInstance().directories) {
        AddDirectoryWatcher(directory);
      }

      // Start HttpServer
      WSRHttpManager.GetInstance().StartHttpServer();
    }

    // Perform cleanup on application exit
    public virtual void Dispose() {
      StopRecognizer();
    }

    protected void logInfo(String context, String msg) {
      WSRConfig.GetInstance().logInfo(context, msg);
    }

    // ==========================================
    //  WSRMacro SYSTEM TRAY
    // ==========================================

    public virtual void HandleCtxMenu(ContextMenuStrip menu) {
      // Sentinel
      var item = new ToolStripMenuItem();
      item.Text = "Sentinel";
      item.Click += new EventHandler(Sentinel_Click);
      item.Image = net.encausse.sarah.Properties.Resources.Logs;
      menu.Items.Add(item);
    }

    void Sentinel_Click(object sender, EventArgs e) {
      Process.Start(@"Sentinel\Sentinel.exe", "nlog udp 9999");
    }

    // ==========================================
    //  WSRMacro GRAMMAR
    // ==========================================

    protected String DIR_PATH = null;    // FIXME: Resolved absolute path
    protected bool reload = true;

    protected void LoadGrammar() {

      if (!reload) {
        return;
      }

      // Stop Watching
      StopDirectoryWatcher();

      // Unload All Grammar
      logInfo("GRAMMAR", "Unload");
      SpeechRecognitionEngine sre = GetEngine();
      sre.UnloadAllGrammars();

      // Iterate throught directories
      foreach (string directory in WSRConfig.GetInstance().directories) {
        DirectoryInfo dir = new DirectoryInfo(directory);
        if (DIR_PATH == null) {
          DIR_PATH = dir.FullName;
        }
        LoadGrammar(dir);
      }

      // Add a Dictation Grammar (too bad)
      // SetDictationGrammar(sre);

      // Start Watching
      StartDirectoryWatcher();

      // Set Context
      SetContext("default");
    }

    protected void LoadGrammar(DirectoryInfo dir) {
      logInfo("GRAMMAR", "Load directory: " + dir.FullName);

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

      logInfo("GRAMMAR", "Load file: " + name + " : " + file);

      // Create a Grammar from file
      Grammar grammar = new Grammar(file);
      grammar.Enabled = file.IndexOf("lazy") < 0;
      if (grammar.RuleName != null) {
        grammar.Enabled = !grammar.RuleName.StartsWith("lazy");
      }
      grammar.Name = name;

      // Load the grammar object into the recognizer.
      SpeechRecognitionEngine sre = GetEngine();
      sre.LoadGrammar(grammar);

      // Add to context if there is no context
      if (!WSRConfig.GetInstance().HasContext() && grammar.Enabled) {
        WSRConfig.GetInstance().context.Add(name);
        logInfo("GRAMMAR", "Add to context list: " + name);
      }

      // FIXME: unload grammar with same name ?
    }

    // ==========================================
    //  WSRMacro GRAMMAR CONTEXT
    // ==========================================

    public void SetContext(List<string> context) {
      if (recognizer == null) { return; }
      if (context.Count < 1) { return; }
      if (context.Count == 1) { SetContext(context[0]); return; }
      logInfo("GRAMMAR", "Context: " + String.Join(", ", context.ToArray()));
      foreach (Grammar g in recognizer.Grammars) {
        if (g.Name == "dictation") { continue; }
        g.Enabled = context.Contains(g.Name);
        logInfo("CONTEXT", g.Name + " = " + g.Enabled);
      }
    }

    public void SetContext(String context) {
      if (recognizer == null || context == null) { return; }
      logInfo("GRAMMAR", "Context: " + context);
      if ("default".Equals(context)) { SetContext(WSRConfig.GetInstance().context); return; }
      bool all = "all".Equals(context);
      foreach (Grammar g in recognizer.Grammars) {
        if (g.Name == "dictation") { continue; }
        g.Enabled = all || context.Equals(g.Name);
        logInfo("CONTEXT", g.Name + " = " + g.Enabled);
      }
    }

    // ==========================================
    //  WSRMacro FILESYSTEM WATCHER
    // ==========================================

    protected Dictionary<string, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>();

    public void AddDirectoryWatcher(String directory) {

      if (!Directory.Exists(directory)) {
        throw new Exception("Macro's directory do not exists: " + directory);
      }

      if (watchers.ContainsKey(directory)) { return; }
      logInfo("WATCHER", "Watching directory: " + directory);

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
      logInfo("WATCHER", "Start watching");
      foreach (FileSystemWatcher watcher in watchers.Values) {
        watcher.EnableRaisingEvents = true;
      }
    }

    public void StopDirectoryWatcher() {
      logInfo("WATCHER", "Stop watching");
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
    public virtual void StartRecognizer() {
      // Load grammar if needed
      LoadGrammar();

      // Start Recognizer
      logInfo("ENGINE", "Start listening");
      try {
        GetEngine().RecognizeAsync(RecognizeMode.Multiple);
      }
      catch (Exception) { logInfo("ENGINE", "No device found"); }
    }

    public virtual void StopRecognizer() {
      logInfo("ENGINE", "Stop listening");
      GetEngine().RecognizeAsyncStop();
      logInfo("ENGINE", "Stop listening...done");
    }

    public SpeechRecognitionEngine GetEngine() {
      if (recognizer != null) {
        return recognizer;
      }

      logInfo("ENGINE", "Init recognizer");
      recognizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo(WSRConfig.GetInstance().language));
      recognizer.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(recognizer_SpeechRecognized);
      recognizer.RecognizeCompleted += new EventHandler<RecognizeCompletedEventArgs>(recognizer_RecognizeCompleted);
      recognizer.AudioStateChanged += new EventHandler<AudioStateChangedEventArgs>(recognizer_AudioStateChanged);
      recognizer.SpeechHypothesized += new EventHandler<SpeechHypothesizedEventArgs>(recognizer_SpeechHypothesized);
      
      // Alternate
      recognizer.MaxAlternates = 2;
      logInfo("ENGINE", "MaxAlternates: " + recognizer.MaxAlternates);

      // Deep configuration
      if (!WSRConfig.GetInstance().adaptation) {
        recognizer.UpdateRecognizerSetting("AdaptationOn", 0);
      }

      // Set the input to the recognizer.
      if (!SetupDevice(recognizer)) {
        try {
          recognizer.SetInputToDefaultAudioDevice();
        }
        catch (InvalidOperationException ex) {
          logInfo("ENGINE", "No default input device: " + ex.Message);
        }
      }

      return recognizer;
    }

    protected void recognizer_SpeechHypothesized(object sender, SpeechHypothesizedEventArgs e) {}
    
    protected void recognizer_RecognizeCompleted(object sender, RecognizeCompletedEventArgs e) {
      String resultText = e.Result != null ? e.Result.Text : "<null>";
      logInfo("ENGINE", "RecognizeCompleted (" + DateTime.Now.ToString("mm:ss.f") + "): " + resultText);
      // logInfo("[Engine]  BabbleTimeout: {0}; InitialSilenceTimeout: {1}; Result text: {2}", e.BabbleTimeout, e.InitialSilenceTimeout, resultText);
      StartRecognizer();
    }

    protected void recognizer_AudioStateChanged(object sender, AudioStateChangedEventArgs e) {
      // logInfo("[Engine] AudioStateChanged ({0}): {1}", DateTime.Now.ToString("mm:ss.f"), e.AudioState);
    }

    // ==========================================
    //  WSRMacro SENSOR & SETUP
    // ==========================================
    /*
    public void SetupProperties(SpeechRecognitionEngine sre) {
      //sre.InitialSilenceTimeout = TimeSpan.FromSeconds(3);
      //sre.BabbleTimeout = TimeSpan.FromSeconds(2);
      //sre.EndSilenceTimeout = TimeSpan.FromSeconds(1);
      //sre.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);

      WSRConfig.GetInstance().logDebug("ENGINE", "BabbleTimeout: " + sre.BabbleTimeout);
      WSRConfig.GetInstance().logDebug("ENGINE", "InitialSilenceTimeout: " + sre.InitialSilenceTimeout);
      WSRConfig.GetInstance().logDebug("ENGINE", "EndSilenceTimeout: " + sre.EndSilenceTimeout);
      WSRConfig.GetInstance().logDebug("ENGINE", "EndSilenceTimeoutAmbiguous: " + sre.EndSilenceTimeoutAmbiguous);

      // Set Max Alternate to 0
      recognizer.MaxAlternates = 2;
      logInfo("ENGINE", "MaxAlternates: " + recognizer.MaxAlternates);
    }
    */

    public virtual Boolean SetupDevice(SpeechRecognitionEngine sre) {
      logInfo("ENGINE", "Using Default Sensors !");
      return false;
    }

    // ==========================================
    //  WSRMacro SPEECH RECOGNIZED
    // ==========================================
    
    protected String dictationUrl = null; // Last dication URL

    protected void recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e) {
      RecognitionResult rr = e.Result;

      // 0. Prevent while speaking
      if (WSRSpeaker.GetInstance().IsSpeaking()) {
        logInfo("ENGINE", "REJECTED Speech while speaking: " + rr.Confidence + " Text: " + rr.Text);
        return;
      }

      // 1. Handle dictation mode
      /*
      if (this.dictation != null && this.dictation.Enabled && HandleDictation(rr, WSRConfig.GetInstance().dictation)) {
        this.dictation.Enabled = false;
        return;
      }*/

      // 2. Handle speech mode
      XPathNavigator xnav = HandleSpeech(rr);
      if (xnav == null) {
        return;
      }

      // 3 Hook
      String path = HandleCustomAttributes(xnav);

      // 4. Parse Result's URL
      String url = GetURL(xnav);

      // 5. Parse Result's Dication
      url = HandleWildcard(rr, url);
      if (url == null) {
        return;
      }

      // 6. Otherwise send the request
      WSRHttpManager.GetInstance().SendRequest(url, path); 
    }

    protected bool HandleDictation(RecognitionResult rr, double confidence) {

      if (rr.Confidence < confidence) {
        logInfo("ENGINE", "REJECTED Dictation: " + rr.Confidence + " Text: " + rr.Text);
        return true;
      }
      logInfo("ENGINE", "RECOGNIZED Dictation: " + rr.Confidence + " Text: " + rr.Text);

      // Send previous request with dictation
      String dictation = System.Uri.EscapeDataString(rr.Text);
      WSRHttpManager.GetInstance().SendRequest(this.dictationUrl + "&dictation=" + dictation);

      this.dictationUrl = null;
      return true; 
    }

    protected XPathNavigator HandleSpeech(RecognitionResult rr) {
      XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();

      double confidence = GetConfidence(xnav);
      if (rr.Confidence < confidence) {
        logInfo("ENGINE", "REJECTED Speech: " + rr.Confidence + " < " + confidence + " Device: " + GetDeviceConfidence() + " Text: " + rr.Text);
        return null;
      }

      if (rr.Words[0].Confidence < WSRConfig.GetInstance().trigger) {
        logInfo("ENGINE", "REJECTED Trigger: " + rr.Words[0].Confidence + " Text: " + rr.Words[0].Text);
        return null;
      }

      logInfo("ENGINE", "RECOGNIZED Speech: " + rr.Confidence + "/" + rr.Words[0].Confidence + " Device: " + GetDeviceConfidence() + " Text: " + rr.Text);
      WSRConfig.GetInstance().logDebug("ENGINE", xnav.OuterXml);
      if (WSRConfig.GetInstance().DEBUG) { DumpAudio(rr); }

      return xnav;
    }

    protected virtual String GetDeviceConfidence() {
      return "";
    }

    protected void HandleContext(XPathNavigator xnav) {
      XPathNavigator ctxt = xnav.SelectSingleNode("/SML/action/@context");
      if (ctxt != null) {
        SetContext(new List<string>(ctxt.Value.Split(',')));
      }
    }

    // ==========================================
    //  WSRMacro DICTATION
    //  Only for Windows.Speech not Microsoft.Speech
    // ==========================================

    protected String HandleWildcard(RecognitionResult rr, String url) {

      XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();
      XPathNavigator wildcard = xnav.SelectSingleNode("/SML/action/@dictation");
      if (wildcard == null) { return url; }

      // Store URL
      this.dictationUrl = url;

      // Google
      using (MemoryStream audioStream = new MemoryStream()) {
        rr.Audio.WriteToWaveStream(audioStream);
     // rr.GetAudioForWordRange(rr.Words[word], rr.Words[word]).WriteToWaveStream(audioStream);
        audioStream.Position = 0;
        var speech2text = ProcessAudioStream(audioStream);
        if (url != null) {
          url += "&dictation=" + HttpUtility.UrlEncode(speech2text);
        }
      }

      return url;
    }

    /*
    protected Grammar dictation = null;  // The dictation grammar

    // Not for Microsoft.Speech
    protected void SetDictationGrammar (SpeechRecognitionEngine sre) {
      
      logInfo("GRAMMAR", "Load dictation grammar");
      dictation = new DictationGrammar("grammar:dictation");
      dictation.Name = "dictation";
      dictation.Enabled = false;
      sre.LoadGrammar(dictation);
      
    }
    
    protected String HandleWildcard(RecognitionResult rr, String url) {
      
      XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();
      XPathNavigator wildcard = xnav.SelectSingleNode("/SML/action/@dictation");
      if (wildcard == null) { return url; }

      // Store URL
      this.dictationUrl = url;

      // Dictation in 2 steps
      if (wildcard.Value == "true") {
        dictation.Enabled = true;
        return null;
      }

      // Wildcards
      int word = int.Parse(wildcard.Value);

      // Google
      using (MemoryStream audioStream = new MemoryStream()) {
        // rr.Audio.WriteToWaveStream(audioStream);
        rr.GetAudioForWordRange(rr.Words[word], rr.Words[word]).WriteToWaveStream(audioStream);
        audioStream.Position = 0;
        url += "&dictation="+ProcessAudioStream(audioStream);
      }
     
      return url;
    }

    
    protected SpeechRecognitionEngine dictanizer = null;
    public SpeechRecognitionEngine GetDictationEngine() {
      if (dictanizer != null) {
        return dictanizer;
      }

      logInfo("ENGINE", "Init recognizer");
      dictanizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo(language));

      // Set recognizer properties
      SetupProperties(dictanizer);

      // Load a Dictation grammar
      DictationGrammar d = new DictationGrammar("grammar:dictation");
      d.Name = "dictation";
      dictanizer.LoadGrammar(d);

      return dictanizer;
    }
    */


    // ==========================================
    //  WSRMacro XPATH
    // ==========================================

    protected double GetConfidence(XPathNavigator xnav) {
      XPathNavigator level = xnav.SelectSingleNode("/SML/action/@threashold");
      if (level != null) {
        logInfo("HTTP", "Using confidence level: " + level.Value);
        return level.ValueAsDouble;
      }
      return WSRConfig.GetInstance().confidence;
    }

    protected String GetURL(XPathNavigator xnav) {
      XPathNavigator xurl = xnav.SelectSingleNode("/SML/action/@uri");
      if (xurl == null) { return null; }

      // Build URI
      String url = xurl.Value + "?";

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

    protected void HandleTTS(XPathNavigator xnav) {
      XPathNavigator tts = xnav.SelectSingleNode("/SML/action/@tts");
      if (tts != null) {
        WSRSpeaker.GetInstance().Speak(tts.Value);
      }
    }

    protected void HandlePlay(XPathNavigator xnav) {
      XPathNavigator play = xnav.SelectSingleNode("/SML/action/@play");
      if (play != null) {
        WSRSpeaker.GetInstance().PlayMP3(play.Value); 
      }
    }

    protected virtual String HandleCustomAttributes(XPathNavigator xnav) {
      // 3.1 Parse Result's TTS
      HandleTTS(xnav);

      // 3.2 Parse Result's Play
      HandlePlay(xnav);

      // 3.3 Handle Result's Context
      HandleContext(xnav);

      return null;
    }

    // ==========================================
    //  WSRMacro GOOGLE
    // // https://bitbucket.org/josephcooney/cloudspeech/src/8619cf699541?at=default
    // ==========================================

    public String ProcessAudioStream(Stream stream) {
      logInfo("GOOGLE", "ProcessAudioStream");
      CultureInfo culture = new System.Globalization.CultureInfo(WSRConfig.GetInstance().language);
      var stt = new SpeechToText("https://www.google.com/speech-api/v1/recognize?xjerr=1&client=chromium&maxresults=2", culture);
      var response = stt.Recognize(stream);
      foreach(TextResponse res in response){
        logInfo("GOOGLE", "Confidence: " + res.Confidence + " Utterance " + res.Utterance);
        return res.Utterance;
      }
      return null;
    }

    // ==========================================
    //  WSRMacro DUMP AUDIO
    // ==========================================
    // http://msdn.microsoft.com/en-us/library/system.speech.recognition.recognitionresult.audio.aspx
    
    public bool DumpAudio (RecognitionResult rr) {
      return DumpAudio(rr,null);
    }

    public bool DumpAudio(RecognitionResult rr, String path) {
      
      // Build Path
      if (path == null){
        path  = "dump/dump_";
        path += DateTime.Now.ToString("yyyy.M.d_hh.mm.ss");
        path += ".wav";
      }

      // Clean Path
      if (File.Exists(path)){ File.Delete(path); }

      // Dump to File
      using (FileStream fileStream = new FileStream(path, FileMode.CreateNew)) {
        rr.Audio.WriteToWaveStream(fileStream);
      }

      // Clean XML data
      path += ".xml";
      if (File.Exists(path)){ File.Delete(path); }

      // Build XML
      XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();

      String xml = "";
      xml += "<match=\"" + rr.Confidence + "\" text\"" + rr.Text+"\">\r\n";
      xml += "<trigger=\"" + rr.Words[0].Confidence + "\" text\"" + rr.Words[0].Text + "\">\r\n";
      xml += xnav.OuterXml;

      // Dump to XML
      System.IO.File.WriteAllText(path, xml);

      return true;
    }

    // ==========================================
    //  WSRMacro HTTPSERVER
    // ==========================================

    public virtual bool HandleCustomRequest(NHttp.HttpRequestEventArgs e) {

      // Stop Music
      String pause = e.Request.Params.Get("pause");
      if (pause != null) {
        WSRSpeaker.GetInstance().StopMP3(pause);
      }

      // Play Music
      String mp3 = e.Request.Params.Get("play");
      if (mp3 != null) {
        WSRSpeaker.GetInstance().PlayMP3(mp3);
      }

      // Text To Speech
      String tts = e.Request.Params.Get("tts");
      if (tts != null) {
        tts = e.Server.HtmlDecode(tts);
        WSRSpeaker.GetInstance().Speak(tts);
      }

      // Set Context
      String ctxt = e.Request.Params.Get("context");
      if (ctxt != null) {
        SetContext(new List<string>(ctxt.Split(',')));
      }

      // Process
      String activate = e.Request.Params.Get("activate");
      if (activate != null) {
        WSRKeyboard.GetInstance().ActivateApp(activate);
      }

      String run   = e.Request.Params.Get("run");
      String param = e.Request.Params.Get("runp");
      if (run != null) {
        WSRKeyboard.GetInstance().RunApp(run, param);
      }

      // Keyboard
      String keyMod = e.Request.Params.Get("keyMod");
      String key    = e.Request.Params.Get("keyPress");
      if (key != null) {
        WSRKeyboard.GetInstance().SimulateKey(key, 0, keyMod);
      }

      key = e.Request.Params.Get("keyDown");
      if (key != null) {
        WSRKeyboard.GetInstance().SimulateKey(key, 1, keyMod);
      }

      key = e.Request.Params.Get("keyUp");
      if (key != null) {
        WSRKeyboard.GetInstance().SimulateKey(key, 2, keyMod);
      }

      String keyText  = e.Request.Params.Get("keyText");
      if (keyText != null) {
        WSRKeyboard.GetInstance().SimulateTextEntry(keyText);
      }

      return false;
    }
  }
}