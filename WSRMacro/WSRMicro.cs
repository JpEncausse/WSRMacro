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

      // Start Speech Manager
      WSRSpeech.GetInstance().Init();

      // Start HttpServer
      WSRHttpManager.GetInstance().StartHttpServer();
    }

    // Perform cleanup on application exit
    public virtual void Dispose() {

      // Stop RTP Client
      DisposeRTPClient();

      // Stop HttpServer
      WSRHttpManager.GetInstance().Dispose();

      // Stop Speech Manager
      WSRSpeech.GetInstance().Dispose();
    }

    // ==========================================
    //  HANDLE RTPClient
    // ==========================================

    protected RTPClient rtpClient = null;

    /**
     *  Can be called on RaspberryPi using ffmpeg
     *  ffmpeg -ac 1 -f alsa -i hw:1,0 -ar 16000 -acodec pcm_s16le -f rtp rtp://192.168.0.8:7887
     *  avconv -f alsa -ac 1 -i hw:0,0 -acodec mp2 -b 64k -f rtp rtp://{IP of your laptop}:1234
     */
    protected void StartRTPClient() {
      var port = WSRConfig.GetInstance().rtpport;
      if (port < 0) { return; }

      WSRConfig.GetInstance().logError("RTPCLIENT", "Start RTPClient: " + port);
      rtpClient = new RTPClient(port);
      rtpClient.StartClient();
    }

    protected void DisposeRTPClient() {
      WSRConfig.GetInstance().logError("RTPCLIENT", "Stop RTPClient");
      if (rtpClient != null) {
        rtpClient.StopClient();
        rtpClient.Dispose();
      }
    }

    public virtual void SetupRTPEngine(WSRSpeechEngine engine) {
      if (rtpClient == null) { return; }
      var format = new SpeechAudioFormatInfo(
        16000, 
        AudioBitsPerSample.Sixteen, 
        AudioChannel.Mono);

      engine.GetEngine().SetInputToAudioStream(rtpClient.AudioStream, format);
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

    public virtual bool HandleCustomRequest(NHttp.HttpRequestEventArgs e) {

      // Stop Music
      String pause = e.Request.Params.Get("pause");
      if (pause != null) {
        WSRSpeaker.GetInstance().Stop(pause);
      }

      // Play Music
      String mp3 = e.Request.Params.Get("play");
      if (mp3 != null) {
        WSRSpeaker.GetInstance().Play(mp3); 
      }

      // Recognize
      String audio = e.Request.Params.Get("recognize");
      if (audio != null) {
        WSRSpeech.GetInstance().Recognize(audio);
      }

      // Recognize File
      NHttp.HttpPostedFile file = e.Request.Files.Get("recognize");
      if (file != null) {
        byte[] data = null;
        using (var reader = new BinaryReader(file.InputStream)) {
          data = reader.ReadBytes(file.ContentLength);
        }
        var path = WSRConfig.GetInstance().audioWatcher+"/"+file.FileName;
        if (File.Exists(path)) { File.Delete(path); }
        File.WriteAllBytes(path, data);
      }

      // Text To Speech
      String tts = e.Request.Params.Get("tts");
      if (tts != null) {
        tts = e.Server.HtmlDecode(tts);
        WSRSpeaker.GetInstance().Speak(tts, e.Request.Params.Get("sync") == null);
      }

      // Text To Speech - Stop
      String notts = e.Request.Params.Get("notts");
      if (notts != null) {
        WSRSpeaker.GetInstance().ShutUp();
      }
      

      // Set Context
      String ctxt = e.Request.Params.Get("context");
      if (ctxt != null) {
        WSRSpeech.GetInstance().SetContext(new List<string>(ctxt.Split(',')));
        WSRSpeech.GetInstance().SetContextTimeout();
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

    // ==========================================
    //  HANDLE SPEECH RECOGNITION
    // ==========================================



    public virtual void SetupAudioEngine(WSRSpeechEngine engine) {
      try {
        /*
        int waveInDevices = NAudio.Wave.WaveIn.DeviceCount;
        for (int waveInDevice = 0; waveInDevice < waveInDevices; waveInDevice++) {
          NAudio.Wave.WaveInCapabilities deviceInfo = NAudio.Wave.WaveIn.GetCapabilities(waveInDevice);
          Console.WriteLine("Device {0}: {1}, {2} channels",
              waveInDevice, deviceInfo.ProductName, deviceInfo.Channels);
        }

        var waveIn = new NAudio.Wave.WaveIn();
        waveIn.DeviceNumber = 0;
        waveIn.DataAvailable += waveIn_DataAvailable;
        waveIn.WaveFormat = new NAudio.Wave.WaveFormat(16000, 2); // 16 kHz - mono
        waveIn.StartRecording();

        recognizer.SetInputToWaveStream(streamer);
        */
        engine.GetEngine().SetInputToDefaultAudioDevice();
      }
      catch (InvalidOperationException ex) {
        WSRConfig.GetInstance().logError("ENGINE", "No default input device: " + ex.Message);
      }
    }
    /*
    SpeechStreamer streamer = new SpeechStreamer(64000);
    private void waveIn_DataAvailable(object sender, NAudio.Wave.WaveInEventArgs e) {
      logInfo("ENGINE", "BytesRecorded" + e.BytesRecorded);
      streamer.Write(e.Buffer, 0, e.BytesRecorded);
    }*/

    public virtual String GetDeviceInfo() {
      return  "";
    }

    public virtual String HandleCustomAttributes(XPathNavigator xnav) {
      // 3.1 Parse Result's TTS
      HandleTTS(xnav);

      // 3.2 Parse Result's Play
      HandlePlay(xnav);

      // 3.3 Handle Result's Context
      HandleContext(xnav);

      return null;
    }

    protected void HandleTTS(XPathNavigator xnav) {
      XPathNavigator tts = xnav.SelectSingleNode("/SML/action/@tts");
      if (tts != null) {
        WSRSpeaker.GetInstance().Speak(tts.Value, true);
      }

      XPathNavigator notts = xnav.SelectSingleNode("/SML/action/@notts");
      if (notts != null) {
        WSRSpeaker.GetInstance().ShutUp();
      }
    }

    protected void HandlePlay(XPathNavigator xnav) {
      XPathNavigator play = xnav.SelectSingleNode("/SML/action/@play");
      if (play != null) {
        WSRSpeaker.GetInstance().Play(play.Value);
      }
    }

    protected void HandleContext(XPathNavigator xnav) {
      XPathNavigator ctxt = xnav.SelectSingleNode("/SML/action/@context");
      if (ctxt != null) {
        WSRSpeech.GetInstance().SetContext(new List<string>(ctxt.Value.Split(',')));
        WSRSpeech.GetInstance().SetContextTimeout();
      }
    }
  }
}