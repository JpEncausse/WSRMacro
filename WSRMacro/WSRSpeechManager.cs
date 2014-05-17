using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Collections.Generic;
using System.Xml.XPath;

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

  public class WSRSpeechManager : IDisposable {

    public bool Listening { get; set; } 
    

    // ==========================================
    //  SINGLETON
    // ==========================================

    private static WSRSpeechManager manager;
    public static WSRSpeechManager GetInstance() {
      if (manager == null) {
        manager = new WSRSpeechManager();
      }
      return manager;
    }

    public void Init() {

      Listening = true;

      // Load grammar files
      LoadXML();

      // Set default context
      SetContext("default");

      // Init engines
      Engines = new Dictionary<String, WSRSpeechEngine>();

      // Start audio watcher
      InitAudioWatcher();
    }

    public void Dispose() {
      foreach (WSRSpeechEngine engine in Engines.Values) {
        engine.Stop(true);
      }
    }
    
    // ==========================================
    //  ENGINE
    // ==========================================

    Dictionary<String, WSRSpeechEngine> Engines { get; set; }

    public void InitEngines() {

      Engines.Clear();
      WSRConfig cfg = WSRConfig.GetInstance();

      // File
      WSRSpeechEngine fileEngine = new WSRSpeechEngine("File", cfg.language, cfg.confidence);
      fileEngine.LoadGrammar();
      fileEngine.Init();
      Engines.Add("File", fileEngine);
    }

    public WSRSpeechEngine AddEngine(String prefix, String language, double confidence, Stream source, SpeechAudioFormatInfo format) {
      WSRSpeechEngine engine = new WSRSpeechEngine(prefix, language, confidence);
      engine.LoadGrammar();
      engine.Init();
      engine.GetEngine().SetInputToAudioStream(source, format);
      engine.Start();

      Engines.Add(prefix, engine);
      return engine;
    }

    public WSRSpeechEngine AddDefaultEngine(String prefix, String language, double confidence) {
      WSRSpeechEngine engine = new WSRSpeechEngine(prefix, language, confidence);
      engine.LoadGrammar();
      engine.Init();
      engine.GetEngine().SetInputToDefaultAudioDevice();
      engine.Start();


      var format = engine.GetEngine().AudioFormat;
      logInfo("ENGINE", "[Default] AudioFormat" + format.EncodingFormat + " channel: " + format.ChannelCount + " AB/S: " + format.AverageBytesPerSecond + " B/S: " + format.BitsPerSample);

      var level = engine.GetEngine().AudioLevel;
      var state = engine.GetEngine().AudioState;
      logInfo("ENGINE", "[Default] level: " + level + " state: " + state);

      Engines.Add(prefix, engine);
      return engine;
    }

    public void RecognizeFile(String fileName) {
      WSRSpeechEngine fileEngine = Engines["File"];
      fileEngine.GetEngine().SetInputToWaveFile(fileName);
      fileEngine.GetEngine().Recognize();
    }

    public void RecognizeString(String text) { 

      WSRSpeechEngine fileEngine = Engines["File"];
      // fileEngine.GetEngine().EmulateRecognize(text);
      fileEngine.GetEngine().SimulateRecognize(text);
    }

    // ==========================================
    //  GRAMMAR
    // ==========================================

    private String DIR_PATH;
    public String GetGrammarPath() {
      return DIR_PATH;
    }

    public Dictionary<string, WSRSpeecGrammar> Cache = new Dictionary<string, WSRSpeecGrammar>();

    public void LoadXML() {
      StopDirectoryWatcher();

      foreach (string directory in WSRConfig.GetInstance().directories) {
        DirectoryInfo dir = new DirectoryInfo(directory);
        
        // Load all grammars
        LoadXML(dir);
        
        // Watch directory for changes
        AddDirectoryWatcher(directory);
      }

      // FIXME: Add "dictation" grammar
      StartDirectoryWatcher();
    }

    protected void LoadXML(DirectoryInfo dir) {
      // WSRConfig.GetInstance().logDebug("GRAMMAR", "Load directory: " + dir.FullName);
      DIR_PATH = DIR_PATH != null ? DIR_PATH : dir.FullName;

      // Load Grammar
      foreach (FileInfo f in dir.GetFiles("*.xml")) {
        LoadXML(f.FullName, f.Name);
      }

      // Recursive directory
      foreach (DirectoryInfo d in dir.GetDirectories()) {
        LoadXML(d);
      }
    }

    public void LoadXML(String file, String name) {
      WSRConfig cfg = WSRConfig.GetInstance();
      WSRSpeecGrammar grammar = null;

      if (Cache.TryGetValue(name, out grammar)) {
        if (grammar.LastModified == File.GetLastWriteTime(file)) {
          logDebug("GRAMMAR", "Ignoring: " + name + " (no changes)");
          return;
        }
      }
      
      try {
        // Load the XML
        logInfo("GRAMMAR", "Load file: " + name + " : " + file);
        String xml = File.ReadAllText(file, Encoding.UTF8);
        xml = Regex.Replace(xml, "([^/])SARAH", "$1" + cfg.Name, RegexOptions.IgnoreCase);

        // Check regexp language
        if (!Regex.IsMatch(xml, "xml:lang=\"" + cfg.language + "\"", RegexOptions.IgnoreCase)) {
          logInfo("GRAMMAR", "Ignoring : " + name + " (" + cfg.language + ")");
          return;
        }

        // New grammar
        if (null == grammar) {
          grammar = new WSRSpeecGrammar();
          Cache.Add(name, grammar); // Add to cache
        }

        // Build grammar
        grammar.XML = xml;
        grammar.Name = name;
        grammar.Path = file;
        grammar.LastModified = File.GetLastWriteTime(file);

        // Check contexte
        grammar.Enabled = true;

        if ((file.IndexOf("lazy") >= 0) || Regex.IsMatch(xml, "root=\"lazy\\w+\"", RegexOptions.IgnoreCase)) {
          grammar.Enabled = false;
        }

        // Add to context if there is no context
        if (!WSRConfig.GetInstance().HasContext() && grammar.Enabled && !WSRConfig.GetInstance().context.Contains(name)) {
          WSRConfig.GetInstance().context.Add(name);
          logInfo("GRAMMAR", "Add to context list: " + name);
        }
      }
      catch (Exception ex) {
        cfg.logError("GRAMMAR", ex);
      }
    }


    // ------------------------------------------
    //  DYNAMIC GRAMMAR
    // ------------------------------------------

    public String ProcessRejected() {
      if (!Cache.ContainsKey("Dyn")) { return null; }
      var dyn = Cache["Dyn"];
      if (dyn.Enabled) { return dyn.Rejected; }
      return null;
    } 

    public void DynamicGrammar(String[] grammar, String[] tags) {
       if (!LoadXML(grammar,tags)) return;

       SetContext("Dyn");
       SetContextTimeout();
       ForwardContext();
       ResetContextTimeout();
    }

    protected bool LoadXML(String[] g, String[] tags) {
      WSRConfig cfg = WSRConfig.GetInstance();

      if (null == Engines)           { return false; }
      if (null == g || null == tags) { return false; }
      if (g.Length != tags.Length)   { return false; }

      bool rejected = false;
      bool garbage = false;
      var xml  = "\n<grammar version=\"1.0\" xml:lang=\""+ cfg.language +"\" mode=\"voice\"  root=\"ruleDyn\" xmlns=\"http://www.w3.org/2001/06/grammar\" tag-format=\"semantics/1.0\">";
          xml += "\n<rule id=\"ruleDyn\" scope=\"public\">";
          xml += "\n<tag>out.action=new Object(); </tag>";
          xml += "\n<one-of>";
          for (var i = 0; i < g.Length; i++) {
            logInfo("GRAMMAR", "Add to DynGrammar: " + g[i] + " => " + tags[i]);
            if (g[i].IndexOf('*') >= 0) { garbage = true; }
            if (g[i].Equals("*")) { rejected = true; continue; }
            xml += "\n<item>" + g[i].Replace("*","<ruleref special=\"GARBAGE\" />") + "<tag>out.action.tag=\"" + tags[i] + "\"</tag></item>";
          }
          xml += "\n</one-of>";
          xml += "\n<tag>out.action._attributes.uri=\"http://127.0.0.1:8080/askme\";</tag>";
          if (garbage) {
            xml += "\n<tag>out.action._attributes.dictation=\"true\";</tag>";
          }
          xml += "\n</rule>";
          xml += "\n</grammar>";

          xml = Regex.Replace(xml, "([^/])SARAH", "$1" + cfg.Name, RegexOptions.IgnoreCase);

          logInfo("GRAMMAR", "DynGrammar" + (rejected ? " (rejected)" : "") + ":" + xml);

      WSRSpeecGrammar grammar = null;
      if (Cache.ContainsKey("Dyn")){
        grammar = Cache["Dyn"];
      } else {
        grammar = new WSRSpeecGrammar();
        grammar.Name = "Dyn";
        Cache.Add("Dyn", grammar);
      }

      grammar.XML = xml;
      grammar.LastModified = DateTime.Now;
      grammar.Enabled = false;
      grammar.Rejected = rejected ? "http://127.0.0.1:8080/askme" : null;

      foreach (WSRSpeechEngine engine in Engines.Values) {
        grammar.LoadGrammar(engine);
      }

      return true;
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

    bool watcher = false; 
    protected void watcher_Changed(object sender, FileSystemEventArgs e) {

      if (watcher) { return; }
      watcher = true;

      // Reload all grammar
      LoadXML();

      // Set context back
      SetContext(tmpContext);

      if (null != Engines) {
        foreach (WSRSpeechEngine engine in Engines.Values) {
          engine.LoadGrammar();
        }
      }

      // Reset context timeout
      ResetContextTimeout();
      watcher = false;
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

      foreach (WSRSpeecGrammar g in Cache.Values) {
        if (g.Name == "dictation") { continue; }
        g.Enabled = context.Contains(g.Name);
        logDebug("CONTEXT", g.Name + " = " + g.Enabled);
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
      foreach (WSRSpeecGrammar g in Cache.Values) {
        if (g.Name == "dictation") { continue; }
        g.Enabled = all || context.Equals(g.Name);
        logDebug("CONTEXT", g.Name + " = " + g.Enabled);
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
      foreach (WSRSpeechEngine engine in Engines.Values) {
        foreach (Grammar g in engine.GetEngine().Grammars) {
          WSRSpeecGrammar s = null;
          if (Cache.TryGetValue(g.Name, out s)) {
            g.Enabled = s.Enabled;
            logDebug("CONTEXT", g.Name + " = " + s.Enabled);
          }
        }
      }
    }

    // ==========================================
    //  AUDIO WATCHER
    // ==========================================

    FileSystemWatcher audioWatcher = null;
    protected void InitAudioWatcher() {

      if (audioWatcher != null) { return; }
      String directory = WSRConfig.GetInstance().audio;
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
      WSRSpeechEngine fileEngine = Engines["File"];
      fileEngine.GetEngine().SetInputToWaveFile(e.FullPath);
      fileEngine.GetEngine().Recognize();
    }

    // ==========================================
    //  GOOGLE
    //  https://bitbucket.org/josephcooney/cloudspeech/src/8619cf699541?at=default
    // ==========================================

    public String ProcessAudioStream(Stream stream, String language) {
      CultureInfo culture = new System.Globalization.CultureInfo(language);
      var stt = new SpeechToText("https://www.google.com/speech-api/v2/recognize?output=json&xjerr=1&client=chromium&maxresults=2&key=AIzaSyCnl6MRydhw_5fLXIdASxkLJzcJh5iX0M4", culture);
      return stt.Recognize(stream);
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

      if (rr.Words.Count <= 0) {
        return true;
      }

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

    private void logDebug(String context, String msg) {
      WSRConfig.GetInstance().logDebug(context, msg);
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
    public DateTime LastModified { get; set; }
    public String Rejected { get; set; }

    public void LoadGrammar(WSRSpeechEngine engine) {

      SpeechRecognitionEngine sre = engine.GetEngine();
      using (Stream s = StreamFromString(XML)) {
        try {
          Grammar grammar = new Grammar(s);
          grammar.Enabled = Enabled;
          grammar.Name = Name;
          WSRConfig.GetInstance().logInfo("GRAMMAR", engine.Name + ": Load: " + Name + " Enabled: " + grammar.Enabled);
          reload(sre, Name, grammar);
        }
        catch (Exception ex) {
          WSRConfig.GetInstance().logError("GRAMMAR", engine.Name + ": Error file: " + Name + ": " + ex.Message);
        }
      }
    }

    private void reload(SpeechRecognitionEngine sre, String name, Grammar grammar) {
      foreach (Grammar g in sre.Grammars) {
        if (g.Name != name) { continue; }
        sre.UnloadGrammar(g);
        break;
      }
      sre.LoadGrammar(grammar);
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
