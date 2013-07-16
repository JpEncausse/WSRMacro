using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Collections.Generic;
using System.Xml.XPath;
using CloudSpeech;

#if MICRO
using System.Speech;
using System.Speech.Recognition;
using System.Speech.AudioFormat;
#endif

#if KINECT
using Microsoft.Speech;
using Microsoft.Speech.Recognition;
using Microsoft.Speech.AudioFormat;
#endif

namespace net.encausse.sarah {

  public class WSRSpeech {

    public bool Listening { get; set; } 
    

    // ==========================================
    //  SINGLETON
    // ==========================================

    private static WSRSpeech manager;
    public static WSRSpeech GetInstance() {
      if (manager == null) {
        manager = new WSRSpeech();
      }
      return manager;
    }

    public void Init() {

      Listening = true;

      // Load grammar files
      LoadGrammar();

      // Set default context
      SetContext("default");

      // Init Engines
      try { InitEngines(); }
      catch (Exception ex) {
        WSRConfig.GetInstance().logError("ENGINE", "InitEngines: " + ex.Message);
      }

      // Start audio watcher
      InitAudioWatcher();
    }

    public void Dispose() {
      if (defaultEngine != null) {
        defaultEngine.Stop();
      }
      
      if (rtpEngine != null) {
        rtpEngine.Stop();
      }

      if (fileEngine != null) {
        fileEngine.Stop();
      }
    }
    
    // ==========================================
    //  ENGINE
    // ==========================================

    WSRSpeechEngine defaultEngine = null;
    WSRSpeechEngine fileEngine = null;
    WSRSpeechEngine rtpEngine = null;

    public void InitEngines() {

      WSRConfig cfg = WSRConfig.GetInstance();

      // Default
      defaultEngine = new WSRSpeechEngine("Default", cfg.language, cfg.confidence);
      defaultEngine.LoadGrammar();
      cfg.GetWSRMicro().SetupAudioEngine(defaultEngine);
      defaultEngine.Init();
      defaultEngine.Start();
      
      // File
      fileEngine = new WSRSpeechEngine("File", cfg.language, cfg.confidence);
      fileEngine.LoadGrammar();
      fileEngine.Init();

      // Network
      if (WSRConfig.GetInstance().rtpport > 0) {
        rtpEngine = new WSRSpeechEngine("RTP", cfg.language, cfg.confidence);
        rtpEngine.LoadGrammar();
        rtpEngine.Init();
        cfg.GetWSRMicro().SetupRTPEngine(rtpEngine);
        rtpEngine.Start();
      }
    }

    public String GetDeviceInfo(WSRSpeechEngine engine) {
      if (engine == defaultEngine) {
        return WSRConfig.GetInstance().GetWSRMicro().GetDeviceInfo();
      }
      return ""; 
    }

    public void Recognize(String fileName) {
      fileEngine.GetEngine().SetInputToWaveFile(fileName);
      fileEngine.GetEngine().Recognize();
    }



    // ==========================================
    //  GRAMMAR
    // ==========================================

    private String DIR_PATH;
    public String GetGrammarPath() {
      return DIR_PATH;
    }

    private Dictionary<string, WSRSpeecGrammar> cache = new Dictionary<string, WSRSpeecGrammar>();

    public void LoadGrammar() {
      cache.Clear();
      StopDirectoryWatcher();

      foreach (string directory in WSRConfig.GetInstance().directories) {
        DirectoryInfo dir = new DirectoryInfo(directory);
        
        // Load all grammars
        LoadGrammar(dir);
        
        // Watch directory for changes
        AddDirectoryWatcher(directory);
      }

      // FIXME: Add "dictation" grammar
      StartDirectoryWatcher();
    }

    protected void LoadGrammar(DirectoryInfo dir) {
      // WSRConfig.GetInstance().logDebug("GRAMMAR", "Load directory: " + dir.FullName);
      DIR_PATH = DIR_PATH != null ? DIR_PATH : dir.FullName;

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
      WSRConfig cfg = WSRConfig.GetInstance();
      // Load the XML
      String xml = File.ReadAllText(file, Encoding.UTF8);
      xml = Regex.Replace(xml, "([^/])SARAH", "$1" + cfg.name, RegexOptions.IgnoreCase);

      // Check regexp language
      if (!Regex.IsMatch(xml, "xml:lang=\"" + cfg.language + "\"", RegexOptions.IgnoreCase)) {
        logInfo("GRAMMAR", "Ignoring: " + name + " (" + cfg.language + ")");
        return;
      }

      // Build grammar
      WSRSpeecGrammar grammar = new WSRSpeecGrammar();
      grammar.XML = xml;
      grammar.Name = name;
      grammar.Path = file;

      // Check contexte
      grammar.Enabled = true;
      
      if ((file.IndexOf("lazy") >= 0) || Regex.IsMatch(xml, "root=\"lazy\\w+\"", RegexOptions.IgnoreCase)) {
        grammar.Enabled = false;
      }

      // Add to context if there is no context
      if (!WSRConfig.GetInstance().HasContext() && grammar.Enabled) {
        WSRConfig.GetInstance().context.Add(name);
        logInfo("GRAMMAR", "Add to context list: " + name);
      }

      // Cache XML
      cache.Add(name, grammar);
    }

    public void LoadGrammar(WSRSpeechEngine engine) {
      SpeechRecognitionEngine sre = engine.GetEngine();
      sre.UnloadAllGrammars();
      foreach (WSRSpeecGrammar g in cache.Values) {
        g.LoadGrammar(engine);
      }
    }

    // ==========================================
    //  GRAMMAR WATCHER
    // ==========================================

    protected Dictionary<string, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>();
    public void AddDirectoryWatcher(String directory) {

      if (!Directory.Exists(directory)) {
        throw new Exception("Directory do not exists: " + directory);
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

    protected void watcher_Changed(object sender, FileSystemEventArgs e) {
      
      // Reload all grammar
      LoadGrammar();

      // Set context back
      SetContext(tmpContext);

      if (defaultEngine != null) {
        defaultEngine.LoadGrammar();
      }

      if (fileEngine != null) {
        fileEngine.LoadGrammar();
      }

      if (rtpEngine != null) {
        rtpEngine.LoadGrammar();
      }

      // Reset context timeout
      ResetContextTimeout();
    }

    // ==========================================
    //  CONTEXT
    // ==========================================

    private List<string> tmpContext = null;
    public void SetContext(List<string> context) {
      if (context == null) { SetContext("default"); return; }
      if (context.Count < 1) { return; }
      tmpContext = context;
      if (context.Count == 1) { SetContext(context[0]); return; }
      logInfo("GRAMMAR", "Context: " + String.Join(", ", context.ToArray()));

      foreach (WSRSpeecGrammar g in cache.Values) {
        if (g.Name == "dictation") { continue; }
        g.Enabled = context.Contains(g.Name);
        logInfo("CONTEXT", g.Name + " = " + g.Enabled);
      }
    }

    public void SetContext(String context) {
      if (context == null) { return; }
      logInfo("GRAMMAR", "Context: " + context);
      if ("default".Equals(context)) {
        SetContext(WSRConfig.GetInstance().context);
        tmpContext = null;
        return;
      }
      bool all = "all".Equals(context);
      foreach (WSRSpeecGrammar g in cache.Values) {
        if (g.Name == "dictation") { continue; }
        g.Enabled = all || context.Equals(g.Name);
        logInfo("CONTEXT", g.Name + " = " + g.Enabled);
      }
    }

    System.Timers.Timer ctxTimer = null;
    public void SetContextTimeout() {
      if (ctxTimer != null) { return; }
      logInfo("CONTEXT", "Start context timeout: " + WSRConfig.GetInstance().ctxTimeout);
      ctxTimer = new System.Timers.Timer();
      ctxTimer.Interval = WSRConfig.GetInstance().ctxTimeout;
      ctxTimer.Elapsed += new System.Timers.ElapsedEventHandler(timer_Elapsed);
      ctxTimer.Enabled = true;
      ctxTimer.Start();
    }

    protected void timer_Elapsed(object sender, EventArgs e) {
      logInfo("CONTEXT", "End context timeout");
      ctxTimer.Stop();
      ctxTimer = null;

      SetContext("default");
      ForwardContext();
    }

    public void ResetContextTimeout() {
      logInfo("CONTEXT", "Reset timeout");
      if (ctxTimer == null) { return; }
      ctxTimer.Stop();
      ctxTimer.Start();
    }

    public void ForwardContext() {
      logInfo("CONTEXT", "Forward Context enable/disable grammar");
      foreach (Grammar g in defaultEngine.GetEngine().Grammars) {
        WSRSpeecGrammar s = null;
        if (cache.TryGetValue(g.Name, out s)){
          g.Enabled = s.Enabled;
          logInfo("CONTEXT", g.Name + " = " + s.Enabled);
        }
      }
    }

    // ==========================================
    //  AUDIO WATCHER
    // ==========================================

    FileSystemWatcher audioWatcher = null;
    protected void InitAudioWatcher() {

      if (audioWatcher != null) { return; }
      String directory = WSRConfig.GetInstance().audioWatcher;
      if (!Directory.Exists(directory)) { return; }


      logInfo("ENGINE", "Init Audio Watcher: " + directory);
      audioWatcher = new FileSystemWatcher();
      audioWatcher.Path = directory;
      audioWatcher.Filter = "*.wav";
      audioWatcher.IncludeSubdirectories = true;
      audioWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
      audioWatcher.Changed += new FileSystemEventHandler(audio_Changed);
      audioWatcher.EnableRaisingEvents = true;
    }

    protected void audio_Changed(object sender, FileSystemEventArgs e) {
      fileEngine.GetEngine().SetInputToWaveFile(e.FullPath);
      fileEngine.GetEngine().Recognize();
    }

    // ==========================================
    //  GOOGLE
    //  https://bitbucket.org/josephcooney/cloudspeech/src/8619cf699541?at=default
    // ==========================================

    public String ProcessAudioStream(Stream stream, String language) {
      logInfo("GOOGLE", "ProcessAudioStream: " + language);
      CultureInfo culture = new System.Globalization.CultureInfo(language);
      var stt = new SpeechToText("https://www.google.com/speech-api/v1/recognize?xjerr=1&client=chromium&maxresults=2", culture);
      var response = stt.Recognize(stream);
      foreach (TextResponse res in response) {
        logInfo("GOOGLE", "Confidence: " + res.Confidence + " Utterance " + res.Utterance);
        return res.Utterance;
      }
      return null;
    }

    // ==========================================
    //  WSRMacro DUMP AUDIO
    // ==========================================
    // http://msdn.microsoft.com/en-us/library/system.speech.recognition.recognitionresult.audio.aspx

    public bool DumpAudio(RecognitionResult rr) {
      return DumpAudio(rr, null);
    }

    public bool DumpAudio(RecognitionResult rr, String path) {

      // Build Path
      if (path == null) {
        path = "dump/dump_";
        path += DateTime.Now.ToString("yyyy.M.d_hh.mm.ss");
        path += ".wav";
      }

      // Clean Path
      if (File.Exists(path)) { File.Delete(path); }

      // Dump to File
      using (FileStream fileStream = new FileStream(path, FileMode.CreateNew)) {
        rr.Audio.WriteToWaveStream(fileStream);
      }

      // Clean XML data
      path += ".xml";
      if (File.Exists(path)) { File.Delete(path); }

      // Build XML
      XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();

      String xml = "";
      xml += "<match=\"" + rr.Confidence + "\" text\"" + rr.Text + "\">\r\n";
      xml += "<trigger=\"" + rr.Words[0].Confidence + "\" text\"" + rr.Words[0].Text + "\">\r\n";
      xml += xnav.OuterXml;

      // Dump to XML
      System.IO.File.WriteAllText(path, xml);

      return true;
    }

    // ==========================================
    //  UTIL
    // ==========================================

    private void logInfo(String context, String msg) {
      WSRConfig.GetInstance().logInfo(context, msg);
    }
  }

  // ==========================================
  //  INNER CLASS
  // ==========================================


  public class WSRSpeecGrammar {
    public String XML { get; set; }
    public String Name { get; set; }
    public String Path { get; set; }
    public bool Enabled { get; set; }

    public void LoadGrammar(WSRSpeechEngine engine) {

      SpeechRecognitionEngine sre = engine.GetEngine();
      using (Stream s = StreamFromString(XML)) {
        try {
          Grammar grammar = new Grammar(s);
          grammar.Enabled = Enabled;
          grammar.Name = Name;
          WSRConfig.GetInstance().logInfo("GRAMMAR", engine.Name + ": Load: " + Name + " Enabled: " + grammar.Enabled);
          sre.LoadGrammar(grammar);
        }
        catch (Exception ex) {
          WSRConfig.GetInstance().logError("GRAMMAR", engine.Name + ": Error file: " + Name + ": " + ex.Message);
        }
      }
    }

    private Stream StreamFromString(string s) {
      MemoryStream stream = new MemoryStream();
      StreamWriter writer = new StreamWriter(stream);
      writer.Write(s);
      writer.Flush();
      stream.Position = 0;
      return stream;
    }
  }
}
