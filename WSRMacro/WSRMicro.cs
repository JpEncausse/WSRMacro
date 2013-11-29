using System;
using System.IO;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Xml.XPath;
using System.Diagnostics;

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

  public partial class WSRMicro : IDisposable {

    protected const int AUDIO_BUFFER_SIZE = 65536;

    // ==========================================
    //  CONSTRUCTOR
    // ==========================================
    
    public virtual void Init() {

      // Start RTP Client
      StartRTPClient();

      // Start HttpServer
      WSRHttpManager.GetInstance().StartHttpServer();

      // Start Speech Manager
      WSRSpeechManager.GetInstance().Init();
      InitSpeechEngine();

      // Start Timeout
      RestartTimeout();
    }

    // Perform cleanup on application exit
    public virtual void Dispose() {

      // Stop RTP Client
      DisposeRTPClient();

      // Stop HttpServer
      WSRHttpManager.GetInstance().Dispose();

      // Stop Speech Manager
      WSRSpeechManager.GetInstance().Dispose();

      // Stop Speaker
      WSRSpeakerManager.GetInstance().Dispose();
    }

    // ==========================================
    //  SPEECH / RESTART
    // ==========================================

    public virtual void Restart() {

      // Stop
      WSRConfig.GetInstance().logInfo("ENGINE", "Restarting WSR: dispose");
      try {
        DisposeRTPClient();
        StopSpeechEngine();
      }
      catch (Exception ex) { WSRConfig.GetInstance().logError("ENGINE", "Restarting WSR: " + ex.Message); }

      // Start
      WSRConfig.GetInstance().logInfo("ENGINE", "Restarting WSR: initialize");
      try {
        StartRTPClient();
        InitSpeechEngine();
      }
      catch (Exception ex) { WSRConfig.GetInstance().logError("ENGINE", "Restarting WSR: " + ex.Message); }
    }

    System.Timers.Timer RestartTimer = null;
    public void RestartTimeout() {
      if (RestartTimer != null) { return; }
      if (WSRConfig.GetInstance().restart <= 0) { return; }

      WSRConfig.GetInstance().logInfo("ENGINE", "Restart timeout: " + WSRConfig.GetInstance().restart);
      RestartTimer = new System.Timers.Timer();
      RestartTimer.Interval = WSRConfig.GetInstance().restart;
      RestartTimer.Elapsed += new System.Timers.ElapsedEventHandler(timer_Elapsed);
      RestartTimer.Enabled = true;
      RestartTimer.Start();
    }

    protected void timer_Elapsed(object sender, EventArgs e) {
      Restart();
      RestartTimer.Stop();
      RestartTimer.Start();
    }
 
    // ==========================================
    //  HANDLE RTPClient
    // ==========================================

    protected RTPClient rtpClient = null;

    /**
     *  Can be called on RaspberryPi using ffmpeg
     *  ffmpeg -ac 1 -f alsa -i hw:1,0 -ar 16000 -acodec pcm_s16le -f rtp rtp://192.168.0.8:7887
     *  avconv -f alsa -ac 1 -i hw:0,0 -acodec mp2 -b 64k -f rtp rtp://{IP of your laptop}:1234
     *  
     *  Or using Kinect on windows
     *  ffmpeg  -f dshow  -i audio="Réseau de microphones (Kinect U"   -ar 16000 -acodec pcm_s16le -f rtp rtp://127.0.0.1:7887
     */
    protected void StartRTPClient() {
      var port = WSRConfig.GetInstance().rtpport;
      if (port < 0) { return; }

      WSRConfig.GetInstance().logInfo("RTPCLIENT", "Start RTPClient: " + port);
      rtpClient = new RTPClient(port);
      rtpClient.StartClient();
    }

    protected void DisposeRTPClient() {
      WSRConfig.GetInstance().logError("RTPCLIENT", "Stop RTPClient");
      if (rtpClient != null) {
        rtpClient.StopClient();
        rtpClient.Dispose();
      }
      WSRConfig.GetInstance().logError("RTPCLIENT", "Stop RTPClient ... Done");
    }

    // ==========================================
    //  HANDLE SYSTEM TRAY
    // ==========================================

    public virtual void HandleCtxMenu(ContextMenuStrip menu) {
      var item = new ToolStripMenuItem();
      item.Text = "Log2Console";
      item.Click += new EventHandler(Watcher_Click);
      item.Image = net.encausse.sarah.Properties.Resources.Logs;
      menu.Items.Add(item);
    }

    protected void Watcher_Click(object sender, EventArgs e) {
      Process.Start(@"Log2Console\Log2Console.exe");
    }

    // ==========================================
    //  HANDLE HTTPSERVER
    // ==========================================

    public virtual bool HandleCustomRequest(NHttp.HttpRequestEventArgs e, StreamWriter writer) {

      // Status
      String status = e.Request.Params.Get("status");
      if (status != null) {
        if (WSRSpeakerManager.GetInstance().Speaking)
          writer.Write("speaking");
      }

      // Askme
      var values = System.Web.HttpUtility.ParseQueryString(e.Request.Url.Query);
      var mgr = WSRSpeechManager.GetInstance();
      String[] grammar = values.GetValues("grammar");
      String[] tags = values.GetValues("tags");
      WSRSpeechManager.GetInstance().DynamicGrammar(grammar,tags);

      // Stop Music
      String pause = e.Request.Params.Get("pause");
      if (pause != null) {
        WSRSpeakerManager.GetInstance().Stop(pause);
      }

      // Play Music
      String mp3 = e.Request.Params.Get("play");
      if (mp3 != null) {
        WSRSpeakerManager.GetInstance().Play(mp3, e.Request.Params.Get("sync") == null); 
      }

      // Recognize
      String audio = e.Request.Params.Get("recognize");
      if (audio != null) {
        WSRSpeechManager.GetInstance().RecognizeFile(audio);
      }

      // Listening
      String listen = e.Request.Params.Get("listen");
      if (listen != null) {
        WSRSpeechManager.GetInstance().Listening = bool.Parse(listen);
      }

      // Recognize File
      NHttp.HttpPostedFile file = e.Request.Files.Get("recognize");
      if (file != null) {
        byte[] data = null;
        using (var reader = new BinaryReader(file.InputStream)) {
          data = reader.ReadBytes(file.ContentLength);
        }
        var path = WSRConfig.GetInstance().audio+"/"+file.FileName;
        if (File.Exists(path)) { File.Delete(path); }
        File.WriteAllBytes(path, data);
      }

      // Text To Speech
      String tts = e.Request.Params.Get("tts");
      if (tts != null) {
        tts = e.Server.HtmlDecode(tts);
        WSRSpeakerManager.GetInstance().Speak(tts, e.Request.Params.Get("sync") == null);
      }

      // Text To Speech - Stop
      String notts = e.Request.Params.Get("notts");
      if (notts != null) {
        WSRSpeakerManager.GetInstance().ShutUp();
      }

      // Text To Speech - Stop
      String restart = e.Request.Params.Get("restart");
      if (restart != null) {
        Restart();
      }

      // Set Context
      String ctxt = e.Request.Params.Get("context");
      if (ctxt != null) {
        WSRSpeechManager.GetInstance().SetContext(new List<string>(ctxt.Split(',')));
        WSRSpeechManager.GetInstance().SetContextTimeout();
        WSRSpeechManager.GetInstance().ForwardContext();
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

      // Recognize String
      String emulate = e.Request.Params.Get("emulate");
      if (emulate != null) {
        WSRSpeechManager.GetInstance().RecognizeString(emulate);
        writer.Write(WSRSpeakerManager.GetInstance().SpeechBuffer);
      }
      return false;
    }

    // ==========================================
    //  HANDLE SPEECH RECOGNITION
    // ==========================================

    public virtual void InitSpeechEngine() { InitSpeechEngine(true); }
    protected      void InitSpeechEngine(bool def) {
      try {

        WSRConfig cfg = WSRConfig.GetInstance();
        WSRSpeechManager manager = WSRSpeechManager.GetInstance();

        // File
        manager.InitEngines();

        // Default
        if (def){
          manager.AddDefaultEngine("Default", cfg.language, cfg.confidence);
        }

        // RTP
        if (rtpClient == null) { return; }
        var format = new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Stereo);
        manager.AddEngine("RTP", cfg.language, cfg.confidence, rtpClient.AudioStream, format);
      }
      catch (Exception ex) {
        WSRConfig.GetInstance().logError("ENGINE", "InitEngines: " + ex.Message);
      }
    }

    public virtual void StopSpeechEngine() {
      WSRSpeechManager.GetInstance().Dispose();
    }

    public virtual String HandleCustomAttributes(XPathNavigator xnav) {
      // 3.1 Parse Result's TTS
      HandleTTS(xnav);

      // 3.2 Parse Result's Play
      HandlePlay(xnav);

      // 3.3 Handle Result's Context
      HandleContext(xnav);

      // 3.4 Handle Result's Listen
      HandleListen(xnav);

      return null;
    }

    protected void HandleTTS(XPathNavigator xnav) {
      XPathNavigator tts = xnav.SelectSingleNode("/SML/action/@tts");
      if (tts != null) {
        WSRSpeakerManager.GetInstance().Speak(tts.Value, false);
      }

      XPathNavigator notts = xnav.SelectSingleNode("/SML/action/@notts");
      if (notts != null) {
        WSRSpeakerManager.GetInstance().ShutUp();
      }
    }

    protected void HandlePlay(XPathNavigator xnav) {
      XPathNavigator play = xnav.SelectSingleNode("/SML/action/@play");
      if (play != null) {
        WSRSpeakerManager.GetInstance().Play(play.Value, false);
      }
    }

    protected void HandleListen(XPathNavigator xnav) {
      XPathNavigator listen = xnav.SelectSingleNode("/SML/action/@listen");
      if (listen != null) {
        WSRSpeechManager.GetInstance().Listening = bool.Parse(listen.Value);
      }
    }

    protected void HandleContext(XPathNavigator xnav) {
      XPathNavigator ctxt = xnav.SelectSingleNode("/SML/action/@context");
      if (ctxt != null) {
        WSRSpeechManager.GetInstance().SetContext(new List<string>(ctxt.Value.Split(',')));
        WSRSpeechManager.GetInstance().SetContextTimeout();
        WSRSpeechManager.GetInstance().ForwardContext();
      }
    }
  }
}