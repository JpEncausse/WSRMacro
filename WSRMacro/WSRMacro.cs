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
using NHttp;
using NAudio;
using NAudio.Wave;
using System.Drawing;
using System.Drawing.Imaging;

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

  public partial class WSRMacro { 

    // ==========================================
    //  WSRMacro CONSTRUCTOR
    // ==========================================

    protected double CONFIDENCE = 0.75;
    protected double CONFIDENCE_DICTATION = 0.30;
    protected String server = "127.0.0.1";
    protected String port = "8080";
    protected int loopback = 8888;
    protected List<String> directories = null;
    protected List<String> context = null;
    protected bool hasContext = false;

    public WSRMacro(List<String> dir, double confidence, String server, String port, int loopback, List<string> context) {

      this.CONFIDENCE = confidence;
      this.server = server;
      this.port = port;
      this.directories = dir;
      this.context = context;
      this.hasContext = context != null && context.Count > 0;

      //PlayMP3(@"medias\Wargames (extrait Fr) 1983.mp3");
      //PlayMP3("https://dl.dropbox.com/u/255810/Temporaire/MP3/Wargames%20%28extrait%20Fr%29%201983.mp3");

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

      // Start HttpServer
      StartHttpServer(loopback);
    }

    protected void log(string context, string msg) {
      WSRLaunch.log(context, msg);
    }
    protected void log(int level, string context, string msg) {
      WSRLaunch.log(level, context, msg);
    }

    // ==========================================
    //  WSRMacro GRAMMAR
    // ==========================================

    protected DictationGrammar dictation = null;   // The dictation grammar
    protected String DIR_PATH = null;             // FIXME: Resolved absolute path
    protected bool reload = true;

    protected void LoadGrammar() {

      if (!reload) {
        return;
      }

      if (!hasContext) {
        this.context = new List<string>();
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
      dictation = new DictationGrammar("grammar:dictation");
      dictation.Name = "dictation";
      dictation.Enabled = false;
      sre.LoadGrammar(dictation);

      // Start Watching
      StartDirectoryWatcher();

      // Set Context
      SetContext("default");
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
      grammar.Enabled = !grammar.RuleName.StartsWith("lazy");
      grammar.Name = name;

      // Load the grammar object into the recognizer.
      SpeechRecognitionEngine sre = GetEngine();
      sre.LoadGrammar(grammar);

      // Add to context if there is no context
      if (!hasContext && grammar.Enabled) {
        this.context.Add(name);
        log("GRAMMAR", "Add to context list: " + name );
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
      log("GRAMMAR", "Context: " + String.Join(", ", context.ToArray()));
      foreach (Grammar g in recognizer.Grammars) {
        if (g.Name == "dictation") { continue; }
        g.Enabled = context.Contains(g.Name);
        log("CONTEXT", g.Name + " = " + g.Enabled);
      }
    }

    public void SetContext(String context) {
      if (recognizer == null || context == null) { return; }
      log("GRAMMAR", "Context: " + context);
      if ("default".Equals(context)) { SetContext(this.context); return; }
      bool all = "all".Equals(context);
      foreach (Grammar g in recognizer.Grammars) {
        if (g.Name == "dictation") { continue; }
        g.Enabled = all || context.Equals(g.Name);
        log("CONTEXT", g.Name + " = " + g.Enabled);
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

    /*
    protected SpeechRecognitionEngine dictanizer = null;
    public SpeechRecognitionEngine GetDictationEngine() {
      if (dictanizer != null) {
        return dictanizer;
      }

      log("ENGINE", "Init recognizer");
      dictanizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("fr-FR"));

      // Set recognizer properties
      SetupProperties(dictanizer);

      // Load a Dictation grammar
      DictationGrammar d = new DictationGrammar("grammar:dictation");
      d.Name = "dictation";
      dictanizer.LoadGrammar(d);

      return dictanizer;
    }
    */

    protected SpeechRecognitionEngine recognizer = null;
    public virtual void StartRecognizer() {
      // Load grammar if needed
      LoadGrammar();

      // Start Recognizer
      log("ENGINE", "Start listening");
      try {
        GetEngine().RecognizeAsync(RecognizeMode.Multiple);
      } 
      catch(Exception){ log("ENGINE", "No device found"); }
    }

    public virtual void StopRecognizer() {
      log("ENGINE", "Stop listening");
      GetEngine().RecognizeAsyncStop();
      log("ENGINE", "Stop listening...done");
    }

    public SpeechRecognitionEngine GetEngine() {
      if (recognizer != null) {
        return recognizer;
      }

      log("ENGINE", "Init recognizer");
      recognizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("fr-FR"));
      recognizer.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(recognizer_SpeechRecognized);
      recognizer.RecognizeCompleted += new EventHandler<RecognizeCompletedEventArgs>(recognizer_RecognizeCompleted);
      recognizer.AudioStateChanged += new EventHandler<AudioStateChangedEventArgs>(recognizer_AudioStateChanged);

      // Alternate
      recognizer.MaxAlternates = 2;
      log("ENGINE", "MaxAlternates: " + recognizer.MaxAlternates);

      // Set the input to the recognizer.
      if (!SetupDevice(recognizer)) {
        try {
          recognizer.SetInputToDefaultAudioDevice();
        }
        catch (InvalidOperationException ex) {
          log("ENGINE", "No default input device: " + ex.Message);
        }
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
      //sre.InitialSilenceTimeout = TimeSpan.FromSeconds(3);
      //sre.BabbleTimeout = TimeSpan.FromSeconds(2);
      //sre.EndSilenceTimeout = TimeSpan.FromSeconds(1);
      //sre.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);

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

    protected void recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e) {
      RecognitionResult rr = e.Result;

      // 0. Prevent while speaking
      if (speaking) {
        log("ENGINE", "REJECTED Speech while speaking: " + rr.Confidence + " Text: " + rr.Text);
        return;
      }

      // 1. Handle dictation mode
      if (this.dictation.Enabled && HandleDictation(rr, CONFIDENCE_DICTATION)) {
        this.dictation.Enabled = false;
        return;
      }

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
      if (HandleWildcard(rr, url)) {
        return;
      }

      // 6. Otherwise send the request
      SendRequest(url, path); 
    }

    protected bool HandleDictation(RecognitionResult rr, double confidence) {

      if (rr.Confidence < confidence) {
        log("ENGINE", "REJECTED Dictation: " + rr.Confidence + " Text: " + rr.Text);
        return true;
      }
      log("ENGINE", "RECOGNIZED Dictation: " + rr.Confidence + " Text: " + rr.Text);

      // Send previous request with dictation
      String dictation = System.Uri.EscapeDataString(rr.Text);
      SendRequest(this.dictationUrl + "&dictation=" + dictation);

      this.dictationUrl = null;
      return true; 
    }

    protected XPathNavigator HandleSpeech(RecognitionResult rr) {
      XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();
      double confidence = GetConfidence(xnav);

      if (rr.Confidence < confidence) {
        log("ENGINE", "REJECTED Speech: " + rr.Confidence + " < " + confidence + " Device: " + GetDeviceConfidence() + " Text: " + rr.Text);
        return null;
      }

      log("ENGINE", "RECOGNIZED Speech: " + rr.Confidence + " Device: " + GetDeviceConfidence() + " Text: " + rr.Text);
      log(-1, "ENGINE", xnav.OuterXml);

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

    protected Boolean HandleWildcard(RecognitionResult rr, String url) {
      XPathNavigator xnav = rr.ConstructSmlFromSemantics().CreateNavigator();
      XPathNavigator wildcard = xnav.SelectSingleNode("/SML/action/@dictation");
      if (wildcard == null) { return false; }

      // Store URL
      this.dictationUrl = url;

      // Dictation in 2 steps
      if (wildcard.Value == "true") {
        dictation.Enabled = true;
      }

      // Wildcards
      int word = int.Parse(wildcard.Value);

      /* == DUMP AUDIO STREAM TO FILE ==========================================
       * http://msdn.microsoft.com/en-us/library/system.speech.recognition.recognitionresult.audio.aspx
      
      RecognizedAudio rAudio = rr.Audio;
      String dump = "D:/dump_sarah.wav";
      FileStream rStream = new FileStream(dump, FileMode.CreateNew);
      rAudio.WriteToWaveStream(rStream);
      rStream.Flush();
      rStream.Close(); 
      */

      return true;
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
        using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) {
          Speak(sr.ReadToEnd());
        }
      }
      catch (WebException ex) {
        log("HTTP", "Exception: " + ex.Message);
      }
    }

    protected void SendRequest(String url, String path) {
      if (url == null) { return; }
      if (path == null) { SendRequest(url); return; }

      log("HTTP", "Build HttpRequest: " + url);

      WebClient client = new WebClient();
      client.Headers.Add("user-agent", "S.A.R.A.H. (Self Actuated Residential Automated Habitat)");

      try {
        byte[] responseArray = client.UploadFile(url, path);
        String response = System.Text.Encoding.ASCII.GetString(responseArray);
        Speak(response);
      }
      catch (Exception ex) {
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

    protected String CleanURL(String url) {
      return url.Replace("http://127.0.0.1:8080", "http://" + server + ":" + port);
    }

    protected String GetURL(XPathNavigator xnav) {
      XPathNavigator xurl = xnav.SelectSingleNode("/SML/action/@uri");
      if (xurl == null) { return null; }

      // Build URI
      String url = CleanURL(xurl.Value + "?");

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
        Speak(tts.Value);
      }
    }

    protected void HandlePlay(XPathNavigator xnav) {
      XPathNavigator play = xnav.SelectSingleNode("/SML/action/@play");
      if (play != null) {
        PlayMP3(play.Value); 
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
    // http://stackoverflow.com/questions/8778624/trying-to-use-google-speech2text-in-c-sharp
    // ==========================================
    /*
    public void ProcessAudioStream(Stream stream) {

      String url = "https://www.google.com/speech-api/v1/recognize?xjerr=1&client=speech2text&lang=fr-FR&maxresults=2";
      ServicePointManager.ServerCertificateValidationCallback += delegate { return true; };
      
      HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url);
      request.Timeout = 60000;
      request.Method = "POST";
      request.KeepAlive = true;
      request.ContentType = "audio/x-flac; rate=16000";
      request.UserAgent = "speech2text";

      // Read Flac data
      byte[] data = new byte[stream.Length];
      stream.Read(data, 0, (int)stream.Length);
      stream.Close();

      using (Stream wrStream = request.GetRequestStream())
        wrStream.Write(data, 0, data.Length);

      try {
        HttpWebResponse response = (HttpWebResponse)request.GetResponse();
        var resp = response.GetResponseStream();

        if (resp != null) {
          StreamReader sr = new StreamReader(resp);
          log("GOOGLE", sr.ReadToEnd());

          resp.Close();
          resp.Dispose();
        }
      }
      catch (System.Exception ex) {
        log("GOOGLE", ex.ToString());
      }
    }
    */

    // ==========================================
    //  WSRMacro SPEECH
    // ==========================================

    protected Boolean speaking = false;
    public bool Speak(String tts) {
      
      if (tts == null) { return false; }
      log("TTS", "Say: " + tts);

      speaking = true;
      using (SpeechSynthesizer synthesizer = new SpeechSynthesizer()) {

        // Configure the audio output.
        synthesizer.SetOutputToDefaultAudioDevice();

        // Build and speak a prompt.
        PromptBuilder builder = new PromptBuilder();
        builder.AppendText(tts);
        synthesizer.Speak(builder);
      }
      speaking = false;
      return true;
    }

    // ==========================================
    //  WSRMacro PLAY
    // ==========================================

    List<String> played = new List<String>();

    public bool PlayMP3(string fileName) {

      if (fileName == null) { return false; }

      if (fileName.StartsWith("http")) {
        return StreamMP3(fileName);
      }

      speaking = true;
      log("PLAYER", "Start MP3 Player");
      using (var ms = File.OpenRead(fileName))
      using (var mp3Reader = new Mp3FileReader(ms))
      using (var pcmStream = WaveFormatConversionStream.CreatePcmStream(mp3Reader))
      using (var baStream = new BlockAlignReductionStream(pcmStream))
      using (var waveOut = new WaveOut(WaveCallbackInfo.FunctionCallback())) {
        waveOut.Init(baStream);
        waveOut.Play();
        played.Add(fileName);
        while (baStream.CurrentTime < baStream.TotalTime && played.Contains(fileName)) {
          Thread.Sleep(100);
        }
        played.Remove(fileName);
        waveOut.Stop();
        
      }
      log("PLAYER", "End MP3 Player");
      speaking = false;
      return true;
    }

    public bool StreamMP3(string url) {

      if (url == null) { return false; }

      speaking = true;
      log("PLAYER", "Stream MP3 Player");

      using (var ms = new MemoryStream()) 
      using (var stream = WebRequest.Create(url).GetResponse().GetResponseStream()){
        byte[] buffer = new byte[32768];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
          ms.Write(buffer, 0, read);
        }
        ms.Position = 0;
        using (var mp3Reader = new Mp3FileReader(ms))
        using (var pcmStream = WaveFormatConversionStream.CreatePcmStream(mp3Reader))
        using (var baStream = new BlockAlignReductionStream(pcmStream))
        using (var waveOut = new WaveOut(WaveCallbackInfo.FunctionCallback())) {
          waveOut.Init(baStream);
          waveOut.Play();
          played.Add(url);
          while (baStream.CurrentTime < baStream.TotalTime && played.Contains(url)) {
            Thread.Sleep(100);
          }
          played.Remove(url);
          waveOut.Stop();
        }
      }

      log("PLAYER", "End MP3 Player");
      speaking = false;
      return true;
    }

    private bool StopMP3(string key) {
      if (key == null) { return false; }
      played.Remove(key);
      return true;
    }

    // ==========================================
    //  WSRMacro HTTPSERVER
    // ==========================================

    HttpServer http = null;
    HttpServer httpLocal = null;
    public void StartHttpServer(int port) {

      // 192.168.0.x
      http = new HttpServer();
      http.EndPoint = new IPEndPoint(GetIpAddress(), port);
      http.RequestReceived += http_RequestReceived;
      http.Start();
      log("INIT", "Starting Server: http://" + http.EndPoint + "/");

      // Localhost
      httpLocal = new HttpServer();
      httpLocal.EndPoint = new IPEndPoint(IPAddress.Loopback, port);
      httpLocal.RequestReceived += http_RequestReceived;
      httpLocal.Start();
      log("INIT", "Starting Server: http://" + httpLocal.EndPoint + "/");
    }

    protected IPAddress GetIpAddress() {
      IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
      foreach (IPAddress ip in host.AddressList) {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
          return ip;
        }
      }
      return IPAddress.Loopback;
    }

    protected void http_RequestReceived(object sender, HttpRequestEventArgs e) {
      log("HTTP", "Request received: " + e.Request.Url.AbsoluteUri);

      if (HandleCustomRequest(e)) {
        return;
      }

      // Fake response
      using (var writer = new StreamWriter(e.Response.OutputStream)) {
        writer.Write(" ");
      }
    }

    protected virtual bool HandleCustomRequest(HttpRequestEventArgs e) {

      // Stop Music
      String pause = e.Request.Params.Get("pause");
      if (pause != null) {
        StopMP3(pause);
      }

      // Play Music
      String mp3 = e.Request.Params.Get("play");
      if (mp3 != null) {
        PlayMP3(mp3);
      }

      // Text To Speech
      String tts = e.Request.Params.Get("tts");
      if (tts != null) {
        tts = e.Server.HtmlDecode(tts);
        Speak(tts);
      }

      // Set Context
      String ctxt = e.Request.Params.Get("context");
      if (ctxt != null) {
        SetContext(new List<string>(ctxt.Split(',')));
      }

      return false;
    }
  }
}